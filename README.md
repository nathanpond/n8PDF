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
  Fonts/         SFNT parsing, metrics, font resolution, shaping
  Text/          the bidirectional algorithm and the Unicode tables it reads
  Images/        PNG, GIF, BMP, TIFF, EMF and JPEG decoding
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

### What a conversion costs

A page of text converts in about 1.4ms on the machine this was measured on. Finding the fonts is
the one thing that costs more than the document does: the platform's font directories here hold 651
files and 1.3GB, and every one of them has to be read to know what face it holds. That is done once
for the process and shared — the index is every face's name, style and file, and comes to 1.6MB —
and a face reads its own file only when a document asks for that face. So the first conversion in a
process pays about 600ms for the scan and every one after it pays nothing.

Before that, a conversion that said nothing about fonts read all 1.3GB, held 1.5GB while it did,
threw it away, and did the whole thing again for the next document: 450ms a page, whatever the page
held.

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
   within 0.62pt, and to within 0.35pt in every document that neither raises a run nor sets one
   in an East Asian face — close to Word's own vertical quantum of 1/300 inch. `Fidelity_report` writes
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
- An **East Asian face gets three tenths of an em of extra leading**, whatever its own metrics
  say. Word gives MS Mincho, MS Gothic, KaiTi and MingLiU at 12pt a line of exactly 15.6pt,
  although those four faces ask for 1.0, 1.0, 1.14 and 1.20 em between them — and although Core
  Text reads those four values back from the files just as this reader does. It does the same for
  a line of Latin letters set in one of them, so the height belongs to the face rather than to the
  script written in it, and a face is taken to be East Asian when `OS/2` declares one of the five
  East Asian code pages. Measured by `east-asian-line-box-probe`; sixteen hundredths of the
  leading go above the ascent and the rest below the descent, which is as close as Word's own
  vertical quantum allows.
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

Three rules about how tall a line is were settled by `superscript-probe` and `numbering`, and none
of them is what the code did before it was asked. A raised or lowered run keeps the line box of the
size it was *given*, not the smaller size it is *drawn* at: a twenty point superscript in a twelve
point line makes that line as tall as a twenty point one, above the baseline and below it, while a
twelve point superscript in a twelve point line changes nothing at all. A line's box is the tallest
ascent over the deepest descent across its runs, which is not the tallest of the runs' own boxes —
twelve point Times with an eleven point Calibri mark on the end takes the Times ascent and the
Calibri descent, and is deeper than either font alone would make it. And a list's number is drawn
on its line without being part of its box, which is the one thing on a line that is not.

The shifts themselves were measured at three sizes rather than fitted to one. A lowered run drops
about a twelfth of its size, which is far less than it looks like it should be and was nearly twice
that here; a raised one rises about a third.

Word also quantises vertical positions to 1/300 inch (0.24pt). That is not implemented — our
residuals are already smaller than one quantum — but it is the floor on how closely anything can
match Word vertically. The one place it is implemented is a chart's markers, which are small enough
that a quantum of it is a twentieth of the marker and shows. It quantises the type size it writes to the same 1/300 inch, which is why a
15pt run comes out of one of its PDFs as 15.12.

### How far inside its own edges a shape sets its text

A text box holds its text clear of its edges by two things added together: the inset the shape
declares — a tenth of an inch at the sides and half of that above and below, where it declares
none — and **half its outline**, the half that falls inside the shape. `shape-inset-probe` is what
says so: its third page sets a six point outline against no inset at all, and the text there begins
3.12pt inside the shape rather than 6pt or nothing.

The outline itself straddles the edge. Word's export fills the whole extent and then strokes the
same rectangle, insetting neither, which is what a PDF does with a stroked path anyway — so the
frame here is drawn the same way and the two agree to a hundredth of a point.

### Where a chart puts things

A chart is the one thing a document describes only as data: series, axes and formatting, with no
drawing of it anywhere, not even the cache a diagram carries. So every number below was measured
from Word's export rather than read anywhere.

- The **plot area** goes exactly where a chart states it, to the last decimal place, when it states
  it as fractions of the frame.
- A **bar's width** falls out of the gap between them, which is a percentage of the bar itself: one
  series at a gap of 150 makes a category two and a half bars wide, so four categories across 252pt
  give 63pt each and a bar of 25.2pt — which is what Word draws, to the quantum. Two series at a
  gap of 100 and an overlap of −27 share their category and then stand apart: 117 ÷ 3.27 = 35.78,
  against Word's 35.76.
- A **label ranged against its axis** ends a little under one em short of it — 9.278pt at ten point
  type and 18.547pt at twenty, so proportional with nothing fixed about it — and is set with the box
  from its ascenders to its descenders centred on its mark, which puts the baseline a quarter of the
  type size below it. The face's typographic ascent and descent are what that quarter comes from, not
  the ones a line is measured by: Calibri says 1536 and 512 of its 2048 for the first pair and 1950
  and 550 for the second, and only the first puts the label where Word puts it.
- A **label written under its axis** sits with its baseline 1.584 times its type size below it, at
  ten point and at twenty alike, and each hundred of `lblOffset` moves it a further 0.312 of that
  size.
- Whether the axis carries **marks** makes no difference to either: a chart drawn with them and one
  drawn without put their labels in exactly the same places. A mark itself reaches 40301 EMU
  outside its axis, which is 3.1733pt — the same on both axes of `chart-axis-probe` and on the
  lying axis of `chart-bar-stacked`, and outwards in every case.
- A chart's own **frame** is a white rectangle with ten point corners outlined in #898989 at half a
  point, which is what Word draws where the chart says nothing about its border.

Every line of all seven chart fixtures lands within 0.012pt of Word's across the page and 0.32pt
down it, and the ink of a page agrees with Word's on better than 99.4% of it.

**Where the plotting goes when the chart does not say** — which is what every chart in a real
document leaves to be worked out — is measured too, by `chart-layout-probe`. A chart carrying no
labels at all puts its plotting **eleven points inside its frame on every side**, whatever size the
frame is; a chart carrying them begins its labels **6.5pt inside the frame** and gives the plotting
what is left:

| side | what it makes room for |
|---|---|
| left | the widest label ranged against the axis there, plus the gap it keeps from the axis |
| foot | the line written under the axis: 1.584 type sizes below it, and its descender below that |
| top | half a label's height, so the topmost number does not overrun the frame |
| right | nothing — a category label wider than its bars is left to overrun, as Word leaves it |

A chart lying on its side swaps the two labelled edges over — the words go up the side and the
numbers along the foot — and swaps what the other two do with them. Its top takes the bare eleven
points, since nothing reaches above the plot; its right takes eleven **plus half the widest number**,
because the last number along the foot is centred on the plot's own corner and half of it hangs
past. Word gives the second page of `chart-bar-stacked` 39.34pt on the left, 11 above, 25.05 below
and 16.07 on the right, and each of the four falls out of the rules above to a fortieth of a point.

The heights in that table are the face as *Windows* reads it, where the baselines are the face as it
reads *itself*: for Calibri, 1950 and 550 of its 2048 against 1536 and 512. Two questions, two
answers, and using the second for both put the plot area two points out at twenty point type.

Across the six charts of the probe — varying the frame size, the width of the numbers, the type
size, the length of the category labels, and whether there are labels at all — the plot area lands
within a quarter of a point of Word's, and the chart with no labels lands exactly.

**What the axis runs between when the chart does not say** is measured by `chart-scale-probe` and
`chart-bar-scale-probe`, twenty-six charts between them, varying the numbers, how long the axis is,
which way it runs and what size its labels are set at. One rule accounts for every one:

> the step is the **smallest** of one, two or five times a power of ten for which the axis — running
> from the largest step at or below the least value to the smallest step **strictly** above the
> greatest — carries no more marks than the axis has room to write

So up a 126pt side at ten point: 7 runs to 8 in ones, 9.5 to 10 in ones, 10 to 12 in twos, 47 to 50
in fives, 105 to 120 in twenties, 1000 to 1200 in two hundreds, and 0.4 to 0.45 in twentieths. The
strictness is what puts a chart of exactly 100 at 120 rather than leaving its tallest bar against
the frame. The foot is nought wherever nothing is negative, whatever the smallest value — a chart of
30 and 55 still starts at nought — and where something is negative the foot steps below it the same
way the top steps above: −20 and 60 give an axis from −30 to 70 in tens.

How much room a mark needs is the part that only the second probe could reach, since every chart in
the first is upright and 126pt tall. A label wants along its axis:

| axis | room per label |
|---|---|
| standing up | a tenth over its own type size — anything from 1.05 to 1.145 fits the measurements |
| lying down | three times it — anything from 2.88 to 3.15 fits |

and the axis takes as many steps as leaves room for one more label than it has steps, since a mark
is written at both ends as well as between — and never more than ten, however long the axis is.
The ten only shows itself on an axis long enough for eleven labels: `chart-area-scatter`'s
fifteenth page holds a chart of exactly one over a plot that would take eleven, and Word runs it to
1.2 in fifths rather than to 1.1 in tenths. That is why the same 47 that runs to 50 in fives up a
side runs to 60 in twenties along a foot of the same length, and why setting the labels in twenty
point rather than ten thirds the number of steps either way. Two of the fourteen pages exist only
to part the readings: a chart of millions divides its foot exactly as a chart of tens does, so the
room has nothing to do with how wide the numbers are; and the same chart set in twenty point divides
it into a third as many steps, so the room does grow with the type. All twenty-six come out label
for label as Word's.

The negative case also showed up something the positive ones cannot: the words under the bars go
beside the **nought** rather than at the foot of the plot, because that is where the two axes cross.
A chart whose bars all stand up puts the two in the same place; one with a bar hanging down does
not, and what hangs down hangs past its own label.

