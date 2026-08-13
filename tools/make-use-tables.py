#!/usr/bin/env python3
"""Writes the tables the universal shaping engine reads.

Most of the writing systems descended from Brahmi are not each given rules of their own. They are
shaped by one set, which works from what each character *is* — a base, a vowel written above, a
consonant written below, a mark on a mark — rather than from which script it belongs to. That is
what makes it universal: a script nobody has written a shaper for still comes out right, provided
its characters are classified and its font says what to do with them.

The classification is not a property in the database. It is derived from five that are — what part
of a syllable the character is, which side of its consonant it is drawn, whether it joins like
Arabic, whether it is ignorable, and its general category — by the rules Microsoft's specification
lays down, together with the overrides Microsoft publishes for the characters the database has not
caught up with. This turns all of that into source, since the library carries no dependencies and
the answer must not depend on a file being present at run time.

    for f in IndicSyllabicCategory IndicPositionalCategory ArabicShaping \\
             DerivedCoreProperties Blocks Scripts PropertyValueAliases; do
        curl -O https://www.unicode.org/Public/15.0.0/ucd/$f.txt
    done
    for f in IndicSyllabicCategory-Additional IndicPositionalCategory-Additional; do
        curl -O https://raw.githubusercontent.com/harfbuzz/harfbuzz/main/src/ms-use/$f.txt
    done
    tools/make-use-tables.py . > src/n8PDF/Text/UseTables.cs
"""

import os
import sys
import unicodedata

# The scripts shaped by rules of their own, which this engine must not touch even where their
# characters would classify.
ELSEWHERE = {"Arabic", "Lao", "Samaritan", "Syriac", "Thai"}

# The scripts the engine is used for. Everything descended from Brahmi that has no shaper of its
# own, plus the handful of others whose fonts are written to the same model.
UNIVERSAL = """
Tibetan Mongolian Sinhala Buhid Hanunoo Tagalog Tagbanwa Limbu Tai_Le Buginese Kharoshthi
Syloti_Nagri Tifinagh Balinese Nko Phags_Pa Cham Kayah_Li Lepcha Rejang Saurashtra Sundanese
Egyptian_Hieroglyphs Javanese Kaithi Meetei_Mayek Tai_Tham Tai_Viet Batak Brahmi Mandaic Chakma
Miao Sharada Takri Duployan Grantha Khojki Khudawadi Mahajani Manichaean Modi Pahawh_Hmong
Psalter_Pahlavi Siddham Tirhuta Ahom Multani Adlam Bhaiksuki Marchen Newa Masaram_Gondi Soyombo
Zanabazar_Square Dogra Gunjala_Gondi Hanifi_Rohingya Makasar Medefaidrin Old_Sogdian Sogdian
Elymaic Nandinagari Nyiakeng_Puachue_Hmong Wancho Chorasmian Dives_Akuru Khitan_Small_Script
Yezidi Cypro_Minoan Old_Uyghur Tangsa Toto Vithkuqi Kawi Nag_Mundari
""".split()


def read(path, field=1):
    """Every character the file names, with the property it names for it."""
    values = {}

    for line in open(path, encoding="utf-8"):
        line = line.split("#")[0].strip()
        if not line:
            continue

        fields = [f.strip() for f in line.split(";")]
        if len(fields) <= field:
            continue

        codes = fields[0].split("..")

        for code in range(int(codes[0], 16), int(codes[-1], 16) + 1):
            values[code] = fields[field]

    return values


def joining(path):
    """The joining type of every character the shaping file names."""
    return read(path, field=2)


def ignorable(path):
    """The characters the database calls ignorable by default."""
    found = set()

    for code, name in read(path).items():
        if name == "Default_Ignorable_Code_Point":
            found.add(code)

    return found


def scripts(path):
    """Which script each character belongs to."""
    return read(path)


# ----- the classification itself, from the specification -----

def is_base(u, syllabic, general, joins):
    return (syllabic in ("Number", "Consonant", "Consonant_Head_Letter", "Tone_Letter",
                         "Vowel_Independent")
            or (joins in ("C", "D", "L", "R") and syllabic != "Joiner")
            or (general == "Lo" and syllabic in ("Avagraha", "Bindu", "Consonant_Final",
                                                 "Consonant_Medial", "Consonant_Subjoined",
                                                 "Vowel", "Vowel_Dependent")))


def is_word_joiner(u, syllabic, general, ignored):
    return (ignored and u not in (0x115F, 0x1160, 0x3164, 0xFFA0,
                                  0x1BCA0, 0x1BCA1, 0x1BCA2, 0x1BCA3)
            and syllabic == "Other") or general == "Cn"


