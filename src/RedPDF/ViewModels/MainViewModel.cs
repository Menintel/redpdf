using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RedPDF.Models;
using RedPDF.Services;

namespace RedPDF.ViewModels;

/// <summary>
/// Main ViewModel for the application shell.
/// Manages document state, toolbar commands, and navigation.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IPdfService _pdfService;
    private readonly ICacheService _cacheService;

    [ObservableProperty]
    private string _currentFilePath = string.Empty;

    [ObservableProperty]
    private string _fileName = "RedPDF";

    [ObservableProperty]
    private bool _isDocumentLoaded;

    [ObservableProperty]
    private bool _isSidebarVisible = true;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private double _zoomLevel = 100;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private PdfDocumentModel? _currentDocument;

    [ObservableProperty]
    private ObservableCollection<PageThumbnailViewModel> _pageThumbnails = [];

    [ObservableProperty]
    private PageThumbnailViewModel? _selectedThumbnail;

    // Search properties
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<SearchResultViewModel> _searchResults = [];

    [ObservableProperty]
    private SearchResultViewModel? _selectedSearchResult;

    [ObservableProperty]
    private int _currentResultIndex;

    [ObservableProperty]
    private int _totalResults;

    // Annotation properties
    private readonly IAnnotationService _annotationService = new AnnotationService();

    [ObservableProperty]
    private AnnotationMode _currentAnnotationMode = AnnotationMode.None;

    [ObservableProperty]
    private bool _hasUnsavedAnnotations;

    public MainViewModel() : this(new PdfService(), new PageCacheService())
    {
    }

    public MainViewModel(IPdfService pdfService, ICacheService cacheService)
    {
        _pdfService = pdfService;
        _cacheService = cacheService;
        _annotationService.AnnotationsChanged += (_, _) => HasUnsavedAnnotations = true;
        Title = "RedPDF";
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
                Title = "Open PDF Document"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadDocumentAsync(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenFile Error: {ex}");
            System.Windows.MessageBox.Show(
                $"Unexpected error:\n\n{ex.Message}",
                "Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        _pdfService.CloseDocument();
        CurrentFilePath = string.Empty;
        FileName = "RedPDF";
        Title = "RedPDF";
        IsDocumentLoaded = false;
        CurrentPage = 0;
        TotalPages = 0;
        CurrentDocument = null;
        PageThumbnails.Clear();
        StatusMessage = "Ready";
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void FirstPage()
    {
        CurrentPage = 1;
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void LastPage()
    {
        CurrentPage = TotalPages;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        if (ZoomLevel < 400)
        {
            ZoomLevel = Math.Min(400, ZoomLevel + 25);
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 25)
        {
            ZoomLevel = Math.Max(25, ZoomLevel - 25);
        }
    }

    [RelayCommand]
    private void FitWidth()
    {
        // Will be implemented with actual viewer dimensions
        ZoomLevel = 100;
        StatusMessage = "Fit to width";
    }

    [RelayCommand]
    private void FitPage()
    {
        // Will be implemented with actual viewer dimensions
        ZoomLevel = 100;
        StatusMessage = "Fit to page";
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    #region Search Commands

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            ClearSearch();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || CurrentDocument == null)
            return;

        try
        {
            IsSearching = true;
            StatusMessage = $"Searching for '{SearchText}'...";
            SearchResults.Clear();
            CurrentResultIndex = 0;
            TotalResults = 0;

            var results = await _pdfService.SearchAsync(SearchText, caseSensitive: false);
            var resultList = results.ToList();

            int index = 1;
            foreach (var result in resultList)
            {
                SearchResults.Add(new SearchResultViewModel
                {
                    PageIndex = result.PageIndex,
                    PageNumber = result.PageIndex + 1,
                    MatchedText = result.MatchedText,
                    ContextText = $"Page {result.PageIndex + 1}",
                    ResultIndex = index++
                });
            }

            TotalResults = SearchResults.Count;
            
            // Notify navigation commands that they may now be executable
            NextSearchResultCommand.NotifyCanExecuteChanged();
            PreviousSearchResultCommand.NotifyCanExecuteChanged();
            
            if (TotalResults > 0)
            {
                CurrentResultIndex = 1;
                SelectedSearchResult = SearchResults[0];
                StatusMessage = $"Found {TotalResults} result(s) for '{SearchText}'";
            }
            else
            {
                StatusMessage = $"No results found for '{SearchText}'";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigateResults))]
    private void NextSearchResult()
    {
        if (SearchResults.Count == 0) return;
        
        CurrentResultIndex = CurrentResultIndex >= TotalResults ? 1 : CurrentResultIndex + 1;
        SelectedSearchResult = SearchResults[CurrentResultIndex - 1];
    }

    [RelayCommand(CanExecute = nameof(CanNavigateResults))]
    private void PreviousSearchResult()
    {
        if (SearchResults.Count == 0) return;
        
        CurrentResultIndex = CurrentResultIndex <= 1 ? TotalResults : CurrentResultIndex - 1;
        SelectedSearchResult = SearchResults[CurrentResultIndex - 1];
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        CurrentResultIndex = 0;
        TotalResults = 0;
        SelectedSearchResult = null;
        
        // Disable navigation commands
        NextSearchResultCommand.NotifyCanExecuteChanged();
        PreviousSearchResultCommand.NotifyCanExecuteChanged();
    }

    private bool CanSearch() => IsDocumentLoaded && !string.IsNullOrWhiteSpace(SearchText);
    private bool CanNavigateResults() => SearchResults.Count > 0;

    partial void OnSelectedSearchResultChanged(SearchResultViewModel? value)
    {
        if (value != null)
        {
            CurrentPage = value.PageNumber;
            StatusMessage = $"Result {value.ResultIndex} of {TotalResults} on page {value.PageNumber}";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Annotation Commands

    [RelayCommand]
    private void SetHighlightMode()
    {
        CurrentAnnotationMode = CurrentAnnotationMode == AnnotationMode.Highlight 
            ? AnnotationMode.None 
            : AnnotationMode.Highlight;
        StatusMessage = CurrentAnnotationMode == AnnotationMode.Highlight 
            ? "Highlight mode: Click and drag to highlight" 
            : "Ready";
    }

    [RelayCommand]
    private void SetUnderlineMode()
    {
        CurrentAnnotationMode = CurrentAnnotationMode == AnnotationMode.Underline 
            ? AnnotationMode.None 
            : AnnotationMode.Underline;
        StatusMessage = CurrentAnnotationMode == AnnotationMode.Underline 
            ? "Underline mode: Click and drag to underline" 
            : "Ready";
    }

    [RelayCommand]
    private void SetStickyNoteMode()
    {
        CurrentAnnotationMode = CurrentAnnotationMode == AnnotationMode.StickyNote 
            ? AnnotationMode.None 
            : AnnotationMode.StickyNote;
        StatusMessage = CurrentAnnotationMode == AnnotationMode.StickyNote 
            ? "Note mode: Click to add a note" 
            : "Ready";
    }

    [RelayCommand]
    private void ClearAnnotationMode()
    {
        CurrentAnnotationMode = AnnotationMode.None;
        StatusMessage = "Ready";
    }

    [RelayCommand(CanExecute = nameof(CanSaveAnnotations))]
    private async Task SaveAnnotationsAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = Path.GetFileNameWithoutExtension(CurrentFilePath) + "_annotated.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Saving annotations...";
                await _annotationService.SaveToPdfAsync(CurrentFilePath, dialog.FileName);
                HasUnsavedAnnotations = false;
                StatusMessage = $"Saved to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private bool CanSaveAnnotations() => IsDocumentLoaded && _annotationService.Annotations.Count > 0;

    /// <summary>
    /// Exposes annotation service for controls.
    /// </summary>
    public IAnnotationService AnnotationService => _annotationService;

    #endregion

    private bool CanNavigate() => IsDocumentLoaded && TotalPages > 0;

    private async Task LoadDocumentAsync(string filePath)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading document...";

            System.Diagnostics.Debug.WriteLine($"Opening PDF: {filePath}");
            
            CurrentDocument = await _pdfService.OpenDocumentAsync(filePath);

            System.Diagnostics.Debug.WriteLine($"PDF opened successfully: {CurrentDocument?.PageCount} pages");

            CurrentFilePath = filePath;
            FileName = CurrentDocument!.FileName;
            Title = $"{FileName} - RedPDF";
            TotalPages = CurrentDocument.PageCount;
            CurrentPage = 1;
            IsDocumentLoaded = true;

            // Load thumbnails
            await LoadThumbnailsAsync();

            StatusMessage = $"Loaded: {FileName} ({TotalPages} pages)";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PDF Load Error: {ex}");
            
            var errorMsg = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Error: {errorMsg}";
            IsDocumentLoaded = false;
            
            // Use dispatcher to ensure MessageBox shows on UI thread
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Failed to open PDF:\n\n{errorMsg}\n\nFull error:\n{ex.Message}\n\nType: {ex.GetType().FullName}",
                    "Error Opening PDF",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        PageThumbnails.Clear();

        if (CurrentDocument == null)
            return;

        for (int i = 0; i < CurrentDocument.PageCount; i++)
        {
            var thumbnail = new PageThumbnailViewModel
            {
                PageIndex = i,
                PageNumber = i + 1
            };

            PageThumbnails.Add(thumbnail);

            // Load thumbnail asynchronously
            try
            {
                var thumbImage = await _pdfService.RenderThumbnailAsync(i, 80);
                thumbnail.ThumbnailImage = thumbImage;
            }
            catch
            {
                // Ignore thumbnail rendering errors
            }
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        StatusMessage = $"Page {value} of {TotalPages}";
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();

        // Update selected thumbnail
        if (value > 0 && value <= PageThumbnails.Count)
        {
            SelectedThumbnail = PageThumbnails[value - 1];
        }
    }

    partial void OnIsDocumentLoadedChanged(bool value)
    {
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnZoomLevelChanged(double value)
    {
        StatusMessage = $"Zoom: {value:F0}%";
    }

    partial void OnSelectedThumbnailChanged(PageThumbnailViewModel? value)
    {
        if (value != null && CurrentPage != value.PageNumber)
        {
            CurrentPage = value.PageNumber;
        }
    }
}

/// <summary>
/// View model for page thumbnails in the sidebar.
/// </summary>
public partial class PageThumbnailViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pageIndex;

    [ObservableProperty]
    private int _pageNumber;

    [ObservableProperty]
    private BitmapSource? _thumbnailImage;
}

/// <summary>
/// View model for search results.
/// </summary>
public partial class SearchResultViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pageIndex;

    [ObservableProperty]
    private int _pageNumber;

    [ObservableProperty]
    private string _matchedText = string.Empty;

    [ObservableProperty]
    private string _contextText = string.Empty;

    [ObservableProperty]
    private int _resultIndex;
}

/// <summary>
/// Annotation mode for the PDF viewer.
/// </summary>
public enum AnnotationMode
{
    None,
    Highlight,
    Underline,
    StickyNote
}