### Bars that lie down, and bars piled on each other

A bar chart is a column chart turned on its side, and almost nothing about it is stated: `barDir` is
the whole of what the format says, and everything that follows had to be measured from
`chart-bar-stacked`.

- The **categories run upwards**: the first is at the foot of the plot, not the top, which is the
  opposite of the left-to-right an upright chart uses.
- Within one category the **series run upwards too**, so of two clustered bars the second is the
  upper. Both reversals together are what makes a bar chart read the same way round as the column
  chart it is a turn of.
- The **value axis stays at the edge** it is drawn on — the foot — however far the categories move.
  The category axis crosses it at the nought, and its labels follow it: the last page of the fixture
  puts them 9.28pt to the left of the nought, three fifths of the way across the plot, and not
  beside the plot's own edge.
- **Gridlines** run the other way, up the plot rather than across it, and the one at the crossing is
  left out because the axis itself is drawn there. A chart with nothing negative leaves out the one
  at the foot of the scale for the same reason; one with something negative draws it and leaves out
  the nought.
- A **mark** on a lying category axis reaches to the left of it, and one on the value axis below —
  outwards in both cases, the same 3.1733pt.

**Stacking** is an overlap of a hundred and nothing else, so far as the width of a bar goes: two
stacked series across a 78pt category give a bar of 78 ÷ 2.5 = 31.2pt, which is what one clustered
series would have given, and Word draws exactly that. What changes is where each bar starts — at
where the last one ended rather than at the axis, with what rises above nought and what hangs below
it piled apart — and what the axis has to reach, which is what a category comes to rather than what
any one bar holds. Word runs the fixture's stacked page to 70 where the same numbers unstacked
would have stopped at 50.

Stacked **to the whole**, each bar is first taken as its share of its own category, and the axis
runs to exactly one — the single place the top of an axis is not a step above what it holds. The
labels are written by the axis's own number format, of which what is read here is what a chart
carries: how many decimal places to keep, whether to group the thousands, and whether the number is
a per cent.

One thing the negative page turned up that has nothing to do with lying down: a bar hanging below
nought is drawn **the other way about** — white, and outlined in black at three quarters of a point
— which is what `invertIfNegative` asks for, and asks for by default. Word draws it so even though
the series it belongs to asks for no outline at all.

All eight pages of the fixture agree with Word rectangle for rectangle within a quarter of a point,
which is the 1/300in Word rounds every edge it draws to.

### How a line curves, and where a pie sits

A line chart **curves through its points unless the series says not to** — the format's default is
smooth, which is not the obvious one, and Word writes `c:smooth` on every line chart it makes so
its own files never depend on it. The curve is a Catmull-Rom spline: each point is passed at a
slope of half the distance between its neighbours, the ends take the slope of their own segment,
and the Bézier controls sit a third of the way along those slopes. Every control point of the
fixture's curve comes out of that to the EMU — Word writes 266700 where the rule gives 266690.

The points themselves sit at the middles of the categories, where a bar chart's bars stand.

A pie is centred in its plot area and reaches the nearer pair of its edges: Word's export puts the
fixture's pie at the middle of a plot 216 by 172.8 with a radius of 86.4, which is half the shorter
side. Its slices begin at the top and run clockwise. A pie carries no axes, so a pie left to place
itself gets the bare eleven points on every side — the same margin a chart with no labels gets, and
Word draws it at exactly that.

Inside the frame, all four of `chart-line-pie` agree with Word on better than 99.9% of their ink.
The one thing left outside it is that Word clips a chart to its own frame, so the outer half of the
border it draws is cut away; nothing here clips, and that border straddles the edge instead. It
comes to a quarter of a point of halo round the outside of a chart.

### An area, and a chart of pairs

An area chart is a line chart with the space under it coloured in, and a scatter is the one kind
that has no categories at all. Both were measured from `chart-area-scatter`, nineteen pages of it.

- An area's corners sit **at the marks rather than between them**, so the first and last touch the
  ends of the plot: Word's four corners land at 162, 240, 318 and 396 across a plot running 162 to
  396. That is what `crossBetween="midCat"` asks for, and Word writes it on every area chart it
  makes; a line chart says `between` instead and keeps its points at the middles of the categories.
- The **category labels follow the points**, so the outermost two are centred on the plot's own
  corners and half of each hangs past. A chart left to place itself makes room for that half — the
  fifth page gives its right edge eleven points plus half of "Four" — and where a label is too wide
  for its category it **wraps**, which grows the foot by a line and the side by half of the widest
  line it came to. The nineteenth page, whose first category is nearly six times as wide as any of
  its numbers, lands within three hundredths of a point of Word both ways.
- Stacked, each area is a **band** rather than a shape hiding the ones behind: it runs along its own
  points and back along the series below it. Unstacked, they are drawn one over another in the order
  the chart lists them, opaquely — Word writes no transparency of its own, so a taller area behind a
  shorter one is simply hidden by it.
- A scatter is scaled **both ways**, and the foot is divided by the rule a lying axis uses rather
  than an upright one: three times the type size per label against a tenth over it. Its eighteenth
  page, left to Word both to place and to scale, divides a 320pt foot into eight and a 180pt side
  into six.

A **marker** is the one thing whose placing is Word's rounding rather than its arithmetic. A marker
of size s is drawn in a box of s rounded to the three-hundredth of an inch, whose corner is the
point less half that box rounded *down* to the same grid, and the shape itself sits half a
three-hundredth inside the box. So a marker of seven comes out 6.72 across and up to a third of a
point left of and above the point it belongs to. Four sizes and two shapes come out of that rule
exactly; on one point in four Word breaks the tie on the grid the other way, and what decides it is
not measurable from what is here.

A series that says nothing about its markers still gets them — in its own colour, outlined in it at
half a point, and seven points across where the series draws a line or six where it does not. Word
runs through **diamond, square, triangle and cross** for the first four such series, which is
measured; what it does with a fifth is Excel's old order and is not.

### What goes round the plotting

A title, a legend and the numbers written at the points are the three things a chart carries that
are not the plotting itself, and the first two take their room out of it. Measured from
`chart-title-legend-label`, nineteen pages.

**A title takes nine points and a line of its own type**, whatever that line comes to: at ten point
it takes 20.076 off the top of the plot, at eighteen 28.931, at twenty 31.146 and at thirty 42.216,
and two lines take two lines' worth. The line in question is the face as *Windows* reads it — for
Times New Roman 1.1074 ems against the 1.1499 a line of body text is set by — which is the same
split between the two pairs of metrics that runs through the rest of a chart. An axis title takes
the same nine points and a line off the side it names.

Where each then goes is measured once apiece: a chart's own title has its first baseline 7.43pt
below the top of the frame and is centred on the **frame**, while an axis title's box ends 12.5pt
inside the edge it belongs to and is centred on the **plot**. The one up the side is turned on its
end, reading upwards, and is drawn into the chart's own picture rather than set as a line of text —
turned text has no baseline to compare with an upright one's, so it is held to Word by ink instead.

**A legend** takes 11.8pt and a line along the top or the foot, and 15.118pt and its widest entry up
a side. Its key is a square 0.5492 of the type size across, the words beside it begin 0.8239 of the
size less 0.376pt from the key's own left edge, and the key sits that much again below their
baseline. Along the foot the entries are set 0.784 of the type size apart — except where one entry
is long enough that a seventh of it is more, which is what the four-series page shows and what
nothing here explains — and the whole block is centred a little right of the middle. Up a side they
are one to a line, 1.8083 type sizes apart, centred on the middle of the frame.

**A number written at a point** takes nothing from the plot and sits clear of what it names: past the
end of a bar by four and a half points and its own descender, inside the end by the same four and a
half and its ascender, and to the right of a point on a line by 8.5pt. What would overrun the top of
the chart is set against it instead. On a slice of a pie it goes out along the middle of the slice,
about seven tenths of the way to the rim — Word fits those to the slices by a rule of its own, and
its four come out between 0.684 and 0.711 of the radius and up to a degree and a half off the middle
of their slice, which is the one place on these pages where two points of disagreement are left.

Everything else agrees with Word to within 0.73pt vertically and half a point horizontally, and the
ink of a page agrees on better than 99.3% of it.

### Content wrapped in something else

A body holds paragraphs and tables, and a reader that walks it looking only for those two is right
about every document written by hand and wrong about most documents written by Word. Three things
wrap ordinary blocks:

- a **content control** (`w:sdt`), which Word puts round the cover page, the table of contents and
  every placeholder a template leaves to be filled in;
- a **compatibility alternative** (`mc:AlternateContent`), which offers the same content twice over;
- the old **custom XML** element (`w:customXml`), round whatever an older document tagged.

All three are unwrapped, wherever blocks are read: the body, a table cell, a running head, a note
and a text box. `content-controls` puts one of each on a page and names the line inside it, and
Word draws all of them in place with no more room between the lines than any other paragraph gets.
Where an alternative offers two branches Word draws the **choice** rather than the fallback — the
fixture's two branches hold different words so that its export says which — and that is the opposite
of how a *run*-level alternative is read here, where the choice may be a drawing this cannot read
and the fallback is what it is for.

This was found by asking the converter what it did with each, and it had been losing all of it in
silence. The test that should have caught it could not: it compared the text on the page against
the text in the *parsed model*, and a construct the reader drops is missing from both sides. It now
reads what the document says from the part itself, so the two sides come from different code, and
reverting the fix fails it.

### What a diagram is, and which half of it to draw

