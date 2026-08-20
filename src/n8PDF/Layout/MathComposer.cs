using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Layout;

/// <summary>
/// What kind of thing a piece of an equation is, which is what decides the room round it.
/// </summary>
internal enum MathAtom
{
    Ordinary,
    Binary,
    Relation,
    Punctuation
}

/// <summary>One run of characters in an equation, at the place the equation puts it.</summary>
/// <param name="Glyph">
/// The shape to draw where the characters do not name it — a bracket that has grown to fit what it
/// holds is a glyph of the face's own, and no character stands for it.
/// </param>
internal sealed record MathPiece(
    string Text, double X, double Baseline, double SizePoints, FontSelection Font, double Width,
    ushort? Glyph = null);

/// <summary>A line drawn as part of an equation: a fraction's bar, a radical's overbar.</summary>
internal sealed record MathRule(double X, double Y, double Width, double Thickness);

/// <summary>
/// A piece of an equation, measured: how much room it takes, and what goes where inside it.
/// </summary>
/// <param name="Ascent">How far it reaches above its own baseline.</param>
internal sealed record MathBox(
    double Width, double Ascent, double Descent,
    IReadOnlyList<MathPiece> Pieces, IReadOnlyList<MathRule> Rules)
{
    public static readonly MathBox Empty = new(0, 0, 0, [], []);

    /// <summary>
    /// What kind of thing it begins and ends with, which is what decides the room between it and
    /// what stands next to it. A fraction is an ordinary thing however it begins inside.
    /// </summary>
    public MathAtom First { get; init; } = MathAtom.Ordinary;

    public MathAtom Last { get; init; } = MathAtom.Ordinary;

    /// <summary>
    /// How much of the width is the lean of the last glyph rather than the glyph itself.
    /// </summary>
    /// <remarks>
    /// A sloped letter overhangs what follows it, and the face says by how much. The room belongs
    /// in a row — Word puts it between an <c>x</c> and the sign after it — but not under a script:
    /// Word sets the two of <c>x²</c> at the letter's plain advance, with none of the lean in
    /// between. So the width carries it and this says how much of the width it is, for the places
    /// that have to take it back off.
    /// </remarks>
    public double Italic { get; init; }


    public double Height => Ascent + Descent;

    /// <summary>
    /// How much room the line holding this asks for above its baseline, and below it.
    /// </summary>
    /// <remarks>
    /// Not the same as the ink it covers. Worked out for the whole equation only, since only the
    /// whole of one stands on a line.
    /// </remarks>
    public double LineAscent { get; init; }

    public double LineDescent { get; init; }

    /// <summary>The same box, moved.</summary>
    public MathBox Shift(double dx, double dy) =>
        new(Width, Ascent + dy, Descent - dy,
            [.. Pieces.Select(piece => piece with { X = piece.X + dx, Baseline = piece.Baseline - dy })],
            [.. Rules.Select(rule => rule with { X = rule.X + dx, Y = rule.Y - dy })]);
}

/// <summary>
/// Sets an equation: works out where every piece of it goes, and how much room the whole comes to.
/// </summary>
/// <remarks>
/// Mathematics is not laid out the way a line of text is. Its proportions — how far a superscript
/// rises, how thick a fraction's bar is, how much room a radical leaves over what is under it —
/// are stated by the face itself, in a <c>MATH</c> table that a face meant for mathematics
/// carries. So almost nothing here is a number chosen by anyone: the rules are the ones the
/// OpenType specification lays down and the numbers are Cambria Math's own.
///
/// What is measured from Word rather than read is where Word departs from those rules, and the
/// departures are set out one by one on the members that carry them. The one that is everywhere
/// is what size an equation is set at: not the size its own runs state, but the size of the text
/// carrying it. Its letters are drawn at their runs' size and everything else — every distance
/// from the table, every bracket and radical it stretches — is measured in the em of the text
/// round it. An equation on a line of its own has no text round it, and takes its own runs.
///
/// A bracket that has to grow is drawn from the taller shapes the face keeps for it, and only a
/// face that keeps none has one drawn larger instead. What is not here is the table's recipe for
/// building one taller than the tallest shape it keeps, out of a top, a bottom and as many middles
/// as it takes.
///
/// How tall a line holding an equation comes out is worked out here as well, at the end of
/// <see cref="Compose(MathNode, ResolvedRunFormat, bool)"/>, from math-line-box-probe.
///
/// What is not here is the face's own kerning for scripts — MathKernInfo, a staircase of kerns by
/// height for every glyph — which decides how far a script is pushed off the letter it sits on. A
/// script hangs off the plain advance instead, which is exact at twelve point and a point short
/// when the letter under it is twenty.
/// </remarks>
internal sealed class MathComposer(FontLibrary fonts, StyleResolver styles)
{
    /// <summary>The face an equation is set in where the document names none.</summary>
    private const string MathFont = "Cambria Math";

    /// <summary>What an equation comes to, where every piece of it goes, and what it asks of
    /// the line that holds it.</summary>
    public MathBox Compose(MathNode node, ResolvedRunFormat format, bool display)
    {
        // What the equation's own runs say they are, which is what its letters are drawn at.
        var stated = Stated(node);

        // And the size it is *set* at, which is the size of the text carrying it and not what its
        // runs say at all. math-structure-probe is what says so: its paragraphs are twenty point
        // and every run in its equations is twelve, and Word draws the letters at twelve and the
        // brackets and radicals round them at 19.92 — twenty, rounded the way it rounds a size.
        // An equation on a line of its own has no text carrying it, and takes its own runs.
        var size = display ? stated ?? format.FontSizePoints : format.FontSizePoints;

        var style = new Style(format with { FontSizePoints = size }, size, 0, display,
            Full: display, RunSize: stated ?? size);

        var selection = Resolve(Format(new RunProperties(), style));

        if (selection is not null)
        {
            var face = selection.Font.Mathematics;

            style = style with
            {
                Script = face.ScriptPercentScaleDown,
                ScriptScript = face.ScriptScriptPercentScaleDown
            };
        }

        var box = Compose(node, style);
        if (selection is null) return box;

        var math = Constants(style, out var unit);

        // What the line holding it comes to. Measured from math-line-box-probe, which stands
        // twenty-six equations between rails of two point type so that the room each asks for
        // above and below the line can be read off Word's own page:
        //
        //   - the ink of everything in it, and over that the leading the face states for
        //     mathematics — 300 units, 1.6 points at eleven point. Nothing below: what hangs
        //     down asks for its ink and no more.
        //   - and never less than a line of the face at the size the equation is set at, which
        //     is what a bare letter gets and what an equation whose ink is small keeps.
        //
        // An equation of nothing but letters is the one case where the floor follows the runs
        // rather than the setting: x at twenty-four point in an eleven point paragraph asks for
        // a twenty-four point line.
        var metrics = selection.Font.Metrics;
        var floor = Structured(node) ? size : style.RunSize;

        return box with
        {
            LineAscent = Math.Max(metrics.Ascender * floor / metrics.UnitsPerEm,
                box.Ascent + math.MathLeading * unit),
            LineDescent = Math.Max(-metrics.Descender * floor / metrics.UnitsPerEm, box.Descent)
        };
    }

