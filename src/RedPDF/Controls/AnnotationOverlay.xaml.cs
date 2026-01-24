using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.Specialized;
using RedPDF.Models;
using RedPDF.Services;

namespace RedPDF.Controls;

public partial class AnnotationOverlay : UserControl
{
    private Point? _startPoint;
    private bool _isSelecting;
    private readonly List<TextCharacter> _pageCharacters = [];
    private readonly List<TextCharacter> _selectedCharacters = [];
    
    // NEW: Word/Line hierarchy and spatial indexing
    private readonly List<TextWord> _pageWords = [];
    private readonly List<TextLine> _pageLines = [];
    private SpatialGrid<TextCharacter>? _charSpatialGrid;  // For cursor hit-testing
    private SpatialGrid<TextWord>? _wordSpatialGrid;       // For selection snapping
    
    // NEW: Efficient rendering with DrawingVisual
    private SelectionVisual? _selectionVisual;
    
    // NEW: Double-click tracking for word selection
    private DateTime _lastClickTime = DateTime.MinValue;
    private Point _lastClickPoint;
    private const double DoubleClickTimeMs = 500;
    private const double DoubleClickDistance = 5;
    
    // Selection visual
    private readonly SolidColorBrush _selectionBrush = new(Color.FromArgb(100, 51, 153, 255)); // Semi-transparent blue
    
    public static readonly DependencyProperty PageIndexProperty =
        DependencyProperty.Register(nameof(PageIndex), typeof(int), typeof(AnnotationOverlay), new PropertyMetadata(-1));