SmartArt is written down twice. There is what it means — points, the connections between them, and
a layout definition saying how points of that shape are arranged — and there is the arrangement it
last came to, kept beside it as a flat list of shapes at absolute positions with their geometry,
colours and text. The first is a language, a system of constraints and algorithms with a hundred
layouts written in it. **Word runs it afresh every time it opens a document**; every other reader
draws the cached arrangement, and so does this.

That is measured, not assumed: the seed for the `smartart` fixture carries a cache no layout would
produce — three boxes stepping down the frame — and Word's export shows three boxes in a row filling
it. `Word_lays_a_diagram_out_again_rather_than_trusting_the_cache` keeps that fact in the suite.

It also decides how a diagram can be held to Word at all. A hand-authored cache says nothing, since
Word throws it away; the only cache worth comparing is the one Word itself wrote, so the fixture is
a **real** document — the seed goes through Word, which rebuilds the diagram and saves its own
arrangement, and that is what gets rendered and compared.

Two things about diagram text differ from anything on a page. Its spacing is a percentage of a
line, where DrawingML's line is a flat six fifths of the type size rather than whatever the face
asks for — Word sets the fixture's paragraphs 15.6pt apart where 35% of the type size would be
12.6pt and 35% of six fifths of it is 15.1pt. And a word too wide for its box **comes apart between
its letters**: Word sets "Three" in a 67.84pt box as "Thre" and "e", where a page would let the word
overrun the margin whole.

What is left is a constant 3.1pt: every line of the diagram is where Word puts it across the page,
and every line the right distance below the one above it, but each box's block of text sits 3.1pt
high. The block is centred, so that is either a 6.2pt disagreement about how tall the block is or a
3.1pt one about where the first baseline sits inside it — and those cannot be told apart here,
because Word writes the cache itself and so chooses the type size, the line spacing and the
anchoring. Both readings fit every line. It is recorded as a known divergence rather than fitted
away.

### How large a watermark is set

A watermark is a word on a path, and the size the document gives it is a single point — the shape
type says the letters are to be fitted to the shape, and that is what decides how large they come
out. What is fitted is the **ink itself**, stretched to fill the shape less its own insets, and not
the box the face would set the word in. `watermark-fit-probe` says so seven times over: the same
rectangle of ink comes back whether the box is asked for DRAFT, for CONFIDENTIAL, for a short word,
for a word with a tail below the line, or for the same word in another face — and it is the box
less the tenth of an inch at the sides and half of that above and below that every text box has.

The letters are stretched to that box rather than scaled to it, so a word with a descender is
squashed to the same height as one without. Every page of the probe agrees with Word on better than
99.8% of its ink, and the diagonal watermark of the `watermark` fixture on 99.89%.

Two things about it are deliberately not Word's. Word's export turns the letters into outlines, so
its own file holds no watermark text at all and a reader cannot search for the word; this keeps it
as text, which is why the line-by-line comparison has to allow the two files a different number of
lines. And Word for Mac draws a watermark in the document's own face whatever face the document
asked for — the probe's sixth page asks for Times New Roman and gets Calibri — where this sets it in
the face that was asked for. That page is the only one of the seven that agrees on less than 99.8%
of its ink; it agrees on 98%.

### What washing a picture out does

The other kind of watermark is a picture, faded until the page can be read through it, and the
fading is two numbers on the image: a gain and a black level, both written in sixty-fourths of a
thousand. `watermark-washout-probe` holds the same bands of flat colour six times over at different
settings, and what comes out of each channel — everything in nought to one — is

    gain × in + (1 − gain) ÷ 2 + black × (1 + gain)

clamped at both ends. The gain is a contrast about mid grey: half a gain leaves grey alone and pulls
black and white halfway towards it. The black level is a brightness on top of that, and counts for
more when the gain is high, which is the part of this that is fitted rather than explained. Word
writes a gain of 19661 and a black level of 22938 for every picture watermark it makes — three
tenths of the contrast, and pale enough to read a page through.

Every band of every setting comes out within one part in 256 of Word's, including the two that
saturate.

### Where an old-style shape is drawn

A shape in the older `w:pict` spelling is drawn a little way down and to the right of where its own
size puts it, and how far depends on the weight of its outline. `vml-stroke-probe` holds the same
rectangle thirteen times over, varying nothing but that weight:

| outline | none | ¼pt | ½pt | ¾pt | 1pt | 1½pt | 2pt | 3pt | 4½pt | 6pt |
|---|---|---|---|---|---|---|---|---|---|---|
| offset | 0 | 0 | 0 | 0 | 0 | 2pt | 2pt | 2pt | 4pt | 6pt |

The offset steps in twos rather than growing with the weight, and it starts a whole point in: it is
the smallest even number of points that reaches a point short of the outline. The text inside moves
by half as much again, which is what the six point page of `vml-inset-probe` shows — its text sits
at the very edge of the box, where the inset alone would put it three points inside. The last three
pages of the probe rule out the two obvious alternatives: a rounded rectangle and an ellipse at the
same weight are offset identically, and two shapes on one line neither shift each other nor
themselves.

Why it steps in twos is not explained here. What is implemented is the rule the measurements fit,
and none of it shows in an ordinary document: Word draws a text box with a ¾pt outline, and
everything at a point or less is offset by nothing at all.

Two things about the same shapes were measured and are **not** implemented, both of them out of
reach of any ordinary document. The first is that an outline thicker than a point also makes the
shape's *line* taller, so what follows it sits lower — by 0.96pt at 1½ and 2 points, 1.92pt at 3,
4.08pt at 4½ and 5.04pt at 6, which follows neither the weight nor the offset the same shape is
drawn at. The second is that such a shape sharing a line with another nudges its neighbour by about
a point. `vml-stroke-probe` holds the first as a known divergence in
`TextPositionComparisonTests`, and `vml-shapes` holds the second: the two pages agree on 96% of
that page's ink rather than the 99% the newer spelling's shapes manage.

### Which part of a table style reaches which cell

A table style is unlike every other kind: what it says depends on where a cell is rather than on
what the cell says about itself. It can describe thirteen different parts of a table — the whole of
it, the banding across the rows and down the columns, the first and last rows, the first and last
columns, and the four corner cells — and the order in which those override each other was measured
rather than read. `table-style-conditional-probe` gives every one of the thirteen a different type
size, so the size Word draws a cell at names the format that reached it, and seven pages of tables
give the whole lattice at once. Two of the answers are not the ones the specification's ordering
would give:

- **Banding down the columns beats banding across the rows.** With both in force the rows leave no
  mark at all — every cell in the middle of the probe's first table comes out at its column band's
  size — so the fixture needs a page with the column banding turned off to see the row banding at
  all.
- **A first row beats a first column.** Where a style defines no corner formats, the cell where the
  two meet is drawn in the first row's size.

The rest is as expected: the whole table, then the banding, then the edge columns, then the edge
rows, then the corners, each overriding what came before. Two orderings could not be measured
because nothing makes the two formats meet in one cell — a last row against a last column, and the
corners against each other — and each follows the pattern of the pair beside it.

A table of one row has a **first row and no last one**, and a table of one column a first column
and no last one. The single row of a four-column table comes out in the first row's size, and the
cells at either end of it in the north-west and north-east corners rather than the southern pair —
which matters, because a one-row table is what a great many documents use for a banner or a form.

Three more things the same fixture settled. `w:tblLook` gates every one of the conditional formats,
the corners and the banding included: with it turned off, a table drawn by a style with all
thirteen comes out entirely in the whole-table formatting. Banding begins counting *after* the
first row or column where there is one, and runs on through the last one whether or not a format
for it exists. And a table style sits between the document's defaults and the paragraph's own style
in the cascade — a paragraph style used in a cell overrides the table style, and direct formatting
overrides both.

## Current scope

