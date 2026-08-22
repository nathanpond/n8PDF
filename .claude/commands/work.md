---
description: Work a GitHub issue end to end on a linked branch
argument-hint: <issue number>
allowed-tools: Bash(gh:*), Bash(git:*), Bash(dotnet:*), Bash(rg:*), Read, Edit, Write, Grep, Glob, Task
---

Work issue **#$1** to completion. Follow the Backlog section of CLAUDE.md throughout.

1. **Read it fully.**
   ```
   gh issue view $1 --comments --json number,title,body,labels,state,assignees
   ```
   Stop and say so if it is already closed, or if it has open `blocked-by` dependencies.

2. **Branch.** Check for existing work first:
   ```
   gh issue develop $1 --list
   ```
   Check out the linked branch if there is one, otherwise `gh issue develop $1 --checkout`.

3. **Plan.** Comment 2-5 bullets on the issue before writing code — the approach and the files
   you expect to touch. If the issue has no acceptance criteria, derive them and put them in this
   comment as a checklist, so the definition of done is written down before the work starts.

4. **Implement.** Stay inside the issue's scope, and inside the invariants in CLAUDE.md: no
   `PackageReference` in `src/n8PDF`, no growth of the public surface, no hand-edits to generated
   tables. If the task appears to require breaching one, stop and ask — that is a conversation,
   not a judgement call.

   A separate problem discovered along the way is filed, not fixed:
   ```
   gh issue create --title "..." --body "Discovered while working #$1. ..." --label needs-triage
   ```
   then mention the number in a comment on #$1.

5. **Verify.** All of it, not a subset:
   ```
   dotnet build n8PDF.sln --configuration Release -warnaserror
   N8PDF_REQUIRE_QPDF=1 N8PDF_REQUIRE_FONTTOOLS=1 N8PDF_REQUIRE_FRIBIDI=1 \
     dotnet test n8PDF.sln --configuration Release
   ```
   - The build must be warning-clean. Never finish with `-p:TreatWarningsAsErrors=false`.
   - Add a fixture or a test for the behavior if one is feasible and absent.
   - **A changed golden trace is a claim, not a result.** If the diff moves a golden, read the
     diff and say in the closing comment why the new output is the correct one. Never regenerate
     a golden to make a test pass.
   - Remember this machine has Word, so it runs all 143 comparison fixtures where CI runs 97. A
     failure here that CI will not see is still a failure.

6. **Commit and push.** `Refs #$1` — never a closing keyword.
   ```
   git commit -m "<subject>

   Refs #$1"
   git push -u origin HEAD
   ```

7. **Close, only after the push succeeds.**
   ```
   gh issue close $1 --reason completed \
     --comment "Done in \`$(git branch --show-current)\` @ $(git rev-parse --short HEAD).

   <what changed>
   <acceptance criteria, checked off>"
   ```

If you cannot complete it, do not close it: comment what is done, what remains and what is
blocking, then `gh issue edit $1 --add-label blocked`.
