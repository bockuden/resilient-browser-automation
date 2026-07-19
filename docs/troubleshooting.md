# Troubleshooting

## Docker Desktop Is Running But Compose Fails

Run:

```powershell
docker compose config
docker ps
```

If Docker reports a pipe permission error on Windows, restart Docker Desktop
and rerun the command from a normal PowerShell session.

## Playwright Browser Is Missing

Build the worker first, then install the Chromium revision that matches the
pinned `Microsoft.Playwright` package:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/.playwright-browsers"
.\eng\dotnet.ps1 build .\ResilientBrowserAutomation.sln --configuration Release
.\src\Automation.Worker\bin\Release\net10.0\playwright.ps1 install chromium
```

## Local Worker Cannot Reach The Demo Site

Host runs use `localhost` or `127.0.0.1` samples. Docker worker runs use the
Compose service name `demo-site`. Use the matching sample family:

| Environment | Sample prefix |
| --- | --- |
| Host PowerShell | `samples/jobs.playwright.*.jsonl` |
| Docker Compose worker | `samples/jobs.compose.*.jsonl` |

## Repeated Runs Return `AlreadyCompleted: True`

That is expected idempotency. Use a new `jobId` and `run_id`, or reset the demo
state under `artifacts/`.

## Permanent Failure Returns A Non-Zero Exit Code

Exit code `3` means the worker classified at least one job as failed. For the
permanent demo scenario this is expected and should produce:

```text
artifacts/{safe-job-id}/{attempt}/
  error.json
  page.html
  screenshot.png
  trace.zip
```

## Compose Demo Takes A Long Time The First Time

The first worker image build downloads .NET packages, Chromium, FFmpeg, and
Linux browser dependencies. Later runs reuse Docker layers.
