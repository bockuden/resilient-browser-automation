param(
    [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataPath = Join-Path $repoRoot "artifacts/docker-demo"
$resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedData = [System.IO.Path]::GetFullPath($dataPath)

if (-not $resolvedData.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset a path outside the repository: $resolvedData"
}

function Invoke-DemoCommand {
    param(
        [string]$Title,
        [string[]]$Arguments,
        [int]$ExpectedExitCode = 0
    )

    Write-Host ""
    Write-Host "== $Title =="
    & docker @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExitCode) {
        throw "Command returned exit code ${exitCode}; expected ${ExpectedExitCode}: docker $($Arguments -join ' ')"
    }
}

function Invoke-CancellationDemo {
    $containerName = "resilient-browser-automation-cancel-$PID"
    try {
        Invoke-DemoCommand "Start cancellable slow job" @(
            "compose", "run", "--detach", "--name", $containerName,
            "-e", "Automation__Concurrency__ShutdownGracePeriodSeconds=2",
            "worker", "--jobs", "/app/samples/jobs.compose.cancel.jsonl"
        )
        Start-Sleep -Seconds 5
        Invoke-DemoCommand "Request graceful cancellation" @("stop", "--timeout", "15", $containerName)
        Invoke-DemoCommand "Print cancelled worker logs" @("logs", $containerName)

        $exitCode = & docker inspect --format "{{.State.ExitCode}}" $containerName
        if ($LASTEXITCODE -ne 0 -or [int]$exitCode -ne 4) {
            throw "Cancelled worker returned exit code $exitCode; expected 4."
        }
    }
    finally {
        & docker rm --force $containerName 2>$null | Out-Null
    }
}

if (Test-Path -LiteralPath $resolvedData) {
    Remove-Item -LiteralPath $resolvedData -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedData | Out-Null

try {
    Invoke-DemoCommand "Build and start deterministic FastAPI stand" @("compose", "up", "--build", "--detach", "--wait", "demo-site")
    Invoke-DemoCommand "Reset scenario counters" @("compose", "exec", "-T", "demo-site", "python", "-c", "import urllib.request; urllib.request.urlopen('http://localhost:8080/admin/reset', data=b'', timeout=5).read()")

    Invoke-DemoCommand "Run happy-path extraction" @("compose", "run", "--build", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.success.jsonl")
    Invoke-DemoCommand "Report persisted state after first run" @("compose", "run", "--rm", "demo-report")

    Invoke-DemoCommand "Repeat the same jobId to prove idempotency" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.success.jsonl")
    Invoke-DemoCommand "Report persisted state after duplicate delivery" @("compose", "run", "--rm", "demo-report")

    Invoke-DemoCommand "Run transient 503 recovery" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.transient.jsonl")
    Invoke-DemoCommand "Stop at the catalog's natural end" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.natural-end.jsonl")
    Invoke-DemoCommand "Run duplicate-item scenario" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.duplicates.jsonl")
    Invoke-DemoCommand "Run bounded concurrency sample" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.concurrent.jsonl")
    Invoke-DemoCommand "Fail on page 3 after durable checkpoints" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.resume.fail.jsonl") -ExpectedExitCode 3
    Invoke-DemoCommand "Report checkpoint before resume" @("compose", "run", "--rm", "demo-report")
    Invoke-DemoCommand "Resume the same jobId from page 3" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.resume.success.jsonl")
    Invoke-CancellationDemo
    Invoke-DemoCommand "Run permanent failure and keep evidence" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.permanent.jsonl") -ExpectedExitCode 3

    Invoke-DemoCommand "Final persisted state" @("compose", "run", "--rm", "demo-report")

    Write-Host ""
    Write-Host "== Failure artifacts =="
    if (Test-Path -LiteralPath (Join-Path $resolvedData "artifacts")) {
        Get-ChildItem -LiteralPath (Join-Path $resolvedData "artifacts") -Recurse -File |
            Select-Object -ExpandProperty FullName
    }
}
finally {
    if (-not $KeepRunning) {
        Invoke-DemoCommand "Stop Compose services" @("compose", "down")
    }
}
