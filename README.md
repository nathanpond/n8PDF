# n8PDF

Converts `.docx` to PDF. Written from scratch: no third-party DOCX or PDF library, no headless
Word, LibreOffice, browser engine, sidecar service or container. A consumer adds one assembly
reference and calls one method.

```csharp
Converter.ConvertFile("report.docx", "report.pdf");
```

## The constraint

`src/n8PDF` carries **zero** `PackageReference` entries, and `LibraryInvariantTests` fails the
build if that ever changes. The only things it builds on are the base class library's
`System.IO.Compression` (the DOCX container, and Flate for PDF streams) and `System.Xml.Linq`.
Everything domain-specific is ours: OPC relationships, WordprocessingML semantics, the style
cascade, TrueType/OpenType parsing, text measurement, line breaking, pagination, and the PDF
writer including font embedding.

## Layout

```
src/n8PDF/
  Packaging/     OPC container: zip, content types, relationships
  Ooxml/         WordprocessingML model and parsers, plus Units
  Styling/       the formatting cascade, producing Resolved*Format
  Fonts/         SFNT parsing, metrics, font resolution
  Images/        PNG decoding and JPEG header reading
  Layout/        measurement, line breaking, page composition, list counters
  Pdf/           object model, writer, content streams, Type0 font embedding
  Diagnostics/   LayoutTrace — the testing spine
  Converter.cs   public API
tests/n8PDF.Tests/
  Fixtures/Minimal/     hand-authored .docx, one feature each (generated, committed)
  Fixtures/Real/        documents Word itself wrote (tools/make-real-fixtures.sh)
  Fixtures/Reference/   Word-exported reference PDFs (tools/make-reference-pdfs.sh)
  Golden/               committed layout traces
```

Data flows one direction: **Packaging → Ooxml → Styling → Layout → Pdf**, with `Fonts` serving
Layout and Pdf. Word's page origin is top-left and PDF's is bottom-left; the flip happens once,
in `PdfRenderer`, and nowhere else.

## Running

```bash
dotnet test                        # everything
N8PDF_BLESS=1 dotnet test          # re-bless layout goldens after an intended change
N8PDF_REQUIRE_QPDF=1 dotnet test   # fail rather than skip if qpdf is missing (use in CI)
tools/make-reference-pdfs.sh       # generate missing Word reference PDFs (macOS + Word)
```

Converted fixtures are written to `artifacts/test-output/` for eyeballing. That directory is
git-ignored.

## Validation

Four tiers, cheapest and most diagnostic first.

1. **Layout goldens** (`Golden/*.json`) — every positioned run's coordinates, font and size,
   compared against a committed trace. A failure names the run that moved and by how much. These
   prove nothing changed, not that we are correct.
2. **Unit and structural tests** — font metrics against published values, unit conversions, the
   style cascade including toggle-property cancellation, and PDF structure read back out of the
   generated file.
3. **Reference comparison** — against PDFs exported from Word into `Fixtures/Reference/`, named
   after the fixture. This is the only tier that can say we match Word. Every fixture is required
   to have one: a missing reference fails rather than skips, because a skipped comparison is
   indistinguishable from a passing one. Generate them with `tools/make-reference-pdfs.sh`.

   Both PDFs are read through one content-stream parser and compared line by line in points.
   Across the fixtures, line start positions match Word exactly and every baseline agrees to
   within 0.29pt — close to Word's own vertical quantum of 1/300 inch. `Fidelity_report` writes
   the full per-line table to `artifacts/test-output/fidelity-report.txt`.

4. **Structural validation** — `qpdf --check` over every converted fixture, verifying the
   cross-reference table, stream lengths and object graph we hand-rolled. A tolerant viewer will
   render a structurally broken PDF perfectly well, so this catches what eyeballing cannot.
   Optional: `brew install qpdf` to enable, or `N8PDF_REQUIRE_QPDF=1` to fail when it is absent.

### Viewers

Preview uses Apple's Quartz engine and Chrome uses PDFium — two independent implementations. Both
agreeing is decent evidence a file is well formed; they are not a substitute for tier 4. Note that
glyph *positions* come from the content stream, so all conforming viewers agree on geometry; what
differs between them is rasterisation, and the one feature of ours genuinely sensitive to that is
synthetic bold (text render mode 2).

## Matching Word

Several layout rules here were derived by measuring Word's own output rather than from the spec,
each with a fixture built so that only one candidate model survives:

- Adjacent paragraph spacing **collapses to the larger** of space-after and space-before, it does
  not sum. Across a page break the collapse still applies, but the previous paragraph's
  space-after is absorbed by the page it ended on.
- A line-spacing multiple's **extra leading goes below** the baseline, so the first line of a
  paragraph sits at its natural ascent whatever the multiple.
- The font's **line gap belongs above the ascent**, not below the descent.
- Word **fills in what a document's styles leave unstated** from its own built-in definitions —
  see `WordBuiltInStyles`. These sit *below* the document's `docDefaults` in precedence: they are
  a fallback for what nothing states, not an override. Set
  `ConversionOptions.ApplyWordBuiltInStyleDefaults = false` to render strictly what the document
  says.

