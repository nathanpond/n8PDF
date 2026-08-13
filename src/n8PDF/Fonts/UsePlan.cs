using n8PDF.Fonts.OpenType;
using n8PDF.Text;

namespace n8PDF.Fonts;

/// <summary>
/// The plan for the scripts shaped by rules that belong to no script in particular.
/// </summary>
/// <remarks>
/// Most of the writing systems descended from Brahmi are not given rules of their own. There are
/// too many of them, they are alike enough, and the differences between them are differences the
/// font already describes. So one set of rules is applied to all: work out what each character is —
/// something to build on, a vowel drawn above, a consonant drawn below, a mark on a mark — divide
/// the run into clusters on that basis, ask the font for the shapes in a fixed order, and move the
/// two things that are drawn away from where they are stored.
///
/// Only two things move. An r at the head of a cluster is drawn towards its end, before whatever is
/// drawn after the base; and a vowel written to the left is drawn before the letters it follows.
/// Everything else is left where it stands, because the font's own rules are what put it in place.
/// That is the whole engine, and it shapes some seventy scripts — Sinhala, Tibetan, Javanese,
/// Balinese, Cham, Tai Tham, Chakma, Newa, Adlam and the rest — none of which has a line of code
/// here to itself.
/// </remarks>
internal sealed class UsePlan : ShapingPlan
{
    /// <summary>The r at the head of a cluster, which only some of the cluster may become.</summary>
    private const uint Rphf = 1u << 1;

    private const uint Isolated = 1u << 2;
    private const uint Initial = 1u << 3;
    private const uint Medial = 1u << 4;
    private const uint Final = 1u << 5;

    private UsePlan(string tag) => _tag = tag;

    private readonly string _tag;

    protected override string[] ScriptTags => [_tag];

    /// <summary>
    /// The scripts whose letters change shape by where they stand in a word, the way Arabic's do,
    /// and which say so per letter rather than per cluster.
    /// </summary>
    private static readonly string[] Joining =
        ["adlm", "chrs", "mand", "mani", "mong", "nko ", "ougr", "phag", "phlp", "rohg", "sogd"];

    private bool JoinsLikeArabic => Array.IndexOf(Joining, _tag) >= 0;

    private static readonly Dictionary<string, UsePlan> Plans = [];

    /// <summary>
    /// The plan for a character, or null where its script is shaped by rules of its own.
    /// </summary>
    /// <remarks>
    /// One plan for each script rather than one for all of them, because the only thing a plan
    /// carries is the name to ask the font by.
    /// </remarks>
    public static ShapingPlan? For(int codePoint)
    {
        if (UseSyllables.ScriptTagOf(codePoint) is not { } tag) return null;

        lock (Plans)
        {
            if (Plans.TryGetValue(tag, out var found)) return found;

            return Plans[tag] = new UsePlan(tag);
        }
    }

    protected override MarkWidths Marks => MarkWidths.BeforePlacing;

    public override bool DecomposesMarks => true;

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        // A font that files its rules under no script in particular is not written to this engine
        // and must not be shaped by it. Reordering a run the font expects in the order it was
        // typed moves letters the font is about to move again — and several of these faces do
        // exactly that, drawing a vowel to the left of its consonant by moving the glyph rather
        // than by asking for the character to be moved.
        if (!substitutor.SelectScript(ScriptTags))
        {
            DefaultPlan.Anywhere.Substitute(font, text, buffer);
            return;
        }

        substitutor.WithinSyllables = true;

        foreach (var item in buffer) item.Category = (byte)UseSyllables.CategoryOf(item.CodePoint);

        var clusters = Divide(buffer);

        // What every script gets before anything of its own: composition, the local forms, the
        // dots that change a letter, and the ligatures a script insists on.
        substitutor.Apply(buffer, Preparation);

        // An r at the head of a cluster may become the mark drawn at its end — and only the font
        // can say whether it does, which is why what it did is read back afterwards.
        Mark(buffer, clusters);

        Clear(buffer);
        substitutor.Apply(buffer, "rphf", Rphf);
        Record(buffer, clusters, UseCategory.Repha, Rphf);

        Clear(buffer);
        substitutor.Apply(buffer, "pref");
        Record(buffer, clusters, UseCategory.VowelPre, uint.MaxValue);

        substitutor.Apply(buffer, Basic);

        // A cluster with nothing to hang from is drawn on a circle, which is how a reader is told
        // that what was typed does not spell anything.
        DottedCircles(font, buffer, clusters);

        foreach (var (start, end, kind) in Ranges(buffer, clusters)) Reorder(buffer, start, end, kind);

