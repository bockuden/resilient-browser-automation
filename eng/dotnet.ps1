param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Local .NET SDK was not found at '$dotnet'. Install the SDK selected by global.json."
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