Implemented: cells merged down the page (`w:vMerge`), which behave as one tall cell — no rule is
drawn across the run, its shading covers all of it, and its text is placed over the run as a whole,
by its own vertical alignment. The rows a run covers keep the heights their own cells ask for and
the merged text runs down through them, so three lines merged across three one-line rows leave
those rows a line tall each; only what will not fit makes the run taller, and the last row of the
run takes all of it. A run reaching the foot of the page divides there wherever the break falls —
inside the row it begins in as readily as between the rows it covers — and Word rules the merged
cell at a page break although it rules none between a run's own rows, so both halves are closed
boxes. Breaking a table row across a page, at a line boundary inside its cells, with both
halves closed off by a full border box the way Word draws them — and moving the row whole instead
where it says `w:cantSplit` or where not even a line of it would fit. Vertical page alignment (top, centred, bottom, and justified — which spreads the
spare height between the paragraphs rather than between the lines, so a paragraph that wraps stays
whole), font subsetting for both kinds of outline. A TrueType face is numbered again from
nothing, so a document that used thirty glyphs embeds thirty rather than the three thousand the
face has; a CFF one keeps its numbering — renumbering it means rewriting what its charset says
about every glyph — and has its charstrings and the subroutines nothing reaches emptied instead.
Times New Roman goes from 676KB to 33KB, Hiragino Sans GB from 11.4MB to 497KB. Kerning, read from a font's GPOS table as well as the legacy one — Calibri has only
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
one says nothing), multiple text columns (evenly divided or individually stated, column breaks,
the rule down the gap where the document asks for one, and the last page of a section evened out
between its columns where a continuous break closes it), footnotes and endnotes (numbered in reference order, arabic for footnotes and roman
for endnotes unless the document says otherwise, and numbered again from the beginning on every
page or in every section where the section asks for it; a footnote goes to the foot of the page its
reference lands on — or under the last line of text on it, where the section asks for that — and
takes that space out of the body above it, dividing between that page and
the next where it is too long for the room left under it, an endnote carries on after the
body like ordinary content — or after each section where the document asks for that — and both are
ruled off by the separator), hyperlinks (external addresses as clickable regions, internal links to bookmarks
anywhere in the document, with the regions placed and padded the way Word places them), headers
and footers (per page, with separate first-page and even-page variants, and
fields evaluated), fields — page numbers (PAGE, NUMPAGES, SECTION,
SECTIONPAGES, PAGEREF, each following a section that begins its numbering again), what a document says about itself (AUTHOR, TITLE, SUBJECT, KEYWORDS,
COMMENTS, LASTSAVEDBY, CREATEDATE, SAVEDATE, PRINTDATE, DOCPROPERTY, FILENAME), counters (SEQ, with
its restart, repeat and format switches), references to a bookmark's text (REF), running heads
(STYLEREF), literal text
(QUOTE), and the clock (DATE, TIME) — each spelled the way its `\*` switch asks, in arabic, roman,
letters, ordinals, words, hex or dollars, and cased by Upper, Lower, FirstCap or Caps, with a
`\@` picture deciding how a date reads, lists and numbering (decimal, letters, roman and bullets, nested levels with
independent counters and multi-level templates, hanging indents), images, inline and floating (PNG — interlaced or not — GIF, BMP, TIFF and EMF all read from scratch, JPEG passed through untouched — and decoded, in every
form including progressive, arithmetic and four channels of ink, where a TIFF holds one; the
four-channel pictures a printing press wants, as either a JPEG or a TIFF; transparency via a soft mask; square, top-and-bottom and no-wrap text flow around anchored
pictures), charts of columns, bars, lines, pies, areas and scatters (the plot area where the chart places it
or, where it does not, worked out from the room the labels need — including the room a label that
has to wrap takes; bars standing or lying, clustered, stacked or
stacked to the whole, sized by the gap and the overlap between them, a line curved through its
points the way Word curves one or straight where it says so, areas filled down to the axis or
banded one on another, markers of nine shapes at the size and on the grid Word draws them, a pie
centred and divided clockwise from the top; gridlines, both axis lines and their marks, and the
labels along each axis in the number format it asks for, a title over the chart and one on each
axis, a legend on any side, and a number at every point,
read from the numbers the chart part caches, with the axis scaled and the plot placed the way Word
does both where the chart leaves them to be worked out), diagrams — SmartArt — drawn from the arrangement the document keeps of them (every shape
with its geometry, its fill and outline in colours named outright, by theme slot or as percentages,
and its text laid out into the rectangle the diagram set aside for it and set at its top, middle or
foot), watermarks of both kinds (a word set across every page of a section, behind the text,
stretched to fill the shape that holds it, turned, and painted see-through — which is a graphics
state of its own, since a PDF carries transparency there rather than in the colour; or a picture,
washed out to the contrast and brightness it carries), text boxes and the shapes they are a kind of, in both spellings (rectangles, rounded rectangles, ellipses
and triangles drawn as themselves and every other preset geometry as the rectangle it is bounded
by; filled and outlined in a colour named outright or taken from the theme; in the line of text or
anchored with the text flowing round them; and holding a document of their own — paragraphs and
tables, laid out into the box, clear of its insets and its outline, and set at its top, middle or
foot as it asks. A shape arrives wrapped in the compatibility element that offers the same drawing
twice over, once for a newer reader and once for an older; the newer is read and the older passed
over, so it is drawn once), table styles (all thirteen conditional formats — the whole table, the banding across
its rows and down its columns, its first and last rows, its first and last columns and its four
corner cells — resolved through the style's `basedOn` chain, gated by the table's `w:tblLook` in
either spelling, and giving a cell its borders, its shading, its margins, its alignment and the
formatting of the text inside it, with everything the table declares for itself winning over all of
it; a `TableGrid` table from a real document is ruled where Word rules it, which before this it was
not, having no borders drawn at all), tables (fixed and autofit column sizing, horizontal spans, borders, shading,
cell margins and vertical alignment, rows kept whole across page breaks), page size and margins
from `sectPr`, paragraphs and runs, `xml:space` handling,
line and page breaks, line breaking by the Unicode algorithm (so the scripts written without
spaces — Chinese, Japanese, Thai, Lao, Khmer, Burmese — wrap where Word wraps them), tabs
(left-aligned stops), font family via theme resolution, size, bold,
italic, underline, strikethrough, colour, caps, super/subscript, character spacing and scaling,
alignment including justification, indents including hanging, spacing before/after with
contextual spacing, line spacing (auto/exact/at-least), pagination, real font metrics with
`.ttc` support, and Type0/CIDFontType2 embedding with a `ToUnicode` map so text stays selectable.

Six kinds of chart are drawn: **columns, bars, lines, pies, areas and scatters**, the bars and
areas clustered, stacked or stacked to the whole. What is there: the plot area, the bars or the
line or the slices or the filled areas, the markers at a series' points, the gridlines, the two
axis lines and the marks along them, and the labels along both axes whichever way round they run —
with the numbers read from the cache the part carries rather than from the workbook stored beside
it, and with the axes scaled and the plot placed the way Word does both where the chart leaves them
to be worked out; a title over the top and one on each axis, the one up the side turned on its end;
a legend on any of the four sides; and a number written at every point, in the format the chart asks
for and where the kind of chart puts it. What is not: bubble, radar, doughnut and stock charts,
trendlines and error bars, and a legend or a title placed by hand rather than by side.

Both spellings of a shape are read: the `w:drawing` Word writes today and the `w:pict` it wrote
before 2007 and still writes for a watermark. The older one says in a CSS-like `style` attribute
what the newer says in elements — its size, its position, what it is anchored to — names its
geometry by the element rather than by an attribute, and defaults its fill to white and its
outline to three quarters of a point of black. Where a document offers both, in a compatibility
wrapper, the newer is read and the older passed over so the shape is drawn once.

What a shape does not do yet: turn (a rotated shape is drawn square), resize itself or its text to
fit the other (a box holding more than it has room for overflows, which is what `noAutofit` asks
for and what Word does with that setting), fill with anything but one flat colour, or carry a
shadow.

Watermarks are drawn, of both kinds: the word, in the face and colour and half-solidity it asks for,
turned the way it asks to be turned; or the picture, washed out to the gain and black level it
carries. Both go behind everything else on every page of their section. A picture in a running head
is read from that part's own relationships, so a header and a body may number their pictures alike
— which they routinely do, both calling their first one `rId1` — without either drawing the other's.

Table autofit is the one piece here that approximates rather than reproduces. Word's algorithm is
undocumented; ours measures each column's minimum (widest word) and maximum (unwrapped) width and
shares out the available space between them. It reproduces both behaviours that were measured —
content-width columns when the table fits, and a table filling the text area exactly when it does
not — and agrees with Word to 0.16pt on `table-autofit-probe`, but it is not derived from the real
algorithm the way the paragraph rules are.

How far inside its edge a cell starts its text was settled by measuring rather than by reading, and
is stranger than it sounds. `table-inset-weights-probe` holds the same one-cell table fifteen times
over — border weights from nothing to six points against no margin, then margins against a fixed
border, then no margin element at all — each on a page of its own so that no table's height carries
into the next one's position. Word's export of it gives three rules, and none of them is the
addition that would be guessed:

- Across, the inset is the greater of the cell margin and half the border, not their sum. Half a
  border falls inside the cell and half outside — Word's own border rectangles straddle the margin
  at every weight — so text starts at the border's inner edge unless a margin reaches further in.
- Down, the whole border is cleared rather than half of it. A six point border pushes the first
  line six points down and three points across.
- Declaring no cell margin is not the same as declaring one of zero: Word puts half a point in a
  table that says nothing about the matter. The familiar 108 twips comes from the built-in
  `TableNormal` style, which a hand-written document does not have, but what is left is not
  nothing. Word rounds every position to 1/300 inch, so the true value is somewhere between seven
  and twelve twips; ten is used.

A field that depends on where it falls cannot be worked out while the page it is on is still being
filled, so a document holding one is laid out twice: the first pass records the page each field
landed on and how the pages divide between sections, and the second uses it. Word settles its own
page numbers the same way, and like Word this converges rather than being exact — a field whose
text changes length between the two passes could in principle move to another page and be a page
out. The second pass is only run for a document that needs one.

Word itself recalculates only these page-dependent fields when it exports, and leaves every other
field showing whatever it last computed, which is what the fields fixture's reference had to work
around: it is exported with its fields updated first, and `tools/make-reference-pdfs.sh` names the
fixtures that are. Anything this converter cannot work out keeps its cached result, which is the
honest answer — showing nothing would lose text the document has, and guessing would show something
it never said. COMPANY and MANAGER are among those: the Word this was measured against does not
evaluate them either.

A running head is the one field whose answer depends on where the pages fell rather than only on
what the document says, and Word's rules for it were read off its export of the `styleref` fixture:
a header shows the first paragraph of the named style on its page, a footer looks *down* its page in
the same way rather than up it, `\l` asks for the last one on the page instead, and a page holding
none of that style carries the last one before it — which is what walks a chapter title through the
pages under it. In the body the field looks backwards to the nearest one above it, and only where
there is none does it look forward. The style is named rather than identified: Word answers
`STYLEREF Heading1` with an error telling the reader to apply the style, so an id is not a name
here even where it looks like one.

A table of contents is the one field whose answer is a run of paragraphs rather than a few words,
and it is worked out again rather than read back: a stale table is as wrong as a stale page number,
and a document that has never had one built has nothing to read back at all. The headings are
gathered by the outline level their style stands at — `\o "1-3"` says which levels, `\t
"Style,Level"` names styles outright, `\n` leaves the page numbers off — and each entry is set in
the `TOCn` style named for its level, with a tab out to that style's right stop and the page the
heading landed on. A document defining no such styles gets an indent and a dotted leader of its own
so that the numbers still line up.

