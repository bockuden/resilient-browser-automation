# Changelog

All notable changes to Resilient Browser Automation are documented here.

## Unreleased

### Added

- A SemVer tag release workflow gated by fast tests and full Compose E2E that
  publishes the worker image, an SPDX JSON SBOM, and a GitHub Release.
- A weekly/manual compatibility canary against the latest published stable Test
  Stand release without automatically changing the stable Compose pin.
- Root contribution and security policies plus structured bug, feature, and
  security-contact issue forms.
- A focused operations guide for Compose, host execution, configuration,
  browser behavior, observability, and exit codes.

### Changed

- Reduced README from 328 to 167 lines and placed the complete copy-paste demo
  near the top.
- Upgraded cache and artifact Actions to their Node.js 24-compatible major
  versions.

### Fixed

- Prepare the ignored Compose demo data directory for the non-root worker on
  Unix hosts so SQLite and failure evidence can be written through the bind
  mount.

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
- Successful release CI: [GitHub Actions run 30397596555](https://github.com/bockuden/resilient-browser-automation/actions/runs/30397596555).

### Scope

This is a production-style reference implementation and portfolio proof. It is
not a distributed crawler, anti-bot system, CAPTCHA solver, or production SLA.
See [limitations](docs/limitations.md) for the explicit boundaries.
