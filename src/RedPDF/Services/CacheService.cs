using System.Windows.Media.Imaging;
using Microsoft.Extensions.Caching.Memory;

namespace RedPDF.Services;

/// <summary>
/// Interface for page caching service.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a cached page or renders and caches it.
    /// </summary>
    Task<BitmapSource> GetOrRenderAsync(
        string documentId,
        int pageIndex,
        double scale,
        Func<Task<BitmapSource>> renderFunc);

    /// <summary>
    /// Clears all cached pages for a specific document.
    /// </summary>
    void ClearDocument(string documentId);

    /// <summary>
    /// Clears all cached pages.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Gets approximate cache size in bytes.
    /// </summary>
    long ApproximateSize { get; }
}

/// <summary>
/// LRU cache service for rendered PDF pages.
/// Manages memory usage and evicts old pages when limits are reached.
/// </summary>
public class PageCacheService : ICacheService, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly MemoryCacheEntryOptions _entryOptions;
    private readonly long _maxMemoryBytes;
    private long _currentSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new page cache service.
    /// </summary>
    /// <param name="maxMemoryMB">Maximum memory usage in megabytes.</param>
    public PageCacheService(int maxMemoryMB = 500)
    {
        _maxMemoryBytes = maxMemoryMB * 1024L * 1024L;

        var options = new MemoryCacheOptions
        {
            SizeLimit = _maxMemoryBytes
        };

        _cache = new MemoryCache(options);

        _entryOptions = new MemoryCacheEntryOptions()
            .SetPriority(CacheItemPriority.Normal)
            .RegisterPostEvictionCallback(OnEvicted);
    }

    public long ApproximateSize => _currentSize;

    public async Task<BitmapSource> GetOrRenderAsync(
        string documentId,
        int pageIndex,
        double scale,
        Func<Task<BitmapSource>> renderFunc)
    {
        string key = GenerateKey(documentId, pageIndex, scale);

        if (_cache.TryGetValue(key, out BitmapSource? cached) && cached != null)
        {
            return cached;
        }

        // Render the page
        var rendered = await renderFunc();

        // Calculate approximate size (width * height * 4 bytes per pixel)
        long size = rendered.PixelWidth * rendered.PixelHeight * 4;

        // Create entry options with size
        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(size)
            .SetPriority(CacheItemPriority.Normal)
            .RegisterPostEvictionCallback(OnEvicted);

        // Cache the rendered page
        _cache.Set(key, rendered, entryOptions);
        Interlocked.Add(ref _currentSize, size);

        return rendered;
    }

    public void ClearDocument(string documentId)
    {
        // Note: MemoryCache doesn't support prefix-based removal easily
        // In a production app, we'd track keys per document
        // For now, we clear all as a simple solution
        ClearAll();
    }

    public void ClearAll()
    {
        _cache.Compact(1.0);
        Interlocked.Exchange(ref _currentSize, 0);
    }

    private static string GenerateKey(string documentId, int pageIndex, double scale)
    {
        return $"{documentId}_{pageIndex}_{scale:F2}";
    }

    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is BitmapSource bitmap)
        {
            long size = bitmap.PixelWidth * bitmap.PixelHeight * 4;
            Interlocked.Add(ref _currentSize, -size);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.Dispose();
            _disposed = true;
        }
    }
}
