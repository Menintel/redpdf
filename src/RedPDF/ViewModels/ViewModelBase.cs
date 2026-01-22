using CommunityToolkit.Mvvm.ComponentModel;

namespace RedPDF.ViewModels;

/// <summary>
/// Base ViewModel class that all ViewModels inherit from.
/// Provides common functionality like property change notifications.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;
}
