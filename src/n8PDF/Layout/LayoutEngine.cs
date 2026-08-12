using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Layout;

/// <summary>Knobs that change how layout is performed.</summary>
public sealed class LayoutOptions
{
    /// <summary>
    /// Apply pair kerning when measuring. Off by default to match Word, which does not kern
    /// unless a document asks it to.
    /// </summary>
    public bool ApplyKerning { get; set; }

    /// <summary>Default tab stop interval in twips. Word's default is half an inch.</summary>
    public int DefaultTabStopTwips { get; set; } = 720;
}

/// <summary>
/// Turns a parsed document into positioned text on pages: measurement, line breaking, vertical
/// stacking and pagination.
/// </summary>
public sealed class LayoutEngine(FontLibrary fonts, StyleResolver styles, LayoutOptions? options = null)
{
    private readonly FontLibrary _fonts = fonts;
    private readonly StyleResolver _styles = styles;
    private readonly LayoutOptions _options = options ?? new LayoutOptions();

    public LaidOutDocument Layout(WordDocument document)
    {
        var section = document.Section;
        var result = new LaidOutDocument { Section = section };

        var contentWidth = section.ContentWidthPoints;
        var contentHeight = section.ContentHeightPoints;
        var contentTop = Units.TwipsToPoints(section.MarginTopTwips);
        var contentLeft = Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips);

        var page = NewPage(result, section);
        var y = contentTop;

        var paragraphs = document.Body.OfType<Paragraph>().ToList();
        ResolvedParagraphFormat? previousFormat = null;

        // Space-after of the paragraph just laid out, held back rather than applied immediately —
        // see the collapsing rule below.
        var pendingSpaceAfter = 0.0;

        for (var index = 0; index < paragraphs.Count; index++)
        {
            var paragraph = paragraphs[index];
            var format = _styles.ResolveParagraph(paragraph.Properties);

            var startedNewPage = false;
            if (format.PageBreakBefore && page.Lines.Count > 0)
            {
                page = NewPage(result, section);
                y = contentTop;
                startedNewPage = true;
            }

            // Contextual spacing suppresses spacing between paragraphs sharing a style, which is
            // what keeps list items tight.
            var spaceBefore = format.SpaceBeforePoints;
            if (previousFormat is not null &&
                format.ContextualSpacing &&
                previousFormat.StyleId == format.StyleId)
            {
                spaceBefore = 0;
            }

            // Word collapses adjacent paragraph spacing to the larger of the two rather than
            // adding them, the way CSS margins collapse. Verified against Word with the
            // paragraph-spacing-asymmetric fixture: with 12pt after and 24pt before it produces
            // 24pt, and with 24pt after and 12pt before it also produces 24pt — which is only
            // consistent with a maximum. Summing them put every paragraph after the first 12pt
            // too low.
            if (previousFormat is null)
            {
                y += spaceBefore;
            }
            else if (startedNewPage)
            {
                // The collapse carries across a page break, but the previous paragraph's
                // space-after is absorbed by the page it ended on — it falls below the bottom
                // margin where nothing can show it — so only the excess appears at the top of
                // the new page. Verified against Word: with 12pt before, a paragraph opening a
                // page sits 12pt down when the previous paragraph had no space-after and flush
                // against the top margin when the previous paragraph had 12pt of it.
                y += Math.Max(0, spaceBefore - pendingSpaceAfter);
            }
            else
            {
                y += Math.Max(pendingSpaceAfter, spaceBefore);
            }

            var lines = ComposeParagraph(paragraph, format, contentWidth);

            foreach (var line in lines)
            {
                if (line.ForcePageBreak && page.Lines.Count > 0)
                {
                    page = NewPage(result, section);
                    y = contentTop;
                }

                // A line that does not fit starts a new page. Widow and orphan control would
                // move whole groups of lines here; it is not implemented yet.
                if (y + line.Height > contentTop + contentHeight && page.Lines.Count > 0)
                {
                    page = NewPage(result, section);
                    y = contentTop;
                }

                EmitLine(page, line, contentLeft, y, index);
                y += line.Height;
            }

            var spaceAfter = format.SpaceAfterPoints;
            if (format.ContextualSpacing &&
                index + 1 < paragraphs.Count &&
                _styles.ResolveParagraph(paragraphs[index + 1].Properties).StyleId == format.StyleId)
            {
                spaceAfter = 0;
            }

            // Held rather than added, so the next paragraph can collapse it against its own
            // space-before.
            pendingSpaceAfter = spaceAfter;
            previousFormat = format;
        }

