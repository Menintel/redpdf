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
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _isSelecting = true;
        ClearSelection();
        CaptureMouse();
        
        // Notify that selection is cleared
        RaiseEvent(new AnnotationSelectionEventArgs(TextSelectionChangedEvent, [], _startPoint.Value));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(this);

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
        bool isOverText = false;
        
        if (_pageCharacters.Count > 0 && Scale > 0)
        {
            foreach (var c in _pageCharacters)
            {
                if (point.X >= c.Left * Scale && point.X <= c.Right * Scale &&
                    point.Y >= c.Top * Scale && point.Y <= c.Bottom * Scale)
                {
                    isOverText = true;
                    break;
                }
            }
        }
        
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
        SelectionLayer.Children.Clear();
    }

    private void UpdateSelection(Point start, Point end)
    {
        // 1. Calculate selection rectangle in view coordinates
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        
        var selectionRect = new Rect(x, y, width, height);
        
        // 2. Find intersecting characters
        _selectedCharacters.Clear();
        SelectionLayer.Children.Clear();

        foreach (var charInfo in _pageCharacters)
        {
            double charLeft = charInfo.Left * Scale;
            double charTop = charInfo.Top * Scale;
            double charRight = charInfo.Right * Scale;
            double charBottom = charInfo.Bottom * Scale;
            
            Rect charRect = new Rect(charLeft, charTop, charRight - charLeft, charBottom - charTop);
            
            if (selectionRect.IntersectsWith(charRect))
            {
                _selectedCharacters.Add(charInfo);
                
                // Draw highlight rect
                var rect = new Rectangle
                {
                    Fill = _selectionBrush,
                    Width = charRect.Width,
                    Height = charRect.Height
                };
                Canvas.SetLeft(rect, charRect.Left);
                Canvas.SetTop(rect, charRect.Top);
                SelectionLayer.Children.Add(rect);
            }
        }
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
