#!/usr/bin/env python3
"""Writes the tables the Indic and South-East Asian shapers read.

An Indic syllable is not drawn in the order it is stored: a vowel written to the left of its
consonant is stored after it, consonants stack into one shape, and a mark belonging to the start of
a cluster is drawn at its end. Which is which is decided by two properties of the Unicode character
database — what part of a syllable a character is, and where round its consonant it is drawn — and
this turns those into source, since the library carries no dependencies and the answer must not
depend on a file being present at run time.

Neither property is enough on its own. Where a matra ends up depends on the script as well as on
the side it is written: the same "drawn above" answers differently in Devanagari and in Kannada,
because what a syllable's parts are sorted into is a visual order and the scripts stack their parts
differently. Those per-script answers, and the handful of characters whose database category is not
what a shaper needs, are written out below; they come from the OpenType script development
specifications, and match what the reference implementation does with the same files.

    for f in IndicSyllabicCategory IndicPositionalCategory Blocks; do
        curl -O https://www.unicode.org/Public/15.0.0/ucd/$f.txt
    done
    tools/make-indic-tables.py . > src/n8PDF/Text/IndicTables.cs
"""

import os
import sys
import unicodedata

# What each syllabic category means to a shaper.
CATEGORIES = {
    "Other": "Other",
    "Avagraha": "Symbol",
    "Bindu": "SyllableModifier",
    "Brahmi_Joining_Number": "Placeholder",
    "Cantillation_Mark": "Accent",
    "Consonant": "Consonant",
    "Consonant_Dead": "Consonant",
    "Consonant_Final": "ConsonantMedial",
    "Consonant_Head_Letter": "Consonant",
    "Consonant_Initial_Postfixed": "Consonant",
    "Consonant_Killer": "Matra",
    "Consonant_Medial": "ConsonantMedial",
    "Consonant_Placeholder": "Placeholder",
    "Consonant_Preceding_Repha": "Repha",
    "Consonant_Prefixed": "Other",
    "Consonant_Subjoined": "ConsonantMedial",
    "Consonant_Succeeding_Repha": "ConsonantMedial",
    "Consonant_With_Stacker": "ConsonantWithStacker",
    "Gemination_Mark": "SyllableModifier",
    "Invisible_Stacker": "Halant",
    "Joiner": "Joiner",
    "Modifying_Letter": "Other",
    "Non_Joiner": "NonJoiner",
    "Nukta": "Nukta",
    "Number": "Placeholder",
    "Number_Joiner": "Placeholder",
    "Pure_Killer": "Matra",
    "Register_Shifter": "RegisterShifter",
    "Syllable_Modifier": "SyllableModifier",
    "Tone_Letter": "Other",
    "Tone_Mark": "Nukta",
    "Virama": "Halant",
    "Visarga": "SyllableModifier",
    "Vowel": "Vowel",
    "Vowel_Dependent": "Matra",
    "Vowel_Independent": "Vowel",
}

POSITIONS = {
    "Not_Applicable": "End",
    "Left": "PreConsonant",
    "Top": "AboveConsonant",
    "Bottom": "BelowConsonant",
    "Right": "PostConsonant",
    # A matra written on more than one side is filed under the side its last part is on, which is
    # where the whole of it has to go.
    "Bottom_And_Right": "PostConsonant",
    "Left_And_Right": "PostConsonant",
    "Top_And_Bottom": "BelowConsonant",
    "Top_And_Bottom_And_Left": "BelowConsonant",
    "Top_And_Bottom_And_Right": "PostConsonant",
    "Top_And_Left": "AboveConsonant",
    "Top_And_Left_And_Right": "PostConsonant",
    "Top_And_Right": "PostConsonant",
    "Overstruck": "AfterMain",
    "Visual_Order_Left": "PreMatra",
}

