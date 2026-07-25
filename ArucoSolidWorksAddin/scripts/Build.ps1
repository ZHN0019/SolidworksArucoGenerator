param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path (Split-Path -Parent $root) 'ArucoSolidWorksAddin.TestHost'
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
. (Join-Path $PSScriptRoot 'Resolve-SolidWorksInterop.ps1')
$interopPath = Resolve-SolidWorksInteropPath
$buildProperties = @(
    '-p:Platform=x64',
    "-p:SolidWorksInteropPath=$interopPath"
)

dotnet build (Join-Path $root 'ArucoSolidWorksAddin.csproj') `
    -c $Configuration @buildProperties
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $testRoot 'ArucoSolidWorksAddin.TestHost.csproj') `
    -c $Configuration @buildProperties
exit $LASTEXITCODE
