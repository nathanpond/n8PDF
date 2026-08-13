using n8PDF.Fonts.OpenType;
using n8PDF.Text;

namespace n8PDF.Fonts;

/// <summary>
/// The plan for the Indic scripts, which are neither drawn in the order they are stored nor drawn
/// one letter to a letter.
/// </summary>
/// <remarks>
/// Three things happen in an Indic syllable that happen nowhere in Latin. A vowel may be written to
/// the left of the consonant it is pronounced after, so what is stored second is drawn first.
/// Consonants with no vowel between them are written as one stacked shape, so what is stored as
/// three characters is drawn as one glyph. And an r at the head of a cluster is written as a small
/// mark at its end, so what is stored first is drawn last.
///
/// None of that can be settled character by character. The syllable is the unit: it is divided out
/// of the run, the consonant the rest of it hangs from is found by asking the font what shapes it
/// has, every part is given a place in the visual order and sorted into it, the font's rules for
/// making conjuncts are applied one at a time, and then what those rules managed to make decides
/// where the vowel and the r finally go. That order — sort, ask the font, sort again — is the
/// shape of the whole thing, and is what the OpenType script development specifications lay down.
/// </remarks>
internal sealed class IndicPlan : ShapingPlan
{
    // Which of the features that fire in one part of a syllable and not another may fire here.
    private const uint Rphf = 1u << 1;
    private const uint Pref = 1u << 2;
    private const uint Blwf = 1u << 3;
    private const uint Abvf = 1u << 4;
    private const uint Half = 1u << 5;
    private const uint Pstf = 1u << 6;
    private const uint Init = 1u << 7;

    /// <summary>Where a repha ends up, which differs by script.</summary>
    private enum RephPosition
    {
        AfterMain = IndicPosition.AfterMain,
        BeforeSub = IndicPosition.BeforeSub,
        AfterSub = IndicPosition.AfterSub,
        BeforePost = IndicPosition.BeforePost,
        AfterPost = IndicPosition.AfterPost
    }

    /// <summary>How a repha is asked for.</summary>
    private enum RephMode
    {
        /// <summary>By an r followed by the joining mark, which is the usual way.</summary>
        Implicit,

        /// <summary>By that and a joiner after it, which Telugu wants.</summary>
        Explicit,

        /// <summary>By a character of its own, which Malayalam has and still reorders.</summary>
        Logical
    }

    private IndicPlan(
        string name, string[] tags, int virama, RephPosition rephPosition, RephMode rephMode,
        bool belowFormsPostOnly)
    {
        _name = name;
        _tags = tags;
        _virama = virama;
        _rephPosition = rephPosition;
        _rephMode = rephMode;
        _belowFormsPostOnly = belowFormsPostOnly;
    }

    private readonly string _name;

    /// <summary>
    /// What the font may call this script. Each of them has two names: the second was given when
    /// the specification was rewritten, and a font that answers to it is written to the newer
    /// rules. Both are asked for, newer first.
    /// </summary>
    private readonly string[] _tags;

    protected override string[] ScriptTags => _tags;

    /// <summary>
    /// These scripts place their marks by their own rules, and a mark that carries a width carries
    /// it.
    /// </summary>
    protected override MarkWidths Marks => MarkWidths.Never;

    public override bool DecomposesMarks => true;

    private readonly int _virama;
    private readonly RephPosition _rephPosition;
    private readonly RephMode _rephMode;
    private readonly bool _belowFormsPostOnly;

    /// <summary>
    /// Whether the questions put to the font about a pair of glyphs are about the pair wherever it
    /// stands rather than about the pair standing alone. The older rules answer them the first way
    /// and Malayalam does too.
    /// </summary>
    private bool InCompany(bool oldSpec) => oldSpec || _name == "Malayalam";

    private static readonly IndicPlan Devanagari =
        new("Devanagari", ["dev2", "deva"], 0x094D, RephPosition.BeforePost, RephMode.Implicit, false);

    private static readonly IndicPlan Bengali =
        new("Bengali", ["bng2", "beng"], 0x09CD, RephPosition.AfterSub, RephMode.Implicit, false);