# Characters whose category the database gives differently from what a shaper needs. Each is the
# script's own business rather than the database's: which letter becomes a repha, which marks
# behave like the bindu, which of Khmer's signs may stand between the parts of a syllable.
CATEGORY_OVERRIDES = {
    # The Ra of each script: the letter that becomes a repha, the mark drawn at the top right of a
    # cluster standing for an r at its start.
    0x0930: "Ra", 0x09B0: "Ra", 0x09F0: "Ra", 0x0A30: "Ra", 0x0AB0: "Ra", 0x0B30: "Ra",
    0x0BB0: "Ra", 0x0C30: "Ra", 0x0CB0: "Ra", 0x0D30: "Ra",

    # These act like the bindu rather than like accents.
    0x0953: "SyllableModifier", 0x0954: "SyllableModifier",

    # A Gurmukhi vowel sign that may be preceded by a bindi, and so is sorted after one.
    0x0A40: "MatraPost",

    # These act like consonants.
    0x0A72: "Consonant", 0x0A73: "Consonant",

    # Vedic tone marks, treated as accents until something needs finer.
    0x1CE2: "Accent", 0x1CE3: "Accent", 0x1CE4: "Accent", 0x1CE5: "Accent", 0x1CE6: "Accent",
    0x1CE7: "Accent", 0x1CE8: "Accent", 0x1CED: "Accent",

    # These take marks the way an avagraha does.
    0xA8F2: "Symbol", 0xA8F3: "Symbol", 0xA8F4: "Symbol", 0xA8F5: "Symbol", 0xA8F6: "Symbol",
    0xA8F7: "Symbol", 0x1CE9: "Symbol", 0x1CEA: "Symbol", 0x1CEB: "Symbol", 0x1CEC: "Symbol",
    0x1CEE: "Symbol", 0x1CEF: "Symbol", 0x1CF0: "Symbol", 0x1CF1: "Symbol",

    0x0A51: "Matra",
    0x0AFB: "Nukta", 0x0B55: "Nukta",
    0x09FC: "Placeholder", 0x0C80: "Placeholder", 0x0D04: "Placeholder",
    0x25CC: "DottedCircle",

    # Grantha marks that Tamil also uses.
    0x11301: "SyllableModifier", 0x11302: "SyllableModifier", 0x11303: "SyllableModifier",
    0x1133B: "Nukta", 0x1133C: "Nukta",

    # Khmer. Its signs fall into three groups by what may stand beside what.
    0x179A: "Ra",
    0x17CC: "Robatic", 0x17C9: "Robatic", 0x17CA: "Robatic",
    0x17C6: "XGroup", 0x17CB: "XGroup", 0x17CD: "XGroup", 0x17CE: "XGroup", 0x17CF: "XGroup",
    0x17D0: "XGroup", 0x17D1: "XGroup",
    0x17C7: "YGroup", 0x17C8: "YGroup", 0x17DD: "YGroup", 0x17D3: "YGroup",
    0x17D9: "Placeholder",

    # Myanmar, whose medial consonants and tone marks are its own affair.
    0x104E: "Consonant",
    0x1004: "Ra", 0x101B: "Ra", 0x105A: "Ra",
    0x1032: "Accent", 0x1036: "Accent",
    0x103A: "Asat",
    0x103E: "MedialHa", 0x1060: "MedialLa", 0x103C: "MedialRa",
    0x103D: "MedialWa", 0x1082: "MedialWa",
    0x103B: "MedialYa", 0x105E: "MedialYa", 0x105F: "MedialYa",
    0x1063: "PwoTone", 0x1064: "PwoTone", 0x1069: "PwoTone", 0x106A: "PwoTone",
    0x106B: "PwoTone", 0x106C: "PwoTone", 0x106D: "PwoTone", 0xAA7B: "PwoTone",
    0x1038: "SyllableModifier", 0x1087: "SyllableModifier", 0x1088: "SyllableModifier",
    0x1089: "SyllableModifier", 0x108A: "SyllableModifier", 0x108B: "SyllableModifier",
    0x108C: "SyllableModifier", 0x108D: "SyllableModifier", 0x108F: "SyllableModifier",
    0x109A: "SyllableModifier", 0x109B: "SyllableModifier", 0x109C: "SyllableModifier",
    0x104A: "Placeholder",
}

CATEGORY_OVERRIDES.update({code: "VariationSelector" for code in range(0xFE00, 0xFE10)})
CATEGORY_OVERRIDES.update({code: "Placeholder" for code in (0x2015, 0x2022, 0x25FB, 0x25FC,
                                                            0x25FD, 0x25FE)})

POSITION_OVERRIDES = {
    0x0A51: "BelowConsonant",
    0x0B01: "BeforeSub",
}

# The no-break space stands in for a consonant that is not there.
PLACEHOLDERS = {0x00A0: "Placeholder"}

CONSONANTS = ("Consonant", "ConsonantWithStacker", "Ra", "ConsonantMedial", "Vowel",
              "Placeholder", "DottedCircle")

MATRAS = ("Matra", "MatraPost")

MODIFIERS = ("SyllableModifier", "SyllableModifierPost", "Accent", "Symbol")

POSITIONED = ("ConsonantMedial", "SyllableModifier", "RegisterShifter", "Halant", "Matra",
              "MatraPost")


