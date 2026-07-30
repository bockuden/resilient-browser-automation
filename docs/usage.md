# Running and Operating the Worker

This guide contains the detailed local, Docker, and worker execution behavior.
For architecture and failure classification, see
[architecture.md](architecture.md) and [failure-matrix.md](failure-matrix.md).

## Requirements

The complete Compose demo requires Docker Desktop or another Docker Engine with
the Compose plugin. It builds the worker image with .NET 10 and downloads the
published Test Stand image, so neither a host .NET SDK nor Python is required.

Host execution requires the .NET 10 SDK selected by `global.json`. Install SDK
`10.0.302` or a compatible patch. The PowerShell wrapper prefers the
repository-local ignored `.dotnet` SDK when present; CI or another SDK location
can instead set `RESILIENT_BROWSER_AUTOMATION_DOTNET_ROOT`.

## Complete Docker Compose Demo

Run the full deterministic suite:

```powershell
.\eng\demo-compose.ps1
```

The script resets `artifacts/docker-demo`, pulls and starts the exact Test Stand
image configured in Compose, and builds the worker image. It then exercises:

- successful extraction and idempotent repeated delivery;
- transient `503` recovery and the natural end of pagination;
- duplicate items and bounded concurrency;
- a page-3 failure followed by resume from the page-2 checkpoint;
- graceful cancellation; and
- permanent failure with inspectable evidence.

The final report prints SQLite job, item, and checkpoint state. Unless
`-KeepRunning` is supplied, the script stops the Compose services when it
finishes.

Terminal evidence is written as:

```text
artifacts/docker-demo/artifacts/compose-permanent/1/
  error.json
  page.html
  screenshot.png
  trace.zip
```

## Published Deterministic Test Stand

The FastAPI stand is maintained in the independent
[Resilient Automation Test Stand repository](https://github.com/bockuden/resilient-automation-test-stand).
This repository does not build or import its Python sources. By default,
Compose pulls the exact compatibility-tested image:

```text
ghcr.io/bockuden/resilient-automation-test-stand:1.1.3
```

Start only the target:

```bash
docker compose pull demo-site
docker compose up --detach --wait demo-site
```

Open `http://localhost:8080/catalog`; health status is available at
`http://localhost:8080/health`.

Useful deterministic scenarios:

| URL parameter | Behavior |
| --- | --- |
| `scenario=success` | Normal dynamically rendered pagination |
| `scenario=transient&fail_for=2` | First two API requests per page return `503` |
| `scenario=permanent` | Every catalog API request returns `500` |
| `scenario=slow&delay_ms=3000` | Delayed API response |
| `scenario=resume&fail_page=3` | Pages 1–2 succeed and page 3 fails |
| `scenario=dom-change` | Alternative DOM nesting and CSS classes |
| `scenario=duplicates` | Repeated item IDs across page boundaries |

Use a unique `run_id` query value to isolate scenario counters. Reset all
counters with `POST /admin/reset`.

`TEST_STAND_IMAGE` can override the Compose image for an explicit compatibility
check. Normal demos and release validation must leave it unset and use the
reviewed stable pin:

```powershell
$env:TEST_STAND_IMAGE = "ghcr.io/bockuden/resilient-automation-test-stand:1.2.0"
.\eng\demo-compose.ps1
Remove-Item Env:TEST_STAND_IMAGE
```

The scheduled canary uses this override with the latest published release. It
never edits `docker-compose.yml` or advances the stable pin automatically.

## Host Build and Tests

PowerShell users can invoke the selected SDK without changing the system PATH:

```powershell
.\eng\dotnet.ps1 --version
.\eng\dotnet.ps1 restore .\ResilientBrowserAutomation.sln
.\eng\dotnet.ps1 build .\ResilientBrowserAutomation.sln `
  --configuration Release --no-restore
.\eng\dotnet.ps1 test .\ResilientBrowserAutomation.sln `
  --configuration Release --no-build
```

Install the Chromium revision paired with the pinned Playwright package:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/.playwright-browsers"
.\src\Automation.Worker\bin\Release\net10.0\playwright.ps1 install chromium
```

Start the stand, then run a host sample:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/.playwright-browsers"
.\eng\dotnet.ps1 run `
  --project .\src\Automation.Worker\Automation.Worker.csproj `
  --configuration Release --no-build `
  -- --jobs .\samples\jobs.playwright.success.jsonl
```

## Input, Output, and Persistence

The worker accepts one JSON object per line from a file or standard input. A
first-record UTF-8 BOM is supported. A job contains `jobId`, `target`,
`startUrl`, and `maxPages`.

The final JSON line is a machine-readable summary. Process exit codes are:

| Code | Meaning |
| --- | --- |
| `0` | Every accepted job completed |
| `2` | One or more input lines were rejected |
| `3` | One or more jobs failed |
| `4` | Execution was cancelled |

SQLite stores jobs, execution attempts, typed catalog items, and checkpoints.
Each item contains its external ID, name, price, source page number, and source
URL. A completed `jobId` is returned idempotently without opening Chromium.
Interrupted `Running` jobs can be reclaimed after
`Automation:Storage:StaleRunningJobSeconds`.

All retry, timeout, concurrency, storage, and artifact settings are validated
at startup from
[`appsettings.json`](../src/Automation.Worker/appsettings.json).

## Browser Interaction

The worker owns one Chromium lifecycle and gives every active job an isolated
browser context. It uses `data-testid` and role/label locators so the
`dom-change` scenario can alter CSS nesting without changing the result.

For protected scenarios, the worker fills the labelled username and password
fields and clicks `Sign in`. It then reads catalog item cards and clicks
`Next page` until the control is absent or `maxPages` is reached. Demo
credentials are typed settings and are never written to logs.

## Retry and Timeout Behavior

HTTP `408`, `429`, `502`, `503`, and `504`, selected browser timeouts, and
browser disconnects are transient. The current page is retried with bounded
exponential backoff and jitter. `Retry-After` is preferred when it fits the
remaining whole-job budget.

A permanent HTTP `500` or an invalid DOM contract is not retried. Retry logs
include the reason, next attempt, delay, and remaining budget. Cancellation
interrupts backoff immediately.

## Failure Evidence and Observability

Terminal browser failures write a redacted bundle to
`artifacts/{safe-job-id}/{attempt}/`. Metadata is written atomically, and a
diagnostic collection failure never replaces the original automation error.
Retention is controlled by `Automation:Artifacts:RetentionDays` and
`MaximumTotalSizeMegabytes`.

JSON logs use stable event IDs for input rejection, completion, cancellation,
job failure, retry scheduling, and artifact creation. .NET `Meter` instruments
include job counters, completed pages, retries, idempotently skipped jobs, and
job duration.

## Concurrency and Rate Limiting

Worker intake uses a bounded channel. `MaxConcurrentJobs` controls active jobs,
and each job keeps its own log scope with `jobId`, `target`,
`executionAttempt`, and `workerId`.

A per-target token bucket controls job starts. During shutdown, intake stops
first; active jobs receive the configured grace period before cancellation.
