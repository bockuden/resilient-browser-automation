# Changelog

All notable changes to Resilient Browser Automation are documented here.

## Unreleased

- Recover from missing, changed, and delayed browser locator tokens through
  unique semantic fallbacks; fail closed on ambiguous, hidden, or disabled
  targets with browser evidence.
- Read configured `data-testid` tokens from the Test Stand page and cover the
  Test Stand 1.2.0 selector-failure matrix and custom selector configuration in
  Compose compatibility CI.

### Added

- A reproducible Test Stand 1.2.0 rate-limit compatibility run that verifies
  `429` recovery, the server-provided `Retry-After` delay, persisted pages, and
  the scheduled retry count.
- Protected-session recovery against Test Stand 1.2.0, including login after
  session expiry, resuming the interrupted page, and verifying persisted
  checkpoints and items.
- A SemVer tag release workflow gated by fast tests and full Compose E2E that
  publishes the worker image, an SPDX JSON SBOM, and a GitHub Release.
- A weekly/manual compatibility canary against the latest published stable Test
  Stand release without automatically changing the stable Compose pin.
- Root contribution and security policies plus structured bug, feature, and
  security-contact issue forms.
- A focused operations guide for Compose, host execution, configuration,
  browser behavior, observability, and exit codes.

### Changed

- Include scheduled retry counts in the worker's final JSON run summary.
- Re-authenticate protected catalog sessions after expiry and continue from the
  page requested by the Test Stand.
- Advanced the default Compose Test Stand pin to the independently released
  `1.1.5` image after compatibility canary validation.
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
