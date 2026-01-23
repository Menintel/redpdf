using System.IO;

namespace RedPDF.Models;

/// <summary>
/// Represents a loaded PDF document with its metadata and page information.
/// </summary>
public class PdfDocumentModel
{
    /// <summary>
    /// Unique identifier for caching purposes.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Full file path to the PDF document.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Display name of the document.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Total number of pages in the document.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Collection of page information.
    /// </summary>
    public List<PdfPageModel> Pages { get; init; } = [];

    /// <summary>
    /// Whether the document is password protected.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// When the document was loaded.
    /// </summary>
    public DateTime LoadedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Represents a single page in a PDF document.
/// </summary>
public class PdfPageModel
{
    /// <summary>
    /// Zero-based page index.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Display page number (1-based).
    /// </summary>
    public int PageNumber => Index + 1;

    /// <summary>
    /// Original page width in points (1/72 inch).
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// Original page height in points (1/72 inch).
    /// </summary>
    public double Height { get; init; }

    /// <summary>
    /// Aspect ratio of the page (width/height).
    /// </summary>
    public double AspectRatio => Height > 0 ? Width / Height : 1;
}
