using n8PDF.Layout;

namespace n8PDF.Pdf;

/// <summary>
/// Draws a laid-out document into a PDF. This is the only place that converts from Word's
/// top-left page origin to PDF's bottom-left one; everything upstream works in Word's frame.
/// </summary>
internal static class PdfRenderer
{
    /// <summary>
    /// Slant applied when a font family has no italic face. Roughly 12 degrees, which is the
    /// conventional synthetic-oblique angle.
    /// </summary>
    private const double SyntheticItalicSkew = 0.21256;

    /// <summary>Stroke width for synthetic bold, as a fraction of the font size.</summary>
    private const double SyntheticBoldStrokeRatio = 0.022;

    /// <summary>
    /// How far a link's clickable region extends past each end of its text, in points.
    /// </summary>
    /// <remarks>
    /// Measured from Word's own exports: it pads by 0.03 inch, and by the same amount at 24pt as
    /// at 12pt, so the padding is fixed rather than proportional to the type size.
    /// </remarks>
    private const double LinkPaddingPoints = 2.16;

    public static void Render(LaidOutDocument document, PdfBuilder builder)
    {
        var pages = new List<(LaidOutPage Source, PdfPage Target)>();

        foreach (var page in document.Pages)
        {
            var target = builder.AddPage(page.WidthPoints, page.HeightPoints);
            var content = target.Content;
            pages.Add((page, target));

            // Shading and table borders go down first, in the order layout added them: fills
            // before the borders that sit on top of them, and both before any text.
            foreach (var rectangle in page.Rectangles)
            {
                content.Save()
                    .SetFillColor(rectangle.Color.Red, rectangle.Color.Green, rectangle.Color.Blue)
                    .Rectangle(rectangle.X, Flip(page, rectangle.Y) - rectangle.Height,
                        rectangle.Width, rectangle.Height)
                    .Fill()
                    .Restore();
            }

            foreach (var image in page.Images)
            {
                // An image XObject is drawn into the unit square, so placing one means scaling by
                // its display size and translating to its bottom-left corner.
                content.Save()
                    .Transform(image.Width, 0, 0, image.Height, image.X, Flip(page, image.Y) - image.Height)
                    .DrawXObject(builder.UseImage(image.Image).ResourceName)
                    .Restore();
            }

            // Rules go down next so that text sits on top of any underline.
            foreach (var rule in page.Rules)
            {
                content.Save()
                    .SetFillColor(rule.Color.Red, rule.Color.Green, rule.Color.Blue)
                    .Rectangle(rule.X, Flip(page, rule.Y) - rule.Thickness, rule.Width, rule.Thickness)
                    .Fill()
                    .Restore();
            }

            foreach (var text in page.Texts)
                RenderText(builder, content, page, text);
        }

        // Annotations are added after every page exists, because an internal link needs a
        // reference to the page it points at and that page may come later in the document.
        foreach (var (source, target) in pages)
            AddLinkAnnotations(document, builder, source, target);
    }

    /// <summary>
    /// Lays a clickable region over every run that carries a link.
    /// </summary>
    /// <remarks>
    /// Runs are merged where they share a target, so a linked phrase broken into several runs by
    /// formatting becomes one region rather than a row of adjacent ones. A link split across two
    /// lines still gets one region per line, which is what a reader expects.
    /// </remarks>
    private static void AddLinkAnnotations(
        LaidOutDocument document, PdfBuilder builder, LaidOutPage source, PdfPage target)
    {
        foreach (var line in source.Lines)
        {
            PositionedText? start = null;
            PositionedText? end = null;

            void Flush()
            {
                if (start is null || end is null) return;

                var link = start.Link!;

                // The clickable region is the run's line box rather than its glyph bounds, which
                // is what Word does: a link is clickable a little above and below the letters, and
                // measuring per run keeps a small link on a line of large text from swelling to
                // the height of its neighbours.
                var metrics = start.Font.Font.Metrics;
                var size = start.FontSizePoints;
                var ascent = metrics.ToPoints(metrics.DefaultAscent, size);
                var height = metrics.ToPoints(metrics.DefaultLineHeight, size);

                var top = start.BaselineY - ascent;
                var bottom = top + height;

                var annotation = new PdfDictionary()
                    .Set("Type", "Annot")
                    .Set("Subtype", "Link")
                    .Set("Rect", new PdfArray()
                        .Add(start.X - LinkPaddingPoints)
                        .Add(Flip(source, bottom))
                        .Add(end.X + end.Width + LinkPaddingPoints)
                        .Add(Flip(source, top)))
                    // Without this most viewers draw a black box around every link.
                    .Set("Border", new PdfArray().Add(0).Add(0).Add(0));

                if (link.Url is { } url)
                {
                    annotation.Set("A", new PdfDictionary()
                        .Set("S", "URI")
                        .Set("URI", PdfString.FromText(url)));
                }
                else if (link.Anchor is { } anchor &&
                         document.Bookmarks.TryGetValue(anchor, out var destination) &&
                         destination.PageIndex >= 0 && destination.PageIndex < document.Pages.Count)
                {
                    // XYZ with a null zoom means "go here and leave the magnification alone".
                    var page = document.Pages[destination.PageIndex];
                    annotation.Set("Dest", new PdfArray()
                        .Add(builder.Document.GetPageReference(destination.PageIndex))
                        .Add(new PdfName("XYZ"))
                        .Add(destination.X)
                        .Add(page.HeightPoints - destination.Y)
                        .Add(PdfNull.Instance));
                }
                else
                {
                    // An anchor pointing at a bookmark that is not in the document leads nowhere,
                    // so no region is created rather than one that does nothing when clicked.
                    start = null;
                    end = null;
                    return;
                }

                target.Annotations.Add(builder.Document.Add(annotation));

                start = null;
                end = null;
            }

            foreach (var text in line.Texts)
            {
                if (text.Link is null)
                {
                    Flush();
                    continue;
                }

                if (start is not null && !Equals(start.Link, text.Link)) Flush();

                start ??= text;
                end = text;
            }

            Flush();
        }
    }