    /// <summary>
    /// Whether an equation is anything more than a run of letters.
    /// </summary>
    /// <remarks>
    /// What it decides is how tall a line holding one is at the least: an equation that is only
    /// text asks for a line of its own text, and one holding anything built — a fraction, a
    /// script, a bracket — asks for a line of the size the equation is set at, however small the
    /// thing it holds. Word's x with an i under it at twelve point asks for the same 10.5 points
    /// above the line as its bracketed x does, where a bare x asks for 11.4.
    /// </remarks>
    private static bool Structured(MathNode node) => node switch
    {
        MathText => false,
        MathSequence sequence => sequence.Children.Any(Structured),
        _ => true
    };

    /// <summary>
    /// How an equation is being set at this point of it: in what, how large, and whether it is
    /// standing on a line of its own.
    /// </summary>
    /// <param name="Full">
    /// Whether the equation this belongs to stands on a line of its own, which changes the room
    /// it leaves round what it holds and the shifts it uses. It stays true down through a fraction
    /// and its scripts, where <c>Display</c> does not.
    /// </param>
    /// <param name="RunSize">
    /// What the equation's own runs say they are, which is what its letters are drawn at and what
    /// the room out in the equation is measured in. Not the same as <c>Size</c>, which is what the
    /// equation is set at.
    /// </param>
    /// <param name="Held">
    /// Whether this is inside something — a bracket, a radical, a fraction — rather than out in
    /// the equation itself. Word sets what a thing holds more tightly than it sets the equation
    /// around it: see <see cref="Room"/>.
    /// </param>
    /// <param name="Script">
    /// What the face says a script is set at, as a percentage, and what it says a script of a
    /// script is. Carried here so that a size can be worked out wherever one is wanted.
    /// </param>
    private readonly record struct Style(
        ResolvedRunFormat Format, double Size, int Level, bool Display, bool Held = false,
        bool Full = false, bool Cramped = false,
        bool Started = false, MathAtom Preceding = MathAtom.Ordinary,
        double Script = 73, double ScriptScript = 60, double RunSize = 0)
    {
        /// <summary>The same again, one step smaller, which is what a script is set at.</summary>
        public Style Smaller => this with
        {
            Level = Math.Min(2, Level + 1),
            Display = false,
            Held = true,
            Started = false,
            Size = Reduced(Format.FontSizePoints, Math.Min(2, Level + 1)),
            RunSize = Reduced(RunSize, Math.Min(2, Level + 1))
        };

        /// <summary>
        /// A size one or two steps down, which is what a script and a script of a script are set
        /// at.
        /// </summary>
        /// <remarks>
        /// The face states both as percentages — 73 and 60 for Cambria Math — and Word takes them
        /// down to a whole half point before using them, which is the unit a document states a
        /// size in. Twelve point gives 17.52 half points, which is 17: eight and a half point,
        /// written into a PDF as the 8.4 Word rounds every size to. The same rule gives Word's
        /// 6.96 for a script of a script of twelve point, its 17.52 for a script of twenty-four,
        /// and its 4.08 for a script of six — three sizes and two levels, none of them a simple
        /// share of the size.
        /// </remarks>
        public double Reduced(double points, int level)
        {
            if (level <= 0) return points;

            var percent = level == 1 ? Script : ScriptScript;

            return Quantised(Math.Floor(points * 2 * percent / 100) / 2);
        }

        /// <summary>What the numerator and denominator of a fraction are set at.</summary>
        public Style Inner =>
            Display ? this with { Display = false, Held = true, Started = false } : Smaller;

        /// <summary>The same again, for what something holds.</summary>
        public Style Inside => this with { Held = true, Started = false };

        /// <summary>
        /// The same again, for what stands under something: under a radical's bar, or under a
        /// fraction's. A superscript there is raised less, because there is a rule over it.
        /// </summary>
        public Style Under => this with { Held = true, Cramped = true, Started = false };

        /// <summary>
        /// The em the room between two things is measured in, and whether a sloped letter's lean
        /// is counted at all.
        /// </summary>
        /// <remarks>
        /// Measured. Out in the equation, Word puts four eighteenths of the em of the text between
        /// a letter and the sign after it and adds the lean the face states for that letter: our
        /// <c>x+y=z</c> agrees with Word's to four decimal places on every one of them. Inside a
        /// bracket or a radical it does neither — it puts 2.4443 points between the same pair,
        /// which is four eighteenths of the eleven point the equation is set at rather than of the
        /// twelve its letters are, and the gap after an <c>x</c> and after a <c>+</c> come out the
        /// same, so no lean is in it.
        /// </remarks>
        public double Room => Held ? Size : RunSize;

        /// <summary>
        /// Whether a sloped letter's lean is counted here. Inline it is only counted out in the
        /// equation itself; on a line of its own Word counts it inside a fraction as well.
        /// </summary>
        public bool Leans => Level == 0 && (!Held || Full);
    }