Two details of Word's own come from measuring its export of the `toc` fixture rather than from
reasoning. Tab stops are measured from the margin and not from the paragraph's indent, so the page
numbers of a second-level entry line up with a first-level one's rather than sitting eleven points
further out — which was wrong here until this fixture showed it. And the paragraph the field closes
in outlives it, empty: Word leaves the mark of it on a line of its own below the entries, set in the
document's default rather than in a table-of-contents style, which is the extra line between a table
of contents and the first heading under it.

An index is written in two halves, and both are implemented. Where a term belongs, the document
carries an `XE` field that draws nothing at all — it is there to be found, not read — and where the
index goes, an `INDEX` field gathers every one of them, sorts them, and lists each term against the
pages it was marked on. A term written `Engine:analytical` is a subentry and reads as `analytical`
under a heading of `Engine`, indented by its `Index2` style; a page marked twice over is one page
number; `\h` asks for a line holding the letter each group begins with, `\e` and `\l` say what goes
between a term and its pages and between one page and the next, `\t` shows something else in place
of the pages ("see Engine"), and `\f` keeps two indexes in one document apart.

A document written for a mail merge does not carry its own data: it names a source — a
spreadsheet, an address book — that only the machine it was written on can reach. Converted as it
stands, its fields show what Word shows for the same document, each field's own name in guillemets
with whatever `\b` and `\f` ask to be printed around it, so a letter reading `Dear «Title»,` reads
that way. Converted with a `MergeRecord` given to it, the same document reads as the letter itself
— and the text around a field then prints only where the field has something to print, so an empty
middle name takes its brackets with it. MERGEREC and MERGESEQ number the record, and the fields
that carry a merge from one record to the next (NEXT, NEXTIF, SKIPIF) draw nothing, which is what
Word draws for them.

The two fields that work something out rather than look it up are the last of them. IF compares two
things and chooses between two pieces of text — numbers as numbers, anything else as text without
regard to case, and the text an equality is asked against may hold `*` and `?` wildcards. A formula
field is an equals sign and an expression: the five operators and their precedence, brackets,
comparisons, percentages, and the functions Word knows (SUM, PRODUCT, AVERAGE, COUNT, MIN, MAX,
ABS, INT, ROUND, SIGN, MOD, AND, OR, NOT, IF, TRUE, FALSE, DEFINED). In a table it reads the cells
around it, by direction or by name.

Three of its answers were measured rather than reasoned about, and none is what would be guessed. A
picture's `#` reserves a *space* where it has no digit to show, so five against `$#,##0.00` comes
out as `$   5.00`. A direction reads only as far as the numbers go — a column of 10, "n/a" and 3
sums to 3 from below it, not to 13 — while a range named outright reads all of it and passes over
what is not a number, so the same column averaged as `A1:A3` is 6.5 rather than 4.33. And a formula
with no picture reads to two decimal places with the zeros at the end dropped: 10/3 is 3.33, 10/4
stays 2.5.

Pictures come in as GIF, BMP, TIFF, PNG or JPEG. Only JPEG passes through untouched — PDF's own
image filter is JPEG, so decoding and re-encoding it would cost quality for nothing — and the rest
are unpacked to samples by decoders written here: a bitmap of one to thirty-two bits a pixel,
written from the foot up or the top down and run-length packed or not; a GIF through its colour
table, interlaced or not, with the colour it treats as transparent becoming the mask a PDF carries
separately; a TIFF at either end, in grey, colour or a palette, written in strips of rows or in
tiles, with its channels together or each kept apart, packed with nothing, LZW, PackBits, Deflate
or one of the fax encodings.

The two layouts are the format's other way of dividing a picture up. Tiles are rectangles rather
than rows, each written at the full size the tags declare however little of the picture it covers,
so the ones along the right and the foot carry padding that has to be left behind. Kept apart, the
channels are three pictures of one sample each rather than one picture of three, laid over one
another at the end. Both were written and read back through `sips` as well as here, which is what
said a tile's sides must be multiples of sixteen: it refused a file of eight-by-four tiles that
this read quite happily.

A fax is not written as pixels at all. A page of black on white is sent as the lengths of the runs
its lines fall into, in a Huffman code the standard fixes rather than one built from the page — and
a line may be written against the line above it instead, saying how far each change of colour has
moved rather than where it is, which on a page of text is far shorter. All three encodings are
read: lines written on their own, lines written either way with a bit each saying which, and lines
written against one another throughout.

Getting that right took the two-way check further than anywhere else here. The tables are large and
mechanical, so a file is written with the library's own tables and handed to macOS's `sips` to
read: if a single code were wrong it could not read it. Then the same file is read back here. The
first version of the writer took the easy way and wrote every group 4 line in full, spelling out
its runs — legal, and passed. Rewriting it to write lines the way a fax actually does, against one
another, immediately produced a file `sips` read perfectly and this one got 171 pixels of 480
wrong: the reader was starting each line at its first pixel where the standard starts one pixel
before it, so a change at the very start of a line could never be found. No amount of round-tripping
against the easy encoding would have shown it.

An interlaced PNG is not one picture but seven, each a coarser or finer sieve of the whole — the
first every eighth pixel of every eighth row, the last every pixel of every other row — and each is
written as an image in its own right, with its own rows and its own filters over them. So each pass
is unfiltered on its own and its pixels are then put where they belong, a few bits at a time where
four of them share a byte.

A picture written with sixteen bits a sample keeps them, in a PNG or in a TIFF. A PDF carries
either precision, so reducing one to eight would throw away exactly what it was written to keep. A
PNG and a PDF both write the bigger half of a sample first, so those two bytes go through as they
lie; a TIFF writes them the way round the rest of its numbers go, which has to be read from the
file rather than assumed — a sample read from the wrong end is still a picture, only the wrong one.
The single thing still reduced to eight bits is the colour table of a TIFF written through a
palette, where the index is what the depth describes and the table is a few hundred entries whose
lower halves no document has ever needed. What says the halves have
not been transposed or the precision misdeclared is the page itself: a picture written at sixteen
bits and described as eight comes out as noise, so the check is that a reader which shares nothing
with this one draws the colour that went in.

Reading a format is easy to do nearly right, so these are checked two ways. Files whose every pixel
is known are built by the tests and read back — which is the only way to reach a bitmap written
upside down or a GIF written in four passes — and then the same picture is turned into each format
by macOS's own `sips`, and by `tiffutil` for each of the TIFF packings, and read back again: what
comes out has to be what the PNG it was made from holds, to the sample. Both found real faults. A
bitmap's height is at a different offset in the modern header than in the old one, and a TIFF
written big end first keeps a small number in the *high* half of the four bytes its tag reserves.

A metafile is not a picture at all but the record of one being drawn — move here, line to there,
fill with this, write that — so reading one is an interpreter rather than a decoder, and what comes
out stays a drawing all the way to the PDF, whose own operators write it out again. That keeps a
chart sharp at any size a reader looks at it, and it keeps the text inside one selectable. What is
handled is what a picture in a document is made of: paths and the shapes that are shorthand for
them, the pens and brushes that colour them, the fonts and the text, and the bitmaps a drawing can
carry. What is not is the rest of an interface built to drive a screen: raster operations, clipping
regions and palettes.

A metafile written by anything modern carries the same drawing twice — once in those records, and
once in the newer GDI+ ones that travel inside their comments, a format smuggled through a format.
Both are read, and where a file has the newer ones they are what draws it. That is what they are
for: the old records beside them are a copy left for readers that have never heard of the new, and
where the two differ at all it is the new half that is the fuller. They are read in the one order
the file puts them in, because the old records are not always only a copy — a file may hand the
drawing back to them part way through, for something the newer interface had no way to record, and
says so where it does. From there the old records draw until the newer ones resume, which is what
the specification asks for and what the file means.

Preferring them used to be impossible to justify, and the reason is worth keeping. Word for Mac
renders classic metafile records and draws nothing whatever for EMF+ ones — its export of a file
holding only those is a blank space — so there was no second implementation here to read EMF+
against, and everything else in this project is measured against Word. What settles it is that a
file carrying both carries one picture twice: Word draws the old half of the metafile fixture and
this draws the new, so comparing the two pages compares this reading of EMF+ against Word after
all. The drawing's text lands within 0.07pt across and 0.12pt down of where Word puts it, and the
two pages agree on 97% of what is covered and what is left as paper — the rest being the edges
along which no two renderers ever agree. That is the same standard the rest of the metafile work is
held to, and it is now the newer records being held to it.

Its text needed one measurement. A drawing says where the *top* of its text goes and a PDF says
where the baseline goes, and the distance between them is the height of the characters themselves:
the em less what hangs below the line, with the leading above them left out. Word's own rendering
of the fixture puts the baseline 10.98pt below the point the record names, at 14pt Times New Roman,
and the em less the descent is 10.97pt of it.

A drawing is also the one thing here that cannot be checked by reading text positions out of a PDF:
a chart could be drawn upside down and the comparison would not notice. So `tools/rasterize.swift`
draws the page with macOS's own PDF reader and the pixels are looked at — which is how the drawing
in the fixture is known to be where it was put, in the colours it was given. It found a real fault
in the *fixture* rather than the reader: a metafile says how big it is twice over, in the frame it
declares and in the resolution of the device it was recorded for, and the test's writer had been
writing the two at odds. Word believed one and this believed the other, and both were right.