        // The forms a letter takes by where it stands in a word, for the scripts that have them.
        Topographical(text, buffer, Ranges(buffer, clusters));


        substitutor.WithinSyllables = false;

        substitutor.Apply(buffer, [("isol", Isolated), ("init", Initial), ("medi", Medial),
            ("fina", Final)]);

        substitutor.Apply(buffer, Presentation);
    }

    /// <summary>What is done to every script before anything of its own.</summary>
    private static readonly string[] Preparation = ["locl", "ccmp", "nukt", "akhn"];

    /// <summary>The shapes a cluster is made of, applied before anything is moved.</summary>
    private static readonly string[] Basic =
        ["rkrf", "abvf", "blwf", "half", "pstf", "vatu", "cjct"];

    /// <summary>
    /// And the typography, applied to the run rather than to a cluster: what the script asks for,
    /// and then what any writing asks for.
    /// </summary>
    private static readonly string[] Presentation =
        ["abvs", "blws", "haln", "pres", "psts", "rlig", "calt", "clig", "rclt", "liga"];

    private static void Clear(List<ShapeItem> buffer)
    {
        foreach (var item in buffer) item.Substituted = false;
    }

    /// <summary>
    /// Divides the run into clusters and numbers each glyph with the one it belongs to.
    /// </summary>
    private static UseCluster[] Divide(List<ShapeItem> buffer)
    {
        var categories = new UseCategory[buffer.Count];
        for (var i = 0; i < buffer.Count; i++) categories[i] = (UseCategory)buffer[i].Category;

        var clusters = UseSyllables.Find(categories);

        for (var i = 0; i < clusters.Count; i++)
        {
            for (var at = clusters[i].Start; at < clusters[i].End; at++) buffer[at].Syllable = i;
        }

        return [.. clusters.Select(cluster => cluster.Kind)];
    }

    /// <summary>Where each cluster now begins and ends, read back off the glyphs themselves.</summary>
    private static List<(int Start, int End, UseCluster Kind)> Ranges(
        List<ShapeItem> buffer, UseCluster[] kinds)
    {
        var ranges = new List<(int, int, UseCluster)>();
        var at = 0;

        while (at < buffer.Count)
        {
            var start = at;
            var cluster = buffer[at].Syllable;

            while (at < buffer.Count && buffer[at].Syllable == cluster) at++;

            ranges.Add((start, at, cluster < kinds.Length ? kinds[cluster] : UseCluster.NotACluster));
        }

        return ranges;
    }

    /// <summary>
    /// Says where the r feature may fire: on the first glyph of a cluster that is already an r,
    /// and otherwise on the first three, since it may take that many to make one.
    /// </summary>
    private static void Mark(List<ShapeItem> buffer, UseCluster[] kinds)
    {
        foreach (var (start, end, _) in Ranges(buffer, kinds))
        {
            var limit = (UseCategory)buffer[start].Category == UseCategory.Repha
                ? 1
                : Math.Min(3, end - start);

            for (var i = start; i < start + limit; i++) buffer[i].Mask |= Rphf;
        }
    }

    /// <summary>
    /// Reads back what a feature did and says so in the categories, since what follows turns on
    /// whether the font made the shape rather than on whether it was asked to.
    /// </summary>
    private static void Record(
        List<ShapeItem> buffer, UseCluster[] kinds, UseCategory becomes, uint mask)
    {
        foreach (var (start, end, _) in Ranges(buffer, kinds))
        {
            for (var i = start; i < end && (buffer[i].Mask & mask) != 0; i++)
            {
                if (!buffer[i].Substituted) continue;

                buffer[i].Category = (byte)becomes;
                break;
            }
        }
    }

    /// <summary>
    /// Draws a circle under a cluster that has nothing to hang from.
    /// </summary>
    /// <remarks>
    /// A vowel sign with no consonant, a joining mark with nothing on either side: the characters
    /// are there and mean nothing, and every shaper draws them on a dotted circle rather than
    /// hanging them off whatever came before. It goes at the head of the cluster, after an r that
    /// is to become a mark, and it takes the cluster of what follows it so that a reader copying
    /// the text back out gets what was typed rather than the circle.
    /// </remarks>
    private static void DottedCircles(TrueTypeFont font, List<ShapeItem> buffer, UseCluster[] kinds)
    {
        if (Array.IndexOf(kinds, UseCluster.Broken) < 0) return;

        var circle = font.GetGlyphIndex(0x25CC);
        if (circle == 0) return;

        for (var i = 0; i < buffer.Count; i++)
        {
            var cluster = buffer[i].Syllable;

            if (cluster >= kinds.Length || kinds[cluster] != UseCluster.Broken) continue;
            if (i > 0 && buffer[i - 1].Syllable == cluster) continue;

            // After the r, where there is one: it belongs to the cluster rather than to the
            // circle standing in for what the cluster lacks.
            var at = i;

            while (at < buffer.Count && buffer[at].Syllable == cluster &&
                   (UseCategory)buffer[at].Category == UseCategory.Repha)
            {
                at++;
            }

            if (at >= buffer.Count || buffer[at].Syllable != cluster) continue;

            buffer.Insert(at, new ShapeItem(circle, buffer[at].Cluster, buffer[at].Mask, 0x25CC)
            {
                Category = (byte)UseCategory.Base,
                Syllable = cluster,
                Advance = font.GetAdvanceWidth(circle)
            });

            i = at;
        }
    }

    /// <summary>
    /// Moves the two things that are drawn away from where they are stored.
    /// </summary>
    /// <remarks>
    /// The r goes forward, to just before whatever is drawn after the base — it is a mark on the
    /// end of the cluster, not the letter it was written as. The vowels written to the left go
    /// back, to just after the last joining mark, which is where the letters they are pronounced
    /// after begin. Nothing else moves.
    /// </remarks>
    private static void Reorder(List<ShapeItem> buffer, int start, int end, UseCluster kind)
    {
        if (kind is not (UseCluster.ViramaTerminated or UseCluster.SakotTerminated
            or UseCluster.Standard or UseCluster.Symbol or UseCluster.Broken))
        {
            return;
        }

        if ((UseCategory)buffer[start].Category == UseCategory.Repha && end - start > 1)
        {
            for (var i = start + 1; i < end; i++)
            {
                var category = (UseCategory)buffer[i].Category;

                var after = UseSyllables.IsAfterBase(category) ||
                            (UseSyllables.IsHalant(category) && !buffer[i].Ligated);

                if (!after && i != end - 1) continue;

                var to = after ? i - 1 : i;

                var repha = buffer[start];

                buffer.RemoveAt(start);
                buffer.Insert(to, repha);

                break;
            }
        }

        var target = start;

        for (var i = start; i < end; i++)
        {
            var category = (UseCategory)buffer[i].Category;

            if (UseSyllables.IsHalant(category) && !buffer[i].Ligated)
            {
                target = i + 1;
                continue;
            }

            if (category is not (UseCategory.VowelPre or UseCategory.VowelModifierPre)) continue;

            // Only the first piece of something the font wrote as several is moved: the rest are
            // drawn where that piece is.
            if (buffer[i].Component != 0 || target >= i) continue;

            var vowel = buffer[i];

            buffer.RemoveAt(i);
            buffer.Insert(target, vowel);
        }
    }

    /// <summary>
    /// Which of the four forms each letter takes, for the scripts whose letters change shape by
    /// where they stand in a word.
    /// </summary>
    /// <remarks>
    /// Two kinds of script want this and want it differently. Those with joining types of their
    /// own — Adlam, Mongolian, N'Ko and their like — are asked the same question Arabic is asked,
    /// letter by letter. The rest are asked it cluster by cluster: a cluster that follows another
    /// in the same word joins to it, and the one before it is corrected from standing alone to
    /// opening, or from closing to standing between.
    /// </remarks>
    private void Topographical(
        string text, List<ShapeItem> buffer, List<(int Start, int End, UseCluster Kind)> clusters)
    {
        if (JoinsLikeArabic)
        {
            var forms = ArabicJoining.Forms(text);

            foreach (var item in buffer)
            {
                if (item.Cluster < 0 || item.Cluster >= forms.Length) continue;

                item.Mask |= forms[item.Cluster] switch
                {
                    JoiningForm.Initial => Initial,
                    JoiningForm.Medial => Medial,
                    JoiningForm.Final => Final,
                    _ => Isolated
                };
            }

            return;
        }

        const uint Forms = Isolated | Initial | Medial | Final;

        var previous = (Start: 0, End: 0, Form: 0u);

        foreach (var (start, end, kind) in clusters)
        {
            if (kind is UseCluster.Hieroglyph or UseCluster.NotACluster)
            {
                previous = (start, end, 0u);
                continue;
            }

            var joins = previous.Form is Final or Isolated;

            if (joins)
            {
                // What stood before it was not standing alone after all.
                var corrected = previous.Form == Final ? Medial : Initial;

                for (var i = previous.Start; i < previous.End; i++)
                    buffer[i].Mask = (buffer[i].Mask & ~Forms) | corrected;
            }

            var form = joins ? Final : Isolated;

            for (var i = start; i < end; i++) buffer[i].Mask = (buffer[i].Mask & ~Forms) | form;

            previous = (start, end, form);
        }
    }
}