    /// <summary>
    /// What the equation's own runs say their size is, or null where none of them says.
    /// </summary>
    /// <remarks>
    /// The first that says anything, walked in reading order: a document that states a size states
    /// the same one throughout an equation, and one that states none leaves it to the text round
    /// it. What this is for is the letters rather than the setting.
    /// </remarks>
    private static double? Stated(MathNode node) => node switch
    {
        MathText { Properties.SizeHalfPoints: { } half } => half / 2.0,
        MathText => null,
        MathSequence sequence => sequence.Children.Select(Stated).FirstOrDefault(size => size is not null),
        MathFraction fraction => Stated(fraction.Numerator) ?? Stated(fraction.Denominator),
        MathScripted scripted => Stated(scripted.Body) ?? Stated(scripted.Sub ?? scripted.Body),
        MathRadical radical => Stated(radical.Body),
        MathFenced fenced => fenced.Parts.Select(Stated).FirstOrDefault(size => size is not null),
        MathNary nary => Stated(nary.Body) ?? Stated(nary.Sub ?? nary.Body),
        MathGrid grid => grid.Rows.SelectMany(row => row).Select(Stated)
            .FirstOrDefault(size => size is not null),
        MathAccented accented => Stated(accented.Body),
        _ => null
    };

    private MathBox Compose(MathNode node, Style style) => node switch
    {
        MathText text => Text(text, style),
        MathSequence sequence => Row(sequence.Children, style),
        MathFraction fraction => Fraction(fraction, style),
        MathScripted scripted => Scripted(scripted, style),
        MathRadical radical => Radical(radical, style),
        MathFenced fenced => Fenced(fenced, style),
        MathNary nary => Nary(nary, style),
        MathGrid grid => Grid(grid, style),
        MathAccented accented => Accented(accented, style),
        _ => MathBox.Empty
    };

    // ---------------------------------------------------------------- text

    /// <summary>
    /// Characters, set in the face the run names, sloped where they are letters standing for
    /// something.
    /// </summary>
    /// <remarks>
    /// A variable is not an upright letter in italic: it is a character of its own, and a face
    /// meant for mathematics draws it from the block Unicode set aside for exactly this. So an "x"
    /// in an equation is drawn as U+1D465, which is what Word draws, rather than as an "x" leaned
    /// over — the shapes are drawn for the purpose and are not the same.
    /// </remarks>
    private MathBox Text(MathText text, Style style)
    {
        if (text.Text.Length == 0) return MathBox.Empty;

        // How large this run's own letters are drawn: its own size where it states one, taken
        // down a step for every script it is inside. Where it states none, the equation's.
        var drawn = text.Properties.SizeHalfPoints is { } half
            ? style.Reduced(half / 2.0, style.Level)
            : style.Size;

        var format = Format(text.Properties, style) with { FontSizePoints = drawn };
        var selection = Resolve(format);
        if (selection is null) return MathBox.Empty;

        var pieces = new List<MathPiece>();
        var x = 0.0;

        var first = MathAtom.Ordinary;

        // What stands before this run, which decides whether a sign it begins with is a sum or a
        // sign of its own.
        var previous = style.Preceding;
        var any = style.Started;

        var lean = 0.0;

        foreach (var (run, raw) in Atoms(text.Text, text.Upright))
        {
            var kind = Settled(raw, any, previous);

            // What it begins with is what it is, not what it counts as here: whether the sign a
            // row begins with is a sum or a sign is for whatever the row goes next to to say.
            if (pieces.Count == 0) first = raw;
            else if (style.Level == 0) x += Space(previous, kind) * style.Room;

            var width = TextMeasurer.Measure(selection.Font, run, drawn);

            pieces.Add(new MathPiece(run, x, 0, drawn, selection, width));

            lean = style.Leans ? Italic(selection, run, drawn) : 0;
            x += width + lean;

            any = true;
            previous = kind;
        }

        var (ascent, descent) = Ink(selection, text.Text, drawn);

        return new MathBox(x, ascent, descent, pieces, [])
        {
            First = first,
            Last = previous,
            Italic = lean
        };
    }

    /// <summary>
    /// How much room goes between one kind of thing and the next, as a share of an em.
    /// </summary>
    /// <remarks>
    /// Measured from Word's own setting of <c>x+y=z</c>: it puts 2.67pt on both sides of the plus
    /// of a twelve point equation and 3.33 on both sides of the equals, which are four eighteenths
    /// of an em and five. The room belongs to the pair rather than to either of them — what sets a
    /// sum apart is having something on each side of it.
    /// </remarks>
    /// <summary>
    /// The room between a sum or an integral and what it is taken of, as a share of the size the
    /// equation is set at.
    /// </summary>
    /// <remarks>
    /// Measured, not derived: Word leaves 1.8886 points after the limits of both the sum and the
    /// integral of the equations fixture, which is set at eleven point, and leaves exactly the
    /// same after each although the two operators differ in every measurement the face states for
    /// them. No constant of the
    /// table and no fraction of an em accounts for the number, so it is written down as what it
    /// was measured to be.
    /// </remarks>
    private const double NaryBodyGap = 1.8886 / 11;

    /// <summary>
    /// How much of what it holds a bracket has to cover before Word stops reaching for a taller
    /// one.
    /// </summary>
    /// <remarks>
    /// Measured from the two delimited equations. Round a+b, what the pair reach either side of
    /// the axis asks for a bracket 10.46 points tall and Word draws the plain one, which is 10.23;
    /// round a over b it asks for 17.54 and Word passes over the 13.34 shape for the 18.21 one.
    /// Nine tenths is the only factor that gives both, and it is the one TeX uses for the same
    /// decision.
    /// </remarks>
    private const double DelimiterFactor = 0.901;

    /// <summary>
    /// How far under what it holds a radical's foot goes, as a share of the size the equation is
    /// set at.
    /// </summary>
    /// <remarks>
    /// Measured, and the only part of a radical that is: Word draws the root of x+1 a quarter of a
    /// point below the line and the cube root of x three quarters of one, and 0.72 points at
    /// eleven point is what puts the foot of both within a sixth of a point of Word's.
    /// </remarks>
    private const double RadicalFootDrop = 0.72 / 11;

    /// <summary>
    /// How far over the line a slanted fraction's numerator goes, as a share of the size the
    /// equation is set at.
    /// </summary>
    private const double SkewedNumeratorRise = 3.6 / 11;

    /// <summary>How far a slanted fraction's three parts run into one another, on each side.</summary>
    private const double SkewedOverlap = 0.888 / 11;

    /// <summary>
    /// What Word rounds a position in an equation to: the three hundredth of an inch it rounds
    /// every other position to.
    /// </summary>
    public const double Quantum = 0.24;

