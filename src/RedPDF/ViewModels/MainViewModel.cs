using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace RedPDF.ViewModels;

/// <summary>
/// Main ViewModel for the application shell.
/// Manages document state, toolbar commands, and navigation.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
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

    public MainViewModel()
    {
        Title = "RedPDF";
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            Title = "Open PDF Document"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadDocument(dialog.FileName);
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        CurrentFilePath = string.Empty;
        FileName = "RedPDF";
        IsDocumentLoaded = false;
        CurrentPage = 0;
        TotalPages = 0;
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
        // Will be implemented with actual viewer
        StatusMessage = "Fit to width";
    }

    [RelayCommand]
    private void FitPage()
    {
        // Will be implemented with actual viewer
        StatusMessage = "Fit to page";
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    private bool CanNavigate() => IsDocumentLoaded && TotalPages > 0;

    private void LoadDocument(string filePath)
    {
        try
        {
            IsBusy = true;
            CurrentFilePath = filePath;
            FileName = Path.GetFileName(filePath);
            Title = $"{FileName} - RedPDF";
            
            // TODO: Actually load the PDF and get page count
            // For now, simulate loading
            TotalPages = 1;
            CurrentPage = 1;
            IsDocumentLoaded = true;
            StatusMessage = $"Loaded: {FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnCurrentPageChanged(int value)
    {
        StatusMessage = $"Page {value} of {TotalPages}";
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
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
}
