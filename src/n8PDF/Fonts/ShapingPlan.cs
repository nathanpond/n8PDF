using n8PDF.Fonts.OpenType;
using n8PDF.Text;

namespace n8PDF.Fonts;

/// <summary>
/// What has to be done to a run of one script, and in what order.
/// </summary>
/// <remarks>
/// A font's features are not a set to be applied but a sequence, and the sequence differs by
/// script: the rules that give an Arabic letter its shape must run before the ones that write two
/// letters as one, and an Indic syllable has to be put in order between two groups of them. Which
/// glyphs each feature may touch differs too — half a syllable is subject to rules the other half
/// is not — so a feature is applied with a mask saying where, rather than everywhere.
///
/// A plan is chosen by looking at the text. Nothing in a document says which script a run is
/// written in, and nothing needs to: the characters say it.
/// </remarks>
internal abstract class ShapingPlan
{
    /// <summary>The mask of a feature that applies wherever it finds something to do.</summary>
    public const uint Everywhere = 1u;

    /// <summary>The plan for the writing this run is in.</summary>
    public static ShapingPlan For(string text)
    {
        foreach (var character in text)
        {
            var plan = ScriptOf(character);
            if (plan is not null) return plan;
        }

        return Default;
    }

    private static readonly ArabicPlan Arabic = new();

    // The scripts that need nothing said about them still need to be named: a font files its
    // features under the script they belong to, and the marks of one script are not the marks of
    // another.
    private static readonly DefaultPlan Latin = new(["latn"]);
    private static readonly DefaultPlan Hebrew = new(["hebr"]);
    private static readonly DefaultPlan Greek = new(["grek"]);
    private static readonly DefaultPlan Cyrillic = new(["cyrl"]);
    private static readonly DefaultPlan Elsewhere = new([]);

    /// <summary>What is used where the text says nothing about its script.</summary>
    private static readonly DefaultPlan Default = Latin;

    /// <summary>
    /// Which plan a character calls for, or null where it says nothing — a space, a digit, a
    /// Latin letter in the middle of a Hindi sentence.
    /// </summary>
    private static ShapingPlan? ScriptOf(char character) =>
        character switch
        {
            <= '\u036f' => null,          // Latin, and the marks that go on it

            >= '\u0370' and <= '\u03ff' => Greek,
            >= '\u1f00' and <= '\u1fff' => Greek,
            >= '\u0400' and <= '\u052f' => Cyrillic,
            >= '\u0590' and <= '\u05ff' => Hebrew,
            >= '\ufb1d' and <= '\ufb4f' => Hebrew,   // its presentation forms

            >= '؀' and <= 'ۿ' => Arabic,   // Arabic
            >= 'ݐ' and <= 'ݿ' => Arabic,   // its supplement
            >= 'ࢠ' and <= 'ࣿ' => Arabic,   // and its extended block
            >= 'ﭐ' and <= '﷿' => Arabic,   // the presentation forms, which still join
            >= 'ﹰ' and <= '﻿' => Arabic,

            >= 'ก' and <= '๛' => ThaiPlan.Thai,
            >= 'ກ' and <= '໹' => ThaiPlan.Lao,
            >= 'ក' and <= '៹' => KhmerPlan.Instance,
            >= 'က' and <= '႟' => MyanmarPlan.Instance,
            >= 'ꧠ' and <= '꧿' => MyanmarPlan.Instance,
            >= 'ꩠ' and <= 'ꩿ' => MyanmarPlan.Instance,

            _ => IndicPlan.For(character) ?? Unnamed(character)
        };

    /// <summary>
    /// Anything else with writing of its own. Nothing is done to it, and nothing is claimed about
    /// its script either: a font's features are taken as they come rather than from the list of
    /// some other script that happens to be in the same file.
    /// </summary>
    private static ShapingPlan? Unnamed(char character) =>
        character >= '\u0530' && !char.IsWhiteSpace(character) && !char.IsDigit(character)
            ? Elsewhere
            : null;

    /// <summary>
    /// What the font may call this script, most specific first. A feature declared under one
    /// script may say something quite different from the same feature declared under another, so
    /// the run's own script is asked for by name.
    /// </summary>
    protected virtual string[] ScriptTags => ["latn"];

