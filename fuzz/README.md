# Fuzzing n8PDF

Coverage-guided fuzzing of the untrusted entry points, with [SharpFuzz](https://github.com/Metalnem/sharpfuzz)
(libFuzzer for .NET). Where [`FuzzTests`](../tests/n8PDF.Tests/FuzzTests.cs) and
[`PropertyTests`](../tests/n8PDF.Tests/PropertyTests.cs) attack from the test project with blind
mutation and generated inputs on a fixed per-push budget, these run under libFuzzer, which
instruments the library and **evolves inputs toward unexplored branches** — the coverage a
`.docx`'s CRC hides from a byte-flip (#263).

This project is **not** part of `n8PDF.sln`: it is built and run on its own, so the normal build,
the test suite, and the zero-`PackageReference` invariant of `src/n8PDF` are untouched. SharpFuzz is
a dev-only dependency and lives here, never in the shipping library.

## The harnesses

One per untrusted entry point, selected by the `FUZZ_TARGET` environment variable so libFuzzer's own
arguments pass through untouched:

| `FUZZ_TARGET` | Entry point | Covers |
|---|---|---|
| `image` | `ImageReader.TryRead` | PNG, GIF, BMP, TIFF, EMF/EMF+, JPEG decoders |
| `font` | `FontLibrary.Register` | the SFNT/OpenType/CFF/AAT font parser |
| `deobfuscate` | `EmbeddedFonts.Deobfuscate` → parse | Word's obfuscated-embedded-font path |
| `package` | `OpcPackage.Open` + read main part | the zip / OPC container and the XML reader |
| `document` | `Converter.Convert` | the WordprocessingML parser, end to end |

The oracle is the one the hardening rounds established: each harness swallows **only the documented
exceptions** (`FontFormatException` for the font paths; `PackageTooLargeException`,
`InvalidDataException`, `IOException`, `XmlException`, `FormatException`, `ArgumentException`,
`InvalidOperationException`, `NotSupportedException` for the rest). Anything else — an
index-out-of-range, a null reference, a raw overflow, a hang — escapes, and libFuzzer records it as
a crash to minimise and file.

## The corpus

Regenerated, never committed (it is `.gitignore`d):

```
dotnet run -c Release -- seed
```

seeds `corpus/<target>/` from the committed fixtures — `inks.jpg`, `n8PDFProbe.ttf`, a real
`.docx` — plus a minimal PNG and a few crafted headers. libFuzzer grows the corpus from there.

## A quick check anywhere (macOS included)

macOS's clang ships no libFuzzer runtime, so the coverage-guided run below is Linux-only. Everywhere,
`replay` runs each harness over the seeded corpus on a time-bounded thread — the check that no known
input escapes the oracle:

```
dotnet run -c Release -- seed
for t in image font deobfuscate package document; do
  FUZZ_TARGET=$t dotnet run -c Release --no-build -- replay
done
```

Each prints `replay <target>: N inputs, 0 escaped the oracle`; a non-zero count (or a `HANG:` /
`ESCAPED:` line) exits non-zero and is a finding.

## A coverage-guided run (Linux)

Prerequisites, once:

```
dotnet tool install --global SharpFuzz.CommandLine        # the `sharpfuzz` instrumenter
clang -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet   # the driver, vendored here
sudo mv libfuzzer-dotnet /usr/local/bin/                  # or anywhere on PATH
```

The libFuzzer driver `libfuzzer-dotnet.cc` is vendored from
[Metalnem/libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet) (MIT), pinned to a commit,
so the build is hermetic.

Then one command per target:

```
./run.sh image        # or font | deobfuscate | package | document
```

`run.sh` builds, instruments `n8PDF.dll`, seeds the corpus if needed, and starts libFuzzer on the
target. Pass extra libFuzzer flags after the target — e.g. `./run.sh image -max_total_time=60` for a
one-minute run. A crash is written to `crash-*` in this directory; reproduce it with
`FUZZ_TARGET=image dotnet run -c Release --no-build -- replay` after dropping it into
`corpus/image/`, and — per the backlog rules — commit the input as a seed in `FuzzTests` and file
the defect.

## Continuous fuzzing (CI)

[`.github/workflows/fuzz.yml`](../.github/workflows/fuzz.yml) runs all five targets nightly (and on
manual dispatch, with a `seconds` input) on a Linux runner: it builds the vendored driver, installs
`sharpfuzz`, instruments the library, and fuzzes each target for a bounded time. The corpus
**accumulates across runs** through the Actions cache, so coverage grows rather than restarting cold.
A crash fails the run and uploads the minimised reproducer as an artifact; a clean run is green. This
is the self-hosted stand-in for OSS-Fuzz, which has no .NET support (#264).

## When something escapes

The oracle is deliberately strict. An escaper is a real finding: minimise it (libFuzzer does this
automatically, or `-minimize_crash=1`), add the minimised input as a seed to `FuzzTests` so the
regression is guarded on every push, and file the defect with the crafted input and what a hostile
`.docx` gets out of it — the framing the [security register](../SECURITY.md) is organised around.
