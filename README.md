# n8PDF

**Converts `.docx` to PDF, written from scratch — aiming for pixel-parity with Word.**

No third-party DOCX or PDF library, no headless Word or LibreOffice, no browser engine, no sidecar
or container. `src/n8PDF` carries **zero** package references: it builds only on the .NET base class
library, and everything domain-specific — the OPC container, WordprocessingML semantics and the
style cascade, TrueType/CFF parsing with subsetting and shaping, the bidirectional algorithm, image
decoding, line breaking and pagination, and the PDF writer — is its own. A consumer adds one
reference and calls one method:

```csharp
Converter.ConvertFile("report.docx", "report.pdf");
```

## Why

Most ways to turn a `.docx` into a PDF without Word itself either shell out to a heavyweight engine
(LibreOffice, a browser) or render with visible divergence from Word — the wrong line breaks, the
wrong spacing, fonts that do not match. The rendering was the hard part, and most from-scratch
options never got it right.

n8PDF takes the opposite bet: every layout rule that could not be read out of a specification was
**measured from Word's own output**, with purpose-built probe documents designed so that only one
candidate model survives the measurement. Line-start positions match Word, and what remains is
almost everywhere a single step of the 1/300-inch grid Word itself rounds to. The goal is a PDF you
cannot tell from Word's own export — and it gets close.

## Installation

`n8PDF` is on [NuGet](https://www.nuget.org/packages/n8PDF) — latest **0.1.4**:

```bash
dotnet add package n8PDF
```

It targets **.NET 10** (`net10.0`) and is pre-1.0 — usable and hardened, but the API is not frozen
until `v1.0.0`.

A file, a stream, or bytes:

```csharp
using n8PDF;

Converter.ConvertFile("report.docx", "report.pdf");

byte[] pdf = Converter.Convert(File.ReadAllBytes("report.docx"));

using var input  = File.OpenRead("report.docx");
using var output = File.Create("report.pdf");
Converter.Convert(input, output);
```

Options — fonts, limits, title, PDF/A, a mail-merge record, a `CancellationToken` — live on
`ConversionOptions`, passed as the optional last argument. See
[Installation](https://github.com/nathanpond/n8PDF/wiki/Installation) and
[The API](https://github.com/nathanpond/n8PDF/wiki/The-API) in the wiki for the whole surface, and
how to verify a release's provenance.

## Security

A `.docx` is a ZIP of XML that can carry images, fonts, charts and metafiles — **every byte of it
attacker-controlled**, and every byte parsed by code in this repository. Because the library is pure
managed .NET with no `unsafe`, the realistic failure mode is denial of service, not code execution,
and it is defended as such:

- **Decompression bombs** — `PackageLimits` bounds per-part and total decompressed size, part count,
  image pixels and embedded-font bytes, counted against what leaves the decompressor rather than what
  a header claims; a document past them throws `PackageTooLargeException`.
- **XML entity expansion** (billion laughs) — parts are read through a reader that prohibits DTDs
  outright.
- **Oversized images** — the declared pixel area is bounded in long arithmetic before a byte is
  allocated; a picture past the limit is dropped, not the conversion.
- **Malformed decoders** — a hostile image, font, chart or metafile costs its own placement, not the
  run. The audit register of memory-exhaustion, hang and stack-overflow findings is closed, each
  attack built as a regression test.
- **Integer overflow** — the library compiles with checked arithmetic, so a length or offset that
  wraps on the way to an allocation or index throws rather than corrupts.
- **Fuzzing** — property-based, coverage-guided (SharpFuzz), continuous (a nightly workflow), and
  differential oracles run against the untrusted entry points.
- **Static analysis** — CodeQL and Semgrep run in CI on every push.
- **Cancellation** — pass a `CancellationToken` so a pathological document cannot hang the caller.

Every release is published through NuGet **trusted publishing** (an OIDC exchange from CI, with no
stored key) and carries a signed [SLSA build-provenance](https://slsa.dev) attestation. For
genuinely untrusted input, still isolate the conversion — read
[Security](https://github.com/nathanpond/n8PDF/wiki/Security) and the
[Threat Model](https://github.com/nathanpond/n8PDF/wiki/Threat-Model) first.

## More in the wiki

The [wiki](https://github.com/nathanpond/n8PDF/wiki) holds the full account:

- [Matching Word](https://github.com/nathanpond/n8PDF/wiki/Matching-Word) — the fidelity method, and how closely it matches
- [Functionality](https://github.com/nathanpond/n8PDF/wiki/Functionality) · [Known Gaps](https://github.com/nathanpond/n8PDF/wiki/Known-Gaps) — what is implemented, approximated, or declined by decision
- [The API](https://github.com/nathanpond/n8PDF/wiki/The-API) — the eight public types, and what a version promises
- [Architecture](https://github.com/nathanpond/n8PDF/wiki/Architecture) — the one-way pipeline from ZIP to PDF
- [Validation](https://github.com/nathanpond/n8PDF/wiki/Validation) — the test tiers and the independent external checkers
- [Security](https://github.com/nathanpond/n8PDF/wiki/Security) · [Threat Model](https://github.com/nathanpond/n8PDF/wiki/Threat-Model) — the attack surface, and what is defended
- [Prerelease Considerations](https://github.com/nathanpond/n8PDF/wiki/Prerelease-Considerations) — what pre-1.0 means here
- [Reporting Bugs](https://github.com/nathanpond/n8PDF/wiki/Reporting-Bugs) · [Developers](https://github.com/nathanpond/n8PDF/wiki/Developers) — filing an issue, and building from source

## Licence

MIT — see [LICENSE](LICENSE). The package carries it, and `LibraryInvariantTests` asserts that what
the package says and what the repository holds are the same thing.
