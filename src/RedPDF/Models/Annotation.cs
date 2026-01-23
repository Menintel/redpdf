namespace RedPDF.Models;

/// <summary>
/// Type of annotation.
/// </summary>
public enum AnnotationType
{
    Highlight,
    Underline,
    StickyNote
}

/// <summary>
/// Base class for all PDF annotations.
/// </summary>
public abstract class Annotation
{
    /// <summary>
    /// Unique identifier for the annotation.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Zero-based page index where annotation is located.
    /// </summary>
    public required int PageIndex { get; init; }

    /// <summary>
    /// Type of annotation.
    /// </summary>
    public abstract AnnotationType Type { get; }

    /// <summary>
    /// When the annotation was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// When the annotation was last modified.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Rectangle bounds for annotations (in PDF coordinates).
/// </summary>
public readonly struct AnnotationRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public AnnotationRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// Highlight annotation (colored rectangle over text).
/// </summary>
public class HighlightAnnotation : Annotation
{
    public override AnnotationType Type => AnnotationType.Highlight;

    /// <summary>
    /// Rectangles covering the highlighted text.
    /// </summary>
    public required List<AnnotationRect> Rects { get; init; }

    /// <summary>
    /// Highlight color in ARGB format (default: yellow).
    /// </summary>
    public string Color { get; set; } = "#80FFFF00"; // Semi-transparent yellow
}

/// <summary>
/// Underline annotation.
/// </summary>
public class UnderlineAnnotation : Annotation
{
    public override AnnotationType Type => AnnotationType.Underline;

    /// <summary>
    /// Rectangles covering the underlined text.
    /// </summary>
    public required List<AnnotationRect> Rects { get; init; }

    /// <summary>
    /// Underline color (default: red).
    /// </summary>
    public string Color { get; set; } = "#FFFF0000";
}

/// <summary>
/// Sticky note annotation.
/// </summary>
public class StickyNoteAnnotation : Annotation
{
    public override AnnotationType Type => AnnotationType.StickyNote;

    /// <summary>
    /// Position of the note icon (PDF coordinates).
    /// </summary>
    public required double X { get; init; }
    public required double Y { get; init; }

    /// <summary>
    /// Note content text.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether the note popup is expanded.
    /// </summary>
    public bool IsExpanded { get; set; }
}
