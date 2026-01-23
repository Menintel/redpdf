using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using RedPDF.Models;

namespace RedPDF.Services;

/// <summary>
/// PDF service implementation using Docnet.Core (PDFium wrapper).
/// Handles document loading, rendering, and text extraction.
/// </summary>
public class PdfService : IPdfService
{
    private IDocReader? _currentReader;
    private PdfDocumentModel? _currentDocument;
    private bool _disposed;

    public PdfDocumentModel? CurrentDocument => _currentDocument;

    public event EventHandler<PdfDocumentModel>? DocumentLoaded;
    public event EventHandler? DocumentClosed;

    public Task<PdfDocumentModel> OpenDocumentAsync(string filePath, string? password = null)
    {
        return Task.Run(() =>
        {
            // Close any existing document
            CloseDocument();

            // Validate file exists
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("PDF file not found.", filePath);
            }

            // Open the document with default dimensions
            _currentReader = DocLib.Instance.GetDocReader(
                filePath, 
                new PageDimensions(1.0));

            // Build page information
            var pages = new List<PdfPageModel>();
            for (int i = 0; i < _currentReader.GetPageCount(); i++)
            {
                using var pageReader = _currentReader.GetPageReader(i);
                pages.Add(new PdfPageModel
                {
                    Index = i,
                    Width = pageReader.GetPageWidth(),
                    Height = pageReader.GetPageHeight()
                });
            }

            // Create document model
            _currentDocument = new PdfDocumentModel
            {
                FilePath = filePath,
                PageCount = _currentReader.GetPageCount(),
                Pages = pages,
                FileSize = new FileInfo(filePath).Length
            };

            // Raise event
            DocumentLoaded?.Invoke(this, _currentDocument);

            return _currentDocument;
        });
    }

    public void CloseDocument()
    {
        if (_currentReader != null)
        {
            _currentReader.Dispose();
            _currentReader = null;
        }

        _currentDocument = null;
        DocumentClosed?.Invoke(this, EventArgs.Empty);
    }

    public Task<BitmapSource> RenderPageAsync(int pageIndex, double scale = 1.0)
    {
        return Task.Run(() =>
        {
            if (_currentDocument == null)
            {
                throw new InvalidOperationException("No document is currently loaded.");
            }

            if (pageIndex < 0 || pageIndex >= _currentDocument.PageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
            }

            // Get page dimensions at the specified scale
            var page = _currentDocument.Pages[pageIndex];
            int targetWidth = Math.Max(1, (int)(page.Width * scale));
            int targetHeight = Math.Max(1, (int)(page.Height * scale));

            // Create a new reader with the target dimensions
            using var scaledReader = DocLib.Instance.GetDocReader(
                _currentDocument.FilePath,
                new PageDimensions(targetWidth, targetHeight));

            using var pageReader = scaledReader.GetPageReader(pageIndex);

            // Get raw bytes (BGRA format)
            var rawBytes = pageReader.GetImage();
            int renderWidth = pageReader.GetPageWidth();
            int renderHeight = pageReader.GetPageHeight();

            // Convert to BitmapSource
            return ConvertToBitmapSource(rawBytes, renderWidth, renderHeight);
        });
    }

    public Task<BitmapSource> RenderThumbnailAsync(int pageIndex, int maxWidth = 120)
    {
        return Task.Run(() =>
        {
            if (_currentDocument == null)
            {
                throw new InvalidOperationException("No document is currently loaded.");
            }

            var page = _currentDocument.Pages[pageIndex];
            
            // Calculate proportional height
            int targetWidth = maxWidth;
            int targetHeight = (int)(maxWidth * (page.Height / page.Width));

            // Create a reader with thumbnail dimensions
            using var thumbReader = DocLib.Instance.GetDocReader(
                _currentDocument.FilePath,
                new PageDimensions(targetWidth, targetHeight));

            using var pageReader = thumbReader.GetPageReader(pageIndex);

            var rawBytes = pageReader.GetImage();
            int width = pageReader.GetPageWidth();
            int height = pageReader.GetPageHeight();

            return ConvertToBitmapSource(rawBytes, width, height);
        });
    }

    public Task<string> ExtractTextAsync(int pageIndex)
    {
        return Task.Run(() =>
        {
            if (_currentReader == null || _currentDocument == null)
            {
                throw new InvalidOperationException("No document is currently loaded.");
            }

            using var pageReader = _currentReader.GetPageReader(pageIndex);
            return pageReader.GetText() ?? string.Empty;
        });
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(string searchText, bool caseSensitive = false)
    {
        if (_currentReader == null || _currentDocument == null)
        {
            return [];
        }

        var results = new List<SearchResult>();
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var text = await ExtractTextAsync(i);
            int index = 0;

            while ((index = text.IndexOf(searchText, index, comparison)) != -1)
            {
                results.Add(new SearchResult(
                    i,
                    text.Substring(index, searchText.Length),
                    index,
                    searchText.Length
                ));
                index += searchText.Length;
            }
        }

        return results;
    }

    private static BitmapSource ConvertToBitmapSource(byte[] rawBytes, int width, int height)
    {
        // Docnet returns BGRA format
        int stride = width * 4;

        // Create a writable bitmap
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

        // Freeze for cross-thread access
        bitmap.Freeze();

        return bitmap;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                CloseDocument();
            }
            _disposed = true;
        }
    }
}
