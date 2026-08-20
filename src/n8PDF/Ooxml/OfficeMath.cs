using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>One piece of an equation.</summary>
/// <remarks>
/// An equation is a tree rather than a line of text: a fraction holds two of these, a radical holds
/// one and may hold a degree, a subscript holds what it is attached to. Nothing about where any of
/// it goes is decided here — see <see cref="Layout.MathComposer"/> — this is only what the markup
/// says.
/// </remarks>
internal abstract record MathNode;

/// <summary>Characters, with the properties of the run they came from.</summary>
/// <param name="Upright">
/// True where the run asked to be set upright. A letter in an equation is a variable and is set
/// in italic unless something says otherwise, which is why this is stated the way round it is.
/// </param>
internal sealed record MathText(string Text, RunProperties Properties, bool Upright) : MathNode;

/// <summary>One thing after another, which is what every container in the markup holds.</summary>
internal sealed record MathSequence(IReadOnlyList<MathNode> Children) : MathNode;

/// <param name="Type">"bar", "skw" for one set at a slant, "lin" for one written with a slash,
/// or "noBar" for one stacked without a rule.</param>
internal sealed record MathFraction(MathNode Numerator, MathNode Denominator, string Type) : MathNode;

internal sealed record MathScripted(MathNode Body, MathNode? Sub, MathNode? Sup) : MathNode;

internal sealed record MathRadical(MathNode Body, MathNode? Degree) : MathNode;

/// <param name="Separator">What goes between the parts where there is more than one.</param>
internal sealed record MathFenced(
    string Open, string Close, string Separator, IReadOnlyList<MathNode> Parts) : MathNode;

/// <param name="UnderOver">
/// True where the limits go above and below the operator rather than beside it. A sum takes them
/// above and below, an integral beside, and the markup may say either.
/// </param>
internal sealed record MathNary(
    string Operator, MathNode? Sub, MathNode? Sup, MathNode Body, bool UnderOver) : MathNode;

/// <summary>A row of rows: a matrix, or the lines of an aligned array.</summary>
internal sealed record MathGrid(IReadOnlyList<IReadOnlyList<MathNode>> Rows) : MathNode;

/// <param name="Above">True for a mark over the top, false for one underneath.</param>
internal sealed record MathAccented(string Accent, MathNode Body, bool Above) : MathNode;

/// <summary>Reads an equation out of the markup Word writes it in.</summary>
/// <remarks>
/// Office Math Markup is its own language inside WordprocessingML, in a namespace of its own, and
/// what it holds is not runs and paragraphs but fractions and radicals. A reader that walks a
/// paragraph looking for <c>w:r</c> finds nothing in it at all, which is what used to happen here:
/// an equation reached the page as the space it took up and nothing else.
///
/// Anything not understood gives up its own children rather than being passed over, so a construct
/// this does not draw still puts its text on the page.
/// </remarks>
internal static class OfficeMath
{
    public static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    /// <summary>Reads an <c>m:oMath</c>, or anything else that holds a run of pieces.</summary>
    public static MathNode Parse(XElement element) => Sequence(element);

    private static MathNode Sequence(XElement container)
    {
        var children = new List<MathNode>();

        foreach (var child in container.Elements())
        {
            if (Node(child) is { } node) children.Add(node);
        }

        return children.Count == 1 ? children[0] : new MathSequence(children);
    }

