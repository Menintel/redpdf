using System.Windows;
using System.Windows.Controls;
using RedPDF.Models;

namespace RedPDF.Controls;

/// <summary>
/// Floating popup that appears when text is selected in the PDF viewer.
/// Provides quick access to annotation actions.
/// </summary>
public partial class AnnotationPopup : UserControl
{
    /// <summary>
    /// Event raised when user wants to highlight selected text.
    /// </summary>
    public event EventHandler<AnnotationEventArgs>? HighlightRequested;

    /// <summary>
    /// Event raised when user wants to underline selected text.
    /// </summary>
    public event EventHandler<AnnotationEventArgs>? UnderlineRequested;

    /// <summary>
    /// Event raised when user wants to add a note.
    /// </summary>
    public event EventHandler<AnnotationEventArgs>? NoteRequested;

    /// <summary>
    /// Event raised when user wants to copy text.
    /// </summary>
    public event EventHandler<TextEventArgs>? CopyRequested;

    /// <summary>
    /// The page index where selection occurred.
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// The selection bounds (in PDF coordinates).
    /// </summary>
    public List<AnnotationRect> SelectionRects { get; set; } = [];

    /// <summary>
    /// The selected text content.
    /// </summary>
    public string SelectedText { get; set; } = string.Empty;

    public AnnotationPopup()
    {
        InitializeComponent();
    }

    private void OnHighlight(object sender, RoutedEventArgs e)
    {
        HighlightRequested?.Invoke(this, new AnnotationEventArgs
        {
            PageIndex = PageIndex,
            Rects = SelectionRects,
            Text = SelectedText
        });
    }

    private void OnUnderline(object sender, RoutedEventArgs e)
    {
        UnderlineRequested?.Invoke(this, new AnnotationEventArgs
        {
            PageIndex = PageIndex,
            Rects = SelectionRects,
            Text = SelectedText
        });
    }

    private void OnAddNote(object sender, RoutedEventArgs e)
    {
        NoteRequested?.Invoke(this, new AnnotationEventArgs
        {
            PageIndex = PageIndex,
            Rects = SelectionRects,
            Text = SelectedText
        });
    }

    private void OnCopyText(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            Clipboard.SetText(SelectedText);
            CopyRequested?.Invoke(this, new TextEventArgs { Text = SelectedText });
        }
    }
}

/// <summary>
/// Event args for annotation requests.
/// </summary>
public class AnnotationEventArgs : EventArgs
{
    public int PageIndex { get; init; }
    public List<AnnotationRect> Rects { get; init; } = [];
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Event args for text operations.
/// </summary>
public class TextEventArgs : EventArgs
{
    public string Text { get; init; } = string.Empty;
}