def classify(u, syllabic, general, joins, ignored):
    """What the engine takes a character to be, before its position is folded in."""
    # A joiner, a variation selector, or an ignorable mark: something that stands between letters
    # without being one.
    if syllabic == "Joiner" or (ignored and general in ("Mc", "Me", "Mn")):
        return "GraphemeJoiner"

    if u == 0x1A60:
        return "Sakot"
    if u == 0x0DCA:
        return "HalantOrVowelModifier"

    if syllabic == "Virama":
        return "Halant"
    if syllabic == "Invisible_Stacker":
        return "InvisibleStacker"
    if syllabic == "Number_Joiner":
        return "HalantNumber"
    if syllabic == "Non_Joiner":
        return "NonJoiner"
    if syllabic == "Reordering_Killer":
        return "ReorderingKiller"
    if syllabic in ("Consonant_Preceding_Repha", "Consonant_Prefixed"):
        return "Repha"
    if syllabic == "Consonant_With_Stacker":
        return "ConsonantWithStacker"
    if syllabic == "Brahmi_Joining_Number":
        return "BaseNumber"

    if syllabic == "Consonant_Placeholder" or u in (0x2015, 0x2022, 0x25FB, 0x25FC, 0x25FD, 0x25FE):
        return "BaseOther"

    if syllabic == "Hieroglyph":
        return "Hieroglyph"
    if syllabic == "Hieroglyph_Joiner":
        return "HieroglyphJoiner"
    if syllabic == "Hieroglyph_Mirror":
        return "HieroglyphMirror"
    if syllabic == "Hieroglyph_Modifier":
        return "HieroglyphModifier"
    if syllabic in ("Hieroglyph_Mark_Begin", "Hieroglyph_Segment_Begin"):
        return "HieroglyphBegin"
    if syllabic in ("Hieroglyph_Mark_End", "Hieroglyph_Segment_End"):
        return "HieroglyphEnd"

    if syllabic == "Symbol_Modifier":
        return "SymbolModifier"

    if is_base(u, syllabic, general, joins):
        return "Base"

    if (syllabic == "Consonant_Final" and general != "Lo") or \
            syllabic == "Consonant_Succeeding_Repha":
        return "ConsonantFinal"
    if syllabic == "Syllable_Modifier":
        return "ConsonantFinalModifier"
    if (syllabic == "Consonant_Medial" and general != "Lo") or \
            syllabic == "Consonant_Initial_Postfixed":
        return "ConsonantMedial"
    if syllabic in ("Nukta", "Gemination_Mark", "Consonant_Killer"):
        return "ConsonantModifier"
    if syllabic == "Consonant_Subjoined" and general != "Lo":
        return "ConsonantSubjoined"

    if syllabic == "Pure_Killer" or (general != "Lo" and syllabic in ("Vowel", "Vowel_Dependent")):
        return "Vowel"
    if syllabic in ("Tone_Mark", "Cantillation_Mark", "Register_Shifter", "Visarga") or \
            (general != "Lo" and syllabic == "Bindu"):
        return "VowelModifier"

    if is_word_joiner(u, syllabic, general, ignored):
        return "WordJoiner"

    if general == "Po" or syllabic in ("Consonant_Dead", "Joiner", "Modifying_Letter", "Other"):
        return "Other"

    return "Other"


# Which of a category's parts a character belongs to, by the side it is drawn on. A category that
# is not here is drawn wherever its base is and has no parts.
SIDES = {
    "ConsonantFinal": {
        "Above": ["Top"], "Below": ["Bottom"], "Post": ["Right"],
    },
    "ConsonantMedial": {
        "Above": ["Top"], "Below": ["Bottom", "Bottom_And_Left", "Bottom_And_Right"],
        "Post": ["Right"], "Pre": ["Left", "Top_And_Bottom_And_Left"],
    },
    "ConsonantModifier": {
        "Above": ["Top"], "Below": ["Bottom", "Overstruck"],
    },
    "Vowel": {
        "Above": ["Top", "Top_And_Bottom", "Top_And_Bottom_And_Right", "Top_And_Right"],
        "Below": ["Bottom", "Overstruck", "Bottom_And_Right"],
        "Post": ["Right"],
        "Pre": ["Left", "Top_And_Left", "Top_And_Left_And_Right", "Left_And_Right"],
    },
    "VowelModifier": {
        "Above": ["Top"], "Below": ["Bottom", "Overstruck"], "Post": ["Right"], "Pre": ["Left"],
    },
    "SymbolModifier": {
        "Above": ["Top"], "Below": ["Bottom"],
    },
    "ConsonantFinalModifier": {
        "Above": ["Top"], "Below": ["Bottom"], "Post": ["Not_Applicable"],
    },
}


