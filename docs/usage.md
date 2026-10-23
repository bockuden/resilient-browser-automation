# Running and Operating the Worker

This guide contains the detailed local, Docker, and worker execution behavior.
For architecture and failure classification, see
[architecture.md](architecture.md) and [failure-matrix.md](failure-matrix.md).

## Requirements

The complete Compose demo requires Docker Desktop or another Docker Engine with
the Compose plugin. It builds the worker image with .NET 10 and downloads the
published Test Stand image, so neither a host .NET SDK nor Python is required.

Host execution requires the .NET 10 SDK selected by `global.json`. Install SDK
`10.0.302` or a newer .NET 10 feature band. The PowerShell wrapper prefers the
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

With `-IncludeRateLimitScenario` and an explicit Test Stand 1.2.0-or-newer image
override, it also checks `429` recovery across four pages and asserts the final
summary contains eight scheduled retries.

The final report prints SQLite job, item, and checkpoint state. Unless
`-KeepRunning` is supplied, the script stops the Compose services when it
finishes. By default it resets `artifacts/docker-demo`; pass `-DataPath` with
another path under `artifacts/` to keep an existing run's output intact.

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
ghcr.io/bockuden/resilient-automation-test-stand:1.1.5
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
| `scenario=rate-limit&rate_limit_for=2&retry_after_seconds=1` | On Test Stand 1.2.0+, first two API requests per page return `429` with `Retry-After`, then succeed |
| `scenario=malformed-api&malformed_mode=invalid_json` | Test Stand 1.2.0 returns a successful response containing invalid JSON |
| `scenario=malformed-api&malformed_mode=truncated_json` | Test Stand 1.2.0 returns a truncated JSON response |
| `scenario=malformed-api&malformed_mode=wrong_content_type` | Test Stand 1.2.0 returns JSON with a non-JSON media type |
| `scenario=malformed-api&malformed_mode=missing_fields` | Test Stand 1.2.0 omits a required catalog response field |
| `scenario=malformed-api&malformed_mode=wrong_field_type` | Test Stand 1.2.0 returns a catalog field with the wrong JSON type |
| `scenario=permanent` | Every catalog API request returns `500` |
| `scenario=slow&delay_ms=3000` | Delayed API response |
| `scenario=resume&fail_page=3` | Pages 1–2 succeed and page 3 fails |
| `scenario=dom-change` | Alternative DOM nesting and CSS classes |
| `scenario=duplicates` | Repeated item IDs across page boundaries |

Use a unique `run_id` query value to isolate scenario counters. Reset all
counters with `POST /admin/reset`.

The worker validates successful catalog API responses before exposing them to
the page parser. Malformed JSON, an unexpected media type, missing contract
fields, and incorrectly typed fields fail the job with a specific reason,
preserve browser failure evidence, and do not commit items or a page checkpoint.
The Compose compatibility workflow exercises all five malformed response
modes against the exact Test Stand 1.2.0 image.

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

Test Stand 1.2.0 adds rate limiting with `429` and `Retry-After`. The browser
E2E workflow runs this scenario against the exact 1.2.0 image; the weekly
canary also includes it when the latest release is 1.2.0 or newer. To run the
extended Compose demo locally, set an exact 1.2.0-or-newer image override and
pass `-IncludeRateLimitScenario`:

```powershell
$env:TEST_STAND_IMAGE = "ghcr.io/bockuden/resilient-automation-test-stand:1.2.0"
.\eng\demo-compose.ps1 -IncludeRateLimitScenario
Remove-Item Env:TEST_STAND_IMAGE
```

The sample triggers two `429` responses on each of four pages. The worker
waits for the one-second `Retry-After` value, then completes all 20 items. Its
final JSON run summary reports eight scheduled retries. See
[`jobs.playwright.rate-limit.jsonl`](../samples/jobs.playwright.rate-limit.jsonl)
and [`jobs.compose.rate-limit.jsonl`](../samples/jobs.compose.rate-limit.jsonl).

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
credentials are typed settings and are never written to URLs or logs. If a
protected page redirects to `/login` after session expiry, the worker signs in
again and waits for the interrupted page to load before continuing, preserving
the completed-page checkpoint.

The Test Stand 1.2.0 session-expiry sample exercises this flow after page 1:
[`jobs.compose.session-expiry.jsonl`](../samples/jobs.compose.session-expiry.jsonl).
The Compose compatibility workflow verifies the resumed checkpoint and all 20
items across four pages.

The same workflow runs the complete selector-failure matrix from
[`jobs.playwright.selector-failures.jsonl`](../samples/jobs.playwright.selector-failures.jsonl):
`missing`, `changed`, and `delayed` targets recover through the target's
configured token or a unique semantic fallback. `multiple`, `hidden`, and
`disabled` targets fail explicitly and retain the error metadata, page HTML,
screenshot, and browser trace. A separate run starts the stand with
[`selector-tokens.toml`](../eng/selector-tokens.toml) and verifies a complete
four-page extraction using its custom `data-testid` tokens.

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
