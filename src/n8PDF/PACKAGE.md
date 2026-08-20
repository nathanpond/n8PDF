# n8PDF

Converts `.docx` to PDF. Written from scratch: no third-party DOCX or PDF library, no headless
Word, LibreOffice, browser engine, sidecar service or container. One assembly, one method.

```csharp
using n8PDF;

Converter.ConvertFile("report.docx", "report.pdf");

// or from bytes and streams
var pdf = Converter.Convert(File.ReadAllBytes("report.docx"));
Converter.Convert(input, output, new ConversionOptions { Title = "Quarterly report" });
```

The package depends on nothing but the base class library, and pulls in no native code.

## What it does

Paragraphs and runs with the whole formatting cascade, tables (including styles, spans, autofit
and borders), lists and numbering, headers and footers, footnotes and endnotes, fields, hyperlinks,
images of every format Word embeds, floating objects with text wrapped round them, shapes and text
boxes, watermarks, SmartArt, and charts — columns, bars, lines, pies, areas and scatters, with
their titles, legends and data labels.

Text is measured against the real font: a from-scratch TrueType and CFF engine with kerning,
OpenType and Apple shaping, the Unicode bidirectional and line-breaking algorithms, and complex
scripts. Fonts are embedded as Type0/CIDFontType2 with a `ToUnicode` map, so what comes out stays
selectable and searchable.

## How closely it matches Word

Every rule in it was measured against Word's own export rather than read out of the specification,
and the repository holds the fixtures, the reference PDFs and the comparison harness that says so:
a page's lines land within a fraction of a point of where Word puts them, and the two agree on
better than 99% of the ink.

Where something is a known divergence it is written down with its numbers rather than smoothed
over. See the repository's README for what is measured, what is fitted, and what is not there yet.

## Fonts

By default the platform's installed fonts are discovered — read once for the process, and each face
read from its file only when a document asks for it. Register fonts explicitly through
`ConversionOptions.Fonts` for output that does not depend on what is installed.
