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
        [switch]$AllowFailure
    )

    Write-Host ""
    Write-Host "== $Title =="
    & docker @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Command failed with exit code ${exitCode}: docker $($Arguments -join ' ')"
    }

    if ($AllowFailure -and $exitCode -ne 0) {
        Write-Host "Expected non-zero exit code: $exitCode"
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
    Invoke-DemoCommand "Run duplicate-item scenario" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.duplicates.jsonl")
    Invoke-DemoCommand "Run bounded concurrency sample" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.concurrent.jsonl")
    Invoke-DemoCommand "Run permanent failure and keep evidence" @("compose", "run", "--rm", "worker", "--jobs", "/app/samples/jobs.compose.permanent.jsonl") -AllowFailure

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