A TIFF may also hold a JPEG rather than pixels, and that one is not decoded at all: a PDF carries a
JPEG as the file it already is, so the work is putting the file back together rather than taking it
apart. A TIFF divides one in two ways — the older keeps the whole file in a tag of its own, and the
newer keeps the tables every scan shares apart from the scan itself, so that a picture in many
strips need not repeat them, which makes the file the tables without their end followed by the scan
without its beginning. What comes out is handed to `sips` to read, because a file put back together
wrongly still parses as far as its header: reading its size back would prove nothing.

A picture divided into several JPEGs, one to a strip, is the one case that has to be decoded: the
strips are separate files with nothing in common but the picture they are parts of, and a PDF has
no way to be handed several of them as one image. So there is a baseline JPEG decoder here after
all, used for that alone — a JPEG holds not the picture but a description of it, each block of
eight by eight pixels written as how much of each of sixty-four waves it is made of, and reading
one is that in reverse. Two decoders never agree to the sample, since they round the same sums
differently, but ours and the one macOS uses agree to within six levels of 255 on the same file,
and a strip put in the wrong place would be out by far more than that.

A JPEG's numbers may be written all at once or a little at a time, and both are read. A sequential
file gives every wave of a block before moving to the next; a progressive one gives the coarsest
waves of the whole picture first and returns for the rest, and may send the high bits of a number
in one pass and its low bits in another — so the numbers are gathered and turned into pixels only
once the last pass has been read. A progressive file is where a JPEG is most easily read *nearly*
right: a pass misread leaves a picture that is still a picture, only softer or blockier than it
should be. So these are tested against real ones rather than any this could write — macOS ships
several, written by encoders that had no idea this existed — and read against its decoder they
agree to three levels of 255, mean under a tenth, at sizes up to 2048 square.

A JPEG may also be coded arithmetically rather than by code tables, which almost nothing writes —
the method was patented for most of the format's life — but files exist and there is no reading
round one. Nothing here recovers from a mistake: Huffman codes resynchronise at the next symbol,
whereas an interval narrowed by the wrong probability is wrong for ever after, so a decoder of this
is either right or produces noise. That makes it testable in a way little else is. Recoding a JPEG
from one entropy coding to the other changes not one number in it, so the same picture is written
both ways by an encoder that shares nothing with this, and the arithmetic file has to decode to
what the ordinary one decodes to — not close to it, equal to it, sample for sample. It does, for
the sequential and progressive forms, with and without restarts. Reading a whole picture correctly
is not evidence that the hundred and thirteen probability states are right, it is proof of it,
which is worth having: two columns of that table transposed reads eleven decisions correctly and
then quietly falls apart.

A picture bound for a printing press holds four channels rather than three — not the light a screen
adds up to a colour but the ink a press lays down to take light away, and the fourth is black
because the first three together make a muddy brown rather than black. Both a JPEG and a TIFF may
hold one, and both are read. The catch is which way up: Adobe's tools write such a JPEG with nought
standing for all of an ink rather than none of it, and every one in practice is one of theirs.
Nothing warns a reader but a marker beside the picture. Read without noticing, the page comes out
in exactly the wrong colours, which is easy to do and — in a document of photographs — not always
easy to see. What is decoded here is turned back as it is read; a JPEG passing through untouched
cannot be, so the PDF is told instead, and told only where that marker says so.

That is the half no amount of reading samples can settle, so it is tested by drawing the page:
a picture of known inks goes into a document and macOS's own PDF reader draws it, and each quarter
has to come out the colour its ink stands for. The inks were chosen so that no two channels hold
the same value, which is what says they arrive in the right order as well as the right way up. A
TIFF of a press's own inks rather than the four is reported rather than drawn in colours that are
not its own.

Two things have no second opinion behind them. The older way of holding a JPEG inside a TIFF:
`sips` will not read a file written that way at all, so what is tested is that the JPEG comes back
and no more. And the four-channel files whose colours were turned into brightness before coding —
nothing installed here writes one, so that path is transcribed from libjpeg's own conversion rather
than checked against a file, and it is the one thing in these formats that has never met a real
example.

A note too long for the room left under the page its reference falls on is divided rather than
moved: a note belongs to the page its mark is on, so what will not fit goes to the foot of the page
after. Where it divides was read off Word's export of `footnote-split-probe`, and it is simpler
than it looks — the note takes everything left under the line that refers to it, the body stops
there, and the rest carries over. Nineteen of that note's twenty lines fit, which is what this
produces, with every line of both pages within half a point of Word's.

Two things follow from it that are worth stating. The rule above a carried note is drawn right
across the measure rather than the two inches drawn above a note that begins where it stands, which
is Word's way of saying without words that what follows is the end of something; the document keeps
that second rule as a note of its own, in the same way as the first. And a note may outlast the
document it belongs to — one referenced near the end and long enough to fill several pages has no
body text left to carry it — so pages are made for the rest of it, holding nothing else. Word does
the same, which `footnote-overrun-probe` is there to have asked: its second page has no body at all
and the last thirty-seven lines of the note at the foot of it.

A section of columns closed by a continuous break has its last page evened out: the columns come to
much the same depth rather than the first being full and the last empty, which is what a continuous
break is usually inserted to do. A section closed by a break to a new page is not evened out, and
neither is the last section of a document — `columns-balanced` holds all three cases and Word's
export of it says so.

Where the columns divide had to be measured too, since the obvious rules disagree. Word divides
thirty-five lines eighteen and seventeen, and ten lines five and five: a column takes lines while it
is still short of the depth the content is to be divided at, so the line that reaches that depth is
the last one in. Rounding either way gives one of those two answers and not the other.

In a section of columns a note goes under the column its reference is in, set to that column's
measure and ruled off by a separator of its own. Each column keeps its own area, so what one column
gives up for its notes is not taken out of the next: in `footnote-columns`, whose first column
carries two notes and second one, the columns stop 13.4pt apart, and that difference is what says
the space comes out of the column rather than the page.

A section may also ask for its notes under the last line of text rather than at the foot of the
page. On a page whose text reaches the bottom margin the two are the same place, which is most
pages; on one whose text stops early — the last page of nearly every document — the notes come up
with the text, and `footnote-beneath-text` is a document of exactly those two pages.

That fixture answered a question nobody had asked, and corrected this. A line carrying a reference
never moves to make room for its own note. `footnote-carry-probe` puts a reference on the very last
line a page has room for and Word keeps the line there, squeezing the whole note in beneath it;
the other fixture puts one where there is no room left at all, and Word still keeps the line and
carries the whole note to the next page under the wide rule. This used to move the line instead,
which is the obvious way to keep a note with its reference and moves body text Word leaves alone.

A section may begin the page numbering again, which is what a document with a preface does, and at
a number of its own rather than necessarily at one. What follows the restart and what does not was
read off Word's export of `page-numbering-restart`, a document of three sections whose footer counts
pages four ways at once: the page number follows it, the total counts the document through
regardless, and a reference to a page names the number that page is printed as rather than where it
stands. The properties on a section break describe the section it closes rather than the one it
opens, which is what makes a fixture for this worth writing rather than reasoning about — the first
number stated belongs to the pages before it.

Endnotes gather at the end of the document, or at the end of each section where the document asks
for it, which is what a book of chapters does with them. Each group is written where its section
stops, before the break that opens the next one, so the notes of a chapter belong to the pages of
that chapter.

Where the instruction lives is the reverse of everywhere else, and cost a fixture to find out.
Every other thing about how a note is set is read from the section; this one Word reads from the
settings part and nowhere else. A document asking for it in its sections alone — which is what the
format's own reading suggests, and what this fixture was written as — comes back from Word with
every note at the end regardless. Word's own writer puts it in both places, which is what says
which of them it believes: setting the option through Word itself and reading back what it wrote is
how that was settled.

Where a document numbers its notes again from the beginning, two things had to be measured rather
than read. The first is where the instruction lives: the format allows it in the settings, as a
default for the whole document, and in each section's properties. Word reads only the section. A
document asking in its settings alone for its notes to begin again on every page comes back from
Word numbered straight through, so that is what happens here, and there is a test that says so.

The second is per-page numbering itself, which cannot be settled while the page it depends on is
still being filled — the line carrying a mark may yet move to the next page and take its number
with it. So it is done the way page numbers are: the first pass records the page each mark landed
on and the second numbers from that, converging rather than being exact for the same reason. Per
section needs none of that, since a mark is composed inside the section it belongs to. Endnotes
restart by section too, and are still gathered at the end of the document, so their numbers repeat
down the one list — which looks wrong until you check, and is exactly what Word does with the same
document.

Not yet: for pictures, nothing a document holds, and nothing left of these formats at all. What
remains elsewhere is Hangul, whose syllables are composed rather than shaped, and the one kind of
Apple attachment that names points on the outlines themselves rather than in a table — no face on
this machine asks for it.

A line of Hebrew or Arabic is not a line drawn backwards. Text is stored in the order it is read
and drawn in the order it appears, and the two part company the moment a line holds both
directions, which nearly every real line does: a number is written left to right whatever is around
it, and so is a Latin name inside a Hebrew sentence. The Unicode bidirectional algorithm decides
which way each character runs and what order the line is drawn in, and it is implemented here in
full — the paragraph rules, the explicit embeddings and isolates, the weak and neutral characters,
the bracket pairs, the levels and the reordering.

The tables it reads are generated from the Unicode character database rather than written, by
`tools/make-bidi-tables.py`, because they are a hundred thousand answers and any of them typed by
hand would be a chance to be wrong. The library carries no dependencies, so the output is committed
as source.

