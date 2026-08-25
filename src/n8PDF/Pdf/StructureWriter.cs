using n8PDF.Layout;

namespace n8PDF.Pdf;

/// <summary>
/// Writes the structure tree a tagged PDF carries (#67): the elements layout allocated in
/// reading order, the marked-content sequences that tie the page's ink to them, the parent
/// tree that lets a reader walk from either side, and the language on the catalogue.
/// </summary>
/// <remarks>
/// Layout owns the structure — what is a heading, what is a cell, what order it reads in — and
/// this owns only the plumbing: MCIDs per page, MCR kids per element, /StructParents per page
/// and /StructParent per link annotation. Elements that end up with no ink and no children are
/// pruned rather than written empty. Reading order in the tree is the order layout allocated
/// the elements, which is document order — the same order the drawing itself is asserted to
/// follow by ContentCoverageTests.
/// </remarks>
internal sealed class StructureWriter(LaidOutDocument document, PdfBuilder builder)
{
    private abstract record Kid;

    private sealed record MarkedKid(int PageIndex, int Mcid) : Kid;

    private sealed record AnnotationKid(PdfReference Annotation, int Key) : Kid;

    private readonly List<List<Kid>> _kids =
        [.. document.Structure.Select(_ => new List<Kid>())];

    private readonly List<List<int>> _pageElements = [];
    private readonly List<PdfPage> _pages = [];
    private readonly List<(int Parent, PdfReference Annotation)> _links = [];

    private int _pageIndex = -1;

    public bool Enabled { get; } = document.Structure.Count > 0;

    /// <summary>Begins a page: its MCIDs start again from nought.</summary>
    public void StartPage(PdfPage page)
    {
        _pageIndex++;
        _pages.Add(page);
        _pageElements.Add([]);
    }

    /// <summary>
    /// Opens a marked-content sequence for the given element — or an artifact where the ink is
    /// decoration rather than content. Close it with <see cref="ContentStreamBuilder.EndMarked"/>.
    /// </summary>
    public void Begin(ContentStreamBuilder content, int structureIndex)
    {
        if (structureIndex < 0 || structureIndex >= _kids.Count)
        {
            content.BeginArtifact();
            return;
        }

        var mcid = _pageElements[_pageIndex].Count;
        _pageElements[_pageIndex].Add(structureIndex);
        _kids[structureIndex].Add(new MarkedKid(_pageIndex, mcid));

        content.BeginMarked(Role(document.Structure[structureIndex].Kind,
            document.Structure[structureIndex].Level), mcid);
    }

    /// <summary>
    /// A link annotation's place in the tree: a Link element under the annotated text's own
    /// element, and the parent-tree key its <c>/StructParent</c> names.
    /// </summary>
    public int AddLink(int parentElement, PdfReference annotation)
    {
        _links.Add((parentElement, annotation));
        return _pages.Count + _links.Count - 1 + PageKeySpace;
    }

    // Page keys are the page indices; link keys start above every possible page.
    private const int PageKeySpace = 0;

