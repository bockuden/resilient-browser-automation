# Contributing

Thank you for considering a contribution to Resilient Browser Automation.
Changes should preserve the project's focus: explicit execution invariants,
finite failure handling, inspectable evidence, and reproducible compatibility.

## Before Opening a Change

- Search existing issues and pull requests.
- Use an issue for behavior changes that affect input, persistence, retry
  classification, browser interaction, or the public Compose contract.
- Report security concerns through [SECURITY.md](SECURITY.md), not a public
  issue.

## Development Requirements

- .NET 10 SDK selected by `global.json`.
- Docker Desktop or another Docker Engine with Compose for browser E2E.
- PowerShell 7 for the repository scripts.

The repository-local `.dotnet` SDK, browser downloads, databases, and generated
artifacts are intentionally ignored by Git.

## Local Verification

Restore, build, format-check, and run the fast tests:

```powershell
.\eng\dotnet.ps1 restore .\ResilientBrowserAutomation.sln
.\eng\dotnet.ps1 build .\ResilientBrowserAutomation.sln `
  --configuration Release --no-restore
.\eng\dotnet.ps1 format .\ResilientBrowserAutomation.sln `
  --verify-no-changes --no-restore
.\eng\dotnet.ps1 test .\ResilientBrowserAutomation.sln `
  --configuration Release --no-build
```

Run the complete containerized browser suite:

```powershell
.\eng\demo-compose.ps1
```

Generated state under `artifacts/` must not be committed.

## Design Expectations

- Domain and application projects must not depend on Playwright, SQLite,
  hosting, or logging implementations.
- Items must be committed before their checkpoint advances.
- Completed `jobId` values must remain idempotent.
- Retry additions require an explicit transient classification and finite
  budget.
- Cancellation must be propagated instead of converted into a generic failure.
- Diagnostics must redact known secrets and must never replace the original
  automation error.

Architecture decisions with a lasting trade-off should be recorded in
`docs/adr/`.

## Test Stand Compatibility

Normal validation uses the exact Test Stand image pinned in
`docker-compose.yml`. Do not replace it with `latest`.

A proposed pin update requires:

1. Test Stand behavior and OpenAPI review.
2. Full Compose browser E2E against the exact published version.
3. Compatibility matrix and changelog updates.
4. A SemVer decision when the worker's public behavior changes.

The scheduled canary is an early-warning signal only. It must not modify the
stable pin.

## Pull Requests

Keep pull requests focused and include:

- the problem and the execution invariant affected;
- tests or reproducible evidence;
- documentation changes for user-visible behavior;
- security and artifact implications; and
- exact Test Stand version when compatibility is involved.

GitHub Actions must pass before merge. Release tags are created only from a
reviewed commit after the release checklist is complete.
