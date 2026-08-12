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

    public DocxBuilder AddEmptyParagraph()
    {
        _body.Append("<w:p/>");
        return this;
    }

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

    public byte[] Build()
    {
        var document = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <w:body>{_body}{_sectionProperties}</w:body>
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
        string? styleId = null)
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

        return ContentTypes.Replace("<!--IMAGE_DEFAULTS-->", defaults.ToString());
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
