namespace n8PDF.Layout;

/// <summary>
/// Where the fields of a document fell, learned by laying it out once.
/// </summary>
/// <remarks>
/// A page number cannot be known while the page it is on is still being filled, so a document
/// holding one is laid out twice: the first pass says which page each field landed on and how the
/// pages divide between sections, and the second uses it. Word settles its own page numbers the
/// same way, and like Word this converges rather than being exact — a field whose text changes
/// length between the passes can in principle move to another page, and is then a page out.
/// </remarks>
/// <param name="TotalPages">How many pages the document came to.</param>
/// <param name="Pages">The page each field landed on, by occurrence, counting from one.</param>
/// <param name="Sections">The section each page belongs to, counting from one.</param>
/// <param name="Counts">How many pages each section holds, in section order.</param>
/// <param name="Headings">
/// The page each heading landed on, counting from one — what a table of contents needs, and the
/// reason a document holding one is laid out twice.
/// </param>
/// <param name="Marks">
/// The page each index entry was marked on. An XE field draws nothing, so where it fell is only
/// known from the paragraph that carried it.
/// </param>
public sealed record FieldPagination(
    int TotalPages,
    IReadOnlyDictionary<int, int> Pages,
    IReadOnlyList<int> Sections,
    IReadOnlyList<int> Counts,
    IReadOnlyDictionary<Ooxml.Paragraph, int> Headings,
    IReadOnlyDictionary<Ooxml.FieldInline, int> Marks)
{
    /// <summary>The page a heading landed on, or zero where it was not recorded.</summary>
    public int PageOfHeading(Ooxml.Paragraph paragraph) => Headings.GetValueOrDefault(paragraph);

    /// <summary>The page an index entry was marked on, or zero where it was not recorded.</summary>
    public int PageOfMark(Ooxml.FieldInline mark) => Marks.GetValueOrDefault(mark);

    /// <summary>The page a field landed on, or zero where it was not recorded.</summary>
    public int PageOfField(int occurrence) => Pages.GetValueOrDefault(occurrence);

    /// <summary>The section a page belongs to, or zero where the page is not known.</summary>
    public int SectionOfPage(int page) =>
        page >= 1 && page <= Sections.Count ? Sections[page - 1] : 0;

    /// <summary>How many pages a section holds, or zero where there is no such section.</summary>
    public int PagesInSection(int section) =>
        section >= 1 && section <= Counts.Count ? Counts[section - 1] : 0;
}