    /// <summary>The nearest position Word would have set something at.</summary>
    public static double Quantised(double points) =>
        Math.Round(points / Quantum, MidpointRounding.AwayFromZero) * Quantum;

    /// <summary>
    /// What a thing counts as where it stands, which is not always what it is.
    /// </summary>
    /// <remarks>
    /// A sign with nothing before it is not a sum but a sign: the minus of <c>-b ± √…</c> belongs
    /// to the b rather than standing between two things, and Word sets it tight against it where
    /// the minus of <c>b - 4ac</c> gets the room a sum gets. The same goes for one following
    /// another sign or a relation, for the same reason.
    /// </remarks>
    private static MathAtom Settled(MathAtom kind, bool any, MathAtom previous) =>
        kind == MathAtom.Binary &&
        (!any || previous is MathAtom.Binary or MathAtom.Relation or MathAtom.Punctuation)
            ? MathAtom.Ordinary
            : kind;

    private static double Space(MathAtom before, MathAtom after)
    {
        if (before == MathAtom.Relation || after == MathAtom.Relation) return 5.0 / 18;
        if (before == MathAtom.Binary || after == MathAtom.Binary) return 4.0 / 18;
        if (before == MathAtom.Punctuation) return 3.0 / 18;

        return 0;
    }

    /// <summary>
    /// The characters of a run, gathered into the things they stand for and mapped to the shapes
    /// a mathematical face draws them with.
    /// </summary>
    private static IEnumerable<(string Text, MathAtom Kind)> Atoms(string text, bool upright)
    {
        var run = new System.Text.StringBuilder();
        var kind = MathAtom.Ordinary;

        foreach (var character in text)
        {
            var next = Kind(character);

            if (run.Length > 0 && (next != MathAtom.Ordinary || kind != MathAtom.Ordinary))
            {
                yield return (run.ToString(), kind);
                run.Clear();
            }

            kind = next;
            run.Append(Mapped(character, upright));
        }

        if (run.Length > 0) yield return (run.ToString(), kind);
    }

    private static MathAtom Kind(char character) => character switch
    {
        '+' or '−' or '-' or '±' or '∓' or '×' or '÷' or '∗' or '⋅' => MathAtom.Binary,
        '=' or '<' or '>' or '≤' or '≥' or '≈' or '≠' or '≡' or '∈' or '→' => MathAtom.Relation,
        ',' or ';' => MathAtom.Punctuation,
        _ => MathAtom.Ordinary
    };

    /// <summary>
    /// The character a mathematical face draws for this one: a hyphen is a minus sign, and a
    /// letter standing for something is the sloped shape from the block Unicode set aside for it.
    /// </summary>
    private static string Mapped(char character, bool upright)
    {
        if (character == '-') return "−";

        if (upright || !char.IsAsciiLetter(character)) return character.ToString();

        // The italic block, whose h is missing because Unicode had already given the constant one
        // a place of its own.
        if (character == 'h') return "ℎ";

        var offset = char.IsAsciiLetterUpper(character)
            ? 0x1D434 + (character - 'A')
            : 0x1D44E + (character - 'a');

        return char.ConvertFromUtf32(offset);
    }

    // ------------------------------------------------------------ sequences

    private MathBox Row(IReadOnlyList<MathNode> children, Style style)
    {
        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();

        var x = 0.0;
        var ascent = 0.0;
        var descent = 0.0;

        var first = MathAtom.Ordinary;
        var previous = MathAtom.Ordinary;
        var lean = 0.0;
        var any = false;

        foreach (var child in children)
        {
            var box = Compose(child, style with { Started = any, Preceding = previous });
            if (box.Width <= 0 && box.Pieces.Count == 0 && box.Rules.Count == 0) continue;

            var kind = Settled(box.First, any, previous);

            if (!any) first = box.First;
            else if (style.Level == 0) x += Space(previous, kind) * style.Room;

            any = true;
            previous = box.Last;

            var placed = box.Shift(x, 0);

            pieces.AddRange(placed.Pieces);
            rules.AddRange(placed.Rules);

            ascent = Math.Max(ascent, box.Ascent);
            descent = Math.Max(descent, box.Descent);
            x += box.Width;
            lean = box.Italic;
        }

        return new MathBox(x, ascent, descent, pieces, rules)
        {
            First = first,
            Last = previous,
            Italic = lean
        };
    }

    // ------------------------------------------------------------ fractions

    private MathBox Fraction(MathFraction fraction, Style style)
    {
        var inner = style.Inner;

        var numerator = Compose(fraction.Numerator, inner);
        var denominator = Compose(fraction.Denominator, inner);

        var math = Constants(style, out var unit);

        if (fraction.Type is "lin")
        {
            // Written on one line with a slash between, which is what "lin" asks for.
            return Row([fraction.Numerator, new MathText("/", new RunProperties(), true),
                fraction.Denominator], style);
        }

        if (fraction.Type is "skw")
        {
            // At the full size, which is what Word sets a slanted one at.
            return Skewed(Compose(fraction.Numerator, style.Inside),
                Compose(fraction.Denominator, style.Inside), style, math, unit);
        }

        var thickness = fraction.Type == "noBar" ? 0 : math.FractionRuleThickness * unit;
        var axis = math.AxisHeight * unit;

        var shiftUp = (style.Display
            ? math.FractionNumeratorDisplayStyleShiftUp
            : math.FractionNumeratorShiftUp) * unit;

        var shiftDown = (style.Display
            ? math.FractionDenominatorDisplayStyleShiftDown
            : math.FractionDenominatorShiftDown) * unit;

        var numeratorGap = (style.Display
            ? math.FractionNumDisplayStyleGapMin
            : math.FractionNumeratorGapMin) * unit;

        var denominatorGap = (style.Display
            ? math.FractionDenomDisplayStyleGapMin
            : math.FractionDenominatorGapMin) * unit;

        // Neither part may come closer to the bar than the face allows.
        var above = axis + thickness / 2 + numeratorGap;
        if (shiftUp - numerator.Descent < above) shiftUp = above + numerator.Descent;

        var below = -axis + thickness / 2 + denominatorGap;
        if (shiftDown - denominator.Ascent < below) shiftDown = below + denominator.Ascent;

        var width = Math.Max(numerator.Width, denominator.Width);

        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();

        var top = numerator.Shift((width - numerator.Width) / 2, shiftUp);
        var bottom = denominator.Shift((width - denominator.Width) / 2, -shiftDown);

        pieces.AddRange(top.Pieces);
        pieces.AddRange(bottom.Pieces);
        rules.AddRange(top.Rules);
        rules.AddRange(bottom.Rules);

        if (thickness > 0)
            rules.Add(new MathRule(0, -axis - thickness / 2, width, thickness));

        return new MathBox(width,
            Math.Max(top.Ascent, axis + thickness / 2),
            Math.Max(bottom.Descent, thickness / 2 - axis),
            pieces, rules);
    }