def properties(directory):
    def path(name):
        return os.path.join(directory, name + ".txt")

    syllabic = read(path("IndicSyllabicCategory"))
    syllabic.update(read(path("IndicSyllabicCategory-Additional")))

    positional = read(path("IndicPositionalCategory"))
    positional.update(read(path("IndicPositionalCategory-Additional")))

    joins = joining(path("ArabicShaping"))
    ignored = ignorable(path("DerivedCoreProperties"))
    where = scripts(path("Scripts"))

    out = {}

    for u in set(syllabic) | set(positional) | set(joins) | ignored:
        if where.get(u, "Unknown") in ELSEWHERE:
            continue

        general = unicodedata.category(chr(u))

        kind = syllabic.get(u, "Other")
        side = positional.get(u, "Not_Applicable")

        # The database leaves these unclassified while giving them a side, and the specification
        # says what they are.
        if 0x1CE2 <= u <= 0x1CE8:
            kind = "Cantillation_Mark"
        if 0x0F18 <= u <= 0x0F19 or 0x0F3E <= u <= 0x0F3F:
            kind = "Vowel_Dependent"
        if u == 0x1CED:
            kind = "Tone_Mark"
        if u in (0x11302, 0x11303, 0x114C1):
            side = "Top"

        category = classify(u, kind, general, joins.get(u, "U"), u in ignored)

        if (parts := SIDES.get(category)) is not None:
            for part, sides in parts.items():
                if side in sides:
                    category += part
                    break
            else:
                # Something classified as positioned but given no position: it goes where the
                # category's own default puts it, which is after the base.
                category += "Post" if "Post" in parts else "Above"

        if category == "Other":
            continue

        out[u] = category

    return out


def ranges(values):
    out = []
    start = last = None
    held = None

    for code in sorted(values):
        if held == values[code] and code == last + 1:
            last = code
            continue

        if held is not None:
            out.append((start, last, held))

        start = last = code
        held = values[code]

    out.append((start, last, held))
    return out


# The names a font files a script under are the database's short names in lower case, with a few
# that are only three letters long and are padded to four.
PADDED = {"nkoo": "nko ", "vaii": "vai ", "laoo": "lao ", "yiii": "yi  "}


def script_ranges(directory):
    """Where each of the scripts this engine is used for lives, and what a font calls it."""
    aliases = {}

    for line in open(os.path.join(directory, "PropertyValueAliases.txt"), encoding="utf-8"):
        line = line.split("#")[0].strip()
        if not line.startswith("sc ;"):
            continue

        fields = [f.strip() for f in line.split(";")]
        tag = fields[1].lower()
        aliases[fields[2]] = PADDED.get(tag, tag)

    where = scripts(os.path.join(directory, "Scripts.txt"))

    tagged = {code: aliases[name] for code, name in where.items()
              if name in UNIVERSAL and name in aliases}

    return ranges(tagged)


directory = sys.argv[1] if len(sys.argv) > 1 else "."

rows = ranges(properties(directory))
script_rows = script_ranges(directory)

print(f"""// Generated by tools/make-use-tables.py from Unicode {unicodedata.unidata_version}
// and Microsoft's published overrides. Do not edit.

namespace n8PDF.Text;

/// <summary>
/// What each character is to the universal shaping engine, and which characters it is used for.
/// </summary>
/// <remarks>
/// Generated rather than written. What a character is to this engine is not in the database: it is
/// worked out from five properties that are, by the rules the specification lays down, and the
/// working out is done once here rather than at run time.
/// </remarks>
internal static class UseTables
{{
    /// <summary>Where each run of characters of one kind begins and ends.</summary>
    internal static readonly int[] Starts =
    [""")


def emit(values, per_line):
    for i in range(0, len(values), per_line):
        print("        " + " ".join(values[i:i + per_line]))


emit([f"0x{start:X}," for start, _, _ in rows], 12)

print("    ];\n\n    internal static readonly int[] Ends =\n    [")
emit([f"0x{end:X}," for _, end, _ in rows], 12)

print("""    ];

    /// <summary>What the characters of each run are.</summary>
    internal static readonly UseCategory[] Kinds =
    [""")
emit([f"UseCategory.{category}," for _, _, category in rows], 4)

print("""    ];

    /// <summary>
    /// Where the scripts this engine shapes live, and the name a font files each of them under.
    /// </summary>
    internal static readonly int[] ScriptStarts =
    [""")
emit([f"0x{start:X}," for start, _, _ in script_rows], 12)

print("    ];\n\n    internal static readonly int[] ScriptEnds =\n    [")
emit([f"0x{end:X}," for _, end, _ in script_rows], 12)

print("""    ];

    internal static readonly string[] ScriptTags =
    [""")
emit([f'"{tag}",' for _, _, tag in script_rows], 8)

print("    ];\n}")
