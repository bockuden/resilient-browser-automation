# Automation Gauntlet evidence

The compatibility workflow tests the exact released Test Stand image
`ghcr.io/bockuden/resilient-automation-test-stand:1.2.0`. Portable evidence is
stored under [`samples/gauntlet`](../samples/gauntlet/) and checked with the
stand's separately installed `automation-gauntlet` validator.

## Coverage

| Level | Objective | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Complete pagination | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 2 | Bounded transient retries | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 3 | Protected catalog authentication | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 4 | Selector change recovery | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 5 | Cross-page deduplication | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 6 | Checkpoint and resume | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 7 | Session expiry recovery | Supported | [Level 7 JSON](../samples/gauntlet/level-7-session-expiry.json) |
| 8 | Rate-limit recovery | Supported | [Level 8 JSON](../samples/gauntlet/level-8-rate-limiting.json) |
| 9 | Multiple API failures | Unsupported | Exact Gauntlet evidence is not collected yet. |
| 10 | Composite recovery | Unsupported | Exact Gauntlet evidence is not collected yet. |

The machine-readable status list is
[`coverage.json`](../samples/gauntlet/coverage.json). Unsupported means the
compatibility workflow does not yet collect and validate the exact level
objective; it does not claim that adjacent worker behavior is absent. The
coverage list changes only when a reproducible scenario and validated evidence
are added.

Level 7 expires the protected session after page 2. CI checks the successful
initial authentication and the single re-authentication, then verifies all four
pages and their 20 persisted items. Level 8 applies two `429` responses to each
of three pages; CI checks six one-second retry waits, the completed checkpoint,
and all 15 persisted items. The evidence records each page's observed status
sequence and deterministic item IDs.

## Reproduce locally

Start the exact stand image in one terminal:

```powershell
$env:TEST_STAND_IMAGE = "ghcr.io/bockuden/resilient-automation-test-stand:1.2.0"
docker compose up --detach --wait demo-site
```

In another terminal, use a clean evidence database and run the two compatibility
jobs:

```powershell
$env:Automation__Storage__ConnectionString = "Data Source=artifacts/gauntlet-evidence.db"
$env:Automation__Artifacts__RootPath = "artifacts/gauntlet-evidence"
dotnet run --project src/Automation.Worker/Automation.Worker.csproj --configuration Release -- --jobs samples/jobs.playwright.session-expiry.jsonl
dotnet run --project src/Automation.Worker/Automation.Worker.csproj --configuration Release -- --jobs samples/jobs.playwright.rate-limit.jsonl
```

Install the validator from the same released package and validate both files:

```powershell
python -m pip install resilient-automation-test-stand==1.2.0
automation-gauntlet validate samples/gauntlet/level-7-session-expiry.json
automation-gauntlet validate samples/gauntlet/level-8-rate-limiting.json
```

Each validation prints a JSON report and returns a nonzero exit code if the
evidence is invalid or the objective is unmet. The GitHub Actions compatibility
workflow performs these validations after the live browser scenarios.
