using System.Collections.ObjectModel;
using RedPDF.Models;

namespace RedPDF.Services;

/// <summary>
/// Interface for managing PDF annotations.
/// </summary>
public interface IAnnotationService
{
    /// <summary>
    /// Gets all annotations for the current document.
    /// </summary>
    ObservableCollection<Annotation> Annotations { get; }

    /// <summary>
    /// Adds an annotation to the current document.
    /// </summary>
    void AddAnnotation(Annotation annotation);

    /// <summary>
    /// Removes an annotation by ID.
    /// </summary>
    bool RemoveAnnotation(Guid id);

    /// <summary>
    /// Gets annotations for a specific page.
    /// </summary>
    IEnumerable<Annotation> GetAnnotationsForPage(int pageIndex);

    /// <summary>
    /// Clears all annotations.
    /// </summary>
    void ClearAnnotations();

    /// <summary>
    /// Saves annotations to a new PDF file using PDFsharp.
    /// </summary>
    /// <param name="inputPath">Source PDF path.</param>
    /// <param name="outputPath">Output PDF path with embedded annotations.</param>
    Task SaveToPdfAsync(string inputPath, string outputPath);

    /// <summary>
    /// Event raised when annotations change.
    /// </summary>
    event EventHandler? AnnotationsChanged;
}
