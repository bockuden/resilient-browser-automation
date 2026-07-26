# Limitations

This repository demonstrates reliable browser automation architecture in a
bounded portfolio project. It does not claim production-scale crawling.

## Not Included

- Distributed queues or multiple worker nodes.
- CAPTCHA bypass or anti-bot evasion.
- Proxy rotation.
- Third-party website scraping.
- Secret management beyond local demo configuration.
- Cloud artifact storage.
- A WinForms, CEF, or Selenium UI.

## Operational Limits

- SQLite is appropriate for the local demo and single-worker proof. A
  distributed deployment would need a central queue and database.
- Screenshots and traces are evidence files and may contain target page data.
- Rate limiting is per worker process, not global across multiple machines.
- The FastAPI stand is deterministic by design and is not a substitute for
  contractual tests against a real product API.
- Compose intentionally pins stand version `1.0.0`. New stand behavior is not
  consumed until the image version is changed and this repository's E2E passes.

## Review Positioning

The project should be presented as evidence of C# architecture, Playwright
automation, idempotency, recovery, retries, concurrency control, diagnostics,
and reproducible testing. It should not be presented as an anti-bot scraping
tool.
