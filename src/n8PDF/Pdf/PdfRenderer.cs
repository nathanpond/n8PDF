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

    public static void Render(LaidOutDocument document, PdfBuilder builder)
    {
        foreach (var page in document.Pages)
        {
            var target = builder.AddPage(page.WidthPoints, page.HeightPoints);
            var content = target.Content;

            // Rules go down first so that text sits on top of any underline.
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

        if (text.WordSpacing > 0 && text.Text.Contains(' '))
            ShowJustified(content, font, text, size);
        else
            content.ShowGlyphs(font.Encode(text.Text));

        content.EndText();
        content.Restore();
    }

    /// <summary>
    /// Shows text with justification spacing applied after each space.
    /// </summary>
    /// <remarks>
    /// The <c>Tw</c> operator cannot do this for an Identity-H font, so the extra space is
    /// emitted as an explicit <c>TJ</c> adjustment instead. Adjustments are in thousandths of an
    /// em and are subtracted from the pen position, so widening a space needs a negative value.
    /// </remarks>
    private static void ShowJustified(ContentStreamBuilder content, PdfFont font, PositionedText text, double size)
    {
        var adjustment = -text.WordSpacing * 1000.0 / size;
        var segments = new List<(byte[] Encoded, double Adjustment)>();

        var start = 0;
        for (var i = 0; i < text.Text.Length; i++)
        {
            if (text.Text[i] != ' ') continue;

            // Include the space itself in the preceding chunk, then push the following text out
            // by the justification amount.
            segments.Add((font.Encode(text.Text[start..(i + 1)]), adjustment));
            start = i + 1;
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
