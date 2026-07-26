param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$configuredDotnet = if ($env:RESILIENT_BROWSER_AUTOMATION_DOTNET_ROOT) {
    Join-Path $env:RESILIENT_BROWSER_AUTOMATION_DOTNET_ROOT 'dotnet.exe'
}

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
}
elseif ($configuredDotnet -and (Test-Path -LiteralPath $configuredDotnet)) {
    $dotnet = $configuredDotnet
}
else {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnet = if ($systemDotnet) { $systemDotnet.Source } else { $null }
}

if (-not $dotnet) {
    throw "No .NET host was found. Install the SDK selected by global.json, add it to PATH, or set RESILIENT_BROWSER_AUTOMATION_DOTNET_ROOT."
}

$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-cli-home'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $repoRoot '.nuget\http-cache'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = '0'

& $dotnet @Arguments
exit $LASTEXITCODE
