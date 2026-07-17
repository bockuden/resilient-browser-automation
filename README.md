# Resilient Browser Automation

A production-style browser automation worker built with C# and .NET 10.

The project demonstrates idempotent job execution, checkpoint-based recovery,
bounded concurrency, structured logging, failure artifacts, and deterministic
end-to-end tests against a local FastAPI test site.

> Status: runnable worker intake with validation, JSON Lines input, typed
> configuration, structured logs, and a deterministic test site. The implementation is
> intentionally split into reviewable milestones in the
> [development plan](docs/development-plan.en.md).

## Why this project

Opening a page and clicking a button proves familiarity with a browser API. This
repository instead focuses on the failure modes that make production browser
automation difficult: interrupted jobs, repeated delivery, transient faults,
DOM drift, duplicates, timeouts, cancellation, and evidence collection.

## Target behavior

The worker will accept a job such as:

```json
{
  "jobId": "catalog-2026-001",
  "target": "demo-catalog",
  "startUrl": "http://demo-site:8080/catalog?scenario=transient&run_id=catalog-2026-001",
  "maxPages": 10
}
```

It will launch Chromium through Playwright, traverse the catalog, persist a
checkpoint after each page, resume after interruption, and store each item only
once. A terminal failure will produce a screenshot, HTML snapshot, trace, and
machine-readable error metadata.

## Architecture

```mermaid
flowchart LR
    Q["Job input"] --> W[".NET worker"]
    W --> A["Application job runner"]
    A --> P["Playwright adapter"]
    A --> S["SQLite repositories"]
    P --> D["FastAPI demo site"]
    A --> F["Failure artifacts"]
    A --> L["Structured logs"]
```

Dependency direction is inward: domain contracts do not reference Playwright,
SQLite, hosting, or logging implementations.

## Run the deterministic test site

Requirements: Docker with the Compose plugin.

```bash
docker compose up --build demo-site
```

Then open `http://localhost:8080/catalog`. Health status is available at
`http://localhost:8080/health`.

Useful deterministic scenarios:

| URL parameter | Behavior |
| --- | --- |
| `scenario=success` | Normal dynamically rendered pagination |
| `scenario=transient&fail_for=2` | First two API requests per page return 503 |
| `scenario=permanent` | Every catalog API request returns 500 |
| `scenario=slow&delay_ms=3000` | Delayed API response |
| `scenario=dom-change` | Alternative DOM nesting and CSS classes |
| `scenario=duplicates` | Repeated item IDs across page boundaries |

Use a unique `run_id` query value to isolate counters between test cases. Reset
all counters with `POST /admin/reset`.

## Repository layout

```text
src/
  Automation.Core/          Domain records and invariants
  Automation.Application/   Use-case boundary and ports
test-stand/                 Deterministic FastAPI browser target
docs/                       Architecture decisions and English execution plan
tests/                      C# test projects added by the implementation plan
```

The complete English plan is tracked at
[`docs/development-plan.en.md`](docs/development-plan.en.md). A Russian working
plan exists locally at `docs/development-plan.ru.md` and is excluded through
the repository-local Git exclude file, not through `.gitignore`.

## Verification available now

```bash
docker compose config
docker compose run --rm demo-site pytest -q
dotnet build --configuration Release
```

The C# build requires the .NET 10 SDK selected by `global.json`.

On this development checkout, SDK `10.0.302` is installed repository-locally in
the ignored `.dotnet` directory. PowerShell users can invoke it without changing
the system PATH:

```powershell
.\eng\dotnet.ps1 --version
.\eng\dotnet.ps1 build .\ResilientBrowserAutomation.sln `
  --configuration Release --no-restore --disable-build-servers '-m:1' `
  '-p:UseSharedCompilation=false'
```

The FastAPI stand also has its own installable package boundary and CLI. See the
[`test-stand` README](test-stand/README.md) and
[ADR 0002](docs/adr/0002-package-ready-test-stand.md).

## Run the worker intake (Milestone 1)

The current worker uses an in-memory repository and a fake browser adapter;
Playwright and SQLite are introduced in later milestones. It accepts one JSON
object per line either from a file or standard input.

```powershell
.\eng\dotnet.ps1 run --project .\src\Automation.Worker\Automation.Worker.csproj `
  -- --jobs .\samples\jobs.success.jsonl
```

The final JSON line is a machine-readable summary. Exit code `0` means every
job completed; `2` means one or more input lines were rejected; `3` means a job
failed; `4` means cancellation. The worker reads settings from
[`appsettings.json`](src/Automation.Worker/appsettings.json); all timeout,
retry, concurrency, storage, and artifact values are validated when it starts.

## Engineering principles

- At-least-once job delivery with exactly-once observable results per `jobId`.
- Checkpoints are committed only after extracted items are durably stored.
- Retries cover explicitly transient failures, never arbitrary exceptions.
- Every timeout and retry budget is finite and configurable.
- Cancellation flows through every asynchronous boundary.
- Integration tests use only the local deterministic site.
- Logs contain identifiers and decisions; artifacts contain page evidence.
- Secrets, local databases, browsers, and generated artifacts never enter Git.

## License

[MIT](LICENSE)