    /// <summary>Turns the run into the glyphs that draw it.</summary>
    public abstract void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer);

    /// <summary>
    /// Places them: kerning where the document asks for it, and the marks on what they belong to,
    /// which no document has to ask for.
    /// </summary>
    public virtual void Position(TrueTypeFont font, List<ShapeItem> buffer, bool applyKerning)
    {
        var positioner = font.Positioner;

        positioner?.SelectScript(ScriptTags);

        if (applyKerning)
        {
            if (positioner?.HasLookups("kern") == true) positioner.Apply(buffer, "kern");
            else Legacy(font, buffer);
        }

        if (positioner is null) return;

        foreach (var feature in PositioningFeatures) positioner.Apply(buffer, feature);
    }

    /// <summary>
    /// The positioning features to run, in order. Distances first, then the marks, which are
    /// placed against glyphs that have stopped moving.
    /// </summary>
    protected virtual string[] PositioningFeatures => ["dist", "abvm", "blwm", "mark", "mkmk"];

    /// <summary>
    /// Kerning from the old <c>kern</c> table, for the faces that predate <c>GPOS</c> — Times New
    /// Roman has no kerning feature at all, and a document that asks for kerning in it means this.
    /// </summary>
    private static void Legacy(TrueTypeFont font, List<ShapeItem> buffer)
    {
        for (var i = 0; i + 1 < buffer.Count; i++)
        {
            buffer[i].Advance += font.GetKerning(buffer[i].Glyph, buffer[i + 1].Glyph);
        }
    }
}

/// <summary>
/// The plan for writing that needs nothing said about it: Latin, Greek, Cyrillic, Hebrew.
/// </summary>
/// <remarks>
/// Composition still runs — a mark and the vowel beside it may be one glyph in the font, and that
/// is true of every script — but the ligature features are left alone. Word does not write "fi" as
/// one shape unless the document asks, and neither does this.
/// </remarks>
internal sealed class DefaultPlan(string[] tags) : ShapingPlan
{
    protected override string[] ScriptTags { get; } = tags;

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        substitutor.SelectScript(ScriptTags);

        substitutor.Apply(buffer, "ccmp");
        substitutor.Apply(buffer, "locl");
    }
}

/// <summary>
/// The plan for the scripts that join their letters.
/// </summary>
/// <remarks>
/// Which of its four shapes a letter takes is settled before any of the font's rules run, and is
/// told to the font by giving each glyph the mask of exactly one of the four features. The
/// features themselves are then applied in the order the specification lays down, and each fires
/// only where its mask allows — which is what makes one feature able to say something about the
/// letters that open a word without saying it about the letters that close one.
/// </remarks>
internal sealed class ArabicPlan : ShapingPlan
{
    protected override string[] ScriptTags => ["arab"];

    private const uint Isolated = 1u << 1;
    private const uint Initial = 1u << 2;
    private const uint Medial = 1u << 3;
    private const uint Final = 1u << 4;

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        substitutor.SelectScript(ScriptTags);

        if (ArabicJoining.Joins(text))
        {
            var forms = ArabicJoining.Forms(text);

            foreach (var item in buffer)
            {
                if (item.Cluster < 0 || item.Cluster >= forms.Length) continue;

                item.Mask = Everywhere | forms[item.Cluster] switch
                {
                    JoiningForm.Initial => Initial,
                    JoiningForm.Medial => Medial,
                    JoiningForm.Final => Final,
                    _ => Isolated
                };
            }
        }

        substitutor.Apply(buffer, "ccmp");
        substitutor.Apply(buffer, "locl");

        substitutor.Apply(buffer, "isol", Isolated);
        substitutor.Apply(buffer, "init", Initial);
        substitutor.Apply(buffer, "medi", Medial);
        substitutor.Apply(buffer, "fina", Final);

        substitutor.Apply(buffer, "rlig");
        substitutor.Apply(buffer, "calt");
        substitutor.Apply(buffer, "liga");
        substitutor.Apply(buffer, "clig");
    }
}
