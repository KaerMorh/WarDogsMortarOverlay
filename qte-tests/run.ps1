param(
    [string]$Samples = '',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$Samples) { $Samples = Join-Path $projectRoot 'test' }
if (!$Output) { $Output = Join-Path $projectRoot 'artifacts/qte-tests' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
& $sdk run --project (Join-Path $PSScriptRoot 'QteTests.csproj') -c Release -- $Samples $Output
if ($LASTEXITCODE -ne 0) { throw "QTE tests failed: exit $LASTEXITCODE" }
