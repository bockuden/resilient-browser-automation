# Changelog

All notable changes to Resilient Browser Automation are documented here.

## 1.0.0 - 2026-07-28

### Added

- A .NET 10 worker that drives Chromium through Playwright with validated JSON
  Lines job input and typed configuration.
- SQLite-backed idempotency, transactional per-page checkpoints, stale-job
  recovery, and source-page provenance for extracted catalog items.
- Classified transient retries with bounded exponential backoff, jitter,
  `Retry-After` handling, finite operation/job budgets, and cancellation.
- Bounded job concurrency, per-target token-bucket rate limiting, structured
  logs, OpenTelemetry metrics, and deterministic exit codes.
- Redacted terminal failure evidence: `error.json`, `page.html`,
  `screenshot.png`, and Playwright `trace.zip`.
- A reproducible Docker Compose demo and GitHub Actions browser E2E against the
  independently released Resilient Automation Test Stand.

### Compatibility

- The release pins Test Stand `1.1.3` exactly; compatibility validation never
  uses `latest`.
- Validation evidence: [Test Stand 1.1.3 compatibility](docs/stand-1.1.3-compatibility.md).
- Successful CI: [GitHub Actions run 30387683418](https://github.com/bockuden/resilient-browser-automation/actions/runs/30387683418).

### Scope

This is a production-style reference implementation and portfolio proof. It is
not a distributed crawler, anti-bot system, CAPTCHA solver, or production SLA.
See [limitations](docs/limitations.md) for the explicit boundaries.