    /// <summary>
    /// A fraction written at a slant, with the two parts either side of a slash rather than one
    /// over the other.
    /// </summary>
    private MathBox Skewed(MathBox numerator, MathBox denominator, Style style,
        MathConstants math, double unit)
    {
        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);
        if (selection is null) return MathBox.Empty;

        // Word writes one of these at the full size rather than a stacked fraction's smaller one,
        // and raises what is over the slash by a quarter of what the table's numerator shift says
        // — 3.6 points at twelve point, against the 6.47 the shift would give. What is under it
        // drops by the table's own denominator shift, which comes to Word's 5.52 exactly.
        var rise = SkewedNumeratorRise * style.Size;
        var drop = math.FractionDenominatorShiftDown * unit;

        var top = numerator.Shift(0, rise);
        var pieces = new List<MathPiece>(top.Pieces);

        // Word draws it with the fraction slash rather than the solidus — the face keeps taller
        // shapes for that one and none for the other — and reaches for the second of them for
        // a over b. What it is tall enough for is the two baselines a stacked fraction would put
        // them at, which is 12.02 points at twelve point where the shape it picks is 12.46 and
        // the one below it 10.04. Not what the parts themselves measure: 𝑏 is taller than 𝑎 and
        // the pair of them come to more than the shape Word picked would cover.
        var height = (math.FractionNumeratorShiftUp + math.FractionDenominatorShiftDown) * unit;
        var (glyph, slashWidth, slashSize, slashAscent, slashDescent) =
            Stretched("\u2044", height, style, selection);

        // The three overlap: Word's numerator, slash and denominator run 1.78 points into one
        // another at twelve point, evenly on each side of the slash.
        var overlap = SkewedOverlap * style.Size;

        // Across the middle of the two: the slash's own ink is centred on the middle of what it
        // separates, which is where Word's is to a quarter of a point. What a reader copies out is
        // a solidus, which is what the equation says however the shape is fetched.
        var slashX = numerator.Width - overlap;
        var middle = (drop + denominator.Descent - rise - numerator.Ascent) / 2;

        pieces.Add(new MathPiece("/", slashX, middle + (slashAscent - slashDescent) / 2,
            slashSize, selection, slashWidth, glyph));

        var bottom = denominator.Shift(slashX + slashWidth - overlap, -drop);
        pieces.AddRange(bottom.Pieces);

