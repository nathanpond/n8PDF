# Security policy

n8PDF converts `.docx` to PDF, and a `.docx` is a file someone else wrote. The library is a
parser exposed to hostile input by design, so it is audited as one: every finding the project's
own audits turn up is filed as a public issue under the [`security`
label](https://github.com/nathanpond/n8PDF/issues?q=is%3Aissue+label%3Asecurity), fixed with the
attack built as a test, and a deterministic fuzzer runs on every push. This page is how to report
what those did not catch.

## The guarantee, in one line

n8PDF is pure managed .NET with no native code and no `unsafe`, so a hostile document's realistic
reach is **denial of service, not memory corruption** — unbounded memory, unbounded work, a stack
overflow that ends the process, or an uncaught exception that aborts one conversion. What is and is
not a security bug, and where the trust boundary runs, is written down in the
[Threat Model](https://github.com/nathanpond/n8PDF/wiki/Threat-Model); read it before deciding
whether something you have found is a vulnerability.

## Reporting a vulnerability

**Report privately, not as a public issue.** Use GitHub's private vulnerability reporting:

> On [the repository](https://github.com/nathanpond/n8PDF), open the **Security** tab and click
> **Report a vulnerability**. This opens a private advisory visible only to you and the maintainer.

That channel keeps the report, the discussion, and any fix private until a fix is ready to
disclose. Please do not open a public issue, a discussion, or a pull request for a suspected
vulnerability first — a public reproduction is a working exploit handed to everyone.

Include what makes any parser finding actionable (see [Reporting
Bugs](https://github.com/nathanpond/n8PDF/wiki/Reporting-Bugs)):

- the **smallest crafted `.docx`** (or image/font part) that demonstrates it — rebuilt with
  placeholder content, never a confidential document;
- the call you made, options included;
- what a hostile document **gets out of it** — memory, CPU, a process-ending crash, or an aborted
  conversion — since that is the framing the register is organised around;
- the exception and stack trace, or "hangs after N seconds / grows past N GB".

### What to expect

This is a small project, so the timeline is stated honestly rather than aspirationally:

| Stage | Target |
|---|---|
| Acknowledgement of your report | within **5 business days** |
| An initial assessment (is it a vulnerability, and how severe) | within **10 business days** |
| A fix or a decision, for an accepted report | tracked in the private advisory until disclosed |

If a report is accepted, it is fixed with a regression test that builds the attack, disclosed
through a published GitHub Security Advisory, and — with your agreement — you are credited in it.
If it is declined, you are told why, measured against the
[Threat Model](https://github.com/nathanpond/n8PDF/wiki/Threat-Model); a document that merely
converts with a broken picture or a missing chart is working as designed, not a vulnerability.

Please give a reasonable window to fix an accepted report before disclosing it publicly. There is
no bug-bounty programme.

## Supported versions

n8PDF has not yet had a numbered public release (see
[#61](https://github.com/nathanpond/n8PDF/issues/61)). Until it does, security fixes land on
`main`. Once releases begin, this table states which lines receive them:

| Version | Supported |
|---|---|
| `main` (unreleased) | ✅ |
| latest released minor (once releases begin) | ✅ |
| older releases | ❌ — upgrade to the latest minor |

## Hardening a deployment

Even with the register clear, treat conversion of untrusted documents as untrusted-input
processing. The [Security](https://github.com/nathanpond/n8PDF/wiki/Security) page gives the full
guidance; the short of it:

1. **Set `ConversionOptions.Limits`** to the smallest bounds your documents genuinely need, and
   catch `PackageTooLargeException`.
2. **Isolate hostile input** — run conversions in a worker process with an OS-level memory cap and
   a timeout, so a not-yet-found failure costs a worker rather than the service.
3. Expect a malformed picture, chart or embedded font to **cost its own placement, not the
   conversion**, and decide whether that is acceptable for your use.