    private static readonly IndicPlan Gurmukhi =
        new("Gurmukhi", ["gur2", "guru"], 0x0A4D, RephPosition.BeforeSub, RephMode.Implicit, false);

    private static readonly IndicPlan Gujarati =
        new("Gujarati", ["gjr2", "gujr"], 0x0ACD, RephPosition.BeforePost, RephMode.Implicit, false);

    private static readonly IndicPlan Oriya =
        new("Oriya", ["ory2", "orya"], 0x0B4D, RephPosition.AfterMain, RephMode.Implicit, false);

    private static readonly IndicPlan Tamil =
        new("Tamil", ["tml2", "taml"], 0x0BCD, RephPosition.AfterPost, RephMode.Implicit, false);

    private static readonly IndicPlan Telugu =
        new("Telugu", ["tel2", "telu"], 0x0C4D, RephPosition.AfterPost, RephMode.Explicit, true);

    private static readonly IndicPlan Kannada =
        new("Kannada", ["knd2", "knda"], 0x0CCD, RephPosition.AfterPost, RephMode.Implicit, true);

    private static readonly IndicPlan Malayalam =
        new("Malayalam", ["mlm2", "mlym"], 0x0D4D, RephPosition.AfterMain, RephMode.Logical, false);

    /// <summary>The plan a character calls for, or null where it is not one of these scripts.</summary>
    public static ShapingPlan? For(int character) =>
        character switch
        {
            >= 'ऀ' and <= 'ॿ' => Devanagari,
            >= 'ঀ' and <= '৿' => Bengali,
            >= '਀' and <= '੿' => Gurmukhi,
            >= '઀' and <= '૿' => Gujarati,
            >= '଀' and <= '୿' => Oriya,
            >= '஀' and <= '௿' => Tamil,
            >= 'ఀ' and <= '౿' => Telugu,
            >= 'ಀ' and <= '೿' => Kannada,
            >= 'ഀ' and <= 'ൿ' => Malayalam,
            _ => null
        };

    /// <summary>Malayalam and Tamil have no half forms, which changes where a vowel may be put.</summary>
    private bool HasHalfForms => _name is not ("Malayalam" or "Tamil");

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        // A font that answers to the older name of this script was written to the older rules,
        // under which a joining mark after the base is moved to the end of the syllable, the
        // below-base feature is not applied before the base, and what the font is asked about a
        // pair of letters is asked of the pair in company.
        var oldSpec = !substitutor.SelectScript(_tags[0]) && substitutor.SelectScript(_tags[1]);

        // Every rule here is a rule about one syllable, and none of them may reach into the next.
        substitutor.WithinSyllables = true;