    private static MathNode? Node(XElement element)
    {
        if (element.Name.Namespace != Main)
        {
            // Ordinary markup inside an equation — a bookmark, a proofing mark — carries nothing
            // to draw, but a w:r inside m:oMath does, and it is text like any other.
            return element.Name == W.Main + "r"
                ? new MathText(Text(element), Properties(element), Upright: true)
                : null;
        }

        return element.Name.LocalName switch
        {
            "r" => new MathText(Text(element), Properties(element), Upright(element)),

            "f" => new MathFraction(
                Child(element, "num"), Child(element, "den"),
                element.Element(Main + "fPr")?.Element(Main + "type")?.Attribute(Main + "val")?.Value
                ?? "bar"),

            "sSup" => new MathScripted(Child(element, "e"), null, Child(element, "sup")),
            "sSub" => new MathScripted(Child(element, "e"), Child(element, "sub"), null),
            "sSubSup" => new MathScripted(
                Child(element, "e"), Child(element, "sub"), Child(element, "sup")),

            // Something set before what it belongs to rather than after it. Where it goes is not
            // drawn differently here yet, but its text is kept.
            "sPre" => new MathSequence([
                new MathScripted(new MathSequence([]), Child(element, "sub"), Child(element, "sup")),
                Child(element, "e")
            ]),

            "rad" => new MathRadical(Child(element, "e"), Degree(element)),

            "d" => Fenced(element),

            "nary" => Nary(element),

            "func" => new MathSequence([Child(element, "fName"), Child(element, "e")]),

            "limLow" => new MathNary(string.Empty, Child(element, "lim"), null,
                Child(element, "e"), UnderOver: true),

            "limUpp" => new MathNary(string.Empty, null, Child(element, "lim"),
                Child(element, "e"), UnderOver: true),

            "m" => new MathGrid([
                .. element.Elements(Main + "mr").Select(row =>
                    (IReadOnlyList<MathNode>)[.. row.Elements(Main + "e").Select(Sequence)])
            ]),

            "eqArr" => new MathGrid([
                .. element.Elements(Main + "e").Select(row => (IReadOnlyList<MathNode>)[Sequence(row)])
            ]),

            "acc" => new MathAccented(
                Character(element, "accPr") ?? "̂", Child(element, "e"), Above: true),

            "bar" => new MathAccented(
                "̅", Child(element, "e"),
                element.Element(Main + "barPr")?.Element(Main + "pos")?
                    .Attribute(Main + "val")?.Value != "bot"),

            // A box, a phantom, a group character, a border: what each of them says about how it
            // is drawn is not read, but what each of them holds is.
            "box" or "borderBox" or "phant" or "groupChr" or "e" or "num" or "den" or "sub" or
                "sup" or "deg" or "fName" or "lim" or "oMath" or "oMathPara" => Sequence(element),

            // Properties, not content.
            "rPr" or "fPr" or "radPr" or "dPr" or "naryPr" or "accPr" or "barPr" or "mPr" or
                "ctrlPr" or "argPr" or "eqArrPr" or "funcPr" or "groupChrPr" or "limLowPr" or
                "limUppPr" or "phantPr" or "boxPr" or "borderBoxPr" or "sSupPr" or "sSubPr" or
                "sSubSupPr" or "sPrePr" => null,

            _ => Sequence(element)
        };
    }

    private static MathNode Child(XElement element, string name) =>
        element.Element(Main + name) is { } child ? Sequence(child) : new MathSequence([]);

    /// <summary>
    /// The degree of a radical, or null where it is a square root. The markup writes an empty
    /// degree for a square root and says so again in its properties.
    /// </summary>
    private static MathNode? Degree(XElement element)
    {
        if (element.Element(Main + "radPr")?.Element(Main + "degHide")?
                .Attribute(Main + "val")?.Value is "1" or "true")
        {
            return null;
        }

        var degree = element.Element(Main + "deg");
        if (degree is null || !degree.Elements().Any()) return null;

        return Sequence(degree);
    }

    private static MathNode Fenced(XElement element)
    {
        var properties = element.Element(Main + "dPr");

        return new MathFenced(
            properties?.Element(Main + "begChr")?.Attribute(Main + "val")?.Value ?? "(",
            properties?.Element(Main + "endChr")?.Attribute(Main + "val")?.Value ?? ")",
            properties?.Element(Main + "sepChr")?.Attribute(Main + "val")?.Value ?? "|",
            [.. element.Elements(Main + "e").Select(Sequence)]);
    }

    private static MathNode Nary(XElement element)
    {
        var properties = element.Element(Main + "naryPr");

        // An integral takes its limits beside it and a sum takes them above and below, and the
        // markup may say which. Word's own default is beside, whatever the operator.
        var location = properties?.Element(Main + "limLoc")?.Attribute(Main + "val")?.Value;
        var character = Character(element, "naryPr") ?? "∫";

        return new MathNary(character,
            element.Element(Main + "sub") is { } sub ? Sequence(sub) : null,
            element.Element(Main + "sup") is { } sup ? Sequence(sup) : null,
            Child(element, "e"),
            UnderOver: location == "undOvr");
    }

    private static string? Character(XElement element, string properties) =>
        element.Element(Main + properties)?.Element(Main + "chr")?.Attribute(Main + "val")?.Value;

    private static string Text(XElement run) =>
        string.Concat(run.Elements().Where(child =>
                child.Name == Main + "t" || child.Name == W.Main + "t")
            .Select(child => child.Value));

    /// <summary>
    /// The formatting of the run the characters came from. An equation's runs carry ordinary
    /// WordprocessingML properties — the face and the size are stated the usual way.
    /// </summary>
    private static RunProperties Properties(XElement run) =>
        run.Element(W.Main + "rPr") is { } rPr
            ? DocumentParser.ParseRunProperties(rPr)
            : new RunProperties();

    /// <summary>
    /// Whether a run is to be set upright rather than sloped. Everything in an equation slopes
    /// unless it says not to, and a run says not to either by naming the plain style or by the
    /// older element that means the same thing.
    /// </summary>
    private static bool Upright(XElement run)
    {
        var properties = run.Element(Main + "rPr");
        if (properties is null) return false;

        if (properties.Element(Main + "nor") is not null) return true;

        return properties.Element(Main + "sty")?.Attribute(Main + "val")?.Value is "p" or "b";
    }
}
