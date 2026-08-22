using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace n8PDF.Tests.Support;

/// <summary>
/// Assembles a minimal but valid <c>.docx</c> in memory.
/// </summary>
/// <remarks>
/// Hand-authored fixtures let a test exercise one feature in isolation, which real Word documents
/// cannot do — they always bring dozens of settings along. The markup here mirrors what Word
/// emits, so fixtures stay representative even though they are synthetic. Real Word documents in
/// Fixtures/Real are what catch the quirks this cannot.
/// </remarks>
public sealed class DocxBuilder
{
    private readonly StringBuilder _body = new();
    private string _sectionProperties = DefaultSection;
    private string _styles = DefaultStyles;

    // The docProps parts, written only when a fixture asks for them: a document without them is
    // what Word writes least often but what a hand-written fixture is by default.
    private string? _coreProperties;
    private string? _appProperties;
    private string? _customProperties;
    private string _theme = DefaultTheme;

    private const string DefaultSection = """
        <w:sectPr>
          <w:pgSz w:w="12240" w:h="15840"/>
          <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
          <w:cols w:space="720"/>
        </w:sectPr>
        """;

    /// <summary>Sets the section properties, for page size and margin fixtures.</summary>
    public DocxBuilder WithSection(string sectionPropertiesXml)
    {
        _sectionProperties = sectionPropertiesXml;
        return this;
    }

    /// <summary>Sets page size and margins in twips.</summary>
    public DocxBuilder WithPage(
        int widthTwips = 12240, int heightTwips = 15840,
        int top = 1440, int right = 1440, int bottom = 1440, int left = 1440)
    {
        _sectionProperties = $"""
            <w:sectPr>
              <w:pgSz w:w="{widthTwips}" w:h="{heightTwips}"/>
              <w:pgMar w:top="{top}" w:right="{right}" w:bottom="{bottom}" w:left="{left}" w:header="720" w:footer="720" w:gutter="0"/>
              <w:cols w:space="720"/>
            </w:sectPr>
            """;
        return this;
    }

