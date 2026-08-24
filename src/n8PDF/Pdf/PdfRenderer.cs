using n8PDF.Fonts;
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

    public static void Render(LaidOutDocument document, PdfBuilder builder, FontLibrary? fonts = null)
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
                // A metafile is a drawing rather than a picture, and is written out as the PDF's
                // own drawing commands rather than embedded as pixels.
                if (image.Image.Drawing is { } drawing)
                {
                    RenderDrawing(builder, content, page, image, drawing, fonts);
                    continue;
                }

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

            // And the lines a rule cannot draw: the cross in a ticked checkbox, corner to corner.
            foreach (var stroke in page.Strokes)
            {
                content.Save()
                    .SetStrokeColor(stroke.Color.Red, stroke.Color.Green, stroke.Color.Blue)
                    .SetLineWidth(stroke.Thickness)
                    .MoveTo(stroke.FromX, Flip(page, stroke.FromY))
                    .LineTo(stroke.ToX, Flip(page, stroke.ToY))
                    .Stroke()
                    .Restore();
            }

            foreach (var text in page.Texts)
                RenderText(builder, content, page, text);
        }

        // Annotations are added after every page exists, because an internal link needs a
        // reference to the page it points at and that page may come later in the document.
        foreach (var (source, target) in pages)
            AddLinkAnnotations(document, builder, source, target);

        WriteOutline(document, builder);
    }

    /// <summary>
    /// The document outline (#66): the tree of headings a reader shows in its navigation pane.
    /// </summary>
    /// <remarks>
    /// The tree is derived from the outline levels the layout recorded: a heading's parent is the
    /// nearest heading above it with a smaller level, so a document whose levels skip - a
    /// Heading3 directly under a Heading1 - still produces a well-formed tree, with the deeper
    /// entry a child of the nearest shallower one. Every node is written open, so a node's
    /// <c>/Count</c> is the positive number of its descendants and the root's is the total. Each
    /// entry is an <c>XYZ</c> destination at the heading's own place on its page, with the zoom
    /// left alone, exactly as the internal-link annotations write theirs. A document with no
    /// headings gets no <c>/Outlines</c> entry at all.
    /// </remarks>
    private static void WriteOutline(LaidOutDocument document, PdfBuilder builder)
    {
        var headings = document.Headings
            .Where(h => h.PageIndex >= 0 && h.PageIndex < document.Pages.Count && h.Title.Length > 0)
            .ToList();

        if (headings.Count == 0) return;

        var pdf = builder.Document;
        var rootRef = pdf.Reserve();
        var refs = headings.Select(_ => pdf.Reserve()).ToList();

        // The parent of each entry: the nearest one above it with a smaller level.
        var parents = new int[headings.Count];
        var childrenOf = new Dictionary<int, List<int>> { [-1] = [] };
        var open = new Stack<int>();

        for (var i = 0; i < headings.Count; i++)
        {
            while (open.Count > 0 && headings[open.Peek()].Level >= headings[i].Level) open.Pop();

            parents[i] = open.Count > 0 ? open.Peek() : -1;
            childrenOf[-1] ??= [];
            if (!childrenOf.TryGetValue(parents[i], out var siblings))
                childrenOf[parents[i]] = siblings = [];
            siblings.Add(i);
            open.Push(i);
        }

        int Descendants(int index) =>
            childrenOf.TryGetValue(index, out var below)
                ? below.Sum(child => 1 + Descendants(child))
                : 0;

        for (var i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            var page = document.Pages[heading.PageIndex];

            var entry = new PdfDictionary()
                .Set("Title", PdfString.FromText(heading.Title))
                .Set("Parent", parents[i] < 0 ? rootRef : refs[parents[i]])
                .Set("Dest", new PdfArray()
                    .Add(pdf.GetPageReference(heading.PageIndex))
                    .Add(new PdfName("XYZ"))
                    .Add(heading.X)
                    .Add(page.HeightPoints - heading.Y)
                    .Add(PdfNull.Instance));

            var siblings = childrenOf[parents[i]];
            var at = siblings.IndexOf(i);
            if (at > 0) entry.Set("Prev", refs[siblings[at - 1]]);
            if (at < siblings.Count - 1) entry.Set("Next", refs[siblings[at + 1]]);

            if (childrenOf.TryGetValue(i, out var below) && below.Count > 0)
            {
                entry.Set("First", refs[below[0]])
                    .Set("Last", refs[below[^1]])
                    .Set("Count", Descendants(i));
            }

            pdf.Assign(refs[i], entry);
        }

        var top = childrenOf[-1];
        pdf.Assign(rootRef, new PdfDictionary()
            .Set("Type", "Outlines")
            .Set("First", refs[top[0]])
            .Set("Last", refs[top[^1]])
            .Set("Count", headings.Count));

        pdf.SetOutlines(rootRef);
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
                    .Set("Border", new PdfArray().Add(0).Add(0).Add(0))
                    // Printable and never hidden, which PDF/A demands of every annotation and
                    // which is simply true of a link either way (#68).
                    .Set("F", 4);

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

    /// <summary>
    /// Writes a drawing out as the page's own drawing commands.
    /// </summary>
    /// <remarks>
    /// The drawing's coordinates are turned into the page's here rather than by a transform around
    /// them. A transform would be shorter, but a drawing counts downwards and a page upwards, so
    /// the transform would have to turn the page over — and every piece of text in it would then
    /// come out upside down and need turning back.
    /// </remarks>
    private static void RenderDrawing(
        PdfBuilder builder, ContentStreamBuilder content, LaidOutPage page,
        PositionedImage placed, Images.VectorDrawing drawing, FontLibrary? fonts)
    {
        var scaleX = placed.Width / Math.Max(0.001, drawing.Width);
        var scaleY = placed.Height / Math.Max(0.001, drawing.Height);

        double X(double x) => placed.X + x * scaleX;
        double Y(double y) => Flip(page, placed.Y + y * scaleY);

        void WritePath(IReadOnlyList<Images.PathStep> steps)
        {
            foreach (var step in steps)
            {
                switch (step.Kind)
                {
                    case Images.PathStepKind.Move:
                        content.MoveTo(X(step.Points[0].X), Y(step.Points[0].Y));
                        break;

                    case Images.PathStepKind.Line:
                        content.LineTo(X(step.Points[0].X), Y(step.Points[0].Y));
                        break;

                    case Images.PathStepKind.Curve:
                        content.CurveTo(
                            X(step.Points[0].X), Y(step.Points[0].Y),
                            X(step.Points[1].X), Y(step.Points[1].Y),
                            X(step.Points[2].X), Y(step.Points[2].Y));
                        break;

                    case Images.PathStepKind.Close:
                        content.ClosePath();
                        break;
                }
            }
        }

        // The clips something was drawn under (#69, #64), each written as a clip path of its
        // own, which is how PDF composes their intersection.
        void WriteClips(IReadOnlyList<Images.ClipShape>? clips)
        {
            foreach (var shape in clips ?? [])
            {
                WritePath(shape.Steps);

                if (shape.EvenOdd) content.ClipEvenOdd();
                else content.Clip();
            }
        }

        foreach (var operation in drawing.Operations)
        {
            switch (operation)
            {
                case Images.PathOperation path:
                {
                    content.Save();

                    // What is to be kept inside a rectangle says so, and the rectangle is written
                    // as a clip before the path itself.
                    if (path.Clip is { } clip)
                    {
                        content.Rectangle(X(clip.X), Y(clip.Y + clip.Height),
                            clip.Width * scaleX, clip.Height * scaleY);

                        content.Clip();
                    }

                    WriteClips(path.Clips);

                    if (path.FillOpacity < 1)
                        content.SetGraphicsState(builder.UseAlpha(path.FillOpacity));

                    // A gradient is painted as an axial shading kept inside the path (#64): the
                    // path becomes a clip, the axis runs with the stated angle across the path's
                    // own bounds, and the outline is stroked after as its own path.
                    if (path.Gradient is { Stops.Count: >= 2 } gradient)
                    {
                        WritePath(path.Steps);
                        content.Clip();

                        var points = path.Steps.SelectMany(step => step.Points).ToList();
                        var minX = points.Min(p => X(p.X));
                        var maxX = points.Max(p => X(p.X));
                        var minY = points.Min(p => Y(p.Y));
                        var maxY = points.Max(p => Y(p.Y));

                        // The angle is clockwise from three o'clock with the drawing's own axes,
                        // which point down; the page's point up, so the sine turns over.
                        var radians = gradient.AngleDegrees * Math.PI / 180;
                        var (dx, dy) = (Math.Cos(radians), -Math.Sin(radians));
                        var (cx, cy) = ((minX + maxX) / 2, (minY + maxY) / 2);
                        var half = (Math.Abs(dx) * (maxX - minX) + Math.Abs(dy) * (maxY - minY)) / 2;

                        content.PaintShading(builder.UseShading(
                            [.. gradient.Stops.Select(stop => (stop.Position,
                                (stop.Color.Red / 255.0, stop.Color.Green / 255.0, stop.Color.Blue / 255.0)))],
                            cx - dx * half, cy - dy * half, cx + dx * half, cy + dy * half));

                        if (path.Stroke is { } outline)
                        {
                            content.SetStrokeColor(outline.Red / 255.0, outline.Green / 255.0, outline.Blue / 255.0);
                            content.SetLineWidth(Math.Max(0.24, path.StrokeWidth * scaleX));
                            WritePath(path.Steps);
                            content.Stroke();
                        }

                        content.Restore();
                        break;
                    }

                    if (path.Fill is { } fill)
                        content.SetFillColor(fill.Red / 255.0, fill.Green / 255.0, fill.Blue / 255.0);

                    if (path.Stroke is { } stroke)
                    {
                        content.SetStrokeColor(stroke.Red / 255.0, stroke.Green / 255.0, stroke.Blue / 255.0);
                        content.SetLineWidth(Math.Max(0.24, path.StrokeWidth * scaleX));

                        // Inside the save above, so it is put back for whatever is drawn next.
                        if (path.RoundCap) content.SetLineCap(1);
                    }

                    WritePath(path.Steps);

                    if (path.Fill is not null && path.Stroke is not null) content.FillAndStroke(path.EvenOdd);
                    else if (path.Fill is not null) _ = path.EvenOdd ? content.FillEvenOdd() : content.Fill();
                    else content.Stroke();

                    content.Restore();
                    break;
                }

                case Images.TextOperation text when fonts is not null:
                {
                    var selection = fonts.Resolve(text.FontFamily, text.Bold, text.Italic);
                    var font = builder.UseFont(selection.Font);

                    // The size is in the drawing's own units, so it scales with everything else.
                    var size = text.SizePoints * scaleY;

                    // A drawing says where the top of its text goes; a PDF says where the
                    // baseline goes. The distance between them is the height of the characters
                    // themselves, which is the em less what hangs below the line — the leading
                    // above them is not part of what the drawing measured. Word's own rendering
                    // of the metafile fixture puts the baseline 10.98pt below the point the
                    // record names, at 14pt Times, and that is exactly this.
                    var metrics = selection.Font.Metrics;
                    var toBaseline = (metrics.UnitsPerEm - metrics.WinDescent) * size / metrics.UnitsPerEm;

                    content.Save();
                    content.SetFillColor(text.Color.Red / 255.0, text.Color.Green / 255.0, text.Color.Blue / 255.0);
                    content.BeginText();
                    content.SetFont(font.ResourceName, size);
                    content.SetTextPosition(X(text.X), Y(text.Y) - toBaseline);
                    content.ShowGlyphs(Encode(font, selection.Font, text.Text));
                    content.EndText();
                    content.Restore();

                    break;
                }

                case Images.WordArtOperation word when fonts is not null:
                {
                    var selection = fonts.Resolve(word.FontFamily, word.Bold, word.Italic);
                    var font = builder.UseFont(selection.Font);

                    var radians = -word.AngleDegrees * Math.PI / 180;
                    var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));

                    var (sx, sy) = (word.ScaleX * scaleX, word.ScaleY * scaleY);

                    content.Save();

                    if (word.Opacity < 1) content.SetGraphicsState(builder.UseAlpha(word.Opacity));

                    content.SetFillColor(
                        word.Color.Red / 255.0, word.Color.Green / 255.0, word.Color.Blue / 255.0);

                    content.BeginText();
                    content.SetFont(font.ResourceName, word.SizePoints);

                    // Turned about where it begins, and stretched along its own axes, which is
                    // what puts a watermark across the corner of a page.
                    content.SetTextMatrix(
                        sx * cos, sx * sin, -sy * sin, sy * cos, X(word.X), Y(word.Y));

                    content.ShowGlyphs(Encode(font, selection.Font, word.Text));
                    content.EndText();
                    content.Restore();

                    break;
                }

                case Images.ImageOperation picture:
                {
                    var width = picture.Width * scaleX;
                    var height = picture.Height * scaleY;

                    content.Save();
                    WriteClips(picture.Clips);

                    content.Transform(width, 0, 0, height, X(picture.X), Y(picture.Y) - height)
                        .DrawXObject(builder.UseImage(picture.Image).ResourceName);

                    content.Restore();
                    break;
                }
            }
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

        if (text.TurnDegrees != 0)
        {
            // Turned about the pen, which is where the first glyph of the run sits. A cell of a
            // table is the only thing that turns its text, and only by a quarter circle either
            // way, so the matrix is exact rather than worked out from a sine and a cosine.
            var up = text.TurnDegrees > 0;
            var skew = selection.SyntheticItalic ? SyntheticItalicSkew : 0;

            content.SetTextMatrix(
                0, up ? 1 : -1,
                up ? -1 : 1, skew,
                text.X, y);
        }
        else if (selection.SyntheticItalic)
        {
            content.SetTextPositionSkewed(text.X, y, SyntheticItalicSkew);
        }
        else
        {
            content.SetTextPosition(text.X, y);
        }

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
    /// <summary>Shapes a run and encodes its glyphs, for text that needs no adjustments.</summary>
    private static byte[] Encode(PdfFont font, TrueTypeFont face, string text)
    {
        var shaped = TextShaper.Shape(face, text);

        var glyphs = new ushort[shaped.Count];
        var texts = new string[shaped.Count];

        for (var i = 0; i < shaped.Count; i++)
        {
            glyphs[i] = shaped.Glyphs[i].Glyph;
            texts[i] = shaped.TextOf(i);
        }

        return font.EncodeGlyphs(glyphs, texts);
    }

    private static void ShowText(ContentStreamBuilder content, PdfFont font, PositionedText text, double size)
    {
        var face = text.Font.Font;

        // A glyph asked for by number is drawn as itself: it is a shape the face keeps for a
        // bracket that has grown, and shaping the text would find the ordinary one instead.
        if (text.Glyph is { } named)
        {
            content.ShowGlyphs(font.EncodeGlyphs([named], [text.Text]));
            return;
        }

        var shaped = TextShaper.Shape(face, text.Text, text.Kerned, text.RightToLeft);

        if (shaped.Count == 0) return;

        var justifying = text.WordSpacing > 0 && text.Text.Contains(' ');
        var spaceAdjustment = -text.WordSpacing * 1000.0 / size;
        var units = face.Metrics.UnitsPerEm;

        var segments = new List<(byte[] Encoded, double Adjustment)>();

        var glyphs = new List<ushort>(shaped.Count);
        var texts = new List<string>(shaped.Count);

        // What the glyphs so far have been raised by. A mark drawn above or below what it belongs
        // to is the only thing that asks for this, and it has to be put back afterwards.
        var rise = 0.0;

        void Cut(double adjustment)
        {
            segments.Add((font.EncodeGlyphs([.. glyphs], [.. texts]), adjustment));
            glyphs.Clear();
            texts.Clear();
        }

        void Flush()
        {
            if (glyphs.Count > 0) Cut(0);
            if (segments.Count == 0) return;

            content.ShowGlyphsAdjusted(segments);
            segments.Clear();
        }

        for (var i = 0; i < shaped.Count; i++)
        {
            var glyph = shaped.Glyphs[i];

            // A mark sits above or below the line it is on, which no movement along the line can
            // express: the text is raised for it and put back down after.
            var wanted = units > 0 ? glyph.YOffset * size / units : 0;

            if (Math.Abs(wanted - rise) > 0.0001)
            {
                Flush();
                content.SetTextRise(wanted);
                rise = wanted;
            }

            // And it is drawn away from where the pen stands without taking the pen with it, so
            // the movement is made before it and unmade after.
            if (glyph.XOffset != 0 && units > 0)
            {
                if (glyphs.Count > 0) Cut(0);

                segments.Add(([], -glyph.XOffset * 1000.0 / units));
            }

            glyphs.Add(glyph.Glyph);
            texts.Add(shaped.TextOf(i));

            var adjustment = 0.0;

            // Justification widens the spaces, which is a property of the line rather than of the
            // text, so it is added here and not by the shaper.
            if (justifying && glyph.Cluster < text.Text.Length && text.Text[glyph.Cluster] == ' ')
                adjustment += spaceAdjustment;

            // Wherever a glyph advances the pen by something other than its own width — which is
            // what kerning is — the difference is written into the page as a movement.
            var natural = face.GetAdvanceWidth(glyph.Glyph);

            if (glyph.Advance != natural && units > 0)
                adjustment += -(glyph.Advance - natural) * 1000.0 / units;

            if (glyph.XOffset != 0 && units > 0) adjustment += glyph.XOffset * 1000.0 / units;

            if (adjustment != 0) Cut(adjustment);
        }

        Flush();

        if (rise != 0) content.SetTextRise(0);
    }

    /// <summary>
    /// Converts a Y measured downward from the page top into PDF user space, where Y grows
    /// upward from the page bottom.
    /// </summary>
    private static double Flip(LaidOutPage page, double y) => page.HeightPoints - y;
}
