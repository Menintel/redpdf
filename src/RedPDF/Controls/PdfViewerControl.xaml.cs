using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Docnet.Core;
using Docnet.Core.Models;
using RedPDF.Models;
using RedPDF.Services;

namespace RedPDF.Controls;

/// <summary>
/// Custom control for rendering and displaying PDF pages.
/// Supports zooming, scrolling, and virtual rendering with optimizations.
/// </summary>
public partial class PdfViewerControl : UserControl, INotifyPropertyChanged
{
    private readonly ICacheService _cacheService;
    private double _zoomLevel = 1.0;
    private int _currentPage;
    
    // Virtual scrolling optimization
    private readonly DispatcherTimer _scrollDebounceTimer;
    private CancellationTokenSource? _renderCts;
    private readonly object _renderLock = new();
    private bool _isRendering;
    private const int ScrollDebounceMs = 100;
    private const int PageBufferSize = 3; // Pages to keep before/after viewport
    private const int MaxConcurrentRenders = 4;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PageViewModel> Pages { get; } = [];

    #region Dependency Properties

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(PdfDocumentModel),
            typeof(PdfViewerControl),
            new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom),
            typeof(double),
            typeof(PdfViewerControl),
            new PropertyMetadata(100.0, OnZoomChanged));

    public static readonly DependencyProperty CurrentPageIndexProperty =
        DependencyProperty.Register(
            nameof(CurrentPageIndex),
            typeof(int),
            typeof(PdfViewerControl),
            new PropertyMetadata(0, OnCurrentPageChanged));

    public PdfDocumentModel? Document
    {
        get => (PdfDocumentModel?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int CurrentPageIndex
    {
        get => (int)GetValue(CurrentPageIndexProperty);
        set => SetValue(CurrentPageIndexProperty, value);
    }

    public static readonly DependencyProperty AnnotationServiceProperty =
        DependencyProperty.Register(
            nameof(AnnotationService),
            typeof(IAnnotationService),
            typeof(PdfViewerControl),
            new PropertyMetadata(null));

    public IAnnotationService? AnnotationService
    {
        get => (IAnnotationService?)GetValue(AnnotationServiceProperty);
        set => SetValue(AnnotationServiceProperty, value);
    }

    #endregion

    public PdfViewerControl()
    {
        _cacheService = new PageCacheService();

        InitializeComponent();

        PagesContainer.ItemsSource = Pages;

        // Setup scroll debounce timer
        _scrollDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScrollDebounceMs)
        };
        _scrollDebounceTimer.Tick += OnScrollDebounceTimerTick;

        // Subscribe to scroll events for virtual rendering
        PageScrollViewer.ScrollChanged += OnScrollChanged;
        
        // Handle annotation selection bubbling from overlays
        AddHandler(AnnotationOverlay.TextSelectionChangedEvent, new RoutedEventHandler(OnTextSelectionChanged));
    }

    private void OnTextSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (e is AnnotationSelectionEventArgs args && sender is AnnotationOverlay overlay)
        {
            if (args.SelectedCharacters.Count == 0)
            {
                AnnotationMenu.Visibility = Visibility.Collapsed;
                return;
            }

            // Position popup near selection
            var positionInViewer = overlay.TranslatePoint(args.Position, this);
            
            // Adjust position to be above selection
            AnnotationMenu.Margin = new Thickness(positionInViewer.X, Math.Max(0, positionInViewer.Y - 50), 0, 0);
            AnnotationMenu.Visibility = Visibility.Visible;
            
            // Pass data to popup
            AnnotationMenu.PageIndex = overlay.PageIndex;
            AnnotationMenu.SelectedText = new string(args.SelectedCharacters.Select(c => c.Char).ToArray());
            
            // Convert characters to rects (simplified: one rect per char, but service should merge)
            AnnotationMenu.SelectionRects = args.SelectedCharacters.Select(c => 
                new AnnotationRect(c.Left, c.Top, c.Right - c.Left, c.Bottom - c.Top)).ToList();
        }
    }

    private void OnHighlightRequested(object sender, AnnotationEventArgs e)
    {
        if (AnnotationService == null) return;

        var annotation = new HighlightAnnotation
        {
            PageIndex = e.PageIndex,
            Rects = e.Rects,
            Color = "#80FFFF00"
        };
        AnnotationService.AddAnnotation(annotation);
        AnnotationMenu.Visibility = Visibility.Collapsed;
        
        // Clear selection on active overlay if possible (requires tracking active overlay)
        // For now, user clicks away to clear
    }

    private void OnUnderlineRequested(object sender, AnnotationEventArgs e)
    {
        if (AnnotationService == null) return;

        var annotation = new UnderlineAnnotation
        {
            PageIndex = e.PageIndex,
            Rects = e.Rects,
            Color = "#FFFF0000"
        };
        AnnotationService.AddAnnotation(annotation);
        AnnotationMenu.Visibility = Visibility.Collapsed;
    }

    private void OnNoteRequested(object sender, AnnotationEventArgs e)
    {
        if (AnnotationService == null) return;

        // Use the first rect position for the note
        var firstRect = e.Rects.FirstOrDefault();
        
        var annotation = new StickyNoteAnnotation
        {
            PageIndex = e.PageIndex,
            X = firstRect.X + firstRect.Width, // Place at end of selection
            Y = firstRect.Y,
            Content = "New Note",
            IsExpanded = true
        };
        AnnotationService.AddAnnotation(annotation);
        AnnotationMenu.Visibility = Visibility.Collapsed;
    }

    private void OnCopyRequested(object sender, TextEventArgs e)
    {
        AnnotationMenu.Visibility = Visibility.Collapsed;
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control)
        {
            control.LoadDocumentAsync();
        }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control)
        {
            control._zoomLevel = (double)e.NewValue / 100.0;
            // Clear all rendered images on zoom change (they'll be re-rendered at new scale)
            control.InvalidateAllPages();
            control.ScheduleRender();
        }
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is int pageIndex)
        {
            control.ScrollToPage(pageIndex);
        }
    }

    /// <summary>
    /// Invalidates all rendered pages, forcing them to re-render.
    /// </summary>
    private void InvalidateAllPages()
    {
        foreach (var page in Pages)
        {
            page.RenderedImage = null;
            page.RenderedScale = 0;
        }
    }

    private async void LoadDocumentAsync()
    {
        // Cancel any pending renders
        CancelPendingRenders();
        
        if (Document == null)
        {
            Pages.Clear();
            return;
        }

        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            Pages.Clear();

            // Create page view models with placeholder heights
            foreach (var page in Document.Pages)
            {
                Pages.Add(new PageViewModel
                {
                    PageIndex = page.Index,
                    Width = page.Width,
                    Height = page.Height
                });
            }

            // Render visible pages
            await RenderVisiblePagesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading document: {ex}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Schedules a render operation with debouncing.
    /// </summary>
    private void ScheduleRender()
    {
        _scrollDebounceTimer.Stop();
        _scrollDebounceTimer.Start();
    }

    private async void OnScrollDebounceTimerTick(object? sender, EventArgs e)
    {
        _scrollDebounceTimer.Stop();
        await RenderVisiblePagesAsync();
    }

    /// <summary>
    /// Cancels any pending render operations.
    /// </summary>
    private void CancelPendingRenders()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
    }

    private async Task RenderVisiblePagesAsync()
    {
        if (Document == null || Pages.Count == 0)
            return;

        // Prevent concurrent render operations
        lock (_renderLock)
        {
            if (_isRendering)
            {
                // Schedule another render after current one completes
                ScheduleRender();
                return;
            }
            _isRendering = true;
        }

        // Cancel any pending renders
        CancelPendingRenders();
        var cancellationToken = _renderCts!.Token;

        try
        {
            // Get visible page indices
            var visibleIndices = GetVisiblePageIndices();

            // Build set of pages to render (visible + buffer)
            var indicesToRender = new HashSet<int>();
            foreach (var index in visibleIndices)
            {
                for (int i = Math.Max(0, index - PageBufferSize); 
                     i <= Math.Min(Pages.Count - 1, index + PageBufferSize); 
                     i++)
                {
                    indicesToRender.Add(i);
                }
            }

            // Unload pages that are far from viewport to save memory
            UnloadDistantPages(indicesToRender);

            // Get pages that need rendering
            var pagesToRender = indicesToRender
                .Where(i => Pages[i].RenderedImage == null || Pages[i].RenderedScale != _zoomLevel)
                .OrderBy(i => Math.Abs(i - visibleIndices.FirstOrDefault())) // Prioritize closest to viewport
                .ToList();

            if (pagesToRender.Count == 0)
                return;

            // Render pages in parallel with limited concurrency
            var semaphore = new SemaphoreSlim(MaxConcurrentRenders);
            var tasks = pagesToRender.Select(async index =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var pageVm = Pages[index];
                    
                    // Double-check if still needs rendering
                    if (pageVm.RenderedImage != null && pageVm.RenderedScale == _zoomLevel)
                        return;

                    var bitmap = await _cacheService.GetOrRenderAsync(
                        Document.Id,
                        index,
                        _zoomLevel,
                        () => RenderPageDirectlyAsync(index, _zoomLevel, cancellationToken));

                    // Fetch characters for selection
                    var characters = await GetPageCharactersDirectlyAsync(index, cancellationToken);

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        // Update on UI thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            pageVm.RenderedImage = bitmap;
                            pageVm.Characters = characters;
                            pageVm.RenderedScale = _zoomLevel;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when scrolling fast
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to render page {index}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
        finally
        {
            lock (_renderLock)
            {
                _isRendering = false;
            }
        }
    }

    /// <summary>
    /// Unloads rendered images from pages far from the viewport to save memory.
    /// </summary>
    private void UnloadDistantPages(HashSet<int> keepIndices)
    {
        const int unloadThreshold = 10; // Pages beyond this distance get unloaded
        
        for (int i = 0; i < Pages.Count; i++)
        {
            if (!keepIndices.Contains(i))
            {
                // Check if page is far from any kept page
                int minDistance = keepIndices.Count > 0 
                    ? keepIndices.Min(k => Math.Abs(k - i)) 
                    : int.MaxValue;
                    
                if (minDistance > unloadThreshold && Pages[i].RenderedImage != null)
                {
                    Pages[i].RenderedImage = null;
                    Pages[i].RenderedScale = 0;
                }
            }
        }
    }

    /// <summary>
    /// Renders a page directly using Docnet, without relying on shared service state.
    /// </summary>
    private Task<BitmapSource> RenderPageDirectlyAsync(int pageIndex, double scale, CancellationToken cancellationToken = default)
    {
        // Capture document info on UI thread before entering background task
        var doc = Document;
        if (doc == null || !File.Exists(doc.FilePath))
        {
            return Task.FromException<BitmapSource>(
                new InvalidOperationException("Document not available."));
        }

        var filePath = doc.FilePath;
        var page = doc.Pages[pageIndex];
        int targetWidth = Math.Max(1, (int)(page.Width * scale));
        int targetHeight = Math.Max(1, (int)(page.Height * scale));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Create a reader directly with the document file
            using var docReader = DocLib.Instance.GetDocReader(
                filePath,
                new PageDimensions(targetWidth, targetHeight));

            cancellationToken.ThrowIfCancellationRequested();

            using var pageReader = docReader.GetPageReader(pageIndex);

            var rawBytes = pageReader.GetImage();
            int renderWidth = pageReader.GetPageWidth();
            int renderHeight = pageReader.GetPageHeight();

            cancellationToken.ThrowIfCancellationRequested();

            return ConvertToBitmapSource(rawBytes, renderWidth, renderHeight);
        }, cancellationToken);

    }

    private Task<List<TextCharacter>> GetPageCharactersDirectlyAsync(int pageIndex, CancellationToken cancellationToken)
    {
        var doc = Document;
        if (doc == null || !File.Exists(doc.FilePath)) return Task.FromResult(new List<TextCharacter>());

        var filePath = doc.FilePath;

        return Task.Run(() =>
        {
            using var docReader = DocLib.Instance.GetDocReader(filePath, new PageDimensions(1.0));
            using var pageReader = docReader.GetPageReader(pageIndex);
            
            var chars = pageReader.GetCharacters();
            return chars.Select(c => new TextCharacter(c.Char, c.Box.Left, c.Box.Top, c.Box.Right, c.Box.Bottom)).ToList();
        }, cancellationToken);
    }

    private static BitmapSource ConvertToBitmapSource(byte[] rawBytes, int width, int height)
    {
        int stride = width * 4;

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

        bitmap.Lock();
        try
        {
            Marshal.Copy(rawBytes, 0, bitmap.BackBuffer, rawBytes.Length);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            bitmap.Unlock();
        }

        bitmap.Freeze();
        return bitmap;
    }

    private List<int> GetVisiblePageIndices()
    {
        var indices = new List<int>();

        if (Pages.Count == 0)
            return indices;

        double scrollOffset = PageScrollViewer.VerticalOffset;
        double viewportHeight = PageScrollViewer.ViewportHeight;

        double currentY = 0;
        for (int i = 0; i < Pages.Count; i++)
        {
            double pageHeight = Pages[i].Height * _zoomLevel + 16;

            if (currentY + pageHeight > scrollOffset && currentY < scrollOffset + viewportHeight)
            {
                indices.Add(i);
            }

            currentY += pageHeight;

            if (currentY > scrollOffset + viewportHeight)
                break;
        }

        if (indices.Count == 0 && Pages.Count > 0)
        {
            indices.Add(0);
        }

        return indices;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) > 0.1)
        {
            // Debounce scroll events to avoid excessive rendering
            ScheduleRender();
            UpdateCurrentPageFromScroll();
        }
    }

    private void UpdateCurrentPageFromScroll()
    {
        var visibleIndices = GetVisiblePageIndices();
        if (visibleIndices.Count > 0 && CurrentPageIndex != visibleIndices[0])
        {
            _currentPage = visibleIndices[0];
            CurrentPageIndex = _currentPage;
        }
    }

    public void ScrollToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= Pages.Count)
            return;

        double offset = 0;
        for (int i = 0; i < pageIndex; i++)
        {
            offset += Pages[i].Height * _zoomLevel + 16;
        }

        PageScrollViewer.ScrollToVerticalOffset(offset);
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// View model for a single page in the PDF viewer.
/// </summary>
public class PageViewModel : INotifyPropertyChanged
{
    private BitmapSource? _renderedImage;
    private double _renderedScale;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int PageIndex { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public BitmapSource? RenderedImage
    {
        get => _renderedImage;
        set
        {
            _renderedImage = value;
            OnPropertyChanged(nameof(RenderedImage));
        }
    }

    public double RenderedScale
    {
        get => _renderedScale;
        set
        {
            _renderedScale = value;
            OnPropertyChanged(nameof(RenderedScale));
        }
    }

    public List<TextCharacter> Characters { get; set; } = [];

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
