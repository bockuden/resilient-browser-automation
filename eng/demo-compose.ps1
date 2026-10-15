param(
    [switch]$KeepRunning,
    [switch]$IncludeRateLimitScenario,
    [string]$DataPath = "artifacts/docker-demo"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataPath = Join-Path $repoRoot $DataPath
$resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$resolvedData = [System.IO.Path]::GetFullPath($dataPath)
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

if ($IncludeRateLimitScenario) {
    if ($env:TEST_STAND_IMAGE -notmatch ':(\d+\.\d+\.\d+)$' -or
        [version]$Matches[1] -lt [version]'1.2.0') {
        throw "The rate-limit scenario requires TEST_STAND_IMAGE pinned to version 1.2.0 or newer."
    }
}

if (-not $resolvedData.StartsWith(
        "$resolvedArtifactsRoot$([System.IO.Path]::DirectorySeparatorChar)",
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset a path outside the artifacts directory: $resolvedData"
}

$relativeDataPath = [System.IO.Path]::GetRelativePath($resolvedRoot, $resolvedData).Replace("\", "/")
$previousComposeDataPath = $env:DEMO_DATA_PATH
$env:DEMO_DATA_PATH = "./$relativeDataPath"

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

function Invoke-RateLimitDemo {
    $arguments = @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.rate-limit.jsonl")
    Write-Host ""
    Write-Host "== Recover from rate limiting using Retry-After =="
    $output = & docker @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "Rate-limit worker returned exit code $exitCode; expected 0."
    }

    $summaryLine = $output |
        Where-Object { $_ -match '^\s*\{"completedJobs":' } |
        Select-Object -Last 1
    if (-not $summaryLine) {
        throw "Rate-limit worker did not emit its JSON run summary."
    }

    $summary = $summaryLine | ConvertFrom-Json
    if ($summary.completedJobs -ne 1 -or $summary.retriesScheduled -ne 8) {
        throw "Rate-limit run summary reported $($summary.completedJobs) completed jobs and $($summary.retriesScheduled) retries; expected 1 job and 8 retries."
    }
}

function Set-UnixDemoDataPermissions {
    param(
        [string]$Path,
        [switch]$Recursive
    )

    if ($isWindowsPlatform) {
        return
    }

    $arguments = if ($Recursive) {
        @("-R", "a+rwX", "--", $Path)
    }
    else {
        @("a+rwx", "--", $Path)
    }

    & chmod @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to make the generated demo data path writable: $Path"
    }
}

if (Test-Path -LiteralPath $resolvedData) {
    Set-UnixDemoDataPermissions -Path $resolvedData -Recursive
    Remove-Item -LiteralPath $resolvedData -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedData | Out-Null
Set-UnixDemoDataPermissions -Path $resolvedData

try {
    Invoke-DemoCommand "Pull deterministic FastAPI stand" @("compose", "pull", "demo-site")
    Invoke-DemoCommand "Start deterministic FastAPI stand" @("compose", "up", "--detach", "--wait", "demo-site")
    Invoke-DemoCommand "Reset scenario counters" @("compose", "exec", "-T", "demo-site", "python", "-c", "import urllib.request; urllib.request.urlopen('http://localhost:8080/admin/reset', data=b'', timeout=5).read()")

    Invoke-DemoCommand "Run happy-path extraction" @("compose", "run", "--build", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.success.jsonl")
    Invoke-DemoCommand "Report persisted state after first run" @("compose", "run", "--rm", "demo-report")

    Invoke-DemoCommand "Repeat the same jobId to prove idempotency" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.success.jsonl")
    Invoke-DemoCommand "Report persisted state after duplicate delivery" @("compose", "run", "--rm", "demo-report")

    Invoke-DemoCommand "Run transient 503 recovery" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.transient.jsonl")
    if ($IncludeRateLimitScenario) {
        Invoke-RateLimitDemo
        Invoke-DemoCommand "Report persisted state after rate-limit recovery" @("compose", "run", "--rm", "demo-report")
    }
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

    if ($null -eq $previousComposeDataPath) {
        Remove-Item Env:DEMO_DATA_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:DEMO_DATA_PATH = $previousComposeDataPath
    }
}
