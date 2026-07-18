# Resilient Browser Automation: execution plan

## 1. Portfolio objective

Build a compact, production-style C# repository that provides public evidence
of senior browser-automation engineering. Existing portfolio projects already
show Python, data pipelines, quantitative research, SQLite, testing, and honest
limitations. This repository fills the visible gap: C# architecture and
reliable Playwright automation.

The project is complete when a reviewer can clone it, run one command, observe
a successful catalog extraction, rerun the same job without duplicated output,
force a recoverable failure, resume from a checkpoint, and inspect artifacts
from a terminal failure.

## 2. Scope

### In scope

- .NET 10 LTS and modern asynchronous C#.
- Playwright for .NET controlling Chromium.
- SQLite persistence with explicit migrations.
- Idempotency by `jobId` and item uniqueness by `(jobId, externalId)`.
- Exponential backoff with jitter for classified transient failures.
- Navigation, operation, and whole-job timeouts.
- Checkpoint recovery after each durably stored page.
- Bounded concurrency and per-target rate limiting.
- End-to-end cancellation-token propagation.
- Structured logs, metrics, screenshot, HTML, trace, and JSON error metadata.
- Deterministic FastAPI target in Docker Compose.
- Unit, integration, and end-to-end tests in GitHub Actions.

### Out of scope for version 1

- Distributed queues and multiple worker nodes.
- CAPTCHA or anti-bot evasion, proxy rotation, and third-party site scraping.
- A WinForms UI or generic visual automation designer.

Historical WinForms/CEF/Selenium experience belongs in the repository narrative;
the implementation should demonstrate current worker architecture and Playwright.

## 3. Target structure

```text
src/
  Automation.Core/          Jobs, results, checkpoints, invariants
  Automation.Application/   Use cases, ports, retry/extraction policies
  Automation.Playwright/    Browser session, navigation, extraction, artifacts
  Automation.Storage/       SQLite repositories and migrations
  Automation.Worker/        Host, configuration, job intake
tests/
  Automation.UnitTests/
  Automation.IntegrationTests/
  Automation.EndToEndTests/
test-stand/                 FastAPI app, tests, Dockerfile
docs/                       ADRs and the tracked English plan
```

Dependencies point inward. Core does not reference Playwright, SQLite, hosting,
or logging. Application defines the use case and ports; outer projects implement
the adapters.

## 4. Milestones

Each milestone should be one small pull request or clearly named commit. Do not
combine Playwright, SQLite, retry behavior, and concurrency in one change.

### Milestone 0 — repository contract and deterministic target

Status in the initial scaffold: complete.

1. Initialize Git with `main` as the default branch.
2. Add .NET 10 solution metadata, strict compiler defaults, MIT license, and CI.
3. Add core job, result, and checkpoint records with input validation.
4. Define ports for browser sessions, jobs, checkpoints, and failure artifacts.
5. Add the FastAPI target and Docker Compose entry point.
6. Test health, login, dynamic catalog, transient failure, and duplicate data.
7. Keep the Russian plan local through `.git/info/exclude`.

Acceptance:

- `docker compose config` succeeds.
- `docker compose run --rm demo-site pytest -q` succeeds.
- `dotnet build -c Release` succeeds with .NET 10.
- `git check-ignore -v docs/development-plan.ru.md` names `.git/info/exclude`.

Commit: `chore: scaffold automation architecture and deterministic test site`.

### Milestone 1 — validated input and worker host

Status: complete.

1. Add JSON deserialization and validation at the host boundary.
2. Add typed browser, retry, timeout, concurrency, storage, and artifact settings.
3. Wire dependencies with `Microsoft.Extensions.Hosting`.
4. Read deterministic jobs from JSON Lines or standard input.
5. Add a correlation scope containing `jobId`, `target`, and execution attempt.
6. Return meaningful exit codes for completed, rejected, failed, and cancelled jobs.

Acceptance: a valid sample reaches a fake browser adapter; invalid URL, empty ID,
or `maxPages` outside 1–100 is rejected before browser startup; Ctrl+C cancels
the active job without an unhandled exception.