This is the one part of the converter with a reference implementation to hand, and it is checked
the way that deserves: GNU FriBidi implements the same standard and shares nothing with this, so
fifteen hundred lines built at random out of Hebrew, Arabic, Latin, digits, brackets, marks and
directional characters go through both and are compared level for level, and another eight hundred
are compared on where every character of them ends up. They agree on all of it. The comparison
found two real faults while it was being written — the backward searches of two rules end on what
a sequence sits after and not only on a character, and the formatting characters count as
whitespace for the rule that resets the end of a line — and a third when the drawing order was
compared: a mark drawn on a letter has to be put back after it when its run is turned round, which
is a rule that matters to anything that draws rather than merely reorders.

Hebrew is laid out with all of that behind it. A paragraph that says `w:bidi` begins at the right
margin; the words of a line are placed in the order they are drawn rather than the order they are
stored; a word that runs right to left is drawn from its own far end, with the marks kept on the
letters they belong to and the brackets facing the way the reader is going.

A run is stored in the order it is read all the way to the writer, and turned round there, as
glyphs. That is not a detail of where the reversal happens. Which letter joins to which, which mark
belongs to which letter, and which letters may be written as one shape are all questions about the
text, and a shaper handed a word backwards answers all three backwards. What can be turned round
safely is the glyphs, because each carries its own advance and its own offset and both are measured
from where that glyph itself begins. What has a direction of
its own keeps it: a number inside a Hebrew sentence reads as it was written, and so does a Latin
name. Hebrew inside an ordinary left-to-right paragraph is turned round too — which way a paragraph
runs says where its lines begin, not which way its characters go.

Word's export of the `hebrew` fixture says the lines begin where Word begins them, to within a
hundredth of a point, in paragraphs running both ways and with numbers, Latin, brackets and
punctuation inside them. What that comparison cannot say is what the lines *say*: Word writes a
line of Hebrew as many runs, and encodes some of them as pairs whose map back to characters gives
the two the other way round, so the text this reader recovers from Word's file is not quite the
text Word drew. The drawn order is checked instead against the algorithm's own answer, and that is
checked against another implementation of the standard.

A font is chosen per character rather than per run, because most fonts hold very few of the
characters there are: Arial Hebrew has no Latin letters at all, Times New Roman has no Japanese,
and a document written in Hebrew with an English name in it names one font for both. Asked for a
character its face has not got, the run is set in two — the rest of it where it was, and that
character in a face that can draw it. A converter that does not do this loses text the document
plainly holds, without failing and without saying so.

Which face is borrowed is a matter of taste rather than of correctness, and the taste is the
document's: the substitution chain a missing family already walks is walked again, and only where
none of those can draw it is everything else tried, in a fixed order so that two machines holding
the same fonts do not disagree. Where nothing at all can draw it the run keeps its own face; the
document is then short of a glyph, which is the truth, rather than short of a page.

That is also why `font-fallback` is compared to Word on where its text goes rather than on how wide
it is. The lines begin exactly where Word begins them, and every character a font could not draw is
on the page; the borrowed faces are not the same width as Word's, because Word's choice is its own
and not discoverable from the document.

A mark is drawn where the font says it goes. An accent, a Hebrew vowel point, an Arabic dot: none
of them has a place of its own and none can be drawn by advancing the pen. The font gives the mark
an anchor and the letter an anchor, and the two are brought together — a movement of the mark alone,
which the pen does not know about, so the letter after is set as though the mark were not there. A
mark drawn on a mark is placed against that mark rather than the letter, which is how a letter
carries two.

Where no movement along the line can express it — a point below a letter, an accent above one —
the text is raised for that glyph and put back down after, which is the only thing a PDF has to say
it with.

There is a reference implementation for this too, and it is used the same way. HarfBuzz shapes text
for nearly everything that draws it and shares nothing with this; asked for the same characters in
the same face it gives the same glyphs, the same advances and the same offsets, to the design unit,
for Latin accents, Hebrew points and pointed Arabic alike — including the marks written over a
shape that stands for four letters. Both are asked for the run in the order it is drawn, so the
numbers are compared as they are rather than translated first.

For the Indic and South-East Asian scripts it is used with one thing to be careful about. Several of
the faces macOS ships for them carry two complete descriptions of how to shape them: the OpenType
tables every other platform reads, and Apple's own state tables. HarfBuzz prefers Apple's wherever a
font has them, so asking it about one of those faces answers a question about a table this converter
does not implement and Word does not read either. The tests therefore ask it about a copy of the
face with those tables taken out. The two mostly agree; where they do not, the difference is worth
seeing rather than hiding — Khmer Sangam MN's OpenType tables write a consonant and its vowel as a
shape plus a blank, and its state machine deletes the blank instead.

Eighty words across sixty-two scripts are compared that way, glyph for glyph, advance for advance and
offset for offset, and agree on all of it. Word's exports of the three fixtures agree about the page:
every line begins exactly where Word begins it, and no line is more than a tenth of a point wider or
narrower. What those comparisons cannot say is what the lines say — a shaped syllable is one glyph
standing for several characters, and Word's file maps them back to whatever code the glyph happens to
sit at, so a line of Devanagari comes out of it as "नम#$".

For Apple's tables the same comparison holds: twenty-six words in ten faces for the shaping and
thirteen more in eleven for the placing, and Word agrees about the page on every line of the fixture
to four hundredths of a point. Three things are kept out of it because Word does something else with
them — or nothing at all. Asked for a line of Malayalam in a face whose positioning is Apple's, its
export holds nothing where the line should be. Asked for Thai in Thonburi, it draws the line in a
font of its own. And for one Devanagari cluster its reading of the same table comes out two points
wider than HarfBuzz's. Which of the two Apple's own engine agrees with is not a question this
machine can answer, so the difference is recorded rather than resolved.

Reading those faces turned up a fault of ours that had nothing to do with shaping. They carry their
family name several times over in several languages, and the first record of the right kind is not
the English one: Gujarati MT calls itself ગુજરાતી એચટી. A document naming "Gujarati MT" then matched
nothing at all, and its text was drawn in whichever face was borrowed for it. The English record now
wins.

For the universal engine the Word comparison covers one script rather than seventy, and that is
Word's limit rather than a choice: asked for Tibetan, Javanese or Cham on this machine, Word draws
the letters side by side without stacking or reordering anything. Sinhala it draws properly, and
agrees with this converter to four hundredths of a point on every line — including the line whose
vowel is written on both sides of its letter. For the rest, HarfBuzz is the only reference there is,
and it is the same reference Word's own engine was written against.

Arabic joins its letters, which makes the shape of a letter a fact about its neighbours rather than
about itself. Most letters have four — alone, opening a word, inside one, ending one — and a
handful join only on the right, which is why a word can end in the middle of itself: nothing after
alef or dal joins back to them. A mark written over a letter must not break the join, and the four
shapes are four glyphs of the same character, chosen through the font's `isol`, `init`, `medi` and
`fina` features. Some pairs may not be written as two at all: lam followed by alef is one shape,
and drawing them apart is a spelling mistake rather than an ugly line.

Two things about ligatures took measuring rather than guessing. A font says, per lookup, whether a
match may reach across the marks between the letters, and both answers are needed in the same
font and the same word: the lookup that writes lam, lam and heh as the one shape for the name of
God reaches across the vowels written over them, while the lookup that combines a shadda with the
vowel beside it is matching marks and must not skip them. And a mark on such a shape is placed by a
further table again — the ligature offers a place for each of the letters it stands for, and a
vowel over the second lam is not a vowel over the first. Which letter it belongs to is how many of
the shape's letters stand before it in the text.

What is drawn as one shape is still read as four characters. A ligature is written into the PDF's
map back to text as everything it stands for, so a word joined on the page can still be searched
for and copied out as the word.

Word's export of the `arabic` fixture agrees on all of it: every line begins within four tenths of
a point of Word's, and along the line each glyph stands where Word stands it to a hundredth. What
that comparison cannot say is what the lines say, for a nearer reason than Hebrew's — Word writes
Arabic as the presentation forms, one glyph to a run, so its file reads back as characters nobody
typed, and the name of God, which it draws as the single glyph the font holds for it, reads back
out of Word's own file as the letter J.

An Indic syllable is drawn neither in the order it is stored nor one shape to a letter. A vowel may
be written to the left of the consonant it is pronounced after though it is stored after it;
consonants with no vowel between them are written as one stacked shape; and an r at the head of a
cluster is written as a small mark at the end of it. A converter that walks the characters and looks
each one up does not draw an ugly line — it draws a different word.

None of that can be settled character by character, and the syllable is the unit throughout. It is
divided out of the run; the consonant the rest hangs from is found by asking the font what shapes it
has — the rules are written in terms of what a font can do, so there is no way round asking; every
part is given a place in the visual order and sorted into it; the font's rules for making conjuncts
are applied one at a time; and then what those rules managed to make decides where the vowel and the
r finally go. Sort, ask the font, sort again. Nine scripts follow it — Devanagari, Bengali,
Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and Malayalam — differing in where a repha ends
up, how it is asked for, and which side of the base a below-base form may appear on.

The specification for those scripts was rewritten, and a font says which set of rules it was drawn
against by which of two names it files its script under. Both are implemented, because both are
shipped: this machine has Shree Devanagari 714 and Arial Unicode MS written to the older rules, and
Devanagari Sangam MN to the newer, and the two are shaped differently — under the older rules a
joining mark after the base is moved to the end of the syllable, the below-base feature is not
applied before the base, and what the font is asked about a pair of letters is asked of the pair in
company rather than standing alone.

