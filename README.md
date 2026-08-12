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
  Layout/        measurement, line breaking, page composition
  Pdf/           object model, writer, content streams, Type0 font embedding
  Diagnostics/   LayoutTrace — the testing spine
  Converter.cs   public API
tests/n8PDF.Tests/
  Fixtures/Minimal/     hand-authored .docx, one feature each (generated, committed)
  Fixtures/Real/        real Word documents — drop yours here
  Fixtures/Reference/   Word-exported reference PDFs — drop yours here
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

Three tiers, cheapest and most diagnostic first.

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

   It currently compares pagination and page geometry. Per-run text-position comparison needs a
   content-stream parser and is the next piece of the harness — until then, green here does not
   mean the lines are in the same places Word put them.

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

## Current scope

Implemented: page size and margins from `sectPr`, paragraphs and runs, `xml:space` handling,
line and page breaks, tabs (left-aligned stops), font family via theme resolution, size, bold,
italic, underline, strikethrough, colour, caps, super/subscript, character spacing and scaling,
alignment including justification, indents including hanging, spacing before/after with
contextual spacing, line spacing (auto/exact/at-least), pagination, real font metrics with
`.ttc` support, and Type0/CIDFontType2 embedding with a `ToUnicode` map so text stays selectable.

Not yet: tables (parsed but not laid out), images, lists and numbering, headers and footers,
fields, hyperlinks, footnotes, floating objects, RTL and complex scripts, font subsetting, GPOS
kerning, widow/orphan control, and centre/right/decimal tab stops.
