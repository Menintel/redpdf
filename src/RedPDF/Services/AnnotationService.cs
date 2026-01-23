using System.Collections.ObjectModel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using RedPDF.Models;

namespace RedPDF.Services;

/// <summary>
/// Annotation service that embeds annotations directly into PDF using PDFsharp.
/// </summary>
public class AnnotationService : IAnnotationService
{
    private readonly ObservableCollection<Annotation> _annotations = [];

    public ObservableCollection<Annotation> Annotations => _annotations;

    public event EventHandler? AnnotationsChanged;

    public void AddAnnotation(Annotation annotation)
    {
        _annotations.Add(annotation);
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveAnnotation(Guid id)
    {
        var annotation = _annotations.FirstOrDefault(a => a.Id == id);
        if (annotation != null)
        {
            _annotations.Remove(annotation);
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    public IEnumerable<Annotation> GetAnnotationsForPage(int pageIndex)
    {
        return _annotations.Where(a => a.PageIndex == pageIndex);
    }

    public void ClearAnnotations()
    {
        _annotations.Clear();
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveToPdfAsync(string inputPath, string outputPath)
    {
        await Task.Run(() =>
        {
            // Open the existing PDF
            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            // Group annotations by page
            var annotationsByPage = _annotations.GroupBy(a => a.PageIndex);

            foreach (var pageGroup in annotationsByPage)
            {
                int pageIndex = pageGroup.Key;
                if (pageIndex >= 0 && pageIndex < document.PageCount)
                {
                    var page = document.Pages[pageIndex];
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                    foreach (var annotation in pageGroup)
                    {
                        DrawAnnotation(gfx, page, annotation);
                    }
                }
            }

            // Save to output path
            document.Save(outputPath);
        });
    }

    private static void DrawAnnotation(XGraphics gfx, PdfPage page, Annotation annotation)
    {
        switch (annotation)
        {
            case HighlightAnnotation highlight:
                DrawHighlight(gfx, page, highlight);
                break;
            case UnderlineAnnotation underline:
                DrawUnderline(gfx, page, underline);
                break;
            case StickyNoteAnnotation stickyNote:
                DrawStickyNote(gfx, page, stickyNote);
                break;
        }
    }

    private static void DrawHighlight(XGraphics gfx, PdfPage page, HighlightAnnotation highlight)
    {
        var color = ParseColor(highlight.Color);
        var brush = new XSolidBrush(color);

        foreach (var rect in highlight.Rects)
        {
            // PDF coordinates: origin at bottom-left, need to flip Y
            double y = page.Height.Point - rect.Y - rect.Height;
            gfx.DrawRectangle(brush, rect.X, y, rect.Width, rect.Height);
        }
    }

    private static void DrawUnderline(XGraphics gfx, PdfPage page, UnderlineAnnotation underline)
    {
        var color = ParseColor(underline.Color);
        var pen = new XPen(color, 1);

        foreach (var rect in underline.Rects)
        {
            // Draw line at bottom of rect
            double y = page.Height.Point - rect.Y;
            gfx.DrawLine(pen, rect.X, y, rect.X + rect.Width, y);
        }
    }

    private static void DrawStickyNote(XGraphics gfx, PdfPage page, StickyNoteAnnotation note)
    {
        // Draw a small yellow note icon
        var color = XColor.FromArgb(255, 255, 255, 0); // Yellow
        var brush = new XSolidBrush(color);
        var pen = new XPen(XColors.Orange, 1);

        double y = page.Height.Point - note.Y - 20;
        double x = note.X;

        // Draw note rectangle
        gfx.DrawRectangle(pen, brush, x, y, 20, 20);

        // Draw lines on note
        var linePen = new XPen(XColors.Orange, 0.5);
        gfx.DrawLine(linePen, x + 3, y + 6, x + 17, y + 6);
        gfx.DrawLine(linePen, x + 3, y + 10, x + 17, y + 10);
        gfx.DrawLine(linePen, x + 3, y + 14, x + 12, y + 14);
    }

    private static XColor ParseColor(string colorString)
    {
        // Parse #AARRGGBB format
        if (colorString.StartsWith("#") && colorString.Length == 9)
        {
            byte a = Convert.ToByte(colorString.Substring(1, 2), 16);
            byte r = Convert.ToByte(colorString.Substring(3, 2), 16);
            byte g = Convert.ToByte(colorString.Substring(5, 2), 16);
            byte b = Convert.ToByte(colorString.Substring(7, 2), 16);
            return XColor.FromArgb(a, r, g, b);
        }
        // Parse #RRGGBB format
        if (colorString.StartsWith("#") && colorString.Length == 7)
        {
            byte r = Convert.ToByte(colorString.Substring(1, 2), 16);
            byte g = Convert.ToByte(colorString.Substring(3, 2), 16);
            byte b = Convert.ToByte(colorString.Substring(5, 2), 16);
            return XColor.FromArgb(255, r, g, b);
        }
        return XColors.Yellow;
    }
}