    /// <summary>Assembles and writes the whole tree, and hangs it off the catalogue.</summary>
    public void Write()
    {
        if (!Enabled) return;

        var pdf = builder.Document;
        var elements = document.Structure;

        // The Link elements join the tree as extra entries after layout's own.
        var linkParents = new int[_links.Count];
        var linkKids = new List<Kid>[_links.Count];

        for (var i = 0; i < _links.Count; i++)
        {
            linkParents[i] = _links[i].Parent;
            linkKids[i] = [new AnnotationKid(_links[i].Annotation, _pages.Count + i)];
        }

        // An element earns its place by carrying ink, an annotation, or a child that does.
        var keep = new bool[elements.Count + _links.Count];

        for (var i = 0; i < elements.Count; i++)
            keep[i] = _kids[i].Count > 0;

        for (var i = 0; i < _links.Count; i++)
            keep[elements.Count + i] = true;

        for (var i = elements.Count + _links.Count - 1; i >= 0; i--)
        {
            if (!keep[i]) continue;

            var parent = i >= elements.Count ? linkParents[i - elements.Count] : elements[i].Parent;
            if (parent >= 0) keep[parent] = true;
        }

        if (!keep.Any(kept => kept)) return;

        var rootRef = pdf.Reserve();
        var documentRef = pdf.Reserve();

        var refs = new PdfReference?[keep.Length];
        for (var i = 0; i < keep.Length; i++)
            if (keep[i])
                refs[i] = pdf.Reserve();

        PdfReference ParentRef(int parent) =>
            parent >= 0 && refs[parent] is { } kept ? kept : documentRef;

        var childrenOf = new Dictionary<PdfReference, PdfArray>();
        var kidsByIndex = new PdfArray[keep.Length];

        for (var i = 0; i < keep.Length; i++)
        {
            if (refs[i] is not { } elementRef) continue;

            var isLink = i >= elements.Count;
            var parentRef = ParentRef(isLink ? linkParents[i - elements.Count] : elements[i].Parent);

            var dictionary = new PdfDictionary()
                .Set("Type", "StructElem")
                .Set("S", isLink
                    ? "Link"
                    : Role(elements[i].Kind, elements[i].Level))
                .Set("P", parentRef);

            if (!isLink && elements[i].Alt is { } alt)
                dictionary.Set("Alt", PdfString.FromText(alt));

            var kids = new PdfArray();

            foreach (var kid in isLink ? linkKids[i - elements.Count] : _kids[i])
            {
                kids.Add(kid switch
                {
                    MarkedKid marked => new PdfDictionary()
                        .Set("Type", "MCR")
                        .Set("Pg", pdf.GetPageReference(marked.PageIndex))
                        .Set("MCID", marked.Mcid),
                    AnnotationKid annotation => new PdfDictionary()
                        .Set("Type", "OBJR")
                        .Set("Obj", annotation.Annotation),
                    _ => PdfNull.Instance
                });
            }

            dictionary.Set("K", kids);
            pdf.Assign(elementRef, dictionary);
            kidsByIndex[i] = kids;

            if (!childrenOf.TryGetValue(parentRef, out var siblings))
                childrenOf[parentRef] = siblings = new PdfArray();

            // Reading order: elements were allocated in document order, and links were
            // allocated as their text was annotated, so index order is the order to keep.
            siblings.Add(elementRef);
        }

        // Children arrays: an element that got children carries them after its marked kids.
        // The dictionary and its marked kids were built once above; here we only append the child
        // refs to that same array, which the assigned dictionary still holds (#223).
        for (var i = 0; i < keep.Length; i++)
        {
            if (refs[i] is not { } elementRef) continue;
            if (!childrenOf.TryGetValue(elementRef, out var children)) continue;

            foreach (var child in Items(children)) kidsByIndex[i].Add(child);
        }

        // The parent tree: for each page, the element of every MCID in order; for each link
        // annotation, its element directly.
        var nums = new PdfArray();

        for (var page = 0; page < _pages.Count; page++)
        {
            var byMcid = new PdfArray();
            foreach (var element in _pageElements[page])
                byMcid.Add(refs[element] is { } elementRef ? elementRef : PdfNull.Instance);

            nums.Add(page);
            nums.Add(pdf.Add(byMcid));
            _pages[page].Dictionary.Set("StructParents", page);
        }

        for (var i = 0; i < _links.Count; i++)
        {
            nums.Add(_pages.Count + i);
            nums.Add(refs[elements.Count + i]!);
        }

        var documentChildren = new PdfArray();
        if (childrenOf.TryGetValue(documentRef, out var top))
            foreach (var child in Items(top))
                documentChildren.Add(child);

        pdf.Assign(documentRef, new PdfDictionary()
            .Set("Type", "StructElem")
            .Set("S", "Document")
            .Set("P", rootRef)
            .Set("K", documentChildren));

        pdf.Assign(rootRef, new PdfDictionary()
            .Set("Type", "StructTreeRoot")
            .Set("K", documentRef)
            .Set("ParentTree", new PdfDictionary().Set("Nums", nums))
            .Set("ParentTreeNextKey", _pages.Count + _links.Count));

        pdf.SetStructure(rootRef);

        if (document.Language is { } language) pdf.SetLanguage(language);
    }

    private static IEnumerable<PdfObject> Items(PdfArray array)
    {
        for (var i = 0; i < array.Count; i++) yield return array[i];
    }

    private static string Role(StructureKind kind, int level) => kind switch
    {
        StructureKind.Heading => "H" + Math.Clamp(level, 1, 6),
        StructureKind.List => "L",
        StructureKind.ListItem => "LI",
        StructureKind.Table => "Table",
        StructureKind.TableRow => "TR",
        StructureKind.TableCell => "TD",
        StructureKind.Figure => "Figure",
        _ => "P"
    };
}