def matra_right(code, block):
    """Where a matra written to the right of its consonant is sorted to."""
    if block == "Bengali" or block == "Gurmukhi" or block == "Gujarati" or block == "Oriya" \
            or block == "Tamil" or block == "Malayalam":
        return "AfterPost"
    if block == "Telugu":
        return "BeforeSub" if code <= 0x0C42 else "AfterSub"
    if block == "Kannada":
        return "BeforeSub" if code < 0x0CC3 or code > 0x0CD6 else "AfterSub"

    return "AfterSub"


def matra_top(code, block):
    if block == "Gurmukhi":
        return "AfterPost"
    if block == "Oriya":
        return "AfterMain"
    if block == "Telugu" or block == "Kannada":
        return "BeforeSub"

    return "AfterSub"


def matra_bottom(code, block):
    if block == "Gurmukhi" or block == "Gujarati" or block == "Tamil" or block == "Malayalam":
        return "AfterPost"
    if block == "Telugu" or block == "Kannada":
        return "BeforeSub"

    return "AfterSub"


def read(path):
    """Every character the file names, with the property it names for it."""
    values = {}

    for line in open(path, encoding="utf-8"):
        line = line.split("#")[0].strip()
        if not line:
            continue

        fields = [f.strip() for f in line.split(";")]
        if len(fields) < 2:
            continue

        codes = fields[0].split("..")

        for code in range(int(codes[0], 16), int(codes[-1], 16) + 1):
            values[code] = fields[1]

    return values


def properties(directory):
    """What each character is, and where it is drawn, as the shaper wants them."""
    syllabic = read(os.path.join(directory, "IndicSyllabicCategory.txt"))
    positional = read(os.path.join(directory, "IndicPositionalCategory.txt"))
    blocks = read(os.path.join(directory, "Blocks.txt"))

    out = {}

    for code, name in syllabic.items():
        category = CATEGORIES.get(name, "Other")
        where = positional.get(code, "Not_Applicable")

        # A syllable modifier with no position of its own is sorted after a post-base matra
        # rather than with the other modifiers.
        if category == "SyllableModifier" and where == "Not_Applicable":
            category = "SyllableModifierPost"

        out[code] = (category, POSITIONS.get(where, "End"))

    for code, category in PLACEHOLDERS.items():
        out[code] = (category, "End")

    for code, category in CATEGORY_OVERRIDES.items():
        out[code] = (category, out.get(code, ("Other", "End"))[1])

    for code, (category, position) in list(out.items()):
        block = blocks.get(code, "")

        # Only the parts of a syllable that are placed round a consonant have a position at all.
        if category not in POSITIONED:
            position = "End"

        if category in CONSONANTS:
            position = "BaseConsonant"
        elif category in MATRAS:
            # Khmer and Myanmar sort their vowels by which side they are on rather than into one
            # visual order, so for those the side becomes the category.
            if block.startswith("Khmer") or block.startswith("Myanmar"):
                category = {
                    "PreConsonant": "VowelPre",
                    "AboveConsonant": "VowelAbove",
                    "BelowConsonant": "VowelBelow",
                    "PostConsonant": "VowelPost",
                }.get(position, category)
            elif position == "PreConsonant":
                position = "PreMatra"
            elif position == "PostConsonant":
                position = matra_right(code, block)
            elif position == "AboveConsonant":
                position = matra_top(code, block)
            elif position == "BelowConsonant":
                position = matra_bottom(code, block)
        elif category in MODIFIERS:
            position = "SyllableModifierOrVedic"
        elif category == "Repha":
            position = "RaToBecomeRepha"

        out[code] = (category, position)

    for code, position in POSITION_OVERRIDES.items():
        out[code] = (out[code][0], position)

    return {code: value for code, value in out.items() if value != ("Other", "End")}


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


rows = ranges(properties(sys.argv[1] if len(sys.argv) > 1 else "."))

print(f"""// Generated by tools/make-indic-tables.py from Unicode {unicodedata.unidata_version}. Do not edit.

namespace n8PDF.Text;

/// <summary>
/// What each character of an Indic or South-East Asian script is to a shaper, and where round its
/// consonant it is drawn.
/// </summary>
/// <remarks>
/// Generated rather than written, for the same reason the bidirectional and joining tables are:
/// the database is the answer, and a table of a thousand entries typed by hand is a thousand
/// chances to be wrong about a letter nobody here can read.
/// </remarks>
internal static class IndicTables
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
    internal static readonly IndicCategory[] Kinds =
    [""")
emit([f"IndicCategory.{category}," for _, _, (category, _) in rows], 4)

print("""    ];

    /// <summary>And where each of them is drawn.</summary>
    internal static readonly IndicPosition[] Places =
    [""")
emit([f"IndicPosition.{position}," for _, _, (_, position) in rows], 4)

print("    ];\n}")
