using System.Windows.Media.Imaging;
using RedPDF.Models;

namespace RedPDF.Services;

/// <summary>
/// Interface for PDF document operations.
/// </summary>
public interface IPdfService : IDisposable
{
    /// <summary>
    /// Opens a PDF document from the specified file path.
    /// </summary>
    /// <param name="filePath">Path to the PDF file.</param>
    /// <param name="password">Optional password for encrypted documents.</param>
    /// <returns>The loaded document model.</returns>
    Task<PdfDocumentModel> OpenDocumentAsync(string filePath, string? password = null);

    /// <summary>
    /// Closes the currently loaded document.
    /// </summary>
    void CloseDocument();

    /// <summary>
    /// Renders a page at the specified zoom level.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="scale">Scale factor (1.0 = 100%).</param>
    /// <returns>Rendered bitmap image of the page.</returns>
    Task<BitmapSource> RenderPageAsync(int pageIndex, double scale = 1.0);

    /// <summary>
    /// Renders a thumbnail for a page.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="maxWidth">Maximum thumbnail width.</param>
    /// <returns>Thumbnail bitmap.</returns>
    Task<BitmapSource> RenderThumbnailAsync(int pageIndex, int maxWidth = 120);

    /// <summary>
    /// Extracts text content from a page.
    /// </summary>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <returns>Text content of the page.</returns>
    Task<string> ExtractTextAsync(int pageIndex);

    /// <summary>
    /// Searches for text across all pages.
    /// </summary>
    /// <param name="searchText">Text to search for.</param>
    /// <param name="caseSensitive">Whether search is case-sensitive.</param>
    /// <returns>Collection of search results with page and position info.</returns>
    Task<IEnumerable<SearchResult>> SearchAsync(string searchText, bool caseSensitive = false);

    /// <summary>
    /// Gets the currently loaded document, or null if none loaded.
    /// </summary>
    PdfDocumentModel? CurrentDocument { get; }

    /// <summary>
    /// Event raised when a document is loaded.
    /// </summary>
    event EventHandler<PdfDocumentModel>? DocumentLoaded;

    /// <summary>
    /// Event raised when a document is closed.
    /// </summary>
    event EventHandler? DocumentClosed;
}

/// <summary>
/// Represents a search result within a PDF document.
/// </summary>
public record SearchResult(
    int PageIndex,
    string MatchedText,
    int StartIndex,
    int Length
);