Commit: `feat: add validated job input and generic worker host`.

### Milestone 2 — SQLite idempotency and checkpoints

Status: complete.

1. Create `jobs`, `checkpoints`, `catalog_items`, and `job_attempts` tables.
2. Make `jobs.job_id` the primary key and `(job_id, external_id)` unique.
3. Use explicit transactions: item upsert first, checkpoint update second.
4. Add a migration runner and schema-version table.
5. Implement `Pending -> Running -> Completed|Failed|Cancelled` transitions.
6. Define restart behavior for a stale `Running` job.

Acceptance: repeating a completed `jobId` does not open a browser; replaying a
page adds no duplicate items; a crash after page 2 resumes at page 3; concurrent
claims for the same ID allow only one active execution.

Commit: `feat: persist idempotent jobs and transactional checkpoints`.

### Milestone 3 — Playwright catalog extraction

Status: complete.

1. Own one Chromium lifecycle in the worker and isolate each job in a context.
2. Navigate to `startUrl` and wait for application state, not arbitrary sleeps.
3. Use role, label, and `data-testid` locators, not styling classes.
4. Extract ID, name, price, page, and source URL into typed records.
5. Traverse until no next page or `maxPages` is reached.
6. Support the demo login without writing credentials to logs.
7. Dispose pages, contexts, browser, and Playwright on every exit path.

Acceptance: success extracts 20 unique items over four pages; DOM-change returns
the same records; the protected catalog works after login; no test uses an
external network target.

Commit: `feat: extract paginated catalogs with Playwright`.

### Milestone 4 — classified retries and finite timeout budgets

1. Classify 408, 429, 502, 503, 504, selected navigation timeouts, and browser disconnect as transient.
2. Classify validation, authentication, unsupported DOM contract, and exhausted retries as permanent.
3. Implement exponential delay with bounded jitter and injectable time/random providers.
4. Respect `Retry-After` only when it fits the remaining job budget.
5. Separate navigation, page-attempt, and whole-job timeouts.
6. Log retry reason, number, delay, and remaining budget.

Acceptance: two deterministic 503 responses recover on attempt 3; permanent
failure cannot retry forever; cancellation interrupts backoff; unit tests do not
wait in real time.

Commit: `feat: add classified retries and finite timeout budgets`.

### Milestone 5 — evidence and observability

1. Configure JSON console logs with stable event IDs.
2. Add OpenTelemetry metrics for jobs, pages, retries, duration, and failures.
3. On terminal browser failure, store screenshot, HTML, trace, and `error.json`.
4. Use `artifacts/{safe-job-id}/{attempt}/` and an atomically written manifest.
5. Redact passwords, cookies, authorization headers, and sensitive query values.
6. Add retention by age and total size.

Acceptance: permanent failure writes all available evidence; artifact-capture
failure does not hide the original exception; logs reconstruct one job without
exposing secrets; metrics distinguish completed, idempotently skipped, failed,
and cancelled runs.

Commit: `feat: capture redacted failure evidence and telemetry`.

### Milestone 6 — bounded concurrency and rate limiting

1. Read jobs through a bounded `Channel<T>`.
2. Limit active contexts with `SemaphoreSlim` or a concurrency limiter.
3. Apply a per-target token-bucket rate limiter.
4. Preserve independent cancellation and correlation scope per job.
5. On shutdown, stop intake, wait within a budget, then cancel remaining work.

Acceptance: 20 jobs never exceed configured concurrency; one slow job does not
block unrelated work once capacity exists; same-target requests respect the
rate; shutdown leaves checkpoints and no orphaned browser processes.

Commit: `feat: add bounded job concurrency and per-target limits`.

### Milestone 7 — complete Docker Compose demo