    private static void RenderText(PdfBuilder builder, ContentStreamBuilder content, LaidOutPage page, PositionedText text)
    {
        var format = text.Format;
        var selection = text.Font;
        var font = builder.UseFont(selection.Font);
        var size = format.EffectiveFontSizePoints;
        var (red, green, blue) = format.GetColor();

        content.Save();
        content.SetFillColor(red, green, blue);

        content.BeginText();
        content.SetFont(font.ResourceName, size);

        if (selection.SyntheticBold)
        {
            // Fill and stroke the glyphs with a hairline outline, which thickens the stems
            // without a real bold face available.
            content.SetTextRenderMode(2);
            content.SetStrokeColor(red, green, blue);
            content.SetLineWidth(size * SyntheticBoldStrokeRatio);
        }

        if (format.ScaleFactor != 1.0)
            content.SetHorizontalScaling(format.ScaleFactor * 100);

        if (format.CharacterSpacingPoints != 0)
            content.SetCharacterSpacing(format.CharacterSpacingPoints);

        var y = Flip(page, text.BaselineY);

        if (selection.SyntheticItalic)
            content.SetTextPositionSkewed(text.X, y, SyntheticItalicSkew);
        else
            content.SetTextPosition(text.X, y);

        ShowText(content, font, text, size);

        content.EndText();
        content.Restore();
    }

    /// <summary>
    /// Shows a run, with the pen moved between characters where justification or kerning asks for
    /// it.
    /// </summary>
    /// <remarks>
    /// The <c>Tw</c> operator cannot space an Identity-H font, and nothing in a PDF font can kern
    /// at all, so both are emitted as explicit <c>TJ</c> adjustments. Adjustments are in
    /// thousandths of an em and are subtracted from the pen, so widening a space takes a negative
    /// value and tightening a kerned pair a positive one.
    ///
    /// Every adjacent pair is kerned, spaces included, because that is what was measured: layout
    /// splits its text at spaces but folds the pair straddling each split back in, so a run's
    /// width already accounts for them.
    /// </remarks>
    private static void ShowText(ContentStreamBuilder content, PdfFont font, PositionedText text, double size)
    {
        var justifying = text.WordSpacing > 0 && text.Text.Contains(' ');

        if (!justifying && !text.Kerned)
        {
            content.ShowGlyphs(font.Encode(text.Text));
            return;
        }

        var spaceAdjustment = -text.WordSpacing * 1000.0 / size;
        var units = text.Font.Font.Metrics.UnitsPerEm;

        var segments = new List<(byte[] Encoded, double Adjustment)>();
        var start = 0;

        for (var i = 0; i < text.Text.Length; i++)
        {
            var adjustment = 0.0;

            if (justifying && text.Text[i] == ' ') adjustment += spaceAdjustment;

            if (text.Kerned && i + 1 < text.Text.Length &&
                !char.IsSurrogate(text.Text[i]) && !char.IsSurrogate(text.Text[i + 1]))
            {
                var kerning = text.Font.Font.GetKerning(
                    text.Font.Font.GetGlyphIndex(text.Text[i]),
                    text.Font.Font.GetGlyphIndex(text.Text[i + 1]));

                if (kerning != 0 && units > 0) adjustment += -kerning * 1000.0 / units;
            }

            if (adjustment == 0) continue;

            // The character the adjustment follows goes in the chunk before it.
            segments.Add((font.Encode(text.Text[start..(i + 1)]), adjustment));
            start = i + 1;
        }

        if (segments.Count == 0)
        {
            content.ShowGlyphs(font.Encode(text.Text));
            return;
        }

        if (start < text.Text.Length)
            segments.Add((font.Encode(text.Text[start..]), 0));

        content.ShowGlyphsAdjusted(segments);
    }

    /// <summary>
    /// Converts a Y measured downward from the page top into PDF user space, where Y grows
    /// upward from the page bottom.
    /// </summary>
    private static double Flip(LaidOutPage page, double y) => page.HeightPoints - y;
}
