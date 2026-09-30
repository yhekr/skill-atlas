# Several repositories, one search

Status: accepted implementation scope, 2026-09-30. Written before implementation.

## Problem and outcome

Scanning another repository currently replaces the previous result. A user comparing skills in Kotlin and MPS needs a single searchable list while retaining the source of every document.

## Behaviour

- Web: enter one GitHub URL or owner/repo per line, up to five entries. The optional branch/tag applies to every entry; empty means each repository's own default branch. Equivalent URL/SSH/short-name aliases are scanned once, ignoring repository-name case. All inputs are validated before scanning starts.
- Run scan replaces the current collection once results arrive. Sources run sequentially, within the existing global two-scan limit, with a ten-minute batch deadline. Cancellation retains the previous visible collection. A failed source is shown separately; successful repositories, including empty ones, remain usable. If every source fails, retain the previous collection and show the failures.
- Show repository, skill count and pinned revision for each successful source. Allow removing a repository from the current collection and choosing one repository or all repositories to search. This collection is in-memory for the page lifetime, without persistence or account setup.
- Search matches all whitespace-separated words across skill names/descriptions, case-insensitively, using the existing filter. Show the matching count against the selected scope. Do not merge identically named skills across repositories.
- Each list item and selected document identifies its repository. Document identity is `(scanId, originalSkillIndex)`, never the filtered row number, name or path alone. Preview, Source and Copy retain current behaviour. Removing another repository preserves the selected document; removing its source clears it.
- Similar retains its existing per-repository scope and scoring. A recommendation may open a skill hidden by the search; its original scan/index determines the document. Cross-repository Similar is outside this change.
- CLI: accept one to five positional GitHub repositories or local directories. The same `--query` searches each result (CLI also searches paths, as before). Multiple-source JSON is `{ repositories: [{ source, reference, result, error }] }`; `result` contains the existing ScanResult or null on failure. Exit 1 for any source failure, 0 for full success including no skills/matches, 2 for invalid arguments, 130 for cancellation. Multi-source `--similar` is rejected with guidance to select one source. Single-source output and API remain compatible.

## API and implementation

- Keep `POST /api/scans` and its document/Similar endpoints unchanged.
- Add `POST /api/scan-batches` with `{ repositories: string[], reference?: string }`. Return HTTP 200 with `{ scans: [...existing scan response plus reference], failures: [{ source, error }] }`, even on partial/all source failures. Invalid input returns 400, global saturation 429, cancellation/deadline 408. Source failures use generic messages, without local paths/credentials. Existing origin checks and cache/security headers apply.
- Put source normalization, limits, deduplication and ordered partial-success scanning in Core. Use existing scanners and discovery rules. Retain scan snapshot expiration and memory limits; expired documents still return 410 with a rescan instruction.
- Web flattens successful snapshots into stable entries, preserving source indices; browser state helpers are testable without network or a DOM.

## Acceptance and review

1. Kotlin + MPS appear together; search narrows across both, repository selection narrows scope, clearing restores results.
2. Same names/paths in two sources open their own pinned content. Removing a source cannot make indices point at another document.
3. Duplicate aliases, blanks, >5 entries, invalid URL/ref, zero skills, partial/all failure, cancellation and expired snapshots are covered.
4. Existing single-source CLI/API, filter and Similar tests pass. Add Core, CLI process, HTTP and JavaScript tests without network. Verify actual desktop/mobile UI with Computer Use.
5. Include a local demo video showing two repositories, cross-repository search, source selection and document reading. Preserve the actual recording method as a discoverable skill with several cases; clearly distinguish video from still-image previews.
6. PR uses `.github/pull_request_template.md`, links this spec, describes risks and tests, links the demo, and passes Windows/Linux CI for its exact SHA. Review the final diff against this spec before delivery. Creating the PR does not imply merging it.
