using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using RedPDF.Models;
using RedPDF.Services;

namespace RedPDF.Controls;

/// <summary>
/// Custom control for rendering and displaying PDF pages.
/// Supports zooming, scrolling, and virtual rendering.
/// </summary>
public partial class PdfViewerControl : UserControl, INotifyPropertyChanged
{
    private readonly ICacheService _cacheService;
    private double _zoomLevel = 1.0;
    private int _currentPage;

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

    #endregion

    public PdfViewerControl()
    {
        _cacheService = new PageCacheService();

        InitializeComponent();

        PagesContainer.ItemsSource = Pages;

        // Subscribe to scroll events for virtual rendering
        PageScrollViewer.ScrollChanged += OnScrollChanged;
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
            control.RefreshVisiblePages();
        }
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is int pageIndex)
        {
            control.ScrollToPage(pageIndex);
        }
    }

    private async void LoadDocumentAsync()
    {
        if (Document == null)
        {
            Pages.Clear();
            return;
        }

        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            Pages.Clear();

            // Create page view models
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

    private async void RefreshVisiblePages()
    {
        await RenderVisiblePagesAsync();
    }

    private async Task RenderVisiblePagesAsync()
    {
        if (Document == null || Pages.Count == 0)
            return;

        // Get visible page indices
        var visibleIndices = GetVisiblePageIndices();

        // Add buffer pages (2 before and after)
        var indicesToRender = new HashSet<int>();
        foreach (var index in visibleIndices)
        {
            for (int i = Math.Max(0, index - 2); i <= Math.Min(Pages.Count - 1, index + 2); i++)
            {
                indicesToRender.Add(i);
            }
        }

        // Render each page
        foreach (var index in indicesToRender)
        {
            var pageVm = Pages[index];
            if (pageVm.RenderedImage == null || pageVm.RenderedScale != _zoomLevel)
            {
                try
                {
                    var bitmap = await _cacheService.GetOrRenderAsync(
                        Document.Id,
                        index,
                        _zoomLevel,
                        () => RenderPageDirectlyAsync(index, _zoomLevel));

                    pageVm.RenderedImage = bitmap;
                    pageVm.RenderedScale = _zoomLevel;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to render page {index}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Renders a page directly using Docnet, without relying on shared service state.
    /// </summary>
    private Task<BitmapSource> RenderPageDirectlyAsync(int pageIndex, double scale)
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
            // Create a reader directly with the document file
            using var docReader = DocLib.Instance.GetDocReader(
                filePath,
                new PageDimensions(targetWidth, targetHeight));

            using var pageReader = docReader.GetPageReader(pageIndex);

            var rawBytes = pageReader.GetImage();
            int renderWidth = pageReader.GetPageWidth();
            int renderHeight = pageReader.GetPageHeight();

            return ConvertToBitmapSource(rawBytes, renderWidth, renderHeight);
        });
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
            RefreshVisiblePages();
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

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