        // The final paragraph's space-after still occupies the page even though nothing follows
        // it, which matters for how much content a page is considered to hold.
        y += pendingSpaceAfter;

        return result;
    }

    private static LaidOutPage NewPage(LaidOutDocument document, SectionProperties section)
    {
        var page = new LaidOutPage
        {
            WidthPoints = section.PageWidthPoints,
            HeightPoints = section.PageHeightPoints
        };

        document.Pages.Add(page);
        return page;
    }

    /// <summary>Places a composed line's segments at their final page coordinates.</summary>
    private static void EmitLine(LaidOutPage page, ComposedLine line, double contentLeft, double top, int paragraphIndex)
    {
        var baselineY = top + line.Ascent;

        var laidOut = new LaidOutLine
        {
            BaselineY = baselineY,
            Height = line.Height,
            Ascent = line.Ascent,
            ParagraphIndex = paragraphIndex
        };

        foreach (var segment in line.Segments)
        {
            if (segment.Text.Length == 0) continue;

            var text = new PositionedText
            {
                X = contentLeft + segment.X,
                BaselineY = baselineY - segment.Format.BaselineShiftPoints,
                Text = segment.Text,
                Format = segment.Format,
                Font = segment.Font,
                Width = segment.Width,
                WordSpacing = segment.WordSpacing
            };

            laidOut.Texts.Add(text);
            AddDecorations(page, text);
        }

        page.Lines.Add(laidOut);
    }

    /// <summary>Adds the rules that draw underline and strikethrough for a placed run.</summary>
    private static void AddDecorations(LaidOutPage page, PositionedText text)
    {
        var format = text.Format;
        var metrics = text.Font.Font.Metrics;
        var size = format.EffectiveFontSizePoints;

        // Scale the thickness with the size so that headings get a proportionate rule.
        var thickness = Math.Max(0.5, size / 14.0);

        if (format.Underline != UnderlineStyle.None)
        {
            var offset = metrics.ToPoints(Math.Abs(metrics.Descender) * 0.45, size);
            page.Rules.Add(new PositionedRule
            {
                X = text.X,
                Y = text.BaselineY + offset,
                Width = text.Width,
                Thickness = format.Underline == UnderlineStyle.Thick ? thickness * 2 : thickness,
                Color = format.GetColor()
            });

            if (format.Underline == UnderlineStyle.Double)
            {
                page.Rules.Add(new PositionedRule
                {
                    X = text.X,
                    Y = text.BaselineY + offset + thickness * 2,
                    Width = text.Width,
                    Thickness = thickness,
                    Color = format.GetColor()
                });
            }
        }

        if (format.Strike)
        {
            // Strike through at roughly a third of the cap height above the baseline.
            page.Rules.Add(new PositionedRule
            {
                X = text.X,
                Y = text.BaselineY - metrics.ToPoints(metrics.XHeight, size) * 0.5,
                Width = text.Width,
                Thickness = thickness,
                Color = format.GetColor()
            });
        }
    }

    // ----- line composition -----

    /// <summary>Breaks one paragraph into lines that fit the available width.</summary>
    private List<ComposedLine> ComposeParagraph(Paragraph paragraph, ResolvedParagraphFormat format, double contentWidth)
    {
        var atoms = BuildAtoms(paragraph, format);
        var lines = new List<ComposedLine>();

        var isFirstLine = true;
        var index = 0;
        var forceBreakOnNextLine = false;

        while (index < atoms.Count || lines.Count == 0)
        {
            var indentLeft = format.IndentLeftPoints + (isFirstLine ? Math.Max(0, format.IndentFirstLinePoints) : 0);

            // A hanging indent pulls the first line left of the others, so it applies to the
            // first line as a negative offset rather than to the rest as a positive one.
            if (isFirstLine && format.IndentFirstLinePoints < 0)
                indentLeft = format.IndentLeftPoints + format.IndentFirstLinePoints;

            var available = Math.Max(1, contentWidth - indentLeft - format.IndentRightPoints);

            var line = new ComposedLine
            {
                ForcePageBreak = forceBreakOnNextLine,
                IndentLeft = indentLeft
            };
            forceBreakOnNextLine = false;

            var consumed = FillLine(atoms, index, available, line, out var hardBreak, out var pageBreak);
            index += consumed;

            var isLastLine = index >= atoms.Count;
            FinishLine(line, format, indentLeft, available, isLastLine || hardBreak);
            lines.Add(line);

            if (pageBreak) forceBreakOnNextLine = true;

            isFirstLine = false;

            // The loop condition allows an empty paragraph one pass so that it still occupies a
            // line; break out once that pass is done.
            if (consumed == 0 && index >= atoms.Count) break;
        }

        // An empty paragraph has no atoms but still takes up a line, sized by its mark.
        foreach (var line in lines.Where(l => l.Segments.Count == 0))
            ApplyEmptyLineMetrics(line, format);

        return lines;
    }

    /// <summary>
    /// Greedily packs atoms onto one line. Trailing spaces are allowed to overflow the measure,
    /// which is what Word does — a line ending in a space does not wrap because of it.
    /// </summary>
    private static int FillLine(
        List<Atom> atoms, int start, double available, ComposedLine line, out bool hardBreak, out bool pageBreak)
    {
        hardBreak = false;
        pageBreak = false;

        var x = 0.0;
        var index = start;
        var placedAnything = false;

        while (index < atoms.Count)
        {
            var atom = atoms[index];

            if (atom is BreakAtom breakAtom)
            {
                index++;
                hardBreak = true;
                pageBreak = breakAtom.Kind == BreakKind.Page;
                break;
            }

            if (atom is TabAtom tab)
            {
                var next = NextTabStop(x, tab.Stops, tab.DefaultIntervalPoints);
                if (next > available && placedAnything) break;

                line.Items.Add(new PlacedAtom(atom, x, next - x));
                x = next;
                index++;
                placedAnything = true;
                continue;
            }

            var textAtom = (TextAtom)atom;

            // Spaces at the end of a line hang past the margin rather than forcing a wrap.
            if (!textAtom.IsSpace && placedAnything && x + textAtom.Width > available + 0.001)
                break;

            line.Items.Add(new PlacedAtom(atom, x, textAtom.Width));
            x += textAtom.Width;
            index++;
            placedAnything = true;

            // A single word longer than the measure has to go somewhere; it overflows rather
            // than looping forever. Breaking inside a word would need hyphenation rules.
            if (!textAtom.IsSpace && x > available && line.Items.Count == 1) break;
        }

        return index - start;
    }

    /// <summary>
    /// Converts the placed atoms into drawable segments, applying alignment and merging adjacent
    /// atoms that share formatting so the content stream carries one show-text per run rather
    /// than one per word.
    /// </summary>
    private static void FinishLine(
        ComposedLine line, ResolvedParagraphFormat format, double indentLeft, double available, bool isLastLine)
    {
        // Trailing spaces do not participate in alignment: a centred line ending in a space
        // would otherwise sit visibly off-centre.
        var content = line.Items;
        var lastVisible = content.Count - 1;
        while (lastVisible >= 0 && content[lastVisible].Atom is TextAtom { IsSpace: true })
            lastVisible--;

        var lineWidth = lastVisible >= 0
            ? content[lastVisible].X + content[lastVisible].Width
            : 0;

        var offset = 0.0;
        var wordSpacing = 0.0;

        switch (format.Justification)
        {
            case Justification.Center:
                offset = (available - lineWidth) / 2;
                break;
            case Justification.Right:
                offset = available - lineWidth;
                break;
            case Justification.Both or Justification.Distribute when !isLastLine:
                // Justification stretches the spaces rather than the words. The last line of a
                // paragraph is left alone, which is why it needs to be identified.
                var spaceCount = content.Take(lastVisible + 1).Count(item => item.Atom is TextAtom { IsSpace: true });
                if (spaceCount > 0 && lineWidth < available)
                    wordSpacing = (available - lineWidth) / spaceCount;
                break;
        }

        offset = Math.Max(offset, 0);

        // Merge runs of atoms that share a format into single segments.
        var maxAscent = 0.0;
        var maxHeight = 0.0;

        Segment? current = null;
        var pen = 0.0;

        // Trailing spaces are dropped rather than emitted. They draw nothing, and keeping them
        // would make the line measure wider than its visible content — which in a justified
        // paragraph pushes the visible text past the right margin.
        foreach (var item in content.Take(lastVisible + 1))
        {
            if (item.Atom is TabAtom)
            {
                current = null;
                pen = item.X + item.Width;
                continue;
            }

            var textAtom = (TextAtom)item.Atom;
            var extra = textAtom.IsSpace ? wordSpacing : 0;

            if (current is not null &&
                ReferenceEquals(current.Format, textAtom.Format) &&
                ReferenceEquals(current.Font, textAtom.Font) &&
                Math.Abs(current.X + current.Width - (indentLeft + offset + pen)) < 0.001)
            {
                current.Text += textAtom.Text;
                current.Width += textAtom.Width + extra;
                current.SpaceCount += textAtom.IsSpace ? 1 : 0;
            }
            else
            {
                current = new Segment
                {
                    X = indentLeft + offset + pen,
                    Text = textAtom.Text,
                    Format = textAtom.Format,
                    Font = textAtom.Font,
                    Width = textAtom.Width + extra,
                    WordSpacing = wordSpacing,
                    SpaceCount = textAtom.IsSpace ? 1 : 0
                };

                line.Segments.Add(current);
            }

            pen += textAtom.Width + extra;

            maxAscent = Math.Max(maxAscent, textAtom.Ascent);
            maxHeight = Math.Max(maxHeight, textAtom.NaturalHeight);
        }

        ApplyLineMetrics(line, format, maxAscent, maxHeight);
    }

    private static void ApplyEmptyLineMetrics(ComposedLine line, ResolvedParagraphFormat format)
    {
        // Nothing was placed, so the paragraph mark's own formatting sets the height.
        if (line.Height > 0) return;

        var size = format.MarkFormat.FontSizePoints;
        ApplyLineMetrics(line, format, size * 0.9, size * 1.15);
    }

    private static void ApplyLineMetrics(
        ComposedLine line, ResolvedParagraphFormat format, double maxAscent, double naturalHeight)
    {
        if (naturalHeight <= 0) return;

        switch (format.LineRule)
        {
            case LineSpacingRule.Exact:
                line.Height = format.LineSpacingPoints;

                // With an exact rule the baseline sits proportionally where it would naturally,
                // so that text does not drift to the top of a tightened line.
                line.Ascent = naturalHeight > 0 ? line.Height * (maxAscent / naturalHeight) : line.Height;
                break;

            case LineSpacingRule.AtLeast:
                line.Height = Math.Max(naturalHeight, format.LineSpacingPoints);
                line.Ascent = maxAscent;
                break;

            default:
                line.Height = naturalHeight * format.LineSpacingMultiple;

                // Extra leading from a multiple goes *below* the baseline, not above it, so the
                // first line of a paragraph sits at its natural ascent no matter what the
                // multiple is. Verified against Word: its first baseline moves by a fifth of a
                // point as the multiple goes from single to double, while adding the leading
                // above moved ours by a full 13.8pt.
                line.Ascent = maxAscent;
                break;
        }
    }

    private static double NextTabStop(double x, IReadOnlyList<TabStop> stops, double defaultInterval)
    {
        foreach (var stop in stops.OrderBy(s => s.PositionTwips))
        {
            if (stop.Alignment == TabAlignment.Clear) continue;

            var position = Units.TwipsToPoints(stop.PositionTwips);
            // Left-aligned stops are the only kind handled so far; centre, right and decimal
            // stops need the following text measured before the position can be resolved.
            if (position > x + 0.001) return position;
        }

        if (defaultInterval <= 0) return x;

        var next = Math.Floor(x / defaultInterval + 1) * defaultInterval;
        return next <= x ? x + defaultInterval : next;
    }

    // ----- atom construction -----

    /// <summary>
    /// Flattens a paragraph into the smallest units line breaking works with: words, individual
    /// spaces, tabs and explicit breaks.
    /// </summary>
    private List<Atom> BuildAtoms(Paragraph paragraph, ResolvedParagraphFormat format)
    {
        var atoms = new List<Atom>();
        var defaultTab = Units.TwipsToPoints(_options.DefaultTabStopTwips);

        foreach (var run in paragraph.Runs)
        {
            var runFormat = _styles.ResolveRun(paragraph.Properties, run.Properties);
            if (runFormat.Hidden) continue;

            var selection = _fonts.Resolve(runFormat.FontFamily, runFormat.Bold, runFormat.Italic);
            var size = runFormat.EffectiveFontSizePoints;
            var ascent = TextMeasurer.GetAscent(selection.Font, size);
            var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, size);

            foreach (var inline in run.Content)
            {
                switch (inline)
                {
                    case TextInline text:
                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(text.Text, runFormat),
                            runFormat, selection, ascent, naturalHeight);
                        break;

                    case TabInline:
                        atoms.Add(new TabAtom
                        {
                            Stops = format.TabStops,
                            DefaultIntervalPoints = defaultTab,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight
                        });
                        break;

                    case BreakInline breakInline:
                        atoms.Add(new BreakAtom
                        {
                            Kind = breakInline.Kind,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight
                        });
                        break;
                }
            }
        }

        return atoms;
    }

    /// <summary>
    /// Splits text into word and space atoms. Spaces are separate atoms because they are both
    /// the break opportunities and the things justification stretches.
    /// </summary>
    private void AddTextAtoms(
        List<Atom> atoms, string text, ResolvedRunFormat format, FontSelection font, double ascent, double naturalHeight)
    {
        var index = 0;
        while (index < text.Length)
        {
            var isSpace = text[index] == ' ';
            var start = index;

            while (index < text.Length && (text[index] == ' ') == isSpace)
                index++;

            var slice = text[start..index];
            atoms.Add(new TextAtom
            {
                Text = slice,
                IsSpace = isSpace,
                Format = format,
                Font = font,
                Ascent = ascent,
                NaturalHeight = naturalHeight,
                Width = TextMeasurer.Measure(
                    font.Font, slice, format.EffectiveFontSizePoints,
                    format.CharacterSpacingPoints, _options.ApplyKerning) * format.ScaleFactor
            });
        }
    }

    // ----- internal composition types -----

    private abstract class Atom
    {
        public double Ascent { get; init; }

        public double NaturalHeight { get; init; }
    }

    private sealed class TextAtom : Atom
    {
        public required string Text { get; init; }

        public required bool IsSpace { get; init; }

        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }

        public required double Width { get; init; }
    }

    private sealed class TabAtom : Atom
    {
        public required IReadOnlyList<TabStop> Stops { get; init; }

        public required double DefaultIntervalPoints { get; init; }
    }

    private sealed class BreakAtom : Atom
    {
        public required BreakKind Kind { get; init; }
    }

    private readonly record struct PlacedAtom(Atom Atom, double X, double Width);

    private sealed class Segment
    {
        public double X { get; set; }

        public string Text { get; set; } = string.Empty;

        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }

        public double Width { get; set; }

        public double WordSpacing { get; init; }

        public int SpaceCount { get; set; }
    }

    private sealed class ComposedLine
    {
        public List<PlacedAtom> Items { get; } = [];

        public List<Segment> Segments { get; } = [];

        public double Height { get; set; }

        public double Ascent { get; set; }

        public double IndentLeft { get; init; }

        public bool ForcePageBreak { get; init; }
    }
}
