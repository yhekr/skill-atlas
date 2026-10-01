# Repeatable browser demos and visual CI

Accepted scope, 2026-09-30. Written before implementation.

Integration, 2026-10-01: the user subsequently requested transfer into the main checkout. The combined suite also covers per-repository stars, Starred with search/scope, persistence across reload/rescan, and mobile reader stars (9 tests, 20 baselines). The original no-merge delivery restriction below describes the earlier CI task.

## Outcome

Every GitHub CI run must replay the multi-repository demo in Chromium, record each test (including successful tests), and compare key screenshots against reviewed, checked-in baselines. A failed comparison must include expected, actual and diff images plus a video and trace.

## Determinism and scope

- Serve the real ASP.NET Core application and its bundled JetBrains Mono fonts. Drive its real UI with Playwright Test. Replace only scanner/document/Similar HTTP responses with fixed test fixtures: six Kotlin and 41 MPS entries, deterministic revisions, documents and Similar results. Fixtures are illustrative test data, not a current GitHub scan or a test of the scanner algorithm.
- Block unexpected API calls and external browser requests. Assert the scan request payload and document identity, so a broken client cannot silently use a generic successful mock.
- Use the same pinned Playwright container, Chromium, viewport, device scale, locale, timezone, colour scheme and reduced-motion preference locally and in CI. Wait for both font weights and asserted UI state; no arbitrary readiness sleeps. Desktop is 1440 × 1000, mobile is 390 × 844.
- Screenshots cover repository entry, collection, common search, source scope, Preview/Source and Similar, plus failures and mobile document identity. Videos and traces are recorded on every test attempt and uploaded even on failure. Generated reports are ignored; reviewed baseline PNGs are versioned test inputs.
- Cover empty results, six-source rejection, partial/full failure, cancellation, expired documents/retry, Similar errors/empty results, and identical names/paths in different repositories. Existing .NET and JavaScript tests remain responsible for scanner, API contracts and matching algorithms.

## Break and restore

A dedicated proof command temporarily changes the scan button's CSS through a test-only response override. It runs the real screenshot assertion and requires a nonzero exit plus expected/actual/diff artifacts for that assertion. It must reject unrelated failures and must not update baselines. Then it runs the unchanged test and requires success. The injected CSS never modifies application source and disappears with the test browser context. CI runs this proof and keeps its artifacts separately from the normal suite.

## Delivery

- Add the browser job to the existing GitHub Actions workflow; retain Windows/Linux build and test jobs.
- Document local run, baseline review/update, negative proof and artifact download commands.
- Run the required .NET checks and the browser suite, inspect baselines and intentional diff, publish to the existing feature PR, and verify the exact head's GitHub CI. Do not merge the PR as part of this work.
