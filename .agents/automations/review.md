# Review changes

Review the requested code changes for actionable bugs and regressions. Follow the
repository's `AGENTS.md` instructions and established conventions.

## Choose the review scope

- Use the repository, pull request, branch, commit, or files explicitly provided.
- Otherwise, review staged and unstaged changes, including relevant untracked
  source files, in the current repository.
- If the working tree is clean, review the current branch against its merge base
  with the repository's configured default branch. Verify the base before using it.
- If the repository or comparison target cannot be determined, ask for the missing
  context. Do not choose arbitrarily among multiple repositories.
- If there are no changes to review, return `No actionable findings.` and stop.

## Inspect and verify

1. Read the diff and surrounding code, including relevant callers, tests, and
   configuration. Trace changed behavior through its actual execution paths.
2. Prioritize correctness, security, data integrity, compatibility, and meaningful
   performance regressions introduced by the changes.
3. Check assumptions against the code and available evidence. Report an issue only
   when you can identify a concrete trigger and explain its impact.
4. Run focused existing checks or a minimal reproduction when useful and feasible.
   Use the results to substantiate findings; do not claim unrun checks passed.
5. Review without editing project files or publishing comments unless requested.

Avoid speculative concerns, style preferences, issues already caught by routine
formatting, and unrelated pre-existing bugs. Do not invent findings to fill a quota.

## Report findings

Return only actionable findings, ordered by severity. For each finding, include:

- A concise title prefixed with `[P0]`, `[P1]`, `[P2]`, or `[P3]`.
- The file and smallest useful line range in the reviewed changes.
- The triggering conditions, resulting behavior, and practical impact.
- A brief suggested correction when the remedy is clear.

Use P0 for critical issues requiring immediate action, P1 for high-impact issues
that should be fixed before release, P2 for ordinary actionable bugs, and P3 for
low-impact bugs.

Do not include an introduction, review summary, scope statement, testing report,
general assessment, or closing remarks. Include verification evidence or necessary
qualifications only within the finding they support. If no actionable issues were
found, return only `No actionable findings.`
