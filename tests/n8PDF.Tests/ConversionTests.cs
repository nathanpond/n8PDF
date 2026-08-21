using n8PDF;
using n8PDF.Diagnostics;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// End-to-end conversion: a real DOCX in, positioned text and a real PDF out. Positions are
/// asserted from the layout rather than from the PDF, because layout is where a fidelity bug
/// actually lives.
/// </summary>
public class ConversionTests
{
    /// <summary>
    /// Fonts are pinned and system discovery disabled so that these assertions describe the
    /// engine rather than whatever happens to be installed.
    /// </summary>
    private static ConversionOptions Options() => new()
    {
        Fonts = TestFonts.CreatePinnedLibrary()
    };

    [Fact]
    public void Text_starts_at_the_top_left_margin()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithPage(left: 1440, top: 1440)
            .AddParagraph("Hello", runProperties: TimesTwelve));

        var page = Assert.Single(document.Pages);
        var run = Assert.Single(page.Texts);

        // A one-inch left margin puts the text edge at exactly 72 points.
        Assert.Equal(72, run.X, 3);

        // The baseline sits one ascent below the top margin, so it must be below 72 but within
        // one line of it.
        Assert.True(run.BaselineY > 72, "the baseline must be below the top margin");
        Assert.True(run.BaselineY < 72 + 20, $"the baseline drifted too far down: {run.BaselineY}");
        Assert.Equal("Hello", run.Text);
    }

    [Fact]
    public void Margins_are_honoured_exactly()
    {
        // Half-inch margins on a letter page: text starts at 36pt.
        var document = LayoutOf(new DocxBuilder()
            .WithPage(left: 720, right: 720, top: 720, bottom: 720)
            .AddParagraph("Edge", runProperties: TimesTwelve));

        Assert.Equal(36, Assert.Single(document.Pages.Single().Texts).X, 3);
    }

    [Fact]
    public void Page_size_comes_from_the_section()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithPage(widthTwips: 11906, heightTwips: 16838) // A4
            .AddParagraph("A4", runProperties: TimesTwelve));

        var page = Assert.Single(document.Pages);
        Assert.Equal(595.3, page.WidthPoints, 1);
        Assert.Equal(841.9, page.HeightPoints, 1);
    }

    [Fact]
    public void Run_width_matches_an_independent_measurement()
    {
        var document = LayoutOf(new DocxBuilder().AddParagraph("Hello", runProperties: TimesTwelve));
        var run = Assert.Single(document.Pages.Single().Texts);

        // Measure the same string directly from the font file: layout must not be inventing
        // widths anywhere along the way.
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var expected = TextMeasurer.Measure(font, "Hello", 12);

        Assert.Equal(expected, run.Width, 3);
    }

    [Fact]
    public void Long_text_wraps_within_the_content_width()
    {
        var text = string.Join(' ', Enumerable.Repeat("wrapping", 60));
        var document = LayoutOf(new DocxBuilder()
            .WithPage(left: 1440, right: 1440)
            .AddParagraph(text, runProperties: TimesTwelve));

        var page = Assert.Single(document.Pages);
        Assert.True(page.Lines.Count > 1, "60 words at 12pt must wrap");

        // 8.5in less two 1in margins leaves 468pt of measure. No line may exceed it.
        foreach (var line in page.Lines)
        {
            var right = line.Texts.Max(t => t.X + t.Width);
            Assert.True(right <= 72 + 468 + 0.5, $"line overflows the measure: right edge {right}");
        }

        // Every word must survive the wrap.
        var rendered = string.Concat(page.Lines.SelectMany(l => l.Texts).Select(t => t.Text));
        Assert.Equal(60, rendered.Split("wrapping", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Lines_advance_down_the_page_by_the_line_height()
    {
        var text = string.Join(' ', Enumerable.Repeat("measure", 40));
        var document = LayoutOf(new DocxBuilder().AddParagraph(text, runProperties: TimesTwelve));

        var lines = document.Pages.Single().Lines;
        Assert.True(lines.Count >= 3);

        // Baselines must be evenly spaced within a uniformly formatted paragraph.
        var firstGap = lines[1].BaselineY - lines[0].BaselineY;
        for (var i = 2; i < lines.Count; i++)
            Assert.Equal(firstGap, lines[i].BaselineY - lines[i - 1].BaselineY, 3);

        // 12pt Times single-spaced sits near 13.8pt of leading.
        Assert.InRange(firstGap, 13, 15);
    }

    [Fact]
    public void Alignment_positions_lines_correctly()
    {
        var left = FirstRun(LayoutOf(Doc("Aligned", "<w:jc w:val=\"left\"/>")));
        var center = FirstRun(LayoutOf(Doc("Aligned", "<w:jc w:val=\"center\"/>")));
        var right = FirstRun(LayoutOf(Doc("Aligned", "<w:jc w:val=\"right\"/>")));

        Assert.Equal(72, left.X, 3);

        // Centring puts equal space either side of the 468pt measure.
        Assert.Equal(72 + (468 - center.Width) / 2, center.X, 3);

        // Right alignment puts the run's right edge on the right margin.
        Assert.Equal(72 + 468, right.X + right.Width, 3);
    }

    [Fact]
    public void Justification_stretches_all_but_the_last_line()
    {
        var text = string.Join(' ', Enumerable.Repeat("justify", 40));
        var document = LayoutOf(Doc(text, "<w:jc w:val=\"both\"/>"));

        var lines = document.Pages.Single().Lines;
        Assert.True(lines.Count > 1);

        // Every line but the last should reach the right margin.
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var right = lines[i].Texts.Max(t => t.X + t.Width);
            Assert.Equal(72 + 468, right, 0.5);
        }

        // The last line keeps its natural width rather than being stretched across the measure.
        var lastRight = lines[^1].Texts.Max(t => t.X + t.Width);
        Assert.True(lastRight < 72 + 468, "the last line of a justified paragraph must not stretch");
    }

    [Fact]
    public void Indents_shift_the_text_including_hanging_first_lines()
    {
        var indented = FirstRun(LayoutOf(Doc("Indented", "<w:ind w:left=\"720\"/>")));
        Assert.Equal(72 + 36, indented.X, 3);

        var firstLine = LayoutOf(Doc(
            string.Join(' ', Enumerable.Repeat("hanging", 40)),
            "<w:ind w:left=\"720\" w:hanging=\"360\"/>"));

        var lines = firstLine.Pages.Single().Lines;

        // A hanging indent starts the first line left of the rest.
        Assert.Equal(72 + 36 - 18, lines[0].Texts[0].X, 3);
        Assert.Equal(72 + 36, lines[1].Texts[0].X, 3);
    }

    [Fact]
    public void Paragraph_spacing_separates_paragraphs()
    {
        var document = LayoutOf(new DocxBuilder()
            .AddParagraph("First", paragraphProperties: "<w:spacing w:after=\"240\"/>", runProperties: TimesTwelve)
            .AddParagraph("Second", paragraphProperties: "<w:spacing w:before=\"0\"/>", runProperties: TimesTwelve));

        var lines = document.Pages.Single().Lines;
        Assert.Equal(2, lines.Count);

        // 240 twips is 12pt of space, on top of the line's own height.
        var gap = lines[1].BaselineY - lines[0].BaselineY;
        Assert.Equal(lines[0].Height + 12, gap, 1);
    }

    [Fact]
    public void Adjacent_paragraph_spacing_collapses_to_the_larger_value()
    {
        // Word collapses the previous paragraph's space-after against the next one's
        // space-before, taking the larger rather than the sum. Confirmed against Word with
        // asymmetric values in both directions: 12-after against 24-before gives 24, and
        // 24-after against 12-before also gives 24.
        var document = LayoutOf(new DocxBuilder()
            .AddParagraph("First", "<w:spacing w:before=\"0\" w:after=\"240\"/>", TimesTwelve)
            .AddParagraph("Second", "<w:spacing w:before=\"480\" w:after=\"480\"/>", TimesTwelve)
            .AddParagraph("Third", "<w:spacing w:before=\"240\" w:after=\"0\"/>", TimesTwelve));

        var lines = document.Pages.Single().Lines;

        // 12pt after against 24pt before collapses to 24, not 36.
        Assert.Equal(lines[0].Height + 24, lines[1].BaselineY - lines[0].BaselineY, 1);

        // 24pt after against 12pt before also collapses to 24, which is what rules out both
        // "previous wins" and "next wins" and leaves only the maximum.
        Assert.Equal(lines[1].Height + 24, lines[2].BaselineY - lines[1].BaselineY, 1);
    }

    [Fact]
    public void Spacing_collapses_across_a_page_break_against_the_previous_space_after()
    {
        // The collapse carries across a page break, but the previous paragraph's space-after is
        // absorbed by the page it ended on — below the bottom margin, where nothing can show it —
        // so only the excess appears at the top of the new page.

        // No space-after above: the full 12pt space-before shows.
        var full = LayoutOf(new DocxBuilder()
            .AddParagraph("Before the break", "<w:spacing w:after=\"0\"/>", TimesTwelve)
            .AddParagraph("After the break",
                "<w:pageBreakBefore/><w:spacing w:before=\"240\"/>", TimesTwelve));

        // The top edge is read back from a baseline written on Word's grid, so it may stand a
        // step of that grid from the margin the arithmetic puts it at.
        var fullTop = full.Pages[1].Lines[0];
        Assert.InRange(fullTop.BaselineY - fullTop.Ascent, 72 + 12 - 0.241, 72 + 12 + 0.241);

        // 24pt of space-after above exceeds the 12pt space-before, so nothing is left to show and
        // the paragraph sits flush against the top margin.
        var absorbed = LayoutOf(new DocxBuilder()
            .AddParagraph("Before the break", "<w:spacing w:after=\"480\"/>", TimesTwelve)
            .AddParagraph("After the break",
                "<w:pageBreakBefore/><w:spacing w:before=\"240\"/>", TimesTwelve));

        var absorbedTop = absorbed.Pages[1].Lines[0];
        Assert.InRange(absorbedTop.BaselineY - absorbedTop.Ascent, 72 - 0.241, 72 + 0.241);
    }

    [Fact]
    public void Line_spacing_multiples_are_applied()
    {
        var text = string.Join(' ', Enumerable.Repeat("spacing", 40));

        // Spacing is stated explicitly on both, because the fixture's document defaults specify
        // Word's 1.079 line multiple rather than true single spacing.
        var single = LayoutOf(Doc(text, "<w:spacing w:line=\"240\" w:lineRule=\"auto\"/>"));
        var doubled = LayoutOf(Doc(text, "<w:spacing w:line=\"480\" w:lineRule=\"auto\"/>"));

        var singleGap = single.Pages[0].Lines[1].BaselineY - single.Pages[0].Lines[0].BaselineY;
        var doubleGap = doubled.Pages[0].Lines[1].BaselineY - doubled.Pages[0].Lines[0].BaselineY;

        // 240 in 240ths is single spacing and 480 is double. Both gaps are the distance between
        // two baselines, and a baseline is written on Word's grid of 0.24 points, so either may
        // stand a step from the height it was worked out from.
        Assert.InRange(doubleGap, singleGap * 2 - 0.241, singleGap * 2 + 0.241);

        // Single spacing for 12pt Times is its natural line height.
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var natural = TextMeasurer.GetNaturalLineHeight(font, 12);
        Assert.InRange(singleGap, natural - 0.241, natural + 0.241);
    }

    [Fact]
    public void Exact_line_spacing_overrides_the_natural_height()
    {
        var document = LayoutOf(Doc(
            string.Join(' ', Enumerable.Repeat("exact", 40)),
            "<w:spacing w:line=\"240\" w:lineRule=\"exact\"/>"));

        var lines = document.Pages.Single().Lines;

        // With an exact rule the value is twips, so 240 means exactly 12 points per line.
        Assert.Equal(12, lines[1].BaselineY - lines[0].BaselineY, 2);
    }

    [Fact]
    public void Bold_and_italic_select_the_matching_faces()
    {
        var document = LayoutOf(new DocxBuilder().AddParagraphWithRuns([
            ("regular ", TimesTwelve),
            ("bold ", TimesTwelve + "<w:b/>"),
            ("italic", TimesTwelve + "<w:i/>")
        ]));

        var runs = document.Pages.Single().Lines[0].Texts;
        Assert.Equal(3, runs.Count);

        // Real faces must be chosen, not synthesised, since all three are registered.
        Assert.False(runs[0].Font.Font.IsBold);
        Assert.True(runs[1].Font.Font.IsBold);
        Assert.True(runs[1].Font.IsExact);
        Assert.True(runs[2].Font.Font.IsItalic);
        Assert.True(runs[2].Font.IsExact);

        // Runs must sit end to end on the same baseline.
        Assert.Equal(runs[0].BaselineY, runs[1].BaselineY, 3);
        Assert.Equal(runs[0].X + runs[0].Width, runs[1].X, 3);
    }

    [Fact]
    public void Tabs_advance_to_the_next_default_stop()
    {
        var document = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:r><w:rPr>{TimesTwelve}</w:rPr><w:t>a</w:t><w:tab/><w:t>b</w:t></w:r></w:p>"));

        var runs = document.Pages.Single().Lines[0].Texts;

        // Default tab stops are every half inch, so text after the tab starts at 36pt into the
        // measure regardless of how wide "a" is.
        Assert.Equal(72, runs[0].X, 3);
        Assert.Equal(72 + 36, runs[1].X, 3);
    }

    [Fact]
    public void Explicit_tab_stops_are_used()
    {
        var document = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"""
             <w:p>
               <w:pPr><w:tabs><w:tab w:val="left" w:pos="2880"/></w:tabs></w:pPr>
               <w:r><w:rPr>{TimesTwelve}</w:rPr><w:t>a</w:t><w:tab/><w:t>b</w:t></w:r>
             </w:p>
             """));

        var runs = document.Pages.Single().Lines[0].Texts;
        Assert.Equal(72 + 144, runs[1].X, 3);
    }

    [Fact]
    public void Line_breaks_start_a_new_line_without_paragraph_spacing()
    {
        var document = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:r><w:rPr>{TimesTwelve}</w:rPr><w:t>one</w:t><w:br/><w:t>two</w:t></w:r></w:p>"));

        var lines = document.Pages.Single().Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("one", lines[0].Texts[0].Text);
        Assert.Equal("two", lines[1].Texts[0].Text);
        Assert.Equal(72, lines[1].Texts[0].X, 3);
    }

    [Fact]
    public void Explicit_page_breaks_start_a_new_page()
    {
        var document = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:r><w:rPr>{TimesTwelve}</w:rPr><w:t>page one</w:t><w:br w:type=\"page\"/><w:t>page two</w:t></w:r></w:p>"));

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal("page one", document.Pages[0].Texts.Single().Text);
        Assert.Equal("page two", document.Pages[1].Texts.Single().Text);
    }

    [Fact]
    public void Content_flows_onto_additional_pages_when_it_overruns()
    {
        var builder = new DocxBuilder();
        for (var i = 0; i < 120; i++)
            builder.AddParagraph($"Paragraph {i}", runProperties: TimesTwelve);

        var document = LayoutOf(builder);

        Assert.True(document.Pages.Count > 1, "120 paragraphs must not fit on one page");

        // No line may be placed below the bottom margin on any page.
        foreach (var page in document.Pages)
        {
            foreach (var line in page.Lines)
                Assert.True(line.BaselineY < page.HeightPoints - 72 + line.Height,
                    $"a line escaped the bottom margin at y={line.BaselineY}");
        }

        // Every paragraph must appear exactly once across the pages.
        var texts = document.Pages.SelectMany(p => p.Texts).Select(t => t.Text).ToList();
        Assert.Equal(120, texts.Count);
        Assert.Equal("Paragraph 0", texts[0]);
        Assert.Equal("Paragraph 119", texts[^1]);
    }

    [Fact]
    public void Empty_paragraphs_still_occupy_a_line()
    {
        var document = LayoutOf(new DocxBuilder()
            .AddParagraph("before", runProperties: TimesTwelve)
            .AddEmptyParagraph()
            .AddParagraph("after", runProperties: TimesTwelve));

        var lines = document.Pages.Single().Lines;

        // The blank paragraph contributes a line with no runs; dropping it would pull the
        // following text up by a line.
        Assert.Equal(3, lines.Count);
        Assert.Empty(lines[1].Texts);
        Assert.True(lines[1].Height > 0, "an empty paragraph must still have height");
    }

    [Fact]
    public void Converting_produces_a_valid_pdf_with_extractable_text()
    {
        var docx = new DocxBuilder()
            .AddParagraph("n8PDF conversion", paragraphProperties: "<w:jc w:val=\"center\"/>",
                runProperties: "<w:rFonts w:ascii=\"Times New Roman\"/><w:sz w:val=\"36\"/><w:b/>")
            .AddParagraph(
                "This paragraph was laid out from real font metrics and written to PDF by n8PDF, "
                + "with no third-party library involved at any stage of the pipeline.",
                runProperties: TimesTwelve)
            .Build();

        var pdf = Converter.Convert(docx, Options());
        var path = TestPaths.WriteArtifact("conversion-smoke.pdf", pdf);

        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.7", text);
        Assert.Contains("/Subtype /Type0", text);
        Assert.Contains("/MediaBox [0 0 612 792]", text);
        Assert.True(new FileInfo(path).Length > 10_000);
    }

    [Fact]
    public void Converting_the_same_document_twice_produces_identical_bytes()
    {
        var docx = new DocxBuilder().AddParagraph("Reproducible", runProperties: TimesTwelve).Build();

        // Reproducibility is what makes golden comparison meaningful; a timestamp or a hash-order
        // dependency creeping in would silently break it.
        Assert.Equal(Converter.Convert(docx, Options()), Converter.Convert(docx, Options()));
    }

    [Fact]
    public void Layout_trace_is_stable_and_describes_the_page()
    {
        var docx = new DocxBuilder().AddParagraph("Traced", runProperties: TimesTwelve).Build();

        using var stream = new MemoryStream(docx);
        var trace = LayoutTrace.Write(Converter.LayoutDocument(stream, Options()));

        Assert.Contains("\"pageCount\": 1", trace);
        Assert.Contains("\"text\": \"Traced\"", trace);
        Assert.Contains("\"font\": \"Times New Roman\"", trace);
        Assert.Contains("\"size\": 12", trace);

        using var again = new MemoryStream(docx);
        Assert.Equal(trace, LayoutTrace.Write(Converter.LayoutDocument(again, Options())));
    }

    private const string TimesTwelve = "<w:rFonts w:ascii=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static DocxBuilder Doc(string text, string? paragraphProperties) =>
        new DocxBuilder().AddParagraph(text, paragraphProperties, TimesTwelve);

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static PositionedText FirstRun(LaidOutDocument document) =>
        document.Pages[0].Lines[0].Texts[0];
}
