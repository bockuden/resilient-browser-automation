# Test Stand 1.1.5 C# compatibility evidence

## Scope

- Date: 2026-07-30
- Consumer: `resilient-browser-automation` on `main`
- Test Stand: `resilient-automation-test-stand` `1.1.5`
- Exact Compose image:
  `ghcr.io/bockuden/resilient-automation-test-stand:1.1.5`
- Stand release:
  [`v1.1.5`](https://github.com/bockuden/resilient-automation-test-stand/releases/tag/v1.1.5)

The worker uses only the released HTTP and browser contract; it does not import,
build, or depend on Test Stand Python implementation details. `latest` was not
used for the stable Compose pin. The canary resolved the latest published
release to the exact `1.1.5` image before this repository adopted the same
version as its default.

## Full Chromium Compose E2E

The scheduled/manual canary path completed the full Compose workflow against
the exact published `1.1.5` image. It demonstrated:

- 20-item paginated extraction and idempotent repeated delivery;
- transient HTTP 503 recovery with `Retry-After`, natural end, duplicates, and
  bounded concurrency;
- a failure after page 2 followed by durable checkpoint resume from page 3;
- graceful cancellation with worker exit code `4`; and
- terminal HTTP 500 evidence with `error.json`, `page.html`, `screenshot.png`,
  and `trace.zip`.

The final report contained 9 completed jobs, 1 cancelled job, 1 expected failed
job, and 117 persisted catalog items. Hosted evidence is recorded in
[GitHub Actions canary run 30528634764](https://github.com/bockuden/resilient-browser-automation/actions/runs/30528634764).

## Challenge mapping

The E2E proves the Test Stand's [Resilience Challenge](https://github.com/bockuden/resilient-automation-test-stand/blob/main/CHALLENGE.md)
surfaces: Level 1 pagination, Level 2 transient recovery, Level 3 stable DOM
locators/duplicates/protected login, and bonus resume/cancellation evidence.

The corresponding pinned-pair record is maintained in
[compatibility-matrix.md](compatibility-matrix.md).