        return new MathBox(slashX + slashWidth - overlap + denominator.Width,
            Math.Max(top.Ascent, slashAscent), Math.Max(bottom.Descent, slashDescent),
            pieces, [.. top.Rules, .. bottom.Rules]);
    }

    // -------------------------------------------------------------- scripts

    private MathBox Scripted(MathScripted scripted, Style style)
    {
        var body = Compose(scripted.Body, style);
        var math = Constants(style, out var unit);

        var smaller = style.Smaller;

        var sub = scripted.Sub is null ? null : Compose(scripted.Sub, smaller);
        var sup = scripted.Sup is null ? null : Compose(scripted.Sup, smaller);

        var pieces = new List<MathPiece>(body.Pieces);
        var rules = new List<MathRule>(body.Rules);

        var ascent = body.Ascent;
        var descent = body.Descent;

        // A script hangs off the letter's own advance, not off its lean: Word sets the two of
        // x² directly after the x, with none of the overhang in between.
        var attach = body.Width - body.Italic;
        var width = body.Width;

        var supShift = 0.0;
        var subShift = 0.0;

        if (sup is not null)
        {
            supShift = Math.Max(
                (style.Cramped ? math.SuperscriptShiftUpCramped : math.SuperscriptShiftUp) * unit,
                Math.Max(style.Cramped ? 0 : body.Ascent - math.SuperscriptBaselineDropMax * unit,
                    math.SuperscriptBottomMin * unit + sup.Descent));
        }

        if (sub is not null)
        {
            subShift = Math.Max(math.SubscriptShiftDown * unit,
                Math.Max(style.Cramped ? 0 : body.Descent + math.SubscriptBaselineDropMin * unit,
                    sub.Ascent - math.SubscriptTopMax * unit));
        }

        // With both, they must not close up on each other, and what is wanted is shared between
        // them: Word sets the two of x with an i under it 4.56 above the line and 2.64 below,
        // where the shifts on their own would be 4.04 and 2.25 and the gap short by 0.85. Half
        // each is what those two numbers are, to the three hundredth of an inch Word rounds to.
        if (sub is not null && sup is not null)
        {
            var gap = supShift - sup.Descent + subShift - sub.Ascent;
            var least = math.SubSuperscriptGapMin * unit;

            if (gap < least)
            {
                supShift += (least - gap) / 2;
                subShift += (least - gap) / 2;
            }
        }

        if (sup is not null)
        {
            var placed = sup.Shift(attach, supShift);
            pieces.AddRange(placed.Pieces);
            rules.AddRange(placed.Rules);

            ascent = Math.Max(ascent, supShift + sup.Ascent);
            width = Math.Max(width, attach + sup.Width + math.SpaceAfterScript * unit);
        }

        if (sub is not null)
        {
            var placed = sub.Shift(attach, -subShift);
            pieces.AddRange(placed.Pieces);
            rules.AddRange(placed.Rules);

            descent = Math.Max(descent, subShift + sub.Descent);
            width = Math.Max(width, attach + sub.Width + math.SpaceAfterScript * unit);
        }

        return new MathBox(width, ascent, descent, pieces, rules);
    }

    // ------------------------------------------------------------- radicals

    private MathBox Radical(MathRadical radical, Style style)
    {
        var body = Compose(radical.Body, style.Under);
        var math = Constants(style, out var unit);

        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);
        if (selection is null) return body;

        var thickness = math.RadicalRuleThickness * unit;
        var gap = (style.Display
            ? math.RadicalDisplayStyleVerticalGap
            : math.RadicalVerticalGap) * unit;

        // The sign has to reach from under what is inside it to over the top of the bar.
        var height = body.Ascent + body.Descent + gap + thickness;
        var (signGlyph, signWidth, signSize, signAscent, _) =
            Stretched("√", height, style, selection);

        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();

        var x = 0.0;

        // Where the sign goes. Its foot sits a little under what it holds — but never so low that
        // its head fails to reach the bar, and the shape the face keeps is taller than the bar
        // needs more often than not. Word's own three: the root of x+1 has its sign a quarter of a
        // point below the line, the cube root of x three quarters of one, and a root over a
        // twenty-four point letter in an eleven point paragraph has it 2.64 points above the line,
        // which is the foot for the first two and the head for the third, each within a quarter of
        // a point.
        var lift = Math.Min(body.Descent + RadicalFootDrop * style.Size,
            signAscent - (body.Ascent + gap + thickness));

        // And the bar is drawn where the sign's own head is, rather than where the body would put
        // it: what a radical looks like is a sign with a line running on from its top corner, so
        // an oversized sign carries the bar up with it. Word's cube root of x is that — its bar is
        // 9.36 points over the line where what is under it reaches 5.7.
        var top = signAscent - lift;

        // And the radical reaches a little over its own bar, which is what the table's extra
        // ascender is for: Word's three roots ask their lines for that much over the bar, to
        // within a third of a point.
        var ascent = Math.Max(top, body.Ascent + gap + thickness) +
                     math.RadicalExtraAscender * unit;

        if (radical.Degree is { } node)
        {
            // The degree goes into the crook of the sign, at the smallest size, raised by a share
            // of the sign's own height.
            var degree = Compose(node, style.Smaller.Smaller);
            var raise = math.RadicalDegreeBottomRaisePercent / 100.0 * height;

            var placed = degree.Shift(math.RadicalKernBeforeDegree * unit, raise - body.Descent);

            pieces.AddRange(placed.Pieces);
            rules.AddRange(placed.Rules);

            x = math.RadicalKernBeforeDegree * unit + degree.Width +
                math.RadicalKernAfterDegree * unit;

            x = Math.Max(0, x);
            ascent = Math.Max(ascent, placed.Ascent);
        }

        pieces.Add(new MathPiece("√", x, lift, signSize, selection, signWidth, signGlyph));

        var placedBody = body.Shift(x + signWidth, 0);
        pieces.AddRange(placedBody.Pieces);
        rules.AddRange(placedBody.Rules);

        rules.Add(new MathRule(x + signWidth, -top, body.Width, thickness));

        return new MathBox(x + signWidth + body.Width, ascent, body.Descent, pieces, rules);
    }

    // --------------------------------------------------------------- fences

    private MathBox Fenced(MathFenced fenced, Style style)
    {
        var parts = new List<MathBox>();

        foreach (var part in fenced.Parts) parts.Add(Compose(part, style.Inside));

        var math = Constants(style, out var unit);
        var axis = math.AxisHeight * unit;

        var ascent = parts.Count == 0 ? 0 : parts.Max(part => part.Ascent);
        var descent = parts.Count == 0 ? 0 : parts.Max(part => part.Descent);

        // A bracket is drawn about the axis, and has to reach as far either side of it as what it
        // holds does.
        var reach = Math.Max(ascent - axis, descent + axis);
        var height = 2 * reach * DelimiterFactor;

        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);
        if (selection is null) return Row(fenced.Parts, style);

        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();
        var x = 0.0;

        void Bracket(string character)
        {
            if (character.Length == 0 || character == " ") return;

            var (glyph, width, size, bracketAscent, bracketDescent) =
                Stretched(character, height, style, selection);

            // Centred on the axis, which is where a bracket is drawn from.
            var middle = (bracketAscent - bracketDescent) / 2;

            pieces.Add(new MathPiece(character, x, -(axis - middle), size, selection, width, glyph));
            x += width;
        }

        Bracket(fenced.Open);

        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0) Bracket(fenced.Separator);

            var placed = parts[i].Shift(x, 0);
            pieces.AddRange(placed.Pieces);
            rules.AddRange(placed.Rules);

            x += parts[i].Width;
        }

        Bracket(fenced.Close);

        return new MathBox(x, Math.Max(ascent, axis + reach), Math.Max(descent, reach - axis),
            pieces, rules);
    }

    // ------------------------------------------------------------- n-aries

    private MathBox Nary(MathNary nary, Style style)
    {
        var math = Constants(style, out var unit);

        var body = Compose(nary.Body, style.Inside);
        var smaller = style.Smaller;

        var sub = nary.Sub is null ? null : Compose(nary.Sub, smaller);
        var sup = nary.Sup is null ? null : Compose(nary.Sup, smaller);

        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);
        if (selection is null) return body;

        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();

        var operatorWidth = 0.0;
        var operatorAscent = 0.0;
        var operatorDescent = 0.0;
        var operatorLean = 0.0;

        if (nary.Operator.Length > 0)
        {
            // Larger than the text around it, which is what a sum or an integral is set at when
            // it stands in a line rather than over one.
            var size = Quantised(style.Size);
            operatorWidth = TextMeasurer.Measure(selection.Font, nary.Operator, size);

            // Its own ink, not the face's ascent and descent — the face's are as tall as its
            // tallest integral, and a sum is not that.
            (operatorAscent, operatorDescent) = Ink(selection, nary.Operator, size);

            // The middle of it goes on the axis, which is where the middle of a bracket goes.
            // Word's sum stands half a point above the line for this reason and its integral
            // half a point below it, and the two agree with the axis to within the three
            // hundredth of an inch Word rounds a position to.
            var lift = math.AxisHeight * unit - (operatorAscent - operatorDescent) / 2;

            pieces.Add(new MathPiece(nary.Operator, 0, -lift, size, selection, operatorWidth));

            operatorAscent += lift;
            operatorDescent -= lift;
            operatorLean = Italic(selection, nary.Operator, size);
        }

        var ascent = Math.Max(body.Ascent, operatorAscent);
        var descent = Math.Max(body.Descent, operatorDescent);
        var x = operatorWidth;

        // Limits go above and below only where the equation stands on a line of its own. In the
        // middle of a sentence they go beside the operator however the markup asks for them,
        // which is what Word does with the sum on the twelfth line of the equations fixture: it
        // says undOvr and Word sets it beside all the same.
        if (nary.UnderOver && style.Display)
        {
            var width = operatorWidth;
            if (sup is not null) width = Math.Max(width, sup.Width);
            if (sub is not null) width = Math.Max(width, sub.Width);

            // The operator is centred under and over its limits.
            if (pieces.Count > 0)
            {
                var centred = pieces[0] with { X = (width - operatorWidth) / 2 };
                pieces[0] = centred;
            }

            if (sup is not null)
            {
                var rise = operatorAscent + math.UpperLimitGapMin * unit + sup.Descent;
                var placed = sup.Shift((width - sup.Width) / 2, rise);

                pieces.AddRange(placed.Pieces);
                rules.AddRange(placed.Rules);
                ascent = Math.Max(ascent, rise + sup.Ascent);
            }

            if (sub is not null)
            {
                var drop = operatorDescent + math.LowerLimitGapMin * unit + sub.Ascent;
                var placed = sub.Shift((width - sub.Width) / 2, -drop);

                pieces.AddRange(placed.Pieces);
                rules.AddRange(placed.Rules);
                descent = Math.Max(descent, drop + sub.Descent);
            }

            x = width;
        }
        else
        {
            // Beside it, a limit is a script on the operator, and takes the shifts a script takes
            // — measured against the operator's own ink, which is what makes an integral's limits
            // stand further off than a sum's.
            //
            // The upper one hangs off the operator's advance and the lower one off the advance
            // less its lean, which is how far an integral leans over what comes after it. Word's
            // integral puts them 2.24 points apart at twelve point, which is the lean the face
            // states to a hundredth of a point.
            var rise = 0.0;
            var drop = 0.0;

            if (sup is not null)
            {
                rise = Math.Max(math.SuperscriptShiftUp * unit,
                    Math.Max(operatorAscent - math.SuperscriptBaselineDropMax * unit,
                        math.SuperscriptBottomMin * unit + sup.Descent));
            }

            if (sub is not null)
            {
                drop = Math.Max(math.SubscriptShiftDown * unit,
                    Math.Max(operatorDescent + math.SubscriptBaselineDropMin * unit,
                        sub.Ascent - math.SubscriptTopMax * unit));
            }

            if (sub is not null && sup is not null)
            {
                var gap = rise - sup.Descent + drop - sub.Ascent;
                var least = math.SubSuperscriptGapMin * unit;

                if (gap < least)
                {
                    rise += (least - gap) / 2;
                    drop += (least - gap) / 2;
                }
            }

            if (sup is not null)
            {
                var placed = sup.Shift(operatorWidth, rise);

                pieces.AddRange(placed.Pieces);
                rules.AddRange(placed.Rules);

                ascent = Math.Max(ascent, rise + sup.Ascent);
                x = Math.Max(x, operatorWidth + sup.Width);
            }

            if (sub is not null)
            {
                var placed = sub.Shift(operatorWidth - operatorLean, -drop);

                pieces.AddRange(placed.Pieces);
                rules.AddRange(placed.Rules);

                descent = Math.Max(descent, drop + sub.Descent);
                x = Math.Max(x, operatorWidth - operatorLean + sub.Width);
            }
        }

        if (nary.Operator.Length > 0) x += NaryBodyGap * style.Size;

        var placedBody = body.Shift(x, 0);
        pieces.AddRange(placedBody.Pieces);
        rules.AddRange(placedBody.Rules);

        return new MathBox(x + body.Width, ascent, descent, pieces, rules);
    }

    // ---------------------------------------------------------------- grids

    private MathBox Grid(MathGrid grid, Style style)
    {
        var boxes = grid.Rows
            .Select(row => row.Select(cell => Compose(cell, style.Inside)).ToList())
            .ToList();

        if (boxes.Count == 0) return MathBox.Empty;

        var columns = boxes.Max(row => row.Count);
        var widths = new double[columns];

        foreach (var row in boxes)
        {
            for (var i = 0; i < row.Count; i++) widths[i] = Math.Max(widths[i], row[i].Width);
        }

        var math = Constants(style, out var unit);

        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);

        // A column of a matrix stands an em of the equation's own size from the next, which is
        // what Word puts between the 1 and the 2 of a two by two: 10.99 points at twelve point
        // against the 11.04 an em comes to.
        var column = style.Size;

        // A row stands a line from the next, the line being the face's own ascent and descent at
        // that size and the leading the table asks for. Word's two rows are 12.72 points apart
        // and this comes to 12.66, which is inside the three hundredth of an inch it rounds to.
        var step = selection is null
            ? column
            : (selection.Font.Metrics.TypoAscender - selection.Font.Metrics.TypoDescender) *
              column / selection.Font.Metrics.UnitsPerEm + math.MathLeading * unit;

        var pieces = new List<MathPiece>();
        var rules = new List<MathRule>();

        var ascent = 0.0;
        var descent = 0.0;
        var y = 0.0;

        for (var r = 0; r < boxes.Count; r++)
        {
            var row = boxes[r];

            var x = 0.0;
            for (var c = 0; c < row.Count; c++)
            {
                var placed = row[c].Shift(x + (widths[c] - row[c].Width) / 2, y);

                pieces.AddRange(placed.Pieces);
                rules.AddRange(placed.Rules);

                ascent = Math.Max(ascent, placed.Ascent);
                descent = Math.Max(descent, placed.Descent);

                x += widths[c] + column;
            }

            y -= step;
        }

        // The whole is centred on the axis, which is what puts a matrix beside what it multiplies.
        var lift = math.AxisHeight * unit - (ascent - descent) / 2;

        var whole = new MathBox(widths.Sum() + column * (columns - 1),
            ascent, descent, pieces, rules).Shift(0, lift);

        return whole;
    }

    // -------------------------------------------------------------- accents

    private MathBox Accented(MathAccented accented, Style style)
    {
        var body = Compose(accented.Body, style.Inside);

        var format = Format(new RunProperties(), style);
        var selection = Resolve(format);
        if (selection is null) return body;

        var math = Constants(style, out var unit);
        var size = Quantised(style.Size);

        var width = TextMeasurer.Measure(selection.Font, accented.Accent, size);
        var pieces = new List<MathPiece>(body.Pieces);

        // Over the middle of what it belongs to, clear of its top.
        var x = (body.Width - width) / 2;

        var baseline = accented.Above
            ? -(body.Ascent + math.OverbarVerticalGap * unit)
            : body.Descent + math.OverbarVerticalGap * unit;

        pieces.Add(new MathPiece(accented.Accent, x, baseline, size, selection, width));

        return new MathBox(body.Width,
            accented.Above ? body.Ascent + math.OverbarExtraAscender * unit : body.Ascent,
            accented.Above ? body.Descent : body.Descent + math.OverbarExtraAscender * unit,
            pieces, body.Rules);
    }

    // ---------------------------------------------------------------- parts

    /// <summary>
    /// How far a sloped run leans out past its own width, which is what the next thing along has
    /// to be moved by.
    /// </summary>
    private static double Italic(FontSelection selection, string text, double size)
    {
        if (text.Length == 0) return 0;

        var font = selection.Font;
        var corrections = font.ItalicCorrections;
        if (corrections.Count == 0) return 0;

        // Only the last glyph of a run leans into what follows it.
        var last = text.EnumerateRunes().LastOrDefault();
        var glyph = font.GetGlyphIndex(last.Value);

        return corrections.TryGetValue(glyph, out var correction)
            ? correction * size / font.Metrics.UnitsPerEm
            : 0;
    }

    /// <summary>
    /// How far the characters themselves reach above and below the baseline.
    /// </summary>
    /// <remarks>
    /// The ink rather than the face's own ascent and descent, because a face meant for mathematics
    /// says its ascent is as tall as its tallest integral — nearly three ems in Cambria Math. Set
    /// a fraction by that and its two halves are an inch apart. What a fraction is set by is how
    /// far its numerator actually reaches down, which is what a glyph's own bounding box says: for
    /// an <c>a</c> that is nothing at all, and the bar sits just under it.
    /// </remarks>
    private static (double Ascent, double Descent) Ink(
        FontSelection selection, string text, double size)
    {
        var font = selection.Font;
        var em = font.Metrics.UnitsPerEm;

        double top = 0, bottom = 0;
        var any = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = font.GetGlyphIndex(rune.Value);
            if (glyph == 0) continue;

            if (font.GetGlyphBounds(glyph) is not { } bounds) continue;

            top = any ? Math.Max(top, bounds.MaxY) : bounds.MaxY;
            bottom = any ? Math.Min(bottom, bounds.MinY) : bounds.MinY;
            any = true;
        }

        if (!any)
        {
            // A face whose outlines are PostScript keeps no such box, so the one it states for
            // its typography stands in.
            return (font.Metrics.TypoAscender * size / em, -font.Metrics.TypoDescender * size / em);
        }

        return (Math.Max(0, top) * size / em, Math.Max(0, -bottom) * size / em);
    }

    /// <summary>
    /// A glyph drawn at whatever size makes it the height asked for, where that is taller than it
    /// comes out at the size in hand.
    /// </summary>
    /// <remarks>
    /// What the face actually offers is a set of larger shapes and a recipe for assembling a
    /// taller one out of a top, a bottom and as many middles as it takes. Scaling the ordinary
    /// shape instead is what Word's own output looks like — every stretched glyph on the equations
    /// page comes out at a size of its own rather than at the type size — and it is within a
    /// fraction of a point of Word for the heights an equation usually asks for.
    /// </remarks>
    private static (ushort? Glyph, double Width, double Size, double Ascent, double Descent) Stretched(
        string character, double height, Style style, FontSelection selection)
    {
        var font = selection.Font;
        var em = font.Metrics.UnitsPerEm;
        var size = Quantised(style.Size);

        var rune = character.EnumerateRunes().FirstOrDefault();
        var glyph = font.GetGlyphIndex(rune.Value);

        // The shapes the face keeps for this one, from its own upwards, until one is tall enough.
        if (font.MathVariants.TryGetValue(glyph, out var variants))
        {
            foreach (var (variant, tall) in variants)
            {
                if (tall * size / em < height - 0.01) continue;

                return Sized(variant);
            }

            if (variants.Count > 0) return Sized(variants[^1].Glyph);
        }

        // A face that offers none is drawn larger, which thickens its strokes with it but is
        // better than a bracket that does not reach.
        var (ascent, descent) = Ink(selection, character, 1);
        var natural = ascent + descent;

        if (natural > 0 && height > natural * size) size = height / natural;

        return (null, TextMeasurer.Measure(font, character, size), size,
            ascent * size, descent * size);

        (ushort? Glyph, double Width, double Size, double Ascent, double Descent) Sized(ushort chosen)
        {
            var bounds = font.GetGlyphBounds(chosen);

            return (chosen, font.GetAdvanceWidth(chosen) * size / em, size,
                bounds is { } box ? Math.Max(0, box.MaxY) * size / em : height,
                bounds is { } under ? Math.Max(0, -under.MinY) * size / em : 0);
        }
    }

    /// <summary>
    /// The face's own account of how mathematics is set, and what one of its units comes to at the
    /// size in hand.
    /// </summary>
    private MathConstants Constants(Style style, out double unit)
    {
        var selection = Resolve(Format(new RunProperties(), style));

        if (selection is null)
        {
            unit = style.Size / 2048;
            return MathConstants.Fallback(2048);
        }

        var metrics = selection.Font.Metrics;
        unit = style.Size / metrics.UnitsPerEm;

        return selection.Font.Mathematics;
    }

    /// <summary>The formatting a piece of an equation is set with.</summary>
    private ResolvedRunFormat Format(RunProperties properties, Style style)
    {
        // The size comes from where the equation is being set, not from the run: a run inside a
        // superscript states the size of the equation, and the superscript is what makes it small.
        var resolved = styles.ResolveRun(null, properties) with
        {
            FontSizePoints = style.Size
        };

        // An equation is set in a face meant for mathematics whatever the paragraph round it is
        // set in, unless the run names one of its own.
        return properties.AsciiFont is { Length: > 0 }
            ? resolved
            : resolved with { FontFamily = MathFont };
    }

    private FontSelection? Resolve(ResolvedRunFormat format) =>
        fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var selection)
            ? selection
            : fonts.TryResolve(MathFont, false, false, out var math)
                ? math
                : null;
}
