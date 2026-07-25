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

$installRoot = Join-Path $env:ProgramData 'Codex\ArucoSolidWorksAddin'
$installedDll = Join-Path $installRoot 'ArucoSolidWorksAddin.dll'
$regAsm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
$guid = '{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}'

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

if (Test-Path -LiteralPath $installedDll) {
    & $regAsm $installedDll /unregister
}

Remove-Item -LiteralPath "HKLM:\SOFTWARE\SOLIDWORKS\Addins\$guid" `
    -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath 'HKCU:\SOFTWARE\SOLIDWORKS\AddInsStartup' `
    -Name $guid -Force -ErrorAction SilentlyContinue

$allowedParent = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'Codex'))
$resolvedInstall = [IO.Path]::GetFullPath($installRoot)
if (-not $resolvedInstall.StartsWith(
    $allowedParent + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove unexpected install path: $resolvedInstall"
}
if (Test-Path -LiteralPath $resolvedInstall) {
    Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
}

Write-Host 'ArUco add-in uninstalled.'