    public int PageIndex
    {
        get => (int)GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(nameof(Scale), typeof(double), typeof(AnnotationOverlay), new PropertyMetadata(1.0));

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public static readonly DependencyProperty CharactersProperty =
        DependencyProperty.Register(nameof(Characters), typeof(IEnumerable<TextCharacter>), typeof(AnnotationOverlay), new PropertyMetadata(null, OnCharactersChanged));

    public IEnumerable<TextCharacter> Characters
    {
        get => (IEnumerable<TextCharacter>)GetValue(CharactersProperty);
        set => SetValue(CharactersProperty, value);
    }

    public static readonly DependencyProperty AnnotationsSourceProperty =
        DependencyProperty.Register(
            nameof(AnnotationsSource),
            typeof(IEnumerable<Annotation>),
            typeof(AnnotationOverlay),
            new PropertyMetadata(null, OnAnnotationsSourceChanged));

    public IEnumerable<Annotation> AnnotationsSource
    {
        get => (IEnumerable<Annotation>)GetValue(AnnotationsSourceProperty);
        set => SetValue(AnnotationsSourceProperty, value);
    }

    private static void OnCharactersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnnotationOverlay overlay && e.NewValue is IEnumerable<TextCharacter> characters)
        {
            overlay.SetCharacters(characters);
        }
    }

    private static void OnAnnotationsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnnotationOverlay overlay)
        {
            if (e.OldValue is INotifyCollectionChanged oldColl)
            {
                oldColl.CollectionChanged -= overlay.OnAnnotationsCollectionChanged;
            }
            if (e.NewValue is INotifyCollectionChanged newColl)
            {
                newColl.CollectionChanged += overlay.OnAnnotationsCollectionChanged;
            }
            overlay.RedrawAnnotations();
        }
    }

    private void OnAnnotationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawAnnotations();
    }

    private void RedrawAnnotations()
    {
        AnnotationsLayer.Children.Clear();
        
        if (AnnotationsSource == null || PageIndex < 0) return;

        var pageAnnotations = AnnotationsSource.Where(a => a.PageIndex == PageIndex);

        foreach (var annotation in pageAnnotations)
        {
            switch (annotation)
            {
                case HighlightAnnotation highlight:
                    DrawHighlight(highlight);
                    break;
                case UnderlineAnnotation underline:
                    DrawUnderline(underline);
                    break;
                case StickyNoteAnnotation note:
                    DrawStickyNote(note);
                    break;
            }
        }
    }

    private void DrawHighlight(HighlightAnnotation highlight)
    {
        var color = (Color)ColorConverter.ConvertFromString(highlight.Color);
        var brush = new SolidColorBrush(color);

        foreach (var rect in highlight.Rects)
        {
            var r = new Rectangle
            {
                Width = rect.Width * Scale,
                Height = rect.Height * Scale,
                Fill = brush
            };
            Canvas.SetLeft(r, rect.X * Scale);
            Canvas.SetTop(r, rect.Y * Scale);
            AnnotationsLayer.Children.Add(r);
        }
    }

    private void DrawUnderline(UnderlineAnnotation underline)
    {
        var color = (Color)ColorConverter.ConvertFromString(underline.Color);
        var brush = new SolidColorBrush(color);

        foreach (var rect in underline.Rects)
        {
            var line = new Rectangle
            {
                Width = rect.Width * Scale,
                Height = 1 * Scale, // Thickness
                Fill = brush
            };
            Canvas.SetLeft(line, rect.X * Scale);
            Canvas.SetTop(line, (rect.Y + rect.Height) * Scale - line.Height);
            AnnotationsLayer.Children.Add(line);
        }
    }

    private void DrawStickyNote(StickyNoteAnnotation note)
    {
        // Simple sticky note icon
        var icon = new Border
        {
            Width = 20,
            Height = 20,
            Background = Brushes.Yellow,
            BorderBrush = Brushes.Orange,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ToolTip = note.Content
        };
        
        Canvas.SetLeft(icon, note.X * Scale);
        Canvas.SetTop(icon, note.Y * Scale);
        AnnotationsLayer.Children.Add(icon);
    }
    
    public static readonly RoutedEvent TextSelectionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(TextSelectionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnnotationOverlay));

    public event RoutedEventHandler TextSelectionChanged
    {
        add => AddHandler(TextSelectionChangedEvent, value);
        remove => RemoveHandler(TextSelectionChangedEvent, value);
    }

    public AnnotationOverlay()
    {
        InitializeComponent();
    }

    public void SetCharacters(IEnumerable<TextCharacter> characters)
    {
        _pageCharacters.Clear();
        _pageCharacters.AddRange(characters);
        
        // Build word/line hierarchy
        BuildTextHierarchy();
        
        // Build spatial index
        RebuildSpatialGrid();
    }
    
    /// <summary>
    /// Groups characters into words and words into lines based on spatial proximity.
    /// Filters out invalid/whitespace characters to prevent selection artifacts.
    /// </summary>
    private void BuildTextHierarchy()
    {
        _pageWords.Clear();
        _pageLines.Clear();
        
        if (_pageCharacters.Count == 0) return;
        
        // Filter out whitespace and invalid-sized characters
        var validChars = _pageCharacters
            .Where(c => !char.IsWhiteSpace(c.Char) && 
                       c.Right > c.Left && 
                       c.Bottom > c.Top &&
                       (c.Right - c.Left) < 200 &&  // Filter unreasonably wide chars
                       (c.Bottom - c.Top) < 200)    // Filter unreasonably tall chars
            .ToList();
        
        if (validChars.Count == 0) return;
        
        // Group by approximate Y position (lines), then sort by X within line
        var lineGroups = validChars
            .GroupBy(c => (int)(c.Top / 10) * 10)  // Group by Y in 10px buckets
            .OrderBy(g => g.Key)
            .ToList();
        
        foreach (var lineGroup in lineGroups)
        {
            var lineChars = lineGroup.OrderBy(c => c.Left).ToList();
            var currentLine = new TextLine();
            TextWord? currentWord = null;
            double lastRight = double.MinValue;
            
            foreach (var ch in lineChars)
            {
                // Detect word break: gap > char width or gap > threshold
                double avgCharWidth = ch.Right - ch.Left;
                double gap = ch.Left - lastRight;
                bool isNewWord = currentWord == null || 
                                gap > Math.Max(avgCharWidth * 0.5, 5);
                
                if (isNewWord && currentWord != null && currentWord.Characters.Count > 0)
                {
                    _pageWords.Add(currentWord);
                    currentLine.AddWord(currentWord);
                    currentWord = null;
                }
                
                currentWord ??= new TextWord();
                currentWord.AddCharacter(ch);
                lastRight = ch.Right;
            }
            
            // Add final word of line
            if (currentWord != null && currentWord.Characters.Count > 0)
            {
                _pageWords.Add(currentWord);
                currentLine.AddWord(currentWord);
            }
            
            if (currentLine.Words.Count > 0)
            {
                _pageLines.Add(currentLine);
            }
        }
    }
    
    /// <summary>
    /// Rebuilds spatial grid using CHARACTERS for precise hit-testing.
    /// Word bounds are used for selection snapping, character bounds for cursor/hit accuracy.
    /// </summary>
    private void RebuildSpatialGrid()
    {
        _charSpatialGrid = null;
        _wordSpatialGrid = null;
        
        if (_pageCharacters.Count == 0) return;
        
        // Filter valid characters
        var validChars = _pageCharacters
            .Where(c => !char.IsWhiteSpace(c.Char) && 
                       c.Right > c.Left && 
                       c.Bottom > c.Top)
            .ToList();
        
        if (validChars.Count == 0) return;
        
        // Determine page bounds
        double maxX = validChars.Max(c => c.Right) + 50;
        double maxY = validChars.Max(c => c.Bottom) + 50;
        
        // Build character grid for hit-testing
        _charSpatialGrid = new SpatialGrid<TextCharacter>(maxX, maxY, 30);
        foreach (var ch in validChars)
        {
            var rect = new Rect(ch.Left, ch.Top, ch.Right - ch.Left, ch.Bottom - ch.Top);
            _charSpatialGrid.Insert(ch, rect);
        }
        
        // Build word grid for selection snapping
        if (_pageWords.Count > 0)
        {
            _wordSpatialGrid = new SpatialGrid<TextWord>(maxX, maxY, 50);
            foreach (var word in _pageWords)
            {
                _wordSpatialGrid.Insert(word, word.Bounds);
            }
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var clickPoint = e.GetPosition(this);
        var now = DateTime.Now;
        
        // Check for double-click (word selection)
        bool isDoubleClick = (now - _lastClickTime).TotalMilliseconds < DoubleClickTimeMs &&
                            (clickPoint - _lastClickPoint).Length < DoubleClickDistance;
        
        _lastClickTime = now;
        _lastClickPoint = clickPoint;
        
        if (isDoubleClick)
        {
            // Select entire word under cursor
            SelectWordAt(clickPoint);
            return;
        }
        
        _startPoint = clickPoint;
        _isSelecting = true;
        ClearSelection();
        CaptureMouse();
        
        // Notify that selection is cleared
        RaiseEvent(new AnnotationSelectionEventArgs(TextSelectionChangedEvent, [], _startPoint.Value));
    }
    
    /// <summary>
    /// Selects the entire word at the given point (for double-click).
    /// </summary>
    private void SelectWordAt(Point point)
    {
        if (_wordSpatialGrid == null) return;
        
        var word = _wordSpatialGrid.Query(point)
            .FirstOrDefault(w => w.Bounds.Contains(point));
        
        if (word != null)
        {
            _selectedCharacters.Clear();
            _selectedCharacters.AddRange(word.Characters);
            UpdateSelectionVisual();
            RaiseEvent(new AnnotationSelectionEventArgs(TextSelectionChangedEvent, _selectedCharacters.ToList(), point));
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(this);
        // System.Diagnostics.Debug.WriteLine($"Mouse: {currentPoint}");

        if (_isSelecting && _startPoint.HasValue)
        {
            UpdateSelection(_startPoint.Value, currentPoint);
        }
        else
        {
            UpdateCursor(currentPoint);
        }
    }

    private void UpdateCursor(Point point)
    {
        // O(1) spatial grid query using character bounds for precision
        bool isOverText = _charSpatialGrid?.Query(point)
            .Any(c => point.X >= c.Left && point.X <= c.Right && 
                     point.Y >= c.Top && point.Y <= c.Bottom) ?? false;
        
        Cursor = isOverText ? Cursors.IBeam : Cursors.Arrow;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            
            var endPoint = e.GetPosition(this);
            
            if (_selectedCharacters.Count > 0)
            {
                RaiseEvent(new AnnotationSelectionEventArgs(TextSelectionChangedEvent, _selectedCharacters.ToList(), endPoint));
            }
            else
            {
                // Ensure cleared state is communicated if user just clicked without selecting
                RaiseEvent(new AnnotationSelectionEventArgs(TextSelectionChangedEvent, [], endPoint));
            }
        }
    }

    public void ClearSelection()
    {
        _selectedCharacters.Clear();
        _selectionVisual?.Clear();
    }

    private void UpdateSelection(Point start, Point end)
    {
        // 1. Calculate selection rectangle in view coordinates
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        
        var selectionRect = new Rect(x, y, width, height);
        
        // 2. Clear previous selection
        _selectedCharacters.Clear();
        
        // 3. Adobe-style: word-based selection
        //    Find all words that intersect the selection rect
        if (_wordSpatialGrid != null)
        {
            var candidateWords = _wordSpatialGrid.Query(selectionRect)
                .Where(w => selectionRect.IntersectsWith(w.Bounds))
                .OrderBy(w => w.Bounds.Top)
                .ThenBy(w => w.Bounds.Left)
                .ToList();
            
            foreach (var word in candidateWords)
            {
                _selectedCharacters.AddRange(word.Characters);
            }
        }
        
        // 4. Efficient rendering via DrawingVisual
        UpdateSelectionVisual();
    }
    
    /// <summary>
    /// Updates the DrawingVisual-based selection rendering.
    /// </summary>
    private void UpdateSelectionVisual()
    {
        // Initialize SelectionVisual on first use
        if (_selectionVisual == null)
        {
            _selectionVisual = new SelectionVisual();
            // Add to visual tree via the SelectionLayer canvas
            // We need to host the DrawingVisual in a VisualHost
            var host = new SelectionVisualHost(_selectionVisual);
            SelectionLayer.Children.Clear();
            SelectionLayer.Children.Add(host);
        }
        
        // Build rects from selected characters
        var rects = _selectedCharacters.Select(c => 
            new Rect(c.Left, c.Top, c.Right - c.Left, c.Bottom - c.Top));
        
        _selectionVisual.Update(rects, _selectionBrush);
    }
}

public class AnnotationSelectionEventArgs : RoutedEventArgs
{
    public List<TextCharacter> SelectedCharacters { get; }
    public Point Position { get; }

    public AnnotationSelectionEventArgs(RoutedEvent routedEvent, List<TextCharacter> selectedCharacters, Point position) 
        : base(routedEvent)
    {
        SelectedCharacters = selectedCharacters;
        Position = position;
    }
}