    /// <summary>
    /// Gives the document the properties Word keeps in its docProps parts, which is where the
    /// fields naming a document's author, title or dates take their values from.
    /// </summary>
    /// <param name="custom">
    /// Custom properties, which DOCPROPERTY reads by name. They live in a part of their own.
    /// </param>
    public DocxBuilder WithDocumentProperties(
        string? title = null,
        string? subject = null,
        string? creator = null,
        string? keywords = null,
        string? description = null,
        string? lastModifiedBy = null,
        string? created = null,
        string? modified = null,
        string? lastPrinted = null,
        string? company = null,
        string? manager = null,
        params (string Name, string Value)[] custom)
    {
        _coreProperties =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<cp:coreProperties " +
            "xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
            "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            Element("dc:title", title) +
            Element("dc:subject", subject) +
            Element("dc:creator", creator) +
            Element("cp:keywords", keywords) +
            Element("dc:description", description) +
            Element("cp:lastModifiedBy", lastModifiedBy) +
            (created is null
                ? string.Empty
                : $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{Escape(created)}</dcterms:created>") +
            (modified is null
                ? string.Empty
                : $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{Escape(modified)}</dcterms:modified>") +
            (lastPrinted is null
                ? string.Empty
                : $"<cp:lastPrinted>{Escape(lastPrinted)}</cp:lastPrinted>") +
            "</cp:coreProperties>";

        _appProperties =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">" +
            Element("Company", company) +
            Element("Manager", manager) +
            "</Properties>";

        if (custom.Length > 0)
        {
            var properties = new StringBuilder();
            var id = 1;

            foreach (var (name, value) in custom)
            {
                // Custom properties are numbered from two: one is reserved by the format.
                properties.Append(
                    "<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" " +
                    $"pid=\"{++id}\" name=\"{Escape(name)}\">" +
                    $"<vt:lpwstr>{Escape(value)}</vt:lpwstr></property>");
            }

            _customProperties =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" " +
                "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                properties + "</Properties>";
        }

        return this;
    }

    private static string Element(string name, string? value) =>
        value is null ? string.Empty : $"<{name}>{Escape(value)}</{name}>";

    public DocxBuilder WithStyles(string stylesXmlBody)
    {
        _styles = stylesXmlBody;
        return this;
    }

    public DocxBuilder WithTheme(string majorLatin, string minorLatin)
    {
        _theme = BuildTheme(majorLatin, minorLatin);
        return this;
    }

    /// <summary>Appends raw paragraph markup.</summary>
    public DocxBuilder AddRawParagraph(string paragraphXml)
    {
        _body.Append(paragraphXml);
        return this;
    }

    /// <summary>Appends a paragraph of plain text in one run.</summary>
    public DocxBuilder AddParagraph(string text, string? paragraphProperties = null, string? runProperties = null)
    {
        _body.Append("<w:p>");
        if (paragraphProperties is not null) _body.Append($"<w:pPr>{paragraphProperties}</w:pPr>");

        _body.Append("<w:r>");
        if (runProperties is not null) _body.Append($"<w:rPr>{runProperties}</w:rPr>");
        _body.Append($"<w:t xml:space=\"preserve\">{Escape(text)}</w:t>");
        _body.Append("</w:r></w:p>");

        return this;
    }

    /// <summary>Appends a paragraph built from several runs, each with its own properties.</summary>
    public DocxBuilder AddParagraphWithRuns(
        IEnumerable<(string Text, string? RunProperties)> runs, string? paragraphProperties = null)
    {
        _body.Append("<w:p>");
        if (paragraphProperties is not null) _body.Append($"<w:pPr>{paragraphProperties}</w:pPr>");

        foreach (var (text, runProperties) in runs)
        {
            _body.Append("<w:r>");
            if (runProperties is not null) _body.Append($"<w:rPr>{runProperties}</w:rPr>");
            _body.Append($"<w:t xml:space=\"preserve\">{Escape(text)}</w:t>");
            _body.Append("</w:r>");
        }

        _body.Append("</w:p>");
        return this;
    }

    /// <summary>
    /// Appends a paragraph that carries a section break.
    /// </summary>
    /// <remarks>
    /// A break is not a paragraph of its own: the properties of the section being closed hang off
    /// the last paragraph in it, which is how Word stores one. In CT_PPr the section properties
    /// come last, after everything else the paragraph declares.
    /// </remarks>
    public DocxBuilder AddParagraphWithSectionBreak(
        string text, string sectionPropertiesXml,
        string? paragraphProperties = null, string? runProperties = null)
    {
        _body.Append("<w:p><w:pPr>");
        _body.Append(paragraphProperties);
        _body.Append(sectionPropertiesXml);
        _body.Append("</w:pPr><w:r>");
        if (runProperties is not null) _body.Append($"<w:rPr>{runProperties}</w:rPr>");
        _body.Append($"<w:t xml:space=\"preserve\">{Escape(text)}</w:t>");
        _body.Append("</w:r></w:p>");

        return this;
    }

    /// <summary>Section properties for a break, in CT_SectPr order.</summary>
    public static string Section(
        IReadOnlyList<(string Kind, string Id)>? headerFooterReferences = null,
        string? type = null,
        int widthTwips = 12240, int heightTwips = 15840,
        int top = 1440, int right = 1440, int bottom = 1440, int left = 1440,
        bool landscape = false, bool titlePage = false, string? verticalAlignment = null,
        int columns = 1, int columnSpaceTwips = 720, bool columnSeparator = false,
        IReadOnlyList<(int Width, int Space)>? columnWidths = null,
        string? footnoteRestart = null, string? endnoteRestart = null,
        string? footnotePosition = null, string? endnotePosition = null,
        int? pageNumberStart = null, string? pageNumberFormat = null,
        string? pageBorders = null, string? lineNumbers = null)
    {
        var typeXml = type is null ? string.Empty : $"<w:type w:val=\"{type}\"/>";

        // Where the section's page numbering begins, and in what. In CT_SectPr this comes after
        // the paper and margins and before the columns.
        var pageNumbers = pageNumberStart is null && pageNumberFormat is null
            ? string.Empty
            : "<w:pgNumType" +
              (pageNumberFormat is null ? string.Empty : $" w:fmt=\"{pageNumberFormat}\"") +
              (pageNumberStart is null ? string.Empty : $" w:start=\"{pageNumberStart}\"") +
              "/>";

        // How the section numbers its notes, which comes after the references and before the
        // type. This is where Word's own Footnote and Endnote dialog writes it.
        // In CT_FtnProps the position comes before the numbering.
        var footnote = (footnotePosition is null ? string.Empty : $"<w:pos w:val=\"{footnotePosition}\"/>") +
            (footnoteRestart is null ? string.Empty : $"<w:numRestart w:val=\"{footnoteRestart}\"/>");

        var endnote = (endnotePosition is null ? string.Empty : $"<w:pos w:val=\"{endnotePosition}\"/>") +
            (endnoteRestart is null ? string.Empty : $"<w:numRestart w:val=\"{endnoteRestart}\"/>");

        var notes =
            (footnote.Length == 0 ? string.Empty : $"<w:footnotePr>{footnote}</w:footnotePr>") +
            (endnote.Length == 0 ? string.Empty : $"<w:endnotePr>{endnote}</w:endnotePr>");
        var orientation = landscape ? " w:orient=\"landscape\"" : string.Empty;

        // References come before everything else in CT_SectPr, headers before footers.
        var references = new StringBuilder();
        foreach (var (kind, id) in headerFooterReferences ?? [])
        {
            var parts = kind.Split(':');
            references.Append($"<w:{parts[0]}Reference w:type=\"{parts[1]}\" r:id=\"{id}\"/>");
        }

        return $"""
            <w:sectPr>
              {references}{notes}{typeXml}<w:pgSz w:w="{widthTwips}" w:h="{heightTwips}"{orientation}/>
              <w:pgMar w:top="{top}" w:right="{right}" w:bottom="{bottom}" w:left="{left}" w:header="720" w:footer="720" w:gutter="0"/>
              {pageBorders}{lineNumbers}{pageNumbers}{Columns(columns, columnSpaceTwips, columnSeparator, columnWidths)}{(verticalAlignment is null ? string.Empty : $"<w:vAlign w:val=\"{verticalAlignment}\"/>")}{(titlePage ? "<w:titlePg/>" : string.Empty)}
            </w:sectPr>
            """;
    }

    /// <summary>
    /// A <c>w:lnNumType</c> element: numbering down the margin, which in CT_SectPr comes between
    /// the page's border and its numbering.
    /// </summary>
    public static string LineNumbers(
        int? countBy = null, int? start = null, string? restart = null, int? distanceTwips = null) =>
        "<w:lnNumType" +
        (countBy is null ? string.Empty : $" w:countBy=\"{countBy}\"") +
        (start is null ? string.Empty : $" w:start=\"{start}\"") +
        (restart is null ? string.Empty : $" w:restart=\"{restart}\"") +
        (distanceTwips is null ? string.Empty : $" w:distance=\"{distanceTwips}\"") + "/>";

    /// <summary>
    /// A <c>w:pgBorders</c> element: the border round a page, which in CT_SectPr comes after the
    /// margins and before the page numbering.
    /// </summary>
    public static string PageBorders(
        string? offsetFrom = null, string? display = null, int size = 8, int space = 24,
        string color = "auto", string style = "single",
        bool top = true, bool left = true, bool bottom = true, bool right = true)
    {
        string Edge(string name, bool wanted) => wanted
            ? $"<w:{name} w:val=\"{style}\" w:sz=\"{size}\" w:space=\"{space}\" w:color=\"{color}\"/>"
            : string.Empty;

        return "<w:pgBorders" +
               (offsetFrom is null ? string.Empty : $" w:offsetFrom=\"{offsetFrom}\"") +
               (display is null ? string.Empty : $" w:display=\"{display}\"") + ">" +
               Edge("top", top) + Edge("left", left) + Edge("bottom", bottom) + Edge("right", right) +
               "</w:pgBorders>";
    }

    /// <summary>
    /// A <c>w:cols</c> element. Unequal columns are stated one by one and need equalWidth off,
    /// which is how Word writes them; the last column declares no space, having nothing after it.
    /// </summary>
    private static string Columns(
        int count, int spaceTwips, bool separator, IReadOnlyList<(int Width, int Space)>? widths)
    {
        var sep = separator ? " w:sep=\"1\"" : string.Empty;

        if (widths is null)
        {
            return count <= 1
                ? $"<w:cols w:space=\"{spaceTwips}\"{sep}/>"
                : $"<w:cols w:num=\"{count}\" w:space=\"{spaceTwips}\"{sep}/>";
        }

        var parts = new StringBuilder();
        for (var i = 0; i < widths.Count; i++)
        {
            var space = i == widths.Count - 1 ? string.Empty : $" w:space=\"{widths[i].Space}\"";
            parts.Append($"<w:col w:w=\"{widths[i].Width}\"{space}/>");
        }

        return $"<w:cols w:num=\"{widths.Count}\" w:space=\"{spaceTwips}\" w:equalWidth=\"0\"{sep}>" +
               parts + "</w:cols>";
    }

    public DocxBuilder AddEmptyParagraph()
    {
        _body.Append("<w:p/>");
        return this;
    }

    private readonly List<(int Id, string Type, string Body)> _footnotes = [];
    private readonly List<(int Id, string Type, string Body)> _endnotes = [];
    /// <summary>Header and footer parts the closing section deliberately does not point at.</summary>
    private readonly HashSet<string> _unreferenced = [];

    private string? _separatorMarkProperties;

    /// <summary>
    /// Formatting for the paragraph mark of the separator's paragraph. Must be set before the
    /// first note is added, since that is when the separators are created.
    /// </summary>
    public DocxBuilder WithFootnoteSeparatorMark(string runProperties)
    {
        _separatorMarkProperties = runProperties;
        return this;
    }

    /// <summary>
    /// Adds a footnote and returns the id its references use.
    /// </summary>
    /// <remarks>
    /// The first call also adds the two separator notes, because Word writes them into every notes
    /// part it creates and the rule above the notes comes from the first of them. The styles the
    /// notes are formatted with are added at the same time, again matching what Word puts in a
    /// document the moment a note is inserted.
    /// </remarks>
    public int AddFootnote(string paragraphsXml) =>
        AddNote(_footnotes, paragraphsXml, FootnoteStyles);

    /// <summary>Adds an endnote and returns the id its references use.</summary>
    public int AddEndnote(string paragraphsXml) =>
        AddNote(_endnotes, paragraphsXml, EndnoteStyles);

    private int AddNote(List<(int Id, string Type, string Body)> notes, string paragraphsXml, string styles)
    {
        if (notes.Count == 0)
        {
            const string spacing = "<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";
            // The mark's properties go last in CT_PPr, after the spacing.
            var mark = _separatorMarkProperties is null
                ? string.Empty
                : $"<w:rPr>{_separatorMarkProperties}</w:rPr>";

            notes.Add((-1, "separator",
                $"<w:p><w:pPr>{spacing}{mark}</w:pPr><w:r><w:separator/></w:r></w:p>"));
            notes.Add((0, "continuationSeparator",
                $"<w:p><w:pPr>{spacing}{mark}</w:pPr><w:r><w:continuationSeparator/></w:r></w:p>"));

            WithExtraStyles(styles);
        }

        var id = notes.Count - 1;
        notes.Add((id, "normal", paragraphsXml));
        return id;
    }

    /// <summary>The styles Word adds to a document when a footnote is inserted into it.</summary>
    private const string FootnoteStyles = """
        <w:style w:type="paragraph" w:styleId="FootnoteText">
          <w:name w:val="footnote text"/>
          <w:basedOn w:val="Normal"/>
          <w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>
          <w:rPr><w:sz w:val="20"/><w:szCs w:val="20"/></w:rPr>
        </w:style>
        <w:style w:type="character" w:styleId="FootnoteReference">
          <w:name w:val="footnote reference"/>
          <w:rPr><w:vertAlign w:val="superscript"/></w:rPr>
        </w:style>
        """;

    /// <summary>The same, for an endnote.</summary>
    private const string EndnoteStyles = """
        <w:style w:type="paragraph" w:styleId="EndnoteText">
          <w:name w:val="endnote text"/>
          <w:basedOn w:val="Normal"/>
          <w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>
          <w:rPr><w:sz w:val="20"/><w:szCs w:val="20"/></w:rPr>
        </w:style>
        <w:style w:type="character" w:styleId="EndnoteReference">
          <w:name w:val="endnote reference"/>
          <w:rPr><w:vertAlign w:val="superscript"/></w:rPr>
        </w:style>
        """;

    /// <summary>Appends style definitions to whatever styles.xml the builder already has.</summary>
    public DocxBuilder WithExtraStyles(string stylesXml)
    {
        _styles = _styles.Replace("</w:styles>", stylesXml + "</w:styles>");
        return this;
    }

    /// <summary>A run holding a reference to a footnote, for use inside a paragraph.</summary>
    public static string FootnoteReference(int id, string? runProperties = null) =>
        NoteReference("footnote", id, runProperties);

    /// <summary>A run holding a reference to an endnote.</summary>
    public static string EndnoteReference(int id, string? runProperties = null) =>
        NoteReference("endnote", id, runProperties);

    private static string NoteReference(string kind, int id, string? runProperties)
    {
        var style = kind == "footnote" ? "FootnoteReference" : "EndnoteReference";
        return "<w:r>" +
               $"<w:rPr><w:rStyle w:val=\"{style}\"/>{runProperties}</w:rPr>" +
               $"<w:{kind}Reference w:id=\"{id}\"/></w:r>";
    }

    /// <summary>
    /// A footnote's text: the note's own number, then the text, the way Word writes one.
    /// </summary>
    public static string FootnoteBody(string text, string? runProperties = null) =>
        NoteBody("footnote", text, runProperties);

    /// <summary>An endnote's text.</summary>
    public static string EndnoteBody(string text, string? runProperties = null) =>
        NoteBody("endnote", text, runProperties);

    private static string NoteBody(string kind, string text, string? runProperties)
    {
        var paragraphStyle = kind == "footnote" ? "FootnoteText" : "EndnoteText";
        var characterStyle = kind == "footnote" ? "FootnoteReference" : "EndnoteReference";

        return $"<w:p><w:pPr><w:pStyle w:val=\"{paragraphStyle}\"/></w:pPr>" +
               $"<w:r><w:rPr><w:rStyle w:val=\"{characterStyle}\"/>{runProperties}</w:rPr><w:{kind}Ref/></w:r>" +
               $"<w:r>{(runProperties is not null ? $"<w:rPr>{runProperties}</w:rPr>" : string.Empty)}" +
               $"<w:t xml:space=\"preserve\"> {Escape(text)}</w:t></w:r></w:p>";
    }

    private readonly List<(string Id, string Url)> _hyperlinks = [];

    /// <summary>
    /// Registers an external address and returns the relationship id a <c>w:hyperlink</c> refers
    /// to it by. Word never puts the address in the document body: it always goes through a
    /// relationship marked <c>TargetMode="External"</c>.
    /// </summary>
    public string AddExternalHyperlink(string url)
    {
        var id = $"rIdLink{_hyperlinks.Count + 1}";
        _hyperlinks.Add((id, url));
        return id;
    }

    /// <summary>Markup for a hyperlink around one run, for use inside a paragraph.</summary>
    public static string Hyperlink(string text, string? relationshipId = null, string? anchor = null,
        string? runProperties = null)
    {
        var attributes = new StringBuilder();
        if (relationshipId is not null) attributes.Append($" r:id=\"{relationshipId}\"");
        if (anchor is not null) attributes.Append($" w:anchor=\"{Escape(anchor)}\"");

        return $"<w:hyperlink{attributes}><w:r>" +
               (runProperties is not null ? $"<w:rPr>{runProperties}</w:rPr>" : string.Empty) +
               $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t>" +
               "</w:r></w:hyperlink>";
    }

    /// <summary>Markup for a bookmark spanning nothing, which is how Word marks a link target.</summary>
    public static string Bookmark(string name, int id = 1) =>
        $"<w:bookmarkStart w:id=\"{id}\" w:name=\"{Escape(name)}\"/><w:bookmarkEnd w:id=\"{id}\"/>";

    /// <summary>
    /// Parts of the package beyond the ones every document has: what each holds, what it is called,
    /// how the document reaches it, and what it reaches in turn.
    /// </summary>
    private readonly List<(string PartName, string ContentType, string Body,
        (string Id, string Type)? FromDocument,
        IReadOnlyList<(string Id, string Type, string Target)> Own)> _parts = [];

    /// <summary>Adds a part, its content type, and the relationships either side of it.</summary>
    public DocxBuilder WithPart(
        string partName, string contentType, string body,
        (string Id, string Type)? fromDocument = null,
        IReadOnlyList<(string Id, string Type, string Target)>? own = null)
    {
        _parts.Add((partName, contentType, body, fromDocument, own ?? []));
        return this;
    }

    private readonly List<(string Id, string PartName, byte[] Data)> _images = [];

    /// <summary>
    /// Pictures a running head reaches, which are parts of the package like any other but are not
    /// referred to from the body. Their relationships belong to the header part, and their ids may
    /// be the same ids the body uses for different pictures entirely.
    /// </summary>
    private readonly List<(string PartName, byte[] Data)> _headerImages = [];

    /// <summary>
    /// Adds an image part and returns the relationship id a drawing refers to it by.
    /// </summary>
    public string AddImagePart(byte[] data, string extension = "png")
    {
        var id = $"rIdImg{_images.Count + 1}";
        _images.Add((id, $"word/media/image{_images.Count + 1}.{extension}", data));
        return id;
    }

    /// <summary>
    /// Appends a paragraph holding one inline image, sized in points.
    /// </summary>
    /// <remarks>
    /// The markup mirrors what Word emits for an inline picture: the display size lives in
    /// <c>wp:extent</c> in EMUs, and the picture itself is reached through a relationship id, so
    /// the drawing carries no image data of its own.
    /// </remarks>
    public DocxBuilder AddImageParagraph(
        string relationshipId, double widthPoints, double heightPoints,
        string? paragraphProperties = null, string? leadingText = null,
        string? leadingRunProperties = null)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);

        _body.Append("<w:p>");
        if (paragraphProperties is not null) _body.Append($"<w:pPr>{paragraphProperties}</w:pPr>");

        if (leadingText is not null)
        {
            _body.Append("<w:r>");
            if (leadingRunProperties is not null) _body.Append($"<w:rPr>{leadingRunProperties}</w:rPr>");
            _body.Append($"<w:t xml:space=\"preserve\">{Escape(leadingText)}</w:t></w:r>");
        }

        _body.Append($"""
            <w:r><w:drawing>
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="{cx}" cy="{cy}"/>
                <wp:docPr id="{_images.Count}" name="Picture {_images.Count}"/>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                      <pic:nvPicPr><pic:cNvPr id="{_images.Count}" name="Picture"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill>
                        <a:blip r:embed="{relationshipId}"/>
                        <a:stretch><a:fillRect/></a:stretch>
                      </pic:blipFill>
                      <pic:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      </pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing></w:r>
            """);

        _body.Append("</w:p>");
        return this;
    }

