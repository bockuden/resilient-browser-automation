# Release Checklist

Use this checklist before tagging `v1.0.0`.

- Clean clone on Windows.
- Clean clone on Linux or a Linux container path.
- `global.json` and GitHub Actions use the same .NET major version.
- `docker compose config` succeeds.
- `docker compose run --rm demo-site pytest -q` succeeds.
- `dotnet build --configuration Release` succeeds with zero warnings.
- Unit and integration tests pass.
- Browser E2E installs only Chromium and required Linux dependencies.
- The Compose demo runs from `.\eng\demo-compose.ps1`.
- Repeating the same `jobId` does not duplicate catalog rows.
- `maxPages` above the catalog size leaves the checkpoint on the real last page.
- Catalog rows contain their source page number.
- Real browser E2E proves checkpoint resume and graceful cancellation.
- Transient and permanent failures are visibly different in logs.
- Permanent failure writes `error.json`, `page.html`, `screenshot.png`, and
  `trace.zip`.
- Generated databases, traces, screenshots, browser binaries, and artifacts are
  absent from Git.
- The Russian plan is absent from `git ls-files` and ignored by
  `.git/info/exclude`.
- README and docs do not claim anti-bot behavior or production-scale crawling.