Text wrapping around a floating picture is single-pass: a float's exclusion applies to the text
laid out from its anchor onwards, while Word lays out the whole page and reflows text above the
anchor too. The one case where that shows — a float whose top clearance reaches back over the
previous paragraph's last line — is handled by moving those lines down, which is correct because
a full-width float does not change their width. A partial-width float reaching backwards would
need the line broken again, and is not handled.

Word also quantises vertical positions to 1/300 inch (0.24pt). That is not implemented — our
residuals are already smaller than one quantum — but it is the floor on how closely anything can
match Word vertically.

## Current scope

Implemented: kerning, read from a font's GPOS table as well as the legacy one — Calibri has only
the former and Times New Roman only the latter, so both are needed to kern either — applied where
`w:kern` asks for it and from the type size it names upwards, tab stops of every alignment — left, centre, right and decimal, the last three
resolved once the text after the tab has been measured, with a stop the line has already passed
falling through to the next one, and leaders filling the gap a stop opens (dots, hyphens,
underscores and middle dots, set on a grid measured from the edge of the page so that entries of
different lengths line up with each other), and the vertical rule a bar stop asks for, down every
line of its paragraph — widow and orphan control (two lines of a paragraph on each side of a page or column
break, or none — a three-line paragraph moves whole, since it cannot give two to both), keeping a
paragraph with the next one and keeping its own lines together (`w:keepNext` and `w:keepLines`,
including chains of headings that move as one), section breaks (next-page, continuous, even- and odd-page, each section with its own
page size, orientation and margins, running heads inherited per kind from the section before where
one says nothing), multiple text columns (evenly divided or individually stated, column breaks, and
the rule down the gap where the document asks for one), footnotes and endnotes (numbered in reference order, arabic for footnotes and roman
for endnotes unless the document says otherwise; a footnote goes to the foot of the page its
reference lands on and takes that space out of the body above it, an endnote carries on after the
body like ordinary content, and both are ruled off by the separator), hyperlinks (external addresses as clickable regions, internal links to bookmarks
anywhere in the document, with the regions placed and padded the way Word places them), headers
and footers (per page, with separate first-page and even-page variants, and
PAGE and NUMPAGES fields evaluated), lists and numbering (decimal, letters, roman and bullets, nested levels with
independent counters and multi-level templates, hanging indents), images, inline and floating (PNG decoded from scratch, JPEG passed through untouched,
transparency via a soft mask; square, top-and-bottom and no-wrap text flow around anchored
pictures), tables (fixed and autofit column sizing, horizontal spans, borders, shading,
cell margins and vertical alignment, rows kept whole across page breaks), page size and margins
from `sectPr`, paragraphs and runs, `xml:space` handling,
line and page breaks, tabs (left-aligned stops), font family via theme resolution, size, bold,
italic, underline, strikethrough, colour, caps, super/subscript, character spacing and scaling,
alignment including justification, indents including hanging, spacing before/after with
contextual spacing, line spacing (auto/exact/at-least), pagination, real font metrics with
`.ttc` support, and Type0/CIDFontType2 embedding with a `ToUnicode` map so text stays selectable.

Table autofit is the one piece here that approximates rather than reproduces. Word's algorithm is
undocumented; ours measures each column's minimum (widest word) and maximum (unwrapped) width and
shares out the available space between them. It reproduces both behaviours that were measured —
content-width columns when the table fits, and a table filling the text area exactly when it does
not — and agrees with Word to 0.16pt on `table-autofit-probe`, but it is not derived from the real
algorithm the way the paragraph rules are.

Not yet: GIF, BMP, TIFF and EMF pictures, interlaced PNG, vertical cell merges beyond suppressing the shared
border, splitting a row across pages, fields other than PAGE and NUMPAGES (their cached values are
shown), splitting a note across pages, restarting note numbering per page or per section, notes
positioned beneath the text rather than at the foot of the page, endnotes gathered at the end of
each section rather than of the document, RTL and complex
scripts, balancing the columns of a section's last page, footnotes under the column that refers to
them rather than under the whole measure, page numbering restarted per section, vertical page
alignment,
and font subsetting.

`ContentCoverageTests` asserts that every text run and every placeable image in a document reaches
the PDF, so an unimplemented construct fails loudly instead of vanishing from the output.

### Real documents

`Fixtures/Real/` holds documents Word wrote. `tools/make-real-fixtures.sh` takes the seed
documents defined in `Fixtures.RealSeeds`, opens each in Word and saves it straight back out,
which rewrites the package in Word's own terms — a `styles.xml` carrying several hundred latent
styles, `settings.xml`, its theme, `docProps`. None of that can be produced by hand, and it is
what these fixtures exist to test. They go through the same per-line comparison as everything
else.

`tools/make-real-fixtures.sh` --list shows what would be generated. Add to `RealSeeds` to cover
more; third-party templates are best avoided, since their licence terms would come with them.
