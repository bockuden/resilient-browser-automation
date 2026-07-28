# Test Stand 1.1.3 C# compatibility evidence

## Scope

- Date: 2026-07-28
- Consumer: `resilient-browser-automation` on `main`
- Test Stand: `resilient-automation-test-stand` `1.1.3`
- Exact Compose image:
  `ghcr.io/bockuden/resilient-automation-test-stand:1.1.3`
- Resolved digest:
  `sha256:39d0b70a874b8cf2f9ba9be6c8743530953e22c1c9d2775445341aecd2cf2bcd`

The worker uses only the released HTTP and browser contract; it does not import,
build, or depend on Test Stand Python implementation details. `latest` was not
used.

## Full Chromium Compose E2E

`eng/demo-compose.ps1` completed successfully in 172 seconds against the exact
published image. It demonstrated:

- 20-item paginated extraction and idempotent repeated delivery;
- transient HTTP 503 recovery with `Retry-After`, natural end, duplicates, and
  bounded concurrency;
- a failure after page 2 followed by durable checkpoint resume from page 3;
- graceful cancellation with worker exit code `4`; and
- terminal HTTP 500 evidence with `error.json`, `page.html`, `screenshot.png`,
  and `trace.zip`.

The final report contained 9 completed jobs, 1 cancelled job, 1 expected failed
job, and 117 persisted catalog items. The Compose stack was stopped at the end
of the run.

## Challenge mapping

The E2E proves the Test Stand's [Resilience Challenge](https://github.com/bockuden/resilient-automation-test-stand/blob/main/CHALLENGE.md)
surfaces: Level 1 pagination, Level 2 transient recovery, Level 3 stable DOM
locators/duplicates/protected login, and bonus resume/cancellation evidence.

The corresponding pinned-pair record is maintained in
[compatibility-matrix.md](compatibility-matrix.md). GitHub Actions must rerun
after this consumer change is published.