Most of the writing systems descended from Brahmi are not given rules of their own at all. There are
too many of them, they are alike enough, and what differs between them is what the font already
describes — so one engine shapes all of them, working from what each character *is* rather than from
which script it belongs to. Something to build on; a vowel drawn above, below, before or after; a
consonant written under the one before it; a mark on a mark. Classify the characters, divide the run
into clusters on that basis, ask the font for its shapes in a fixed order, and move the two things
that are drawn away from where they are stored: an r at the head of a cluster, which is drawn as a
mark at its end, and a vowel written to the left of the letters it is pronounced after. That is the
whole of it, and it shapes some seventy scripts — Sinhala, Tibetan, Javanese, Balinese, Cham, Newa,
Chakma, Adlam, Egyptian hieroglyphs — none of which has a line of code to itself.

The classification is not a property in the database. It is worked out from five that are — what
part of a syllable a character is, which side of its consonant it is drawn, whether it joins like
Arabic, whether it is ignorable, and its general category — by rules Microsoft publishes, together
with the overrides Microsoft publishes for the characters the database has not caught up with.
`tools/make-use-tables.py` does that once and writes out the answer.

Three things about it were only found by comparing. A joiner does not divide a cluster: the grammar
is read over what is visible, or the very character written to ask for two letters to be joined
would separate them. A font that files its rules under no script in particular is not written to
this engine and must not be shaped by it — Noto Sans Tai Tham is such a font, and draws a left-side
vowel by moving the glyph, so reordering the characters first moves it twice. And a vowel written on
both sides of its consonant at once is stored as one character and cannot be drawn as one: it is
taken apart first, into the two halves the database says it is made of, and only the left half is
moved.

Khmer and Myanmar descend from the same writing and reorder for the same reasons, more plainly.
Khmer marks a stacked consonant with a character of its own rather than by the absence of a vowel,
and moves an r written under a consonant to the front of the whole cluster; Myanmar decides which
part of the syllable each character belongs to in one pass and sorts, with the medial r and the
left-side vowel going before the consonant they are written on. Thai and Lao reorder nothing at all:
they stack their vowels and tone marks above and below, and store every character in the order it is
drawn, so what they need is the marks put where the font says.

What each character is to a shaper, and where round its consonant it is drawn, is generated from the
Unicode character database by `tools/make-indic-tables.py` rather than typed. Not all of it can be:
where a matra ends up depends on the script as well as on the side it is written on, because the
sorted order is a visual one and the scripts stack their parts differently. Those per-script answers
are in the generator, from the OpenType script development specifications.

Not every face describes its shaping in OpenType. A hundred and sixty of the ones on this machine
carry Apple's `morx` table and no `GSUB` at all: Devanagari MT, Gujarati MT, Gurmukhi MT, Thonburi,
Geeza Pro, Corsiva Hebrew, and the whole of Helvetica, Palatino and Optima. A converter that reads
only OpenType draws those scripts as rows of unjoined letters, and there is nothing in the file to
fall back on.

It is the older idea and the more general one. Where OpenType says "these glyphs in this company
become those glyphs", this says "in this state, a glyph of this class takes you to that state, and
on the way you may mark this one, swap that one, or write several as one" — one machine expressing
what OpenType needs four kinds of lookup for, and two more besides: rearranging glyphs, and
inserting ones the text never held. All five kinds are read here, and the reading is used only where
the font has no OpenType tables. A face carrying both carries the same shaping twice, and the
OpenType half is the one every other reader of the file will use.

Where those faces go on to say how far apart the glyphs go, they say that in Apple's tables too, and
those are read as well. Two quite different things live in the one table. Most of it is kerning — by
naming pairs, by naming classes of glyphs, or by a machine that keeps a stack of what it has passed
and moves several of them at once — and it is applied only where the document asks for kerning, as
everywhere else here. The rest is attachment: a machine that marks a letter and fastens what follows
to it by naming a point on each out of a table of anchors, which is how these faces put a vowel sign
on a consonant. That is applied always, because a mark that is not fastened is not merely unkerned
but in the wrong place.

Its kerning is shared between the two glyphs rather than taken out of the first one's advance: half
moves the glyph on the left and half moves the one on the right, which is drawn half a kern along as
well. OpenType expresses the same thing the other way, as a shortening of the left glyph alone. The
two give a pair the same width and put the second glyph in different places, and each face is drawn
against one of them.

Two things about it cost an afternoon each. A machine that writes several glyphs as one leaves the
others behind marked as gone, and they have to be swept up before the next machine runs rather than
at the end — left in place, they sit between the pair the next machine is looking for and the run
comes out with its shapes half made. And which way round a subtable reads the run is decided by a
flag saying so, not by comparing the flag that says "logical order" against the direction of the
text: getting that wrong shapes Arabic with every letter's join one place out.

Where a line may be broken is not a question about spaces. Chinese and Japanese are written with
none at all and break between one character and the next; Thai, Lao, Khmer and Burmese have none
between their words either; and even English has spaces that may not be broken at — the one in
"10 kg" — and places without a space where a break is allowed, as after a hyphen. A converter that
looks for spaces draws a line of Japanese straight off the edge of the page, which is what this one
did.

The Unicode line breaking algorithm decides it from a property of each character and a list of rules
about pairs, applied in order, the first that matches winning. The property is generated from the
database by `tools/make-linebreak-tables.py`, and the rules are checked against the file Unicode
publishes for exactly that purpose: 7310 of its 7654 cases, every one that does not turn on a script
needing a dictionary, and all of them pass. Half of what that file caught was rules read backwards
or applied to the wrong side of a pair — no break *before* an opening bracket rather than after one,
glue looked for past the spaces rather than beside them — and the sort of thing no amount of reading
the text again would have shown.

The 344 it does not answer are the scripts written without spaces *and* without a break between
every character. For those the algorithm says to consult a dictionary of the language, which this
converter has not got. What it does instead is what Word does with the same paragraph: break between
one syllable and the next. The marks are folded into the letters they are written on, along with the
handful of Thai and Lao vowels that are letters in their own right but sound after a consonant and
cannot begin a line; the four vowels those two scripts write *before* the consonant they are sounded
after keep hold of it, as does the Khmer sign that turns the letter following it into a subscript;
and what is left standing is a letter that may begin a syllable, and so a place a line may be
broken. It is a coarser answer than a dictionary would give — more places than a Thai reader would
choose — but every place it offers is one, and a greedy filler picks the last that fits.

Which is where the measurement is. Given the `wrapping` fixture — a paragraph of Japanese, one of
Chinese and one of Thai, none with a space in it — Word and this converter break all three into the
same lines, to a hundredth of a point in width, kinsoku and all: no line begins with a full stop, a
comma or a closing bracket, and the Thai breaks where Word breaks it, in the middle of a word.

Text reaches the page as glyphs rather than as characters. Between the two stands a shaper: it
takes a run and a face and gives back the glyphs that draw it, each carrying its own advance and
the character it came from. Everything downstream reads that — a line's width is the sum of its
glyphs' advances, and what is written into the page is those same glyphs.

What the shaper does is decided by the writing in front of it: a character takes the glyph the
font's character map gives it; a letter of a script that joins takes the shape its neighbours call
for; letters the font says may not be written apart are written as one; a pair is drawn closer
together where the face says so; and a mark is moved onto what it belongs to. The point of it is
not cleverness but shape. A character is not a glyph — one may need
several, several may need one, and which glyph a character takes can depend on its neighbours —
and a pipeline that carries characters as far as the page cannot be told the difference. This one
carries glyphs, so it can be.

It also puts right something that was true before it: a line's width and the kerning written into
the page were worked out by two separate walks over the same text, one in the measurer and one in
the writer. Two walks that must agree are two walks that can disagree. There is one now.

Underneath it is a reader for the two tables a font describes its own shaping in. `GSUB` says what
glyphs a run becomes and `GPOS` says where they go, and the two are the same machine pointed at
different ends of the problem: both walk a run, both pass over what a lookup says it cannot see,
both have rules that match on what stands before and after, and in both a matched rule does not act
itself but names other lookups to run at places inside the match. So the walking, the skipping, the
matching and the nesting are written once, and only the acting is written twice — substitution of
one glyph for another, of one for several and of several for one; adjustment of a single glyph, of
a pair, and of a mark onto the letter, the ligature or the mark it belongs to.

Two of its parts are what make a complex script work at all. The first is `GDEF`, which says what
each glyph is: a rule about two letters that must still fire with a vowel written between them says
"ignore marks", and which glyphs are marks is a question only the font can answer. The second is
the script list, which had been passed over on the grounds that a lookup fires only on the glyphs
it covers and those are its own script's. That is true of nearly every feature and false of the one
whose whole purpose is to draw the same glyphs differently: `locl`, taken from every script at once,
draws Hindi in Marathi's letters. The script a run is in is now asked for by name.

Hinting is kept, because Word keeps it: its own exports carry `cvt`, `fpgm` and `prep` in every
subset they embed. It cannot be subset in any case — control values are reached by index and
function numbers are worked out as the instructions run, so which of them a glyph needs is not a
question that can be answered without running them. `ConversionOptions.DropFontHinting` takes all
three tables out along with the instructions inside each glyph, which more than halves the file at
no cost to the shapes; it is off because it is a departure from Word rather than a step towards
it.

CFF subsetting is the one thing here with no Word reference behind it: every PostScript-outline
face on this machine is for a script the converter cannot shape, so there is no document Word
could be asked to render. It is checked instead by fontTools, which reads the rebuilt font and
*draws* its glyphs — a subset that emptied a subroutine something still calls parses perfectly and
draws rubbish, so executing them is the check that matters. Install it with `python3 -m pip install
fonttools`; without it those tests report and skip, and `N8PDF_REQUIRE_FONTTOOLS=1` makes absence
a failure.

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
