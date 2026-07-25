param(
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $process.ExitCode
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'ArucoSolidWorksAddin.csproj'
$buildOutput = Join-Path $root 'bin\x64\Release\net48'
$installRoot = Join-Path $env:ProgramData 'Codex\ArucoSolidWorksAddin'
$installedDll = Join-Path $installRoot 'ArucoSolidWorksAddin.dll'
$regAsm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
. (Join-Path $PSScriptRoot 'Resolve-SolidWorksInterop.ps1')
$interopPath = Resolve-SolidWorksInteropPath

Write-Host 'Building x64 Release add-in...'
dotnet build $project -c Release -p:Platform=x64 `
    "-p:SolidWorksInteropPath=$interopPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

try {
    $runningSw = [Runtime.InteropServices.Marshal]::GetActiveObject(
        'SldWorks.Application.33')
    if (Test-Path -LiteralPath $installedDll) {
        [void]$runningSw.UnloadAddIn($installedDll)
    }
}
catch {
    $runningSw = $null
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Get-ChildItem -LiteralPath $buildOutput -File |
    Where-Object { $_.Extension -in '.dll', '.pdb' } |
    Copy-Item -Destination $installRoot -Force

if (-not (Test-Path -LiteralPath $installedDll)) {
    throw "Installed DLL was not copied to $installedDll"
}

Write-Host 'Registering COM server and SOLIDWORKS add-in...'
& $regAsm $installedDll /codebase
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($runningSw) {
    $loadResult = $runningSw.LoadAddIn($installedDll)
    Write-Host "Running SOLIDWORKS LoadAddIn result: $loadResult"
}

Write-Host ''
Write-Host 'ArUco add-in installed.'
Write-Host "Location: $installedDll"
Write-Host 'In SOLIDWORKS, use Tools > Add-Ins if the add-in is not already enabled.'
Write-Host 'Open the generator from Tools > ArUco Generator > Generate ArUco.'