    /// <summary>
    /// Appends a paragraph whose first run carries an anchored (floating) image, followed by the
    /// given text. The picture is positioned independently and the text flows around it.
    /// </summary>
    /// <param name="wrap">"square", "topAndBottom" or "none".</param>
    /// <param name="offsetXPoints">Horizontal offset from <paramref name="relativeFromH"/>.</param>
    /// <param name="alignX">Used instead of an offset when given: "left", "center" or "right".</param>
    public DocxBuilder AddAnchoredImageParagraph(
        string relationshipId, double widthPoints, double heightPoints, string text,
        string wrap = "square",
        double offsetXPoints = 0,
        double offsetYPoints = 0,
        string? alignX = null,
        string relativeFromH = "column",
        string relativeFromV = "paragraph",
        double distancePoints = 6,
        bool behindText = false,
        string? paragraphProperties = null,
        string? runProperties = null)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);
        var dist = (long)Math.Round(distancePoints * 12700);

        var wrapElement = wrap switch
        {
            "none" => "<wp:wrapNone/>",
            "topAndBottom" => "<wp:wrapTopAndBottom/>",
            _ => "<wp:wrapSquare wrapText=\"bothSides\"/>"
        };

        var horizontal = alignX is not null
            ? $"<wp:align>{alignX}</wp:align>"
            : $"<wp:posOffset>{(long)Math.Round(offsetXPoints * 12700)}</wp:posOffset>";

        _body.Append("<w:p>");
        if (paragraphProperties is not null) _body.Append($"<w:pPr>{paragraphProperties}</w:pPr>");

        _body.Append($"""
            <w:r><w:drawing>
              <wp:anchor distT="{dist}" distB="{dist}" distL="{dist}" distR="{dist}"
                         simplePos="0" relativeHeight="251658240" behindDoc="{(behindText ? 1 : 0)}"
                         locked="0" layoutInCell="1" allowOverlap="1">
                <wp:simplePos x="0" y="0"/>
                <wp:positionH relativeFrom="{relativeFromH}">{horizontal}</wp:positionH>
                <wp:positionV relativeFrom="{relativeFromV}">
                  <wp:posOffset>{(long)Math.Round(offsetYPoints * 12700)}</wp:posOffset>
                </wp:positionV>
                <wp:extent cx="{cx}" cy="{cy}"/>
                {wrapElement}
                <wp:docPr id="{_images.Count}" name="Floating {_images.Count}"/>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                      <pic:nvPicPr><pic:cNvPr id="{_images.Count}" name="Floating"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill>
                        <a:blip r:embed="{relationshipId}"/>
                        <a:stretch><a:fillRect/></a:stretch>
                      </pic:blipFill>
                      <pic:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      </pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
              </wp:anchor>
            </w:drawing></w:r>
            """);

        _body.Append("<w:r>");
        if (runProperties is not null) _body.Append($"<w:rPr>{runProperties}</w:rPr>");
        _body.Append($"<w:t xml:space=\"preserve\">{Escape(text)}</w:t>");
        _body.Append("</w:r></w:p>");

        return this;
    }

    /// <summary>
    /// A shape drawn in the text: its geometry, how it is painted, and what it holds.
    /// </summary>
    /// <remarks>
    /// This is the markup Word writes for a text box, minus the compatibility wrapper — a shape
    /// goes into the document as a drawing holding a <c>wps:wsp</c>, which is the same graphic
    /// frame a picture goes into with a different thing inside it. What makes it a text box rather
    /// than a plain shape is the <c>wps:txbx</c>, and what makes it a plain shape is leaving that
    /// out; both are the same element otherwise.
    /// </remarks>
    /// <param name="content">
    /// The paragraphs inside it, or null for a shape with no text at all.
    /// </param>
    /// <param name="geometry">A preset geometry name: rect, roundRect, ellipse, triangle.</param>
    /// <param name="insets">
    /// How far the text sits inside the shape's edges, in points, or null for Word's own — a tenth
    /// of an inch at the sides and half that above and below.
    /// </param>
    /// <param name="anchor">Where the text sits in the height it has: t, ctr or b.</param>
    public static string ShapeGraphic(
        long cx, long cy, string? content = null, string geometry = "rect",
        string? fillHex = null, string? lineHex = null, double lineWidthPoints = 1,
        (double Left, double Top, double Right, double Bottom)? insets = null,
        string anchor = "t")
    {
        static long Emu(double points) => (long)Math.Round(points * 12700);

        // A colour is either named outright or taken from the theme, which is how the shapes in
        // Word's own gallery name theirs: "accent1" rather than a number.
        static string Color(string value) =>
            value.Length == 6 && value.All(Uri.IsHexDigit)
                ? $"<a:srgbClr val=\"{value}\"/>"
                : $"<a:schemeClr val=\"{value}\"/>";

        var fill = fillHex is null
            ? "<a:noFill/>"
            : $"<a:solidFill>{Color(fillHex)}</a:solidFill>";

        var line = lineHex is null
            ? "<a:ln><a:noFill/></a:ln>"
            : $"<a:ln w=\"{Emu(lineWidthPoints)}\"><a:solidFill>{Color(lineHex)}</a:solidFill></a:ln>";

        var body = content is null ? string.Empty : $"<wps:txbx><w:txbxContent>{content}</w:txbxContent></wps:txbx>";

        var space = insets is { } given
            ? $"lIns=\"{Emu(given.Left)}\" tIns=\"{Emu(given.Top)}\" " +
              $"rIns=\"{Emu(given.Right)}\" bIns=\"{Emu(given.Bottom)}\" "
            : string.Empty;

        return $"""
            <a:graphic>
              <a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
                <wps:wsp>
                  <wps:cNvSpPr txBox="{(content is null ? 0 : 1)}"/>
                  <wps:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm>
                    <a:prstGeom prst="{geometry}"><a:avLst/></a:prstGeom>
                    {fill}
                    {line}
                  </wps:spPr>
                  {body}
                  <wps:bodyPr rot="0" vertOverflow="overflow" horzOverflow="overflow" vert="horz"
                              wrap="square" {space}anchor="{anchor}" anchorCtr="0">
                    <a:noAutofit/>
                  </wps:bodyPr>
                </wps:wsp>
              </a:graphicData>
            </a:graphic>
            """;
    }

    /// <summary>
    /// A shape in the older spelling: the one Word wrote before 2007, and still writes for a
    /// watermark and inside the fallback of every shape it writes in the newer one.
    /// </summary>
    /// <remarks>
    /// VML says everything in a style attribute borrowed from CSS rather than in elements of its
    /// own, and states the geometry as a path over a 21600-square grid rather than by name. The
    /// shape type here is <c>_x0000_t202</c>, which is Word's own text box: a plain rectangle.
    /// </remarks>
    /// <param name="style">
    /// The CSS-like style: the size for a shape in the line, and a position as well for one the
    /// text flows around.
    /// </param>
    public static string VmlShape(
        string style, string? content = null, string element = "shape",
        string? fillColor = "#ffffff", string? strokeColor = "#000000",
        string? strokeWeight = "1pt", string? inset = null, string? wrap = null, int id = 1026)
    {
        var textbox = content is null
            ? string.Empty
            : $"<v:textbox{(inset is null ? string.Empty : $" inset=\"{inset}\"")}>" +
              $"<w:txbxContent>{content}</w:txbxContent></v:textbox>";

        var attributes =
            (fillColor is null ? " filled=\"f\"" : $" fillcolor=\"{fillColor}\"") +
            (strokeColor is null ? " stroked=\"f\"" : $" strokecolor=\"{strokeColor}\"") +
            (strokeWeight is null || strokeColor is null ? string.Empty : $" strokeweight=\"{strokeWeight}\"");

        // The type reference is only meaningful for v:shape; the named elements carry their own
        // geometry, which is the whole reason Word writes them.
        var type = element == "shape" ? " type=\"#_x0000_t202\"" : string.Empty;

        var shapeType = element == "shape"
            ? """
              <v:shapetype id="_x0000_t202" coordsize="21600,21600" o:spt="202"
                           path="m,l,21600r21600,l21600,xe">
                <v:stroke joinstyle="miter"/>
                <v:path gradientshapeok="t" o:connecttype="rect"/>
              </v:shapetype>
              """
            : string.Empty;

        return $"""
            <w:r><w:pict>
              {shapeType}
              <v:{element} id="_x0000_s{id}"{type} style="{style}"{attributes}>
                {textbox}
                {wrap ?? string.Empty}
              </v:{element}>
            </w:pict></w:r>
            """;
    }

    /// <summary>
    /// A watermark, in the markup Word writes one in: a shape in a header, holding its text on a
    /// path rather than in paragraphs, turned, and set behind everything else on the page.
    /// </summary>
    /// <remarks>
    /// The shape type is <c>_x0000_t136</c>, which is Word's WordArt, and the whole of the
    /// watermark is in its attributes — the string itself, the face to set it in, and a size of
    /// one point standing for "as large as the shape will hold", which is what the shape type's
    /// own <c>fitshape</c> asks for.
    /// </remarks>
    /// <param name="rotation">
    /// How far it is turned, clockwise, in degrees. Word writes 315 for the diagonal watermark it
    /// offers and nothing at all for the horizontal one.
    /// </param>
    public static string Watermark(
        string text, double widthPoints, double heightPoints, string fontFamily = "Calibri",
        string fillColor = "#d9d9d9", double opacity = 0.5, int? rotation = 315, int id = 2049)
    {
        var style =
            "position:absolute;margin-left:0;margin-top:0;" +
            $"width:{widthPoints.ToString(CultureInfo.InvariantCulture)}pt;" +
            $"height:{heightPoints.ToString(CultureInfo.InvariantCulture)}pt;" +
            (rotation is null ? string.Empty : $"rotation:{rotation};") +
            "z-index:-251658752;mso-position-horizontal:center;" +
            "mso-position-horizontal-relative:margin;mso-position-vertical:center;" +
            "mso-position-vertical-relative:margin";

        return $"""
            <w:r><w:pict>
              <v:shapetype id="_x0000_t136" coordsize="21600,21600" o:spt="136" adj="10800"
                           path="m@7,l@8,m@5,21600l@6,21600e">
                <v:formulas>
                  <v:f eqn="sum #0 0 10800"/><v:f eqn="prod #0 2 1"/>
                  <v:f eqn="sum 21600 0 @1"/><v:f eqn="sum 0 0 @2"/>
                  <v:f eqn="sum 21600 0 @3"/><v:f eqn="if @0 @3 0"/>
                  <v:f eqn="if @0 21600 @1"/><v:f eqn="if @0 0 @2"/>
                  <v:f eqn="if @0 @4 21600"/><v:f eqn="mid @5 @6"/>
                  <v:f eqn="mid @8 @5"/><v:f eqn="mid @7 @8"/>
                  <v:f eqn="mid @6 @7"/><v:f eqn="sum @6 0 @5"/>
                </v:formulas>
                <v:path textpathok="t" o:connecttype="custom"
                        o:connectlocs="@9,0;@10,10800;@11,21600;@12,10800"
                        o:connectangles="270,180,90,0"/>
                <v:textpath on="t" fitshape="t"/>
                <v:handles><v:h position="#0,bottomRight" xrange="6629,14971"/></v:handles>
                <o:lock v:ext="edit" text="t" shapetype="t"/>
              </v:shapetype>
              <v:shape id="PowerPlusWaterMarkObject{id}" o:spid="_x0000_s{id}" type="#_x0000_t136"
                       style="{style}" o:allowincell="f" fillcolor="{fillColor}" stroked="f">
                <v:fill opacity="{opacity.ToString(CultureInfo.InvariantCulture)}"/>
                <v:textpath style="font-family:&quot;{fontFamily}&quot;;font-size:1pt" string="{Escape(text)}"/>
              </v:shape>
            </w:pict></w:r>
            """;
    }

    /// <summary>
    /// A watermark made of a picture rather than a word: the same shape in the same place, holding
    /// an image instead of text, and washed out so the page can be read through it.
    /// </summary>
    /// <remarks>
    /// The washing out is two numbers on the image itself — a gain and a black level, both in
    /// sixty-fourths of a thousand — and Word writes the same pair for every picture watermark it
    /// makes: a gain of about three tenths and a black level of about a third.
    /// </remarks>
    public static string PictureWatermark(
        string relationshipId, double widthPoints, double heightPoints,
        string gain = "19661f", string blackLevel = "22938f", int id = 2049)
    {
        var style =
            "position:absolute;margin-left:0;margin-top:0;" +
            $"width:{widthPoints.ToString(CultureInfo.InvariantCulture)}pt;" +
            $"height:{heightPoints.ToString(CultureInfo.InvariantCulture)}pt;" +
            "z-index:-251658752;mso-position-horizontal:center;" +
            "mso-position-horizontal-relative:margin;mso-position-vertical:center;" +
            "mso-position-vertical-relative:margin";

        return $"""
            <w:r><w:pict>
              <v:shapetype id="_x0000_t75" coordsize="21600,21600" o:spt="75" o:preferrelative="t"
                           path="m@4@5l@4@11@9@11@9@5xe" filled="f" stroked="f">
                <v:stroke joinstyle="miter"/>
                <v:formulas>
                  <v:f eqn="if lineDrawn pixelLineWidth 0"/><v:f eqn="sum @0 1 0"/>
                  <v:f eqn="sum 0 0 @1"/><v:f eqn="prod @2 1 2"/>
                  <v:f eqn="prod @3 21600 pixelWidth"/><v:f eqn="prod @3 21600 pixelHeight"/>
                  <v:f eqn="sum @0 0 1"/><v:f eqn="prod @6 1 2"/>
                  <v:f eqn="prod @7 21600 pixelWidth"/><v:f eqn="sum @8 21600 0"/>
                  <v:f eqn="prod @7 21600 pixelHeight"/><v:f eqn="sum @10 21600 0"/>
                </v:formulas>
                <v:path o:extrusionok="f" gradientshapeok="t" o:connecttype="rect"/>
                <o:lock v:ext="edit" aspectratio="t"/>
              </v:shapetype>
              <v:shape id="WordPictureWatermark{id}" o:spid="_x0000_s{id}" type="#_x0000_t75"
                       style="{style}" o:allowincell="f">
                <v:imagedata r:id="{relationshipId}" o:title="" gain="{gain}" blacklevel="{blackLevel}"/>
              </v:shape>
            </w:pict></w:r>
            """;
    }

    private const string ChartNamespace = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// A chart, as its own part, and the paragraph markup that reaches it.
    /// </summary>
    /// <remarks>
    /// A chart is the one thing a document describes only as data: series, axes and formatting,
    /// with no drawing of it anywhere. The numbers are held twice — as a formula naming a cell
    /// range in a workbook stored alongside, and as a cache of what those cells last held — and it
    /// is the cache that is read, since the workbook is a spreadsheet and not a drawing.
    /// </remarks>
    public DocxBuilder WithChart(string chartXml) =>
        WithPart("word/charts/chart1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            $"""
             <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
             <c:chartSpace xmlns:c="{ChartNamespace}"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                           xmlns:r="{OfficeRelationships}">
               {chartXml}
             </c:chartSpace>
             """,
            fromDocument: ("rIdChart", $"{OfficeRelationships}/chart"));

    /// <summary>The frame a chart sits in, inline like a picture.</summary>
    public static string ChartDrawing(
        double widthPoints, double heightPoints, int id = 500, string relationshipId = "rIdChart")
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);

        return $"""
            <w:r><w:drawing>
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="{cx}" cy="{cy}"/>
                <wp:docPr id="{id}" name="Chart {id}"/>
                <a:graphic>
                  <a:graphicData uri="{ChartNamespace}">
                    <c:chart xmlns:c="{ChartNamespace}" xmlns:r="{OfficeRelationships}" r:id="{relationshipId}"/>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing></w:r>
            """;
    }

    /// <summary>One series of a chart: its name, and the values it holds against the categories.</summary>
    public static string ChartSeries(
        int index, string name, IReadOnlyList<string> categories, IReadOnlyList<double> values,
        string fillHex)
    {
        static string Points(IEnumerable<string> written) =>
            string.Concat(written.Select((value, i) =>
                $"<c:pt idx=\"{i}\"><c:v>{value}</c:v></c:pt>"));

        return $"""
            <c:ser>
              <c:idx val="{index}"/>
              <c:order val="{index}"/>
              <c:tx><c:strRef><c:f>Sheet1!$B${index + 1}</c:f>
                <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>{Escape(name)}</c:v></c:pt></c:strCache>
              </c:strRef></c:tx>
              <c:spPr><a:solidFill><a:srgbClr val="{fillHex}"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>
              <c:cat><c:strRef><c:f>Sheet1!$A$2:$A${categories.Count + 1}</c:f>
                <c:strCache><c:ptCount val="{categories.Count}"/>{Points(categories.Select(Escape))}</c:strCache>
              </c:strRef></c:cat>
              <c:val><c:numRef><c:f>Sheet1!$B$2:$B${values.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{values.Count}"/>
                  {Points(values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}
                </c:numCache>
              </c:numRef></c:val>
            </c:ser>
            """;
    }

    /// <summary>One series of a line chart: a colour and a width rather than a fill.</summary>
    public static string ChartLineSeries(
        int index, string name, IReadOnlyList<string> categories, IReadOnlyList<double> values,
        string lineHex, double widthPoints = 2.25, string marker = "none", int markerSize = 0)
    {
        var series = ChartSeries(index, name, categories, values, "FFFFFF");

        return series.Replace(
            $"<c:spPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>",
            $"""
             <c:spPr>
               <a:ln w="{(long)Math.Round(widthPoints * 12700)}" cap="rnd">
                 <a:solidFill><a:srgbClr val="{lineHex}"/></a:solidFill>
                 <a:round/>
               </a:ln>
             </c:spPr>
             <c:marker><c:symbol val="{marker}"/>{(markerSize > 0
                 ? $"<c:size val=\"{markerSize}\"/>"
                 : string.Empty)}</c:marker>
             """);
    }

    /// <summary>A pie's slices, each stating its own colour.</summary>
    public static string ChartPieSeries(
        string name, IReadOnlyList<string> categories, IReadOnlyList<double> values,
        IReadOnlyList<string> fills, int index = 0)
    {
        var series = ChartSeries(index, name, categories, values, "FFFFFF");

        var points = string.Concat(fills.Select((fill, i) => $"""
            <c:dPt>
              <c:idx val="{i}"/>
              <c:bubble3D val="0"/>
              <c:spPr><a:solidFill><a:srgbClr val="{fill}"/></a:solidFill>
                <a:ln w="19050"><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill></a:ln>
              </c:spPr>
            </c:dPt>
            """));

        return series.Replace(
            $"<c:spPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>",
            points);
    }

    /// <summary>
    /// One series of a scatter chart, which holds pairs rather than categories: an x for every y,
    /// and a marker at each pair where the series asks for one.
    /// </summary>
    public static string ChartScatterSeries(
        int index, string name, IReadOnlyList<double> x, IReadOnlyList<double> y,
        string colorHex, string? marker = "circle", double markerSize = 7,
        bool line = true, bool smooth = false, double widthPoints = 2.25)
    {
        static string Points(IEnumerable<double> values) =>
            string.Concat(values.Select((value, i) =>
                $"<c:pt idx=\"{i}\"><c:v>{value.ToString(CultureInfo.InvariantCulture)}</c:v></c:pt>"));

        var stroke = line
            ? $"""
               <a:ln w="{(long)Math.Round(widthPoints * 12700)}" cap="rnd">
                 <a:solidFill><a:srgbClr val="{colorHex}"/></a:solidFill><a:round/>
               </a:ln>
               """
            : "<a:ln w=\"19050\"><a:noFill/></a:ln>";

        var symbol = marker is null
            ? string.Empty
            : $"""
               <c:marker>
                 <c:symbol val="{marker}"/>
                 <c:size val="{(int)Math.Round(markerSize)}"/>
                 <c:spPr>
                   <a:solidFill><a:srgbClr val="{colorHex}"/></a:solidFill>
                   <a:ln w="9525"><a:solidFill><a:srgbClr val="{colorHex}"/></a:solidFill></a:ln>
                 </c:spPr>
               </c:marker>
               """;

        return $"""
            <c:ser>
              <c:idx val="{index}"/>
              <c:order val="{index}"/>
              <c:tx><c:strRef><c:f>Sheet1!$B${index + 1}</c:f>
                <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>{Escape(name)}</c:v></c:pt></c:strCache>
              </c:strRef></c:tx>
              <c:spPr>{stroke}</c:spPr>
              {symbol}
              <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A${x.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{x.Count}"/>
                  {Points(x)}
                </c:numCache>
              </c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B${y.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{y.Count}"/>
                  {Points(y)}
                </c:numCache>
              </c:numRef></c:yVal>
              <c:smooth val="{(smooth ? 1 : 0)}"/>
            </c:ser>
            """;
    }

    /// <summary>
    /// One series of a bubble chart: pairs of numbers as a scatter holds them, and a third number
    /// at each pair saying how large the bubble drawn there is.
    /// </summary>
    public static string ChartBubbleSeries(
        int index, string name, IReadOnlyList<double> x, IReadOnlyList<double> y,
        IReadOnlyList<double> sizes, string colorHex)
    {
        static string Points(IEnumerable<double> values) =>
            string.Concat(values.Select((value, i) =>
                $"<c:pt idx=\"{i}\"><c:v>{value.ToString(CultureInfo.InvariantCulture)}</c:v></c:pt>"));

        return $"""
            <c:ser>
              <c:idx val="{index}"/>
              <c:order val="{index}"/>
              <c:tx><c:strRef><c:f>Sheet1!$B${index + 1}</c:f>
                <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>{Escape(name)}</c:v></c:pt></c:strCache>
              </c:strRef></c:tx>
              <c:spPr><a:solidFill><a:srgbClr val="{colorHex}"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>
              <c:invertIfNegative val="0"/>
              <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A${x.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{x.Count}"/>
                  {Points(x)}
                </c:numCache>
              </c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B${y.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{y.Count}"/>
                  {Points(y)}
                </c:numCache>
              </c:numRef></c:yVal>
              <c:bubbleSize><c:numRef><c:f>Sheet1!$C$2:$C${sizes.Count + 1}</c:f>
                <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="{sizes.Count}"/>
                  {Points(sizes)}
                </c:numCache>
              </c:numRef></c:bubbleSize>
              <c:bubble3D val="0"/>
            </c:ser>
            """;
    }

    /// <summary>
    /// One series of a stock chart: a line series drawing no line of its own, since what a stock
    /// chart draws is the lines between its series rather than along them.
    /// </summary>
    public static string ChartStockSeries(
        int index, string name, IReadOnlyList<string> categories, IReadOnlyList<double> values,
        string marker = "none", string markerColorHex = "000000")
    {
        var series = ChartSeries(index, name, categories, values, "FFFFFF");

        var symbol = marker == "none"
            ? "<c:marker><c:symbol val=\"none\"/></c:marker>"
            : $"""
               <c:marker>
                 <c:symbol val="{marker}"/>
                 <c:size val="5"/>
                 <c:spPr>
                   <a:noFill/>
                   <a:ln w="9525"><a:solidFill><a:srgbClr val="{markerColorHex}"/></a:solidFill></a:ln>
                 </c:spPr>
               </c:marker>
               """;

        return series.Replace(
            "<c:spPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr>",
            $"<c:spPr><a:ln w=\"28575\"><a:noFill/></a:ln></c:spPr>{symbol}");
    }

    private const string DiagramNamespace = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// A diagram — SmartArt — with its five parts, and the paragraph markup that reaches it.
    /// </summary>
    /// <remarks>
    /// A diagram is described twice over. The data and the layout say what it means and how it is
    /// to be arranged, and are what Word rebuilds it from; beside them sits a drawing part holding
    /// the arrangement it last came to, shape by shape, at absolute positions. Every reader but
    /// Word draws that cached arrangement, since rebuilding it means implementing the layout
    /// language, and so does this.
    ///
    /// The cached drawing here is deliberately not what any layout would produce — three shapes
    /// stepping down the frame — so that Word's export says outright which of the two it drew.
    /// </remarks>
    /// <param name="nodes">
    /// The words each node holds, which must be the words the cached drawing holds: Word rebuilds
    /// the cache from the data every time it opens the document, and a data part saying something
    /// else would rebuild it into something else.
    /// </param>
    public DocxBuilder WithSmartArt(string drawingXml, params string[] nodes)
    {
        const string diagrams = "word/diagrams";

        WithPart($"{diagrams}/drawing1.xml",
            "application/vnd.ms-office.drawingml.diagramDrawing+xml", drawingXml);

        WithPart($"{diagrams}/data1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            DiagramData(nodes.Length > 0 ? nodes : ["One", "Two", "Three\nFour"]),
            fromDocument: ("rIdDgmData", $"{OfficeRelationships}/diagramData"),
            own: [("rIdDgmDrawing",
                "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", "drawing1.xml")]);

        WithPart($"{diagrams}/layout1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            DiagramLayout,
            fromDocument: ("rIdDgmLayout", $"{OfficeRelationships}/diagramLayout"));

        WithPart($"{diagrams}/quickStyle1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            DiagramQuickStyle,
            fromDocument: ("rIdDgmStyle", $"{OfficeRelationships}/diagramQuickStyle"));

        WithPart($"{diagrams}/colors1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            DiagramColors,
            fromDocument: ("rIdDgmColors", $"{OfficeRelationships}/diagramColors"));

        return this;
    }

    /// <summary>The drawing frame a diagram sits in, inline like a picture.</summary>
    public static string SmartArtDrawing(double widthPoints, double heightPoints, int id = 400)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);

        return $"""
            <w:r><w:drawing>
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="{cx}" cy="{cy}"/>
                <wp:docPr id="{id}" name="Diagram {id}"/>
                <a:graphic>
                  <a:graphicData uri="{DiagramNamespace}">
                    <dgm:relIds xmlns:dgm="{DiagramNamespace}"
                                r:dm="rIdDgmData" r:lo="rIdDgmLayout"
                                r:qs="rIdDgmStyle" r:cs="rIdDgmColors"/>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing></w:r>
            """;
    }

    /// <summary>
    /// One shape of a cached diagram: where it is in the frame, what it is drawn as, and what it
    /// says. The text rectangle is given separately, as Word gives it.
    /// </summary>
    public static string SmartArtShape(
        string text, double xPoints, double yPoints, double widthPoints, double heightPoints,
        string geometry = "roundRect", string fillHex = "4472C4", int sizeHundredths = 1800,
        double textInsetPoints = 6)
    {
        static long Emu(double points) => (long)Math.Round(points * 12700);

        return $"""
            <dsp:sp modelId="{Guid.Empty.ToString("B")}">
              <dsp:nvSpPr>
                <dsp:cNvPr id="0" name=""/>
                <dsp:cNvSpPr/>
              </dsp:nvSpPr>
              <dsp:spPr>
                <a:xfrm>
                  <a:off x="{Emu(xPoints)}" y="{Emu(yPoints)}"/>
                  <a:ext cx="{Emu(widthPoints)}" cy="{Emu(heightPoints)}"/>
                </a:xfrm>
                <a:prstGeom prst="{geometry}"><a:avLst/></a:prstGeom>
                <a:solidFill><a:srgbClr val="{fillHex}"/></a:solidFill>
                <a:ln w="12700"><a:solidFill><a:srgbClr val="2F528F"/></a:solidFill></a:ln>
              </dsp:spPr>
              <dsp:txBody>
                <a:bodyPr spcFirstLastPara="0" vert="horz" wrap="square" anchor="ctr"/>
                <a:lstStyle/>
                <a:p>
                  <a:pPr algn="ctr"/>
                  <a:r>
                    <a:rPr lang="en-GB" sz="{sizeHundredths}" kern="1200">
                      <a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill>
                      <a:latin typeface="Times New Roman"/>
                    </a:rPr>
                    <a:t>{Escape(text)}</a:t>
                  </a:r>
                </a:p>
              </dsp:txBody>
              <dsp:txXfrm>
                <a:off x="{Emu(xPoints + textInsetPoints)}" y="{Emu(yPoints + textInsetPoints)}"/>
                <a:ext cx="{Emu(widthPoints - 2 * textInsetPoints)}" cy="{Emu(heightPoints - 2 * textInsetPoints)}"/>
              </dsp:txXfrm>
            </dsp:sp>
            """;
    }

    /// <summary>The cached drawing part, holding the shapes as they were last arranged.</summary>
    public static string SmartArtCachedDrawing(params string[] shapes) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <dsp:drawing xmlns:dsp="http://schemas.microsoft.com/office/drawing/2008/diagram"
                     xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <dsp:spTree>
            <dsp:nvGrpSpPr><dsp:cNvPr id="0" name=""/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr>
            <dsp:grpSpPr/>
            {string.Concat(shapes)}
          </dsp:spTree>
        </dsp:drawing>
        """;

    /// <summary>
    /// What the diagram means: three points of text and nothing else. Word rebuilds the drawing
    /// from this and the layout beside it, so it has to hold the same words the cache does.
    /// </summary>
    private static string DiagramData(string[] nodes) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <dgm:dataModel xmlns:dgm="{DiagramNamespace}"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <dgm:ptLst>
            <dgm:pt modelId="1" type="doc">
              <dgm:prSet loTypeId="urn:microsoft.com/office/officeart/2005/8/layout/default"
                         qsTypeId="urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"
                         csTypeId="urn:microsoft.com/office/officeart/2005/8/colors/accent1_2"/>
              <dgm:spPr/>
              <dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang="en-GB"/></a:p></dgm:t>
            </dgm:pt>
            {string.Concat(nodes.Select((text, i) => DiagramPoint(i + 2, text.Split('\n'))))}
          </dgm:ptLst>
          <dgm:cxnLst>
            {string.Concat(nodes.Select((_, i) =>
                $"<dgm:cxn modelId=\"{10 + i}\" srcId=\"1\" destId=\"{i + 2}\" srcOrd=\"{i}\" destOrd=\"0\"/>"))}
          </dgm:cxnLst>
          <dgm:bg/>
          <dgm:whole/>
        </dgm:dataModel>
        """;

    private static string DiagramPoint(int id, params string[] paragraphs) => $"""
        <dgm:pt modelId="{id}">
          <dgm:prSet phldrT="[Text]"/>
          <dgm:spPr/>
          <dgm:t>
            <a:bodyPr/><a:lstStyle/>
            {string.Concat(paragraphs.Select(text =>
                $"<a:p><a:r><a:rPr lang=\"en-GB\"/><a:t>{Escape(text)}</a:t></a:r></a:p>"))}
          </dgm:t>
        </dgm:pt>
        """;

    /// <summary>A layout that puts its points in a row, which is the simplest one there is.</summary>
    /// <summary>
    /// Where the text sits in a node's box, as a layout parameter rather than in the cache: Word
    /// rebuilds the cache from the layout, so this is the only way to ask it for anything but the
    /// default. <c>t</c> puts the text at the top of the box, which is what tells the height of a
    /// block apart from where its first line sits inside it.
    /// </summary>
    public string? DiagramTextAnchor { get; set; }

    private string DiagramLayout => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <dgm:layoutDef xmlns:dgm="{DiagramNamespace}"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       uniqueId="urn:microsoft.com/office/officeart/2005/8/layout/default">
          <dgm:title val="Basic Block List"/>
          <dgm:desc val=""/>
          <dgm:catLst><dgm:cat type="list" pri="1000"/></dgm:catLst>
          <dgm:sampData><dgm:dataModel><dgm:ptLst/></dgm:dataModel></dgm:sampData>
          <dgm:styleData><dgm:dataModel><dgm:ptLst/></dgm:dataModel></dgm:styleData>
          <dgm:clrData><dgm:dataModel><dgm:ptLst/></dgm:dataModel></dgm:clrData>
          <dgm:layoutNode name="diagram">
            <dgm:varLst><dgm:animLvl val="lvl"/><dgm:resizeHandles val="exact"/></dgm:varLst>
            <dgm:alg type="lin"/>
            <dgm:shape xmlns:r="{OfficeRelationships}" r:blip=""><dgm:adjLst/></dgm:shape>
            <dgm:presOf/>
            <dgm:constrLst/>
            <dgm:ruleLst/>
            <dgm:forEach name="Name0" axis="ch" ptType="node">
              <dgm:layoutNode name="node">
                <dgm:alg type="tx">{(DiagramTextAnchor is null ? string.Empty : $"<dgm:param type=\"txAnchorVert\" val=\"{DiagramTextAnchor}\"/>")}</dgm:alg>
                <dgm:shape xmlns:r="{OfficeRelationships}" type="roundRect" r:blip=""><dgm:adjLst/></dgm:shape>
                <dgm:presOf axis="desOrSelf" ptType="node"/>
                <dgm:constrLst/>
                <dgm:ruleLst/>
              </dgm:layoutNode>
            </dgm:forEach>
          </dgm:layoutNode>
        </dgm:layoutDef>
        """;

    private static readonly string DiagramQuickStyle = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <dgm:styleDef xmlns:dgm="{DiagramNamespace}"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      uniqueId="urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1">
          <dgm:title val=""/>
          <dgm:desc val=""/>
          <dgm:catLst><dgm:cat type="simple" pri="10100"/></dgm:catLst>
          <dgm:scene3d><a:camera prst="orthographicFront"/><a:lightRig rig="threePt" dir="t"/></dgm:scene3d>
          <dgm:styleLbl name="node0">
            <dgm:scene3d><a:camera prst="orthographicFront"/><a:lightRig rig="threePt" dir="t"/></dgm:scene3d>
            <dgm:sp3d/>
            <dgm:txPr/>
            <dgm:style>
              <a:lnRef idx="2"><a:scrgbClr r="0" g="0" b="0"/></a:lnRef>
              <a:fillRef idx="1"><a:scrgbClr r="0" g="0" b="0"/></a:fillRef>
              <a:effectRef idx="0"><a:scrgbClr r="0" g="0" b="0"/></a:effectRef>
              <a:fontRef idx="minor"><a:schemeClr val="lt1"/></a:fontRef>
            </dgm:style>
          </dgm:styleLbl>
        </dgm:styleDef>
        """;

    private static readonly string DiagramColors = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <dgm:colorsDef xmlns:dgm="{DiagramNamespace}"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       uniqueId="urn:microsoft.com/office/officeart/2005/8/colors/accent1_2">
          <dgm:title val=""/>
          <dgm:desc val=""/>
          <dgm:catLst><dgm:cat type="accent1" pri="11200"/></dgm:catLst>
          <dgm:styleLbl name="node0">
            <dgm:fillClrLst meth="repeat"><a:schemeClr val="accent1"/></dgm:fillClrLst>
            <dgm:linClrLst meth="repeat"><a:schemeClr val="lt1"/></dgm:linClrLst>
            <dgm:txLinClrLst/>
            <dgm:txFillClrLst/>
          </dgm:styleLbl>
        </dgm:colorsDef>
        """;

    /// <summary>A shape sitting in the line of text, like an inline picture.</summary>
    public static string InlineShape(
        double widthPoints, double heightPoints, string? content = null, string geometry = "rect",
        string? fillHex = null, string? lineHex = null, double lineWidthPoints = 1,
        (double Left, double Top, double Right, double Bottom)? insets = null,
        string anchor = "t", int id = 100)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);

        return $"""
            <w:r><w:drawing>
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="{cx}" cy="{cy}"/>
                <wp:docPr id="{id}" name="Shape {id}"/>
                {ShapeGraphic(cx, cy, content, geometry, fillHex, lineHex, lineWidthPoints, insets, anchor)}
              </wp:inline>
            </w:drawing></w:r>
            """;
    }

    /// <summary>A shape anchored to the page, with text flowing around it.</summary>
    public static string AnchoredShape(
        double widthPoints, double heightPoints, string? content = null,
        double offsetXPoints = 0, double offsetYPoints = 0, string? alignX = null,
        string wrap = "square", double distancePoints = 9,
        string geometry = "rect", string? fillHex = null, string? lineHex = null,
        double lineWidthPoints = 1,
        (double Left, double Top, double Right, double Bottom)? insets = null,
        string anchor = "t", int id = 200)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);
        var dist = (long)Math.Round(distancePoints * 12700);

        var wrapElement = wrap switch
        {
            "none" => "<wp:wrapNone/>",
            "topAndBottom" => "<wp:wrapTopAndBottom/>",
            _ => "<wp:wrapSquare wrapText=\"bothSides\"/>"
        };

        var horizontal = alignX is not null
            ? $"<wp:align>{alignX}</wp:align>"
            : $"<wp:posOffset>{(long)Math.Round(offsetXPoints * 12700)}</wp:posOffset>";

        return $"""
            <w:r><w:drawing>
              <wp:anchor distT="{dist}" distB="{dist}" distL="{dist}" distR="{dist}"
                         simplePos="0" relativeHeight="251658240" behindDoc="0"
                         locked="0" layoutInCell="1" allowOverlap="1">
                <wp:simplePos x="0" y="0"/>
                <wp:positionH relativeFrom="column">{horizontal}</wp:positionH>
                <wp:positionV relativeFrom="paragraph">
                  <wp:posOffset>{(long)Math.Round(offsetYPoints * 12700)}</wp:posOffset>
                </wp:positionV>
                <wp:extent cx="{cx}" cy="{cy}"/>
                {wrapElement}
                <wp:docPr id="{id}" name="Shape {id}"/>
                {ShapeGraphic(cx, cy, content, geometry, fillHex, lineHex, lineWidthPoints, insets, anchor)}
              </wp:anchor>
            </w:drawing></w:r>
            """;
    }

    private readonly List<(string Id, string PartName, string Kind, string Body,
        IReadOnlyList<(string Id, string Url)> Hyperlinks,
        IReadOnlyList<(string Id, string Target)> Images)> _headersFooters = [];
    private bool _titlePage;

    /// <summary>
    /// What goes into the settings part, in the order the schema wants it. A document with none of
    /// this has no settings part at all, which is a document Word will still open.
    /// </summary>
    private readonly List<string> _settings = [];

    /// <summary>
    /// What goes inside the settings part's own note properties, which come before everything else
    /// in it and in that order: footnotes then endnotes.
    /// </summary>
    private readonly List<string> _footnoteSettings = [];

    private readonly List<string> _endnoteSettings = [];

    /// <summary>
    /// Adds a header or footer part and references it from the section.
    /// </summary>
    /// <param name="kind">"default", "first" or "even".</param>
    /// <param name="headerHyperlinks">
    /// External addresses for the links in this part, by relationship id. A header owns its own
    /// relationship part, so these ids are independent of the body's and may repeat them.
    /// </param>
    /// <param name="referenceFromFinalSection">
    /// Whether the closing section should point at this part. Pass false to place the reference by
    /// hand — on an earlier section's break, say, to test that a later one inherits it.
    /// </param>
    public DocxBuilder WithHeaderFooter(bool header, string paragraphsXml, string kind = "default",
        IReadOnlyList<(string Id, string Url)>? headerHyperlinks = null,
        bool referenceFromFinalSection = true,
        IReadOnlyList<(string Id, string Target)>? headerImages = null)
    {
        var index = _headersFooters.Count + 1;
        var id = $"rIdHF{index}";
        var name = header ? "header" : "footer";

        // The part is called header1.xml but its root element is w:hdr, not w:header. Word
        // silently ignores a part whose root it does not recognise — no error, no header.
        var root = header ? "hdr" : "ftr";

        if (!referenceFromFinalSection) _unreferenced.Add(id);

        _headersFooters.Add((id, $"word/{name}{index}.xml", $"{name}:{kind}",
            $"""
             <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
             <w:{root} xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                       xmlns:v="urn:schemas-microsoft-com:vml"
                       xmlns:o="urn:schemas-microsoft-com:office:office"
                       xmlns:w10="urn:schemas-microsoft-com:office:word">
               {paragraphsXml}
             </w:{root}>
             """,
            headerHyperlinks ?? [], headerImages ?? []));

        return this;
    }

    /// <summary>
    /// Adds a picture and returns a relationship for a running head to reach it by.
    /// </summary>
    /// <remarks>
    /// A header owns its relationships, so the id here belongs to that part alone and may be the
    /// same id the body uses for a different picture entirely — which is the trap a watermark of a
    /// picture lays for anything that keeps one list of them for the whole document.
    /// </remarks>
    public (string Id, string Target) AddHeaderImage(byte[] data, string id, string extension = "png")
    {
        var name = $"word/media/header{_headerImages.Count + 1}.{extension}";
        _headerImages.Add((name, data));

        return (id, name["word/".Length..]);
    }

    /// <summary>The first page takes its own header and footer.</summary>
    public DocxBuilder WithTitlePage()
    {
        _titlePage = true;
        return this;
    }

    /// <summary>
    /// Word breaks words at the end of a line where it has to, from <c>w:autoHyphenation</c>.
    /// </summary>
    /// <param name="zoneTwips">
    /// How much white a line may be left with before a word is broken to fill it, from
    /// <c>w:hyphenationZone</c>. Word's own default is a quarter of an inch.
    /// </param>
    /// <param name="consecutive">
    /// How many lines in a row may end in a hyphen, from <c>w:consecutiveHyphenLimit</c>. Zero is
    /// no limit at all.
    /// </param>
    public DocxBuilder WithAutoHyphenation(
        int? zoneTwips = null, int? consecutive = null, bool doNotHyphenateCaps = false)
    {
        // CT_Settings is a sequence: these four come in this order and after nothing this builder
        // writes, so they are simply appended.
        _settings.Add("<w:autoHyphenation w:val=\"true\"/>");

        if (consecutive is { } limit)
            _settings.Add($"<w:consecutiveHyphenLimit w:val=\"{limit}\"/>");

        if (zoneTwips is { } zone) _settings.Add($"<w:hyphenationZone w:val=\"{zone}\"/>");
        if (doNotHyphenateCaps) _settings.Add("<w:doNotHyphenateCaps w:val=\"true\"/>");

        return this;
    }

    /// <summary>Odd and even pages take different headers and footers.</summary>
    public DocxBuilder WithEvenAndOddHeaders()
    {
        _settings.Add("<w:evenAndOddHeaders/>");
        return this;
    }

    /// <summary>
    /// How the document numbers its notes: <c>continuous</c> through the whole of it, or restarted
    /// <c>eachPage</c> or <c>eachSect</c>.
    /// </summary>
    public DocxBuilder WithNoteNumbering(string kind, string restart)
    {
        (kind == "footnote" ? _footnoteSettings : _endnoteSettings)
            .Add($"<w:numRestart w:val=\"{restart}\"/>");

        return this;
    }

    /// <summary>
    /// Where the document's endnotes are gathered: <c>docEnd</c> or <c>sectEnd</c>. This one goes
    /// in the settings part rather than the section, which is where Word writes it and — measured
    /// rather than assumed — the only place it reads it from.
    /// </summary>
    public DocxBuilder WithEndnotePosition(string position)
    {
        _endnoteSettings.Add($"<w:pos w:val=\"{position}\"/>");

        return this;
    }

    /// <summary>Whether anything asked for a settings part at all.</summary>
    private bool HasSettings =>
        _settings.Count > 0 || _footnoteSettings.Count > 0 || _endnoteSettings.Count > 0;

    /// <summary>A paragraph holding a simple field, with the value Word would have cached.</summary>
    public static string FieldParagraph(string instruction, string cachedText, string? runProperties = null) =>
        $"""
         <w:p><w:fldSimple w:instr="{instruction}">
           <w:r>{(runProperties is null ? string.Empty : $"<w:rPr>{runProperties}</w:rPr>")}
             <w:t>{cachedText}</w:t></w:r>
         </w:fldSimple></w:p>
         """;

    private string? _numbering;

    /// <summary>
    /// Adds a numbering part. Each entry is one abstract definition's levels; the resulting lists
    /// are numbered from 1 in the order given, so the first is numId 1.
    /// </summary>
    public DocxBuilder WithNumbering(params string[] abstractDefinitions)
    {
        var body = new StringBuilder();

        for (var i = 0; i < abstractDefinitions.Length; i++)
            body.Append($"<w:abstractNum w:abstractNumId=\"{i}\">{abstractDefinitions[i]}</w:abstractNum>");

        for (var i = 0; i < abstractDefinitions.Length; i++)
            body.Append($"<w:num w:numId=\"{i + 1}\"><w:abstractNumId w:val=\"{i}\"/></w:num>");

        _numbering = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              {body}
            </w:numbering>
            """;

        return this;
    }

    /// <summary>
    /// One level of a list definition. Indents follow Word's own pattern: each level is half an
    /// inch further in, with the label hanging back a quarter inch from the text.
    /// </summary>
    public static string NumberingLevel(
        int level, string format, string levelText, int start = 1,
        string? runProperties = null, string? suffix = null)
    {
        var indentLeft = 720 * (level + 1);
        var suffixElement = suffix is null ? string.Empty : $"<w:suff w:val=\"{suffix}\"/>";

        return $"""
            <w:lvl w:ilvl="{level}">
              <w:start w:val="{start}"/>
              <w:numFmt w:val="{format}"/>
              <w:lvlText w:val="{levelText}"/>
              {suffixElement}
              <w:lvlJc w:val="left"/>
              <w:pPr><w:ind w:left="{indentLeft}" w:hanging="360"/></w:pPr>
              {(runProperties is null ? string.Empty : $"<w:rPr>{runProperties}</w:rPr>")}
            </w:lvl>
            """;
    }

    /// <summary>Appends a paragraph belonging to a list.</summary>
    public DocxBuilder AddListParagraph(
        string text, int numId, int level = 0, string? runProperties = null)
    {
        // numPr precedes spacing and indent in CT_PPr.
        return AddParagraph(text,
            $"<w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr>",
            runProperties);
    }

    public byte[] Build()
    {
        var document = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                        xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
                        xmlns:v="urn:schemas-microsoft-com:vml"
                        xmlns:o="urn:schemas-microsoft-com:office:office"
                        xmlns:w10="urn:schemas-microsoft-com:office:word"
                        xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <w:body>{_body}{BuildSectionProperties()}</w:body>
            </w:document>
            """;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", BuildContentTypes());
            Write(archive, "_rels/.rels", BuildPackageRelationships());
            Write(archive, "word/document.xml", document);
            Write(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships());
            Write(archive, "word/styles.xml", _styles);
            Write(archive, "word/theme/theme1.xml", _theme);
            if (_numbering is not null) Write(archive, "word/numbering.xml", _numbering);
            if (_coreProperties is not null) Write(archive, "docProps/core.xml", _coreProperties);
            if (_appProperties is not null) Write(archive, "docProps/app.xml", _appProperties);
            if (_customProperties is not null) Write(archive, "docProps/custom.xml", _customProperties);
            if (HasSettings) Write(archive, "word/settings.xml", BuildSettings());

            foreach (var (partName, _, body, _, own) in _parts)
            {
                Write(archive, partName, body);
                if (own.Count == 0) continue;

                var relationships = new StringBuilder();
                foreach (var (id, type, target) in own)
                    relationships.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"/>");

                var directory = Path.GetDirectoryName(partName)!.Replace('\\', '/');
                Write(archive,
                    $"{directory}/_rels/{Path.GetFileName(partName)}.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    relationships + "</Relationships>");
            }

            if (_footnotes.Count > 0) Write(archive, "word/footnotes.xml", BuildNotes("footnote", _footnotes));
            if (_endnotes.Count > 0) Write(archive, "word/endnotes.xml", BuildNotes("endnote", _endnotes));

            foreach (var (_, partName, _, body, hyperlinks, images) in _headersFooters)
            {
                Write(archive, partName, body);

                // A part that references anything needs its own relationship part beside it, in a
                // _rels folder next to the part itself.
                if (hyperlinks.Count == 0 && images.Count == 0) continue;

                var relationships = new StringBuilder();
                foreach (var (id, url) in hyperlinks)
                {
                    relationships.Append(
                        $"<Relationship Id=\"{id}\" " +
                        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" " +
                        $"Target=\"{Escape(url)}\" TargetMode=\"External\"/>");
                }

                foreach (var (id, target) in images)
                {
                    relationships.Append(
                        $"<Relationship Id=\"{id}\" " +
                        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
                        $"Target=\"{target}\"/>");
                }

                var directory = Path.GetDirectoryName(partName)!.Replace('\\', '/');
                Write(archive,
                    $"{directory}/_rels/{Path.GetFileName(partName)}.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    relationships + "</Relationships>");
            }

            foreach (var (partName, data) in _headerImages)
            {
                var part = archive.CreateEntry(partName, CompressionLevel.Optimal);
                part.LastWriteTime = FixedTimestamp;
                using var bytes = part.Open();
                bytes.Write(data);
            }

            foreach (var (_, partName, data) in _images)
            {
                var entry = archive.CreateEntry(partName, CompressionLevel.Optimal);
                entry.LastWriteTime = FixedTimestamp;
                using var stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Builds the document and writes it to a file, returning the path.</summary>
    public string BuildToFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Build());
        return path;
    }

    public Stream BuildStream() => new MemoryStream(Build());

    /// <summary>
    /// A fixed timestamp for every zip entry.
    /// </summary>
    /// <remarks>
    /// Zip entries record a modification time, which defaults to now. Fixtures are regenerated on
    /// every test run and committed to the repository, so without a fixed value their bytes
    /// change on every run and the working tree is permanently dirty for no reason.
    /// </remarks>
    private static readonly DateTimeOffset FixedTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The section, with header and footer references spliced in. CT_SectPr is a sequence and the
    /// references come first, before the page size.
    /// </summary>
    private string BuildSectionProperties()
    {
        if (_headersFooters.Count == 0 && !_titlePage) return _sectionProperties;

        var references = new StringBuilder();
        foreach (var (id, _, kind, _, _, _) in _headersFooters)
        {
            if (_unreferenced.Contains(id)) continue;

            var parts = kind.Split(':');
            references.Append($"<w:{parts[0]}Reference w:type=\"{parts[1]}\" r:id=\"{id}\"/>");
        }

        var titlePage = _titlePage ? "<w:titlePg/>" : string.Empty;

        return _sectionProperties
            .Replace("<w:sectPr>", "<w:sectPr>" + references)
            .Replace("</w:sectPr>", titlePage + "</w:sectPr>");
    }

    private string BuildSettings() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        (_footnoteSettings.Count > 0 ? $"<w:footnotePr>{string.Concat(_footnoteSettings)}</w:footnotePr>" : "") +
        (_endnoteSettings.Count > 0 ? $"<w:endnotePr>{string.Concat(_endnoteSettings)}</w:endnotePr>" : "") +
        string.Concat(_settings) +
        "</w:settings>";

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;

        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Builds a <c>w:rPr</c> body with its children in schema order.
    /// </summary>
    /// <remarks>
    /// <c>CT_RPr</c> is a sequence, not a choice: the children have a required order, and
    /// <c>b</c>, <c>i</c>, <c>strike</c> and <c>color</c> all come <em>before</em> <c>sz</c>.
    /// Concatenating property strings by hand gets this wrong silently — our own parser does not
    /// care about order, so the mistake is invisible until Word refuses to open the document.
    /// Every fixture goes through here so the order is right by construction.
    /// </remarks>
    public static string RunProperties(
        string? font = null,
        int? halfPoints = null,
        bool bold = false,
        bool italic = false,
        bool caps = false,
        bool smallCaps = false,
        bool strike = false,
        string? color = null,
        string? highlight = null,
        string? underline = null,
        string? verticalAlign = null,
        string? styleId = null,
        int? kerningHalfPoints = null,
        int? positionHalfPoints = null)
    {
        var sb = new StringBuilder();

        // Order below follows ECMA-376 CT_RPr exactly. Do not rearrange.
        if (styleId is not null) sb.Append($"<w:rStyle w:val=\"{styleId}\"/>");
        if (font is not null) sb.Append($"<w:rFonts w:ascii=\"{font}\" w:hAnsi=\"{font}\"/>");
        if (bold) sb.Append("<w:b/>");
        if (italic) sb.Append("<w:i/>");
        if (caps) sb.Append("<w:caps/>");
        if (smallCaps) sb.Append("<w:smallCaps/>");
        if (strike) sb.Append("<w:strike/>");
        if (color is not null) sb.Append($"<w:color w:val=\"{color}\"/>");
        if (kerningHalfPoints is not null) sb.Append($"<w:kern w:val=\"{kerningHalfPoints}\"/>");
        if (positionHalfPoints is not null) sb.Append($"<w:position w:val=\"{positionHalfPoints}\"/>");
        if (halfPoints is not null) sb.Append($"<w:sz w:val=\"{halfPoints}\"/>");
        if (highlight is not null) sb.Append($"<w:highlight w:val=\"{highlight}\"/>");
        if (underline is not null) sb.Append($"<w:u w:val=\"{underline}\"/>");
        if (verticalAlign is not null) sb.Append($"<w:vertAlign w:val=\"{verticalAlign}\"/>");

        return sb.ToString();
    }

    /// <summary>
    /// Content types, with a Default entry for each image extension in use. A part whose type is
    /// undeclared makes the package invalid, so this has to track what was actually added.
    /// </summary>
    private string BuildContentTypes()
    {
        var defaults = new StringBuilder();
        foreach (var extension in _images.Select(i => Path.GetExtension(i.PartName).TrimStart('.'))
                     .Concat(_headerImages.Select(i => Path.GetExtension(i.PartName).TrimStart('.')))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var type = extension.ToLowerInvariant() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "tif" or "tiff" => "image/tiff",
                "emf" => "image/x-emf",
                _ => "application/octet-stream"
            };

            defaults.Append($"<Default Extension=\"{extension}\" ContentType=\"{type}\"/>");
        }

        foreach (var (partName, contentType, _, _, _) in _parts)
            defaults.Append($"<Override PartName=\"/{partName}\" ContentType=\"{contentType}\"/>");

        foreach (var (_, partName, kind, _, _, _) in _headersFooters)
        {
            var type = kind.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            defaults.Append(
                $"<Override PartName=\"/{partName}\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.{type}+xml\"/>");
        }

        foreach (var kind in NoteKindsInUse())
        {
            defaults.Append(
                $"<Override PartName=\"/word/{kind}s.xml\" " +
                $"ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}s+xml\"/>");
        }

        if (HasSettings)
        {
            defaults.Append(
                "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
        }

        if (_coreProperties is not null)
        {
            defaults.Append(
                "<Override PartName=\"/docProps/core.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
        }

        if (_appProperties is not null)
        {
            defaults.Append(
                "<Override PartName=\"/docProps/app.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
        }

        if (_customProperties is not null)
        {
            defaults.Append(
                "<Override PartName=\"/docProps/custom.xml\" " +
                "ContentType=\"application/vnd.openxmlformats-officedocument.custom-properties+xml\"/>");
        }

        var numberingType = _numbering is null
            ? string.Empty
            : "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>";

        return ContentTypes
            .Replace("<!--IMAGE_DEFAULTS-->", defaults.ToString())
            .Replace("<!--NUMBERING_TYPE-->", numberingType);
    }

    /// <summary>Which notes parts this document has, as the element name each one uses.</summary>
    private IEnumerable<string> NoteKindsInUse()
    {
        if (_footnotes.Count > 0) yield return "footnote";
        if (_endnotes.Count > 0) yield return "endnote";
    }

    private static string BuildNotes(string kind, List<(int Id, string Type, string Body)> notes)
    {
        var parts = new StringBuilder();
        foreach (var (id, type, body) in notes)
        {
            var typeAttribute = type == "normal" ? string.Empty : $" w:type=\"{type}\"";
            parts.Append($"<w:{kind}{typeAttribute} w:id=\"{id}\">{body}</w:{kind}>");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               $"<w:{kind}s xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
               "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
               parts + $"</w:{kind}s>";
    }

    private string BuildDocumentRelationships()
    {
        var extra = new StringBuilder();

        foreach (var (partName, _, _, fromDocument, _) in _parts)
        {
            if (fromDocument is not { } reference) continue;

            extra.Append(
                $"<Relationship Id=\"{reference.Id}\" Type=\"{reference.Type}\" " +
                $"Target=\"{partName["word/".Length..]}\"/>");
        }

        foreach (var (id, partName, _) in _images)
        {
            extra.Append(
                $"<Relationship Id=\"{id}\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" " +
                $"Target=\"{partName["word/".Length..]}\"/>");
        }

        if (_numbering is not null)
        {
            extra.Append(
                "<Relationship Id=\"rIdNum\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" " +
                "Target=\"numbering.xml\"/>");
        }

        foreach (var (id, partName, kind, _, _, _) in _headersFooters)
        {
            var type = kind.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            extra.Append(
                $"<Relationship Id=\"{id}\" " +
                $"Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{type}\" " +
                $"Target=\"{partName["word/".Length..]}\"/>");
        }

        foreach (var kind in NoteKindsInUse())
        {
            extra.Append(
                $"<Relationship Id=\"rId{char.ToUpperInvariant(kind[0])}{kind[1..]}s\" " +
                $"Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{kind}s\" " +
                $"Target=\"{kind}s.xml\"/>");
        }

        foreach (var (id, url) in _hyperlinks)
        {
            extra.Append(
                $"<Relationship Id=\"{id}\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" " +
                $"Target=\"{Escape(url)}\" TargetMode=\"External\"/>");
        }

        if (HasSettings)
        {
            extra.Append(
                "<Relationship Id=\"rIdSettings\" " +
                "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" " +
                "Target=\"settings.xml\"/>");
        }

        return DocumentRelationships.Replace("<!--IMAGE_RELATIONSHIPS-->", extra.ToString());
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <!--IMAGE_DEFAULTS-->
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <!--NUMBERING_TYPE-->
          <Override PartName="/word/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>
        </Types>
        """;

    private string BuildPackageRelationships()
    {
        const string relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        const string metadata = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/";

        // A document with no properties keeps the part it always had, byte for byte: every
        // fixture is regenerated on every run and committed, and rewriting them all to say the
        // same thing differently would be churn in the working tree for nothing.
        if (_coreProperties is null && _appProperties is null && _customProperties is null)
            return PackageRelationships;

        var parts = new StringBuilder();
        parts.Append($"<Relationship Id=\"rId1\" Type=\"{relationships}officeDocument\" Target=\"word/document.xml\"/>");

        if (_coreProperties is not null)
            parts.Append($"<Relationship Id=\"rId2\" Type=\"{metadata}core-properties\" Target=\"docProps/core.xml\"/>");

        if (_appProperties is not null)
            parts.Append($"<Relationship Id=\"rId3\" Type=\"{relationships}extended-properties\" Target=\"docProps/app.xml\"/>");

        if (_customProperties is not null)
            parts.Append($"<Relationship Id=\"rId4\" Type=\"{relationships}custom-properties\" Target=\"docProps/custom.xml\"/>");

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               parts + "</Relationships>";
    }

    private const string PackageRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    private const string DocumentRelationships = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="theme/theme1.xml"/>
          <!--IMAGE_RELATIONSHIPS-->
        </Relationships>
        """;

    /// <summary>
    /// Document defaults matching what Word writes for a new blank document: 11pt body text with
    /// the theme's minor font, and the 8pt-after / 1.08-line spacing of the modern Normal style.
    /// </summary>
    public const string DefaultStyles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults>
            <w:rPrDefault>
              <w:rPr>
                <w:rFonts w:asciiTheme="minorHAnsi" w:hAnsiTheme="minorHAnsi"/>
                <w:sz w:val="22"/>
              </w:rPr>
            </w:rPrDefault>
            <w:pPrDefault>
              <w:pPr>
                <w:spacing w:after="160" w:line="259" w:lineRule="auto"/>
              </w:pPr>
            </w:pPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
            <w:name w:val="Normal"/>
          </w:style>
        </w:styles>
        """;

    /// <summary>
    /// Builds a theme part.
    /// </summary>
    /// <remarks>
    /// All of this boilerplate is load-bearing. DrawingML requires <c>themeElements</c> to carry
    /// <c>clrScheme</c>, <c>fontScheme</c> and <c>fmtScheme</c> in that order, requires the colour
    /// scheme to define all twelve slots, requires each font collection to declare
    /// <c>latin</c>, <c>ea</c> and <c>cs</c>, and requires each format-scheme list to hold exactly
    /// three entries. A theme carrying only the fonts we care about parses fine with our own
    /// reader but makes Word refuse the document with "unreadable content".
    /// </remarks>
    private static string BuildTheme(string majorLatin, string minorLatin) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office Theme">
          <a:themeElements>
            <a:clrScheme name="Office">
              <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
              <a:dk2><a:srgbClr val="44546A"/></a:dk2>
              <a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
              <a:accent1><a:srgbClr val="4472C4"/></a:accent1>
              <a:accent2><a:srgbClr val="ED7D31"/></a:accent2>
              <a:accent3><a:srgbClr val="A5A5A5"/></a:accent3>
              <a:accent4><a:srgbClr val="FFC000"/></a:accent4>
              <a:accent5><a:srgbClr val="5B9BD5"/></a:accent5>
              <a:accent6><a:srgbClr val="70AD47"/></a:accent6>
              <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
              <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Office">
              <a:majorFont>
                <a:latin typeface="{majorLatin}"/>
                <a:ea typeface=""/>
                <a:cs typeface=""/>
              </a:majorFont>
              <a:minorFont>
                <a:latin typeface="{minorLatin}"/>
                <a:ea typeface=""/>
                <a:cs typeface=""/>
              </a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Office">
              <a:fillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:fillStyleLst>
              <a:lnStyleLst>
                <a:ln w="6350" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
                <a:ln w="12700" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
                <a:ln w="19050" cap="flat" cmpd="sng" algn="ctr"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
              </a:lnStyleLst>
              <a:effectStyleLst>
                <a:effectStyle><a:effectLst/></a:effectStyle>
                <a:effectStyle><a:effectLst/></a:effectStyle>
                <a:effectStyle><a:effectLst/></a:effectStyle>
              </a:effectStyleLst>
              <a:bgFillStyleLst>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
                <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
              </a:bgFillStyleLst>
            </a:fmtScheme>
          </a:themeElements>
        </a:theme>
        """;

    private static readonly string DefaultTheme = BuildTheme("Calibri Light", "Calibri");
}
