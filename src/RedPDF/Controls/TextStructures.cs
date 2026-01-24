using System.Windows;
using RedPDF.Services;

namespace RedPDF.Controls;

/// <summary>
/// Represents a word composed of multiple TextCharacters with a merged bounding box.
/// </summary>
public class TextWord
{
    public List<TextCharacter> Characters { get; } = [];
    public Rect Bounds { get; private set; }
    
    /// <summary>Gets the combined text of all characters in this word.</summary>
    public string Text => new string(Characters.Select(c => c.Char).ToArray());

    public void AddCharacter(TextCharacter ch)
    {
        Characters.Add(ch);
        var charRect = new Rect(ch.Left, ch.Top, ch.Right - ch.Left, ch.Bottom - ch.Top);
        Bounds = Characters.Count == 1 ? charRect : Rect.Union(Bounds, charRect);
    }
}

/// <summary>
/// Represents a line of text composed of multiple TextWords with a merged bounding box.
/// </summary>
public class TextLine
{
    public List<TextWord> Words { get; } = [];
    public Rect Bounds { get; private set; }

    public void AddWord(TextWord word)
    {
        Words.Add(word);
        Bounds = Words.Count == 1 ? word.Bounds : Rect.Union(Bounds, word.Bounds);
    }
}

/// <summary>
/// A simple spatial grid for O(1) average-case point/region queries.
/// </summary>
public class SpatialGrid<T> where T : class
{
    private readonly List<T>?[,] _cells;
    private readonly int _cellSize;
    private readonly int _cols;
    private readonly int _rows;
    private readonly double _width;
    private readonly double _height;

    public SpatialGrid(double width, double height, int cellSize = 50)
    {
        _width = width;
        _height = height;
        _cellSize = cellSize;
        _cols = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        _rows = Math.Max(1, (int)Math.Ceiling(height / cellSize));
        _cells = new List<T>?[_cols, _rows];
    }

    /// <summary>
    /// Inserts an item into all cells that intersect its bounding box.
    /// </summary>
    public void Insert(T item, Rect bounds)
    {
        int minCol = Math.Clamp((int)(bounds.Left / _cellSize), 0, _cols - 1);
        int maxCol = Math.Clamp((int)(bounds.Right / _cellSize), 0, _cols - 1);
        int minRow = Math.Clamp((int)(bounds.Top / _cellSize), 0, _rows - 1);
        int maxRow = Math.Clamp((int)(bounds.Bottom / _cellSize), 0, _rows - 1);

        for (int col = minCol; col <= maxCol; col++)
        {
            for (int row = minRow; row <= maxRow; row++)
            {
                _cells[col, row] ??= [];
                _cells[col, row]!.Add(item);
            }
        }
    }

    /// <summary>
    /// Returns all items in the cell containing the given point.
    /// </summary>
    public IEnumerable<T> Query(Point point)
    {
        int col = Math.Clamp((int)(point.X / _cellSize), 0, _cols - 1);
        int row = Math.Clamp((int)(point.Y / _cellSize), 0, _rows - 1);
        return _cells[col, row] ?? Enumerable.Empty<T>();
    }

    /// <summary>
    /// Returns all unique items in cells that intersect the given region.
    /// </summary>
    public IEnumerable<T> Query(Rect region)
    {
        int minCol = Math.Clamp((int)(region.Left / _cellSize), 0, _cols - 1);
        int maxCol = Math.Clamp((int)(region.Right / _cellSize), 0, _cols - 1);
        int minRow = Math.Clamp((int)(region.Top / _cellSize), 0, _rows - 1);
        int maxRow = Math.Clamp((int)(region.Bottom / _cellSize), 0, _rows - 1);

        var seen = new HashSet<T>();
        for (int col = minCol; col <= maxCol; col++)
        {
            for (int row = minRow; row <= maxRow; row++)
            {
                var cell = _cells[col, row];
                if (cell == null) continue;
                foreach (var item in cell)
                {
                    if (seen.Add(item))
                        yield return item;
                }
            }
        }
    }

    public void Clear()
    {
        Array.Clear(_cells, 0, _cells.Length);
    }
}

/// <summary>
/// DrawingVisual-based selection renderer for efficient highlight rendering.
/// Replaces thousands of WPF Rectangle elements with a single visual.
/// </summary>
public class SelectionVisual : System.Windows.Media.DrawingVisual
{
    /// <summary>
    /// Updates the selection visual with merged rectangles.
    /// </summary>
    public void Update(IEnumerable<Rect> rects, System.Windows.Media.Brush brush)
    {
        using var dc = RenderOpen();
        foreach (var rect in MergeAdjacentRects(rects))
        {
            dc.DrawRectangle(brush, null, rect);
        }
    }

    /// <summary>
    /// Clears the selection visual.
    /// </summary>
    public void Clear()
    {
        using var dc = RenderOpen();
        // Empty drawing context clears the visual
    }

    /// <summary>
    /// Merges horizontally adjacent rectangles on the same line to reduce draw calls.
    /// </summary>
    private static IEnumerable<Rect> MergeAdjacentRects(IEnumerable<Rect> rects)
    {
        var sorted = rects.OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();
        if (sorted.Count == 0) yield break;

        Rect current = sorted[0];
        const double tolerance = 2.0; // Pixel tolerance for merging

        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            
            // Check if on same line and adjacent horizontally
            bool sameLine = Math.Abs(next.Top - current.Top) < tolerance && 
                           Math.Abs(next.Height - current.Height) < tolerance;
            bool adjacent = next.Left <= current.Right + tolerance;

            if (sameLine && adjacent)
            {
                // Merge: extend current rect to include next
                current = new Rect(
                    current.Left,
                    Math.Min(current.Top, next.Top),
                    Math.Max(current.Right, next.Right) - current.Left,
                    Math.Max(current.Height, next.Height)
                );
            }
            else
            {
                yield return current;
                current = next;
            }
        }
        yield return current;
    }
}

/// <summary>
/// FrameworkElement host for SelectionVisual to integrate into WPF visual tree.
/// </summary>
public class SelectionVisualHost : FrameworkElement
{
    private readonly System.Windows.Media.Visual _visual;
    
    public SelectionVisualHost(System.Windows.Media.Visual visual)
    {
        _visual = visual;
        AddVisualChild(visual);
    }
    
    protected override int VisualChildrenCount => 1;
    
    protected override System.Windows.Media.Visual GetVisualChild(int index) => _visual;
}
