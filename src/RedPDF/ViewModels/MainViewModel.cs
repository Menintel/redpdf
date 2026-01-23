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

    public MainViewModel() : this(new PdfService(), new PageCacheService())
    {
    }

    public MainViewModel(IPdfService pdfService, ICacheService cacheService)
    {
        _pdfService = pdfService;
        _cacheService = cacheService;
        Title = "RedPDF";
    }

    [RelayCommand]
    private async Task OpenFileAsync()
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

    private bool CanNavigate() => IsDocumentLoaded && TotalPages > 0;

    private async Task LoadDocumentAsync(string filePath)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading document...";

            CurrentDocument = await _pdfService.OpenDocumentAsync(filePath);

            CurrentFilePath = filePath;
            FileName = CurrentDocument.FileName;
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
            var errorMsg = ex.InnerException?.Message ?? ex.Message;
            StatusMessage = $"Error: {errorMsg}";
            System.Diagnostics.Debug.WriteLine($"PDF Load Error: {ex}");
            System.Windows.MessageBox.Show(
                $"Failed to open PDF:\n\n{errorMsg}\n\nDetails: {ex.GetType().Name}",
                "Error Opening PDF",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            IsDocumentLoaded = false;
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
