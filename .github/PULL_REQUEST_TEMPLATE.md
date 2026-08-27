<!--
  Thanks for contributing. A few things worth knowing before you open this:
  - `src/n8PDF` carries ZERO package references — the build fails if one is added. If your change
    seems to need a dependency, it needs a conversation first (open an issue).
  - The public surface is exactly eight types; growing it is a deliberate act PublicApiTests will flag.
  - Warnings are errors. A change to layout, metrics, or font handling needs its golden trace
    reviewed, not merely regenerated.
  See CONTRIBUTING.md for the whole of it.
-->

## What this changes

<!-- One or two sentences. What is different after this PR, and why. -->

## Linked issue

<!-- Refs #NNN. Do NOT use "Fixes/Closes #NNN" — auto-close keywords only fire on the default
     branch and silently do nothing from a feature branch. -->
Refs #

## How it was verified

- [ ] `dotnet build n8PDF.sln -c Release -warnaserror` is clean (warnings are errors)
- [ ] `dotnet test n8PDF.sln -c Release` passes, with the external checkers on
      (`N8PDF_REQUIRE_QPDF=1 N8PDF_REQUIRE_FONTTOOLS=1 N8PDF_REQUIRE_FRIBIDI=1`)
- [ ] A layout/metrics/font change has its **golden trace reviewed**, not just regenerated
- [ ] No new `PackageReference` in `src/n8PDF`; the public surface is unchanged (or the change is intended and explained above)

## Notes for the reviewer

<!-- Anything non-obvious: a measurement you took from Word, a trade-off, a fixture you added. -->