1. Add a worker Dockerfile using an official Playwright .NET image or explicit browser dependencies.
2. Extend Compose with `worker`, `demo-site`, SQLite storage, and artifact volumes.
3. Add sample jobs for success, retry, resume, duplicate, cancellation, and failure.
4. Add a one-command demo that resets state, runs scenarios, and prints outputs.
5. Add health checks and non-root execution where supported.

Acceptance: Compose demonstrates the happy path; a second identical run reports
idempotent completion and keeps 20 rows; an interrupted run resumes from its
last durable page; host-side files have predictable ownership.

Commit: `feat: package the complete local automation demo`.

### Milestone 8 — CI, documentation, and release proof

1. Keep fast unit tests in the main job and browser E2E in a separate job.
2. Install only Chromium and its required Linux dependencies in E2E CI.
3. Cache NuGet packages, not browser profiles or generated user data.
4. Upload failure artifacts only when E2E fails.
5. Add architecture, failure matrix, security, troubleshooting, and limitations.
6. Add a short terminal demo, architecture diagram, and sample artifact tree.
7. Tag `v1.0.0` only after clean-clone checks on Windows and Linux/container paths.

Acceptance: CI runs analyzers, build, unit, integration, E2E, and FastAPI tests;
workflow permissions remain read-only; README reaches a visible result in under
ten minutes; limitations avoid anti-bot or production-scale claims.

Commit: `docs: publish reproducible v1 automation evidence`.

## 5. Required test matrix

| Test | Layer | Main proof |
| --- | --- | --- |
| `SuccessfulCatalogExtraction` | E2E | Four pages produce 20 typed items |
| `RetriesTransientNavigationFailure` | Integration/E2E | Classified 503 failures recover |
| `DoesNotDuplicateCompletedJob` | Integration | Re-delivery is idempotent |
| `ResumesFromLastCheckpoint` | Integration/E2E | Restart skips durable pages |
| `StopsAfterCancellation` | Integration | Cancellation is timely and persisted |
| `StoresScreenshotOnPermanentFailure` | E2E | Terminal evidence exists |
| `HonorsMaximumConcurrency` | Integration | Active sessions remain bounded |
| `RejectsInvalidJobConfiguration` | Unit | Invalid work never opens a browser |
| `DoesNotCheckpointBeforeItemCommit` | Integration | Transaction ordering prevents loss |
| `RedactsSecretsFromArtifacts` | Integration | Diagnostics do not leak credentials |

## 6. Failure matrix

| Failure | Classification | Action | Evidence |
| --- | --- | --- | --- |
| HTTP 503/429 | Transient | Backoff and retry within budget | Retry metadata |
| Navigation timeout | Transient, bounded | Recreate page if needed and retry | Trace on exhaustion |
| Invalid job | Permanent | Reject before claim | Validation errors |
| Authentication rejected | Permanent | No credential retry loop | Redacted error JSON |
| Missing stable locator | Permanent contract drift | Capture DOM and screenshot | Failure bundle |
| Duplicate item | Expected condition | Upsert by stable key | Duplicate counter |
| Process interruption | Recoverable | Resume after checkpoint | Attempt history |
| Cancellation | Terminal, non-error | Stop and persist cancelled | Structured event |

## 7. Review loop for every milestone

1. Write or update the acceptance test first.
2. Make the smallest implementation that passes it.
3. Run formatting, build, unit tests, and the relevant scenario.
4. Inspect `git diff` for generated data, secrets, or accidental artifacts.
5. Update README status and an ADR if architecture changed.
6. Commit with a message describing externally visible behavior.

## 8. Release checklist

- Clean clone works with documented prerequisites.
- `global.json` and CI use the same .NET major version.
- The database starts empty and is created by migrations.
- Submitting the same `jobId` twice cannot duplicate results.
- Recovery is demonstrated with a real scenario, not only mocked.
- Transient and permanent failures are visibly different in logs.
- Cancellation completes within the documented shutdown budget.
- Failure artifacts are useful and redacted.
- Browser processes and contexts are always disposed.
- No test depends on a public website or live credential.
- The Russian plan is absent from `git ls-files` and ignored by `.git/info/exclude`.
