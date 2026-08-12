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
        IReadOnlyList<(int Width, int Space)>? columnWidths = null)
    {
        var typeXml = type is null ? string.Empty : $"<w:type w:val=\"{type}\"/>";
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
              {references}{typeXml}<w:pgSz w:w="{widthTwips}" w:h="{heightTwips}"{orientation}/>
              <w:pgMar w:top="{top}" w:right="{right}" w:bottom="{bottom}" w:left="{left}" w:header="720" w:footer="720" w:gutter="0"/>
              {Columns(columns, columnSpaceTwips, columnSeparator, columnWidths)}{(verticalAlignment is null ? string.Empty : $"<w:vAlign w:val=\"{verticalAlignment}\"/>")}{(titlePage ? "<w:titlePg/>" : string.Empty)}
            </w:sectPr>
            """;
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

    private readonly List<(string Id, string PartName, byte[] Data)> _images = [];

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
        string? paragraphProperties = null, string? leadingText = null)
    {
        var cx = (long)Math.Round(widthPoints * 12700);
        var cy = (long)Math.Round(heightPoints * 12700);

        _body.Append("<w:p>");
        if (paragraphProperties is not null) _body.Append($"<w:pPr>{paragraphProperties}</w:pPr>");

        if (leadingText is not null)
            _body.Append($"<w:r><w:t xml:space=\"preserve\">{Escape(leadingText)}</w:t></w:r>");

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

    private readonly List<(string Id, string PartName, string Kind, string Body,
        IReadOnlyList<(string Id, string Url)> Hyperlinks)> _headersFooters = [];
    private bool _titlePage;
    private bool _evenAndOddHeaders;

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
        bool referenceFromFinalSection = true)
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
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
               {paragraphsXml}
             </w:{root}>
             """,
            headerHyperlinks ?? []));

        return this;
    }

    /// <summary>The first page takes its own header and footer.</summary>
    public DocxBuilder WithTitlePage()
    {
        _titlePage = true;
        return this;
    }

    /// <summary>Odd and even pages take different headers and footers.</summary>
    public DocxBuilder WithEvenAndOddHeaders()
    {
        _evenAndOddHeaders = true;
        return this;
    }

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
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <w:body>{_body}{BuildSectionProperties()}</w:body>
            </w:document>
            """;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", BuildContentTypes());
            Write(archive, "_rels/.rels", PackageRelationships);
            Write(archive, "word/document.xml", document);
            Write(archive, "word/_rels/document.xml.rels", BuildDocumentRelationships());
            Write(archive, "word/styles.xml", _styles);
            Write(archive, "word/theme/theme1.xml", _theme);
            if (_numbering is not null) Write(archive, "word/numbering.xml", _numbering);
            if (_evenAndOddHeaders) Write(archive, "word/settings.xml", EvenOddSettings);

            if (_footnotes.Count > 0) Write(archive, "word/footnotes.xml", BuildNotes("footnote", _footnotes));
            if (_endnotes.Count > 0) Write(archive, "word/endnotes.xml", BuildNotes("endnote", _endnotes));

            foreach (var (_, partName, _, body, hyperlinks) in _headersFooters)
            {
                Write(archive, partName, body);

                // A part that references anything needs its own relationship part beside it, in a
                // _rels folder next to the part itself.
                if (hyperlinks.Count == 0) continue;

                var relationships = new StringBuilder();
                foreach (var (id, url) in hyperlinks)
                {
                    relationships.Append(
                        $"<Relationship Id=\"{id}\" " +
                        "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" " +
                        $"Target=\"{Escape(url)}\" TargetMode=\"External\"/>");
                }

                var directory = Path.GetDirectoryName(partName)!.Replace('\\', '/');
                Write(archive,
                    $"{directory}/_rels/{Path.GetFileName(partName)}.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    relationships + "</Relationships>");
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
        foreach (var (id, _, kind, _, _) in _headersFooters)
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

    private const string EvenOddSettings = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:evenAndOddHeaders/>
        </w:settings>
        """;

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;

        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Escape(string text) =>
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
        string? underline = null,
        string? verticalAlign = null,
        string? styleId = null,
        int? kerningHalfPoints = null)
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
        if (halfPoints is not null) sb.Append($"<w:sz w:val=\"{halfPoints}\"/>");
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
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var type = extension.ToLowerInvariant() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                _ => "application/octet-stream"
            };

            defaults.Append($"<Default Extension=\"{extension}\" ContentType=\"{type}\"/>");
        }

        foreach (var (_, partName, kind, _, _) in _headersFooters)
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

        if (_evenAndOddHeaders)
        {
            defaults.Append(
                "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
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

        foreach (var (id, partName, kind, _, _) in _headersFooters)
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

        if (_evenAndOddHeaders)
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