        Shape(font, substitutor, buffer, oldSpec);
    }

    private void Shape(
        TrueTypeFont font, Substitutor substitutor, List<ShapeItem> buffer, bool oldSpec)
    {
        foreach (var item in buffer)
        {
            item.Category = (byte)IndicSyllables.CategoryOf(item.CodePoint);
            item.Position = (byte)IndicSyllables.PositionOf(item.CodePoint);
        }

        var kinds = Divide(buffer);

        substitutor.Apply(buffer, "locl");
        substitutor.Apply(buffer, "ccmp");

        // What the font can do decides where a consonant goes: one that has a form drawn below the
        // base is not itself a candidate to be the base.
        var virama = font.GetGlyphIndex(_virama);

        if (virama != 0)
        {
            foreach (var item in buffer)
            {
                if ((IndicPosition)item.Position == IndicPosition.BaseConsonant)
                    item.Position = (byte)ConsonantPosition(substitutor, item.Glyph, virama, oldSpec);
            }
        }

        foreach (var syllable in Ranges(buffer, kinds))
            Arrange(substitutor, buffer, syllable, oldSpec);

        // The rules that make the conjuncts, one at a time and each over the whole run: a font may
        // put its half forms and its below forms in one lookup and expect them applied in order.
        substitutor.Apply(buffer, "nukt");
        substitutor.Apply(buffer, "akhn");
        substitutor.Apply(buffer, "rphf", Rphf);
        substitutor.Apply(buffer, "rkrf");
        substitutor.Apply(buffer, "pref", Pref);
        substitutor.Apply(buffer, "blwf", Blwf);
        substitutor.Apply(buffer, "abvf", Abvf);
        substitutor.Apply(buffer, "half", Half);
        substitutor.Apply(buffer, "pstf", Pstf);
        substitutor.Apply(buffer, "vatu");
        substitutor.Apply(buffer, "cjct");

        foreach (var syllable in Ranges(buffer, kinds)) Finish(font, buffer, syllable);

        substitutor.Apply(buffer, "init", Init);
        substitutor.Apply(buffer, "pres");
        substitutor.Apply(buffer, "abvs");
        substitutor.Apply(buffer, "blws");
        substitutor.Apply(buffer, "psts");
        substitutor.Apply(buffer, "haln");
        substitutor.Apply(buffer, "calt");
        substitutor.Apply(buffer, "clig");
    }

    /// <summary>
    /// Divides the run into syllables and numbers each glyph with the one it belongs to.
    /// </summary>
    /// <remarks>
    /// Once, before anything is substituted, and never again. What a syllable is is a fact about
    /// the characters, and by the time the font's rules have run there may be nothing left to read
    /// it from: the mark that joined two consonants is inside the shape they became, and a run
    /// divided a second time would put the r of a cluster in a syllable of its own and leave it
    /// there. Each glyph carries its number instead, and a shape made of several keeps the number
    /// of the first.
    /// </remarks>
    private static SyllableKind[] Divide(List<ShapeItem> buffer)
    {
        var categories = new IndicCategory[buffer.Count];
        for (var i = 0; i < buffer.Count; i++) categories[i] = (IndicCategory)buffer[i].Category;

        var syllables = IndicSyllables.Find(categories);

        for (var i = 0; i < syllables.Count; i++)
        {
            for (var at = syllables[i].Start; at < syllables[i].End; at++) buffer[at].Syllable = i;
        }

        return [.. syllables.Select(syllable => syllable.Kind)];
    }

    /// <summary>Where each syllable now begins and ends, read back off the glyphs themselves.</summary>
    private static List<(int Start, int End, SyllableKind Kind)> Ranges(
        List<ShapeItem> buffer, SyllableKind[] kinds)
    {
        var ranges = new List<(int, int, SyllableKind)>();

        var at = 0;

        while (at < buffer.Count)
        {
            var start = at;
            var syllable = buffer[at].Syllable;

            while (at < buffer.Count && buffer[at].Syllable == syllable) at++;

            ranges.Add((start, at, syllable < kinds.Length ? kinds[syllable] : SyllableKind.NotIndic));
        }

        return ranges;
    }

    /// <summary>
    /// Where a consonant is drawn, asked of the font: a consonant the font draws below the base,
    /// or after it, is not itself a candidate for being the base.
    /// </summary>
    /// <remarks>
    /// The two glyphs are offered in both orders. The specification changed which way round a
    /// conjunct is written — the joining mark used to follow the consonant and now precedes it —
    /// and fonts exist that were converted from one to the other without their lookups being
    /// rewritten. Matching either is what every shaper does, and what those fonts were drawn
    /// against.
    /// </remarks>
    private IndicPosition ConsonantPosition(
        Substitutor substitutor, ushort consonant, ushort virama, bool oldSpec)
    {
        var company = InCompany(oldSpec);

        if (substitutor.WouldSubstitute("blwf", company, virama, consonant) ||
            substitutor.WouldSubstitute("blwf", company, consonant, virama) ||
            substitutor.WouldSubstitute("vatu", company, virama, consonant) ||
            substitutor.WouldSubstitute("vatu", company, consonant, virama))
        {
            return IndicPosition.BelowConsonant;
        }

        if (substitutor.WouldSubstitute("pstf", company, virama, consonant) ||
            substitutor.WouldSubstitute("pstf", company, consonant, virama) ||
            substitutor.WouldSubstitute("pref", company, virama, consonant) ||
            substitutor.WouldSubstitute("pref", company, consonant, virama))
        {
            return IndicPosition.PostConsonant;
        }

        return IndicPosition.BaseConsonant;
    }

    // ----- putting a syllable in order -----

    /// <summary>
    /// Finds the consonant a syllable hangs from, gives every part of it a place, and sorts them
    /// into that order.
    /// </summary>
    private void Arrange(
        Substitutor substitutor, List<ShapeItem> buffer,
        (int Start, int End, SyllableKind Kind) syllable, bool oldSpec)
    {
        var (start, end, kind) = syllable;

        if (kind is SyllableKind.Symbol or SyllableKind.NotIndic) return;

        var hasReph = false;
        var limit = start;
        var  @base = end;

        // An r at the head of a cluster becomes a mark at its end, if the font has one and if
        // there is another consonant for the cluster to hang from.
        if (substitutor.HasLookups("rphf") && start + 3 <= end &&
            ((_rephMode == RephMode.Implicit &&
              !IndicSyllables.IsJoiner((IndicCategory)buffer[start + 2].Category)) ||
             (_rephMode == RephMode.Explicit &&
              (IndicCategory)buffer[start + 2].Category == IndicCategory.Joiner)))
        {
            // A question about the two standing alone — an r becomes a repha at the head of a
            // cluster and nowhere else — except in the fonts written to the older rules, which
            // say it of the pair in company.
            var would = _rephMode == RephMode.Explicit
                ? substitutor.WouldSubstitute("rphf", InCompany(oldSpec), buffer[start].Glyph,
                      buffer[start + 1].Glyph, buffer[start + 2].Glyph) ||
                  substitutor.WouldSubstitute("rphf", InCompany(oldSpec), buffer[start].Glyph,
                      buffer[start + 1].Glyph)
                : substitutor.WouldSubstitute("rphf", InCompany(oldSpec), buffer[start].Glyph,
                      buffer[start + 1].Glyph);

            if (would)
            {
                limit += 2;

                while (limit < end && IndicSyllables.IsJoiner((IndicCategory)buffer[limit].Category))
                    limit++;

                @base = start;
                hasReph = true;
            }
        }
        else if (_rephMode == RephMode.Logical &&
                 (IndicCategory)buffer[start].Category == IndicCategory.Repha)
        {
            limit += 1;

            while (limit < end && IndicSyllables.IsJoiner((IndicCategory)buffer[limit].Category))
                limit++;

            @base = start;
            hasReph = true;
        }

        // Starting from the end and moving back: the base is the last consonant that is not drawn
        // below or after the base, or else the first consonant there is.
        {
            var i = end;
            var seenBelow = false;

            do
            {
                i--;

                var category = (IndicCategory)buffer[i].Category;

                if (IndicSyllables.IsConsonant(category))
                {
                    var position = (IndicPosition)buffer[i].Position;

                    if (position != IndicPosition.BelowConsonant &&
                        (position != IndicPosition.PostConsonant || seenBelow))
                    {
                        @base = i;
                        break;
                    }

                    if (position == IndicPosition.BelowConsonant) seenBelow = true;

                    @base = i;
                }
                else if (start < i && category == IndicCategory.Joiner &&
                         (IndicCategory)buffer[i - 1].Category == IndicCategory.Halant)
                {
                    // A joiner after the joining mark asks for a half form and stops the search.
                    break;
                }
            } while (i > limit);
        }

        // An r with nothing after it to hang the cluster on is not a repha but the base itself.
        if (hasReph && @base == start && limit - @base <= 2) hasReph = false;

        for (var i = start; i < @base; i++)
        {
            buffer[i].Position = Math.Min((byte)IndicPosition.PreConsonant, buffer[i].Position);
        }

        if (@base < end) buffer[@base].Position = (byte)IndicPosition.BaseConsonant;

        if (hasReph) buffer[start].Position = (byte)IndicPosition.RaToBecomeRepha;

        // Under the older rules a joining mark after the base is moved to after the last consonant
        // of the syllable, which is where those fonts expect to find it.
        if (oldSpec) MoveTrailingHalant(buffer, @base, end);

        // The marks that have no place of their own go where whatever they were written on goes,
        // so that they travel with it.
        {
            var last = IndicPosition.Start;

            for (var i = start; i < end; i++)
            {
                var category = (IndicCategory)buffer[i].Category;

                if (IndicSyllables.IsJoiner(category) || category is IndicCategory.Nukta
                        or IndicCategory.RegisterShifter or IndicCategory.ConsonantMedial
                        or IndicCategory.Halant)
                {
                    buffer[i].Position = (byte)last;

                    // A joining mark is not carried to the left with a left-side vowel: it belongs
                    // to the consonant, which stays where it is.
                    if (category == IndicCategory.Halant &&
                        (IndicPosition)buffer[i].Position == IndicPosition.PreMatra)
                    {
                        for (var j = i; j > start; j--)
                        {
                            if ((IndicPosition)buffer[j - 1].Position == IndicPosition.PreMatra) continue;

                            buffer[i].Position = buffer[j - 1].Position;
                            break;
                        }
                    }
                }
                else if ((IndicPosition)buffer[i].Position != IndicPosition.SyllableModifierOrVedic)
                {
                    if (category == IndicCategory.MatraPost && i > start &&
                        (IndicCategory)buffer[i - 1].Category == IndicCategory.SyllableModifier)
                    {
                        buffer[i - 1].Position = buffer[i].Position;
                    }

                    last = (IndicPosition)buffer[i].Position;
                }
            }
        }

        // A consonant after the base owns everything between it and whatever came before it, so
        // that a mark written on it goes where it goes.
        {
            var last = @base;

            for (var i = @base + 1; i < end; i++)
            {
                var category = (IndicCategory)buffer[i].Category;

                if (IndicSyllables.IsConsonant(category))
                {
                    for (var j = last + 1; j < i; j++)
                    {
                        if (buffer[j].Position < (byte)IndicPosition.SyllableModifierOrVedic)
                            buffer[j].Position = buffer[i].Position;
                    }

                    last = i;
                }
                else if (IndicSyllables.IsMatra(category))
                {
                    last = i;
                }
            }
        }

        Sort(buffer, start, end);

        // Where several vowels were written to the left, they keep the order they were stored in
        // rather than being reversed with everything else.
        var firstLeft = end;
        var lastLeft = end;

        @base = end;

        for (var i = start; i < end; i++)
        {
            var position = (IndicPosition)buffer[i].Position;

            if (position == IndicPosition.BaseConsonant)
            {
                @base = i;
                break;
            }

            if (position != IndicPosition.PreMatra) continue;

            if (firstLeft == end) firstLeft = i;
            lastLeft = i;
        }

        if (firstLeft < lastLeft)
        {
            buffer.Reverse(firstLeft, lastLeft - firstLeft + 1);

            var from = firstLeft;

            for (var j = from; j <= lastLeft; j++)
            {
                if (!IndicSyllables.IsMatra((IndicCategory)buffer[j].Category)) continue;

                buffer.Reverse(from, j - from + 1);
                from = j + 1;
            }
        }

        // Which features may fire where.
        for (var i = start; i < end && (IndicPosition)buffer[i].Position == IndicPosition.RaToBecomeRepha; i++)
            buffer[i].Mask |= Rphf;

        var beforeBase = Half;

        // A below-base form before the base is asked for only under the newer rules; the older
        // ones ask for it by naming the pair, which is the eyelash r below.
        if (!oldSpec && !_belowFormsPostOnly) beforeBase |= Blwf;

        for (var i = start; i < @base; i++) buffer[i].Mask |= beforeBase;

        for (var i = @base + 1; i < end; i++) buffer[i].Mask |= Blwf | Abvf | Pstf;

        // The older rules say the below-base feature applies to an r under a half form as well as
        // to one under the base, which is the eyelash r. An r joined by a mark and then a joiner
        // is asking for that shape and is left alone.
        if (oldSpec && _name == "Devanagari")
        {
            for (var i = start; i + 1 < @base; i++)
            {
                if ((IndicCategory)buffer[i].Category != IndicCategory.Ra) continue;
                if ((IndicCategory)buffer[i + 1].Category != IndicCategory.Halant) continue;

                if (i + 2 != @base &&
                    (IndicCategory)buffer[i + 2].Category == IndicCategory.Joiner)
                {
                    continue;
                }

                buffer[i].Mask |= Blwf;
                buffer[i + 1].Mask |= Blwf;
            }
        }

        // A joining mark and an r after the base may become a consonant drawn before it.
        if (substitutor.HasLookups("pref") && @base + 2 < end)
        {
            for (var i = @base + 1; i + 1 < end; i++)
            {
                if (!substitutor.WouldSubstitute("pref", InCompany(oldSpec), buffer[i].Glyph,
                        buffer[i + 1].Glyph))
                {
                    continue;
                }

                buffer[i].Mask |= Pref;
                buffer[i + 1].Mask |= Pref;

                break;
            }
        }

        // A non-joiner asks for the letter before it to keep its full form.
        for (var i = start + 1; i < end; i++)
        {
            if (!IndicSyllables.IsJoiner((IndicCategory)buffer[i].Category)) continue;

            var nonJoiner = (IndicCategory)buffer[i].Category == IndicCategory.NonJoiner;
            var j = i;

            do
            {
                j--;
                if (nonJoiner) buffer[j].Mask &= ~Half;
            } while (j > start && !IndicSyllables.IsConsonant((IndicCategory)buffer[j].Category));
        }
    }

    /// <summary>
    /// Moves the first joining mark after the base to after the last consonant of the syllable,
    /// which is what the older rules ask for.
    /// </summary>
    /// <remarks>
    /// Kannada is left alone: reports of what Windows does with these fonts agree that it moves
    /// the mark in the other scripts and does not move it there.
    /// </remarks>
    private void MoveTrailingHalant(List<ShapeItem> buffer, int @base, int end)
    {
        var double_ = _name == "Kannada";

        for (var i = @base + 1; i < end; i++)
        {
            if ((IndicCategory)buffer[i].Category != IndicCategory.Halant) continue;

            var last = end - 1;

            while (last > i)
            {
                var category = (IndicCategory)buffer[last].Category;

                if (IndicSyllables.IsConsonant(category) ||
                    (double_ && category == IndicCategory.Halant))
                {
                    break;
                }

                last--;
            }

            if ((IndicCategory)buffer[last].Category != IndicCategory.Halant && last > i)
            {
                var held = buffer[i];

                buffer.RemoveAt(i);
                buffer.Insert(last, held);
            }

            return;
        }
    }

    /// <summary>
    /// Sorts a syllable into the order it is drawn, keeping what shares a place in the order it
    /// was stored.
    /// </summary>
    private static void Sort(List<ShapeItem> buffer, int start, int end)
    {
        // A short insertion sort rather than the framework's: a syllable is a handful of glyphs,
        // and this one is stable, which the framework's list sort is not.
        for (var i = start + 1; i < end; i++)
        {
            var held = buffer[i];
            var j = i - 1;

            while (j >= start && buffer[j].Position > held.Position)
            {
                buffer[j + 1] = buffer[j];
                j--;
            }

            buffer[j + 1] = held;
        }
    }

    // ----- putting it in order again, once the font has had its say -----

    /// <summary>
    /// Moves the left-side vowel, the repha and the pre-base consonant to where what the font
    /// managed to make says they belong.
    /// </summary>
    private void Finish(
        TrueTypeFont font, List<ShapeItem> buffer, (int Start, int End, SyllableKind Kind) syllable)
    {
        var (start, end, kind) = syllable;

        if (kind is SyllableKind.Symbol or SyllableKind.NotIndic) return;

        // A joining mark that became part of a conjunct may have lost its category along the way,
        // and the rules below are written in terms of it.
        var virama = font.GetGlyphIndex(_virama);

        if (virama != 0)
        {
            for (var i = start; i < end; i++)
            {
                if (buffer[i].Glyph == virama && buffer[i].Ligated && buffer[i].Multiplied)
                {
                    buffer[i].Category = (byte)IndicCategory.Halant;
                    buffer[i].Ligated = false;
                    buffer[i].Multiplied = false;
                }
            }
        }

        var tryPref = buffer.Skip(start).Take(end - start).Any(item => (item.Mask & Pref) != 0);

        // Find the base again: everything has moved.
        int @base;

        for (@base = start; @base < end; @base++)
        {
            if (buffer[@base].Position < (byte)IndicPosition.BaseConsonant) continue;

            if (tryPref && @base + 1 < end)
            {
                for (var i = @base + 1; i < end; i++)
                {
                    if ((buffer[i].Mask & Pref) == 0) continue;

                    if (!buffer[i].Substituted || !buffer[i].LigatedAndDidNotMultiply)
                    {
                        // It was a candidate and came to nothing, so the base is around here.
                        @base = i;

                        while (@base < end && IsHalant(buffer[@base])) @base++;

                        if (@base < end) buffer[@base].Position = (byte)IndicPosition.BaseConsonant;

                        tryPref = false;
                    }

                    break;
                }

                if (@base == end) break;
            }

            if (start < @base && buffer[@base].Position > (byte)IndicPosition.BaseConsonant) @base--;

            break;
        }

        if (@base == end && start < @base &&
            (IndicCategory)buffer[@base - 1].Category == IndicCategory.Joiner)
        {
            @base--;
        }

        if (@base < end)
        {
            while (start < @base &&
                   (IndicCategory)buffer[@base].Category is IndicCategory.Nukta or IndicCategory.Halant)
            {
                @base--;
            }
        }

        @base = MoveLeftVowel(buffer, start, end, @base);
        @base = MoveReph(buffer, start, end, @base);

        if (tryPref) MovePreBase(buffer, start, end, @base);

        // A left-side vowel opening a word may have a form of its own.
        if ((IndicPosition)buffer[start].Position == IndicPosition.PreMatra) buffer[start].Mask |= Init;
    }

    private static bool IsHalant(ShapeItem item) =>
        (IndicCategory)item.Category == IndicCategory.Halant && !item.Ligated;

    /// <summary>
    /// Brings a left-side vowel closer to the consonant it belongs to: after the last joining mark
    /// that stands on its own, and before the base.
    /// </summary>
    private int MoveLeftVowel(List<ShapeItem> buffer, int start, int end, int @base)
    {
        if (start + 1 >= end || start >= @base) return @base;

        var target = @base == end ? @base - 2 : @base - 1;

        // Malayalam and Tamil have no half forms: what their half-form feature makes is a letter
        // in its own right, and the vowel goes after it.
        if (HasHalfForms)
        {
            while (true)
            {
                while (target > start &&
                       !IndicSyllables.IsMatra((IndicCategory)buffer[target].Category) &&
                       (IndicCategory)buffer[target].Category != IndicCategory.Halant)
                {
                    target--;
                }

                if (IsHalant(buffer[target]) &&
                    (IndicPosition)buffer[target].Position != IndicPosition.PreMatra)
                {
                    // A joiner after this mark means the vowel stays where it is.
                    if (target + 1 < end &&
                        (IndicCategory)buffer[target + 1].Category == IndicCategory.Joiner &&
                        target > start)
                    {
                        target--;
                        continue;
                    }
                }
                else
                {
                    target = start;
                }

                break;
            }
        }

        if (start >= target || (IndicPosition)buffer[target].Position == IndicPosition.PreMatra)
            return @base;

        for (var i = target; i > start; i--)
        {
            if ((IndicPosition)buffer[i - 1].Position != IndicPosition.PreMatra) continue;

            var from = i - 1;
            if (from < @base && @base <= target) @base--;

            var held = buffer[from];
            buffer.RemoveAt(from);
            buffer.Insert(target, held);

            target--;
        }

        return @base;
    }

    /// <summary>
    /// Moves the r that became a repha to where its script draws it — which is after the first
    /// joining mark, or after the base, or at the end of the syllable.
    /// </summary>
    private int MoveReph(List<ShapeItem> buffer, int start, int end, int @base)
    {
        if (start + 1 >= end) return @base;
        if ((IndicPosition)buffer[start].Position != IndicPosition.RaToBecomeRepha) return @base;

        // Where the repha is a character of its own it is moved only if it did not become part of
        // something else; where it was made of two letters it is moved only if it did.
        var encoded = (IndicCategory)buffer[start].Category == IndicCategory.Repha;

        if (encoded == buffer[start].LigatedAndDidNotMultiply) return @base;

        var moved = -1;

        // After the first joining mark between the repha and the base.
        if (_rephPosition != RephPosition.AfterPost)
        {
            moved = start + 1;
            while (moved < @base && !IsHalant(buffer[moved])) moved++;

            if (moved < @base && IsHalant(buffer[moved]))
            {
                if (moved + 1 < @base &&
                    IndicSyllables.IsJoiner((IndicCategory)buffer[moved + 1].Category))
                {
                    moved++;
                }
            }
            else
            {
                moved = -1;
            }
        }

        // After the base and everything that belongs with it.
        if (moved < 0 && _rephPosition == RephPosition.AfterMain)
        {
            moved = @base;

            while (moved + 1 < end && buffer[moved + 1].Position <= (byte)IndicPosition.AfterMain)
                moved++;

            if (moved >= end) moved = -1;
        }

        // Before the parts drawn after the base.
        if (moved < 0 && _rephPosition == RephPosition.AfterSub)
        {
            moved = @base;

            while (moved + 1 < end &&
                   (IndicPosition)buffer[moved + 1].Position is not (IndicPosition.PostConsonant
                       or IndicPosition.AfterPost or IndicPosition.SyllableModifierOrVedic))
            {
                moved++;
            }

            if (moved >= end) moved = -1;
        }

        // For the scripts that draw it after everything: after the first joining mark again, and
        // otherwise at the end of the syllable.
        if (moved < 0)
        {
            moved = start + 1;
            while (moved < @base && !IsHalant(buffer[moved])) moved++;

            if (moved < @base && IsHalant(buffer[moved]))
            {
                if (moved + 1 < @base &&
                    IndicSyllables.IsJoiner((IndicCategory)buffer[moved + 1].Category))
                {
                    moved++;
                }
            }
            else
            {
                moved = end - 1;

                while (moved > start &&
                       (IndicPosition)buffer[moved].Position == IndicPosition.SyllableModifierOrVedic)
                {
                    moved--;
                }

                // Where it would land after a vowel and its joining mark, put it before the mark
                // so that the two can be drawn together.
                if (IsHalant(buffer[moved]))
                {
                    for (var i = @base + 1; i < moved; i++)
                    {
                        if (IndicSyllables.IsMatra((IndicCategory)buffer[i].Category)) moved--;
                    }
                }
            }
        }

        var reph = buffer[start];
        buffer.RemoveAt(start);
        buffer.Insert(moved, reph);

        if (start < @base && @base <= moved) @base--;

        return @base;
    }

    /// <summary>
    /// Moves a consonant the font drew as a pre-base form to before the base, which is where such
    /// a form is meant to be drawn.
    /// </summary>
    private void MovePreBase(List<ShapeItem> buffer, int start, int end, int @base)
    {
        if (@base + 1 >= end) return;

        for (var i = @base + 1; i < end; i++)
        {
            if ((buffer[i].Mask & Pref) == 0) continue;

            // Only what the font actually made into one shape is moved.
            if (!buffer[i].LigatedAndDidNotMultiply) return;

            var target = @base;

            if (HasHalfForms)
            {
                while (target > start &&
                       !IndicSyllables.IsMatra((IndicCategory)buffer[target - 1].Category) &&
                       (IndicCategory)buffer[target - 1].Category != IndicCategory.Halant)
                {
                    target--;
                }
            }

            if (target > start && IsHalant(buffer[target - 1]) && target < end &&
                IndicSyllables.IsJoiner((IndicCategory)buffer[target].Category))
            {
                target++;
            }

            var held = buffer[i];
            buffer.RemoveAt(i);
            buffer.Insert(target, held);

            return;
        }
    }
}
