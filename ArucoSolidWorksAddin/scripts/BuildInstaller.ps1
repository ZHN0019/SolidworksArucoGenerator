param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $root 'installer'
$distRoot = Join-Path $installerRoot 'dist'
$iconPath = Join-Path $installerRoot 'assets\ArucoInstaller.ico'
$issPath = Join-Path $installerRoot 'ArucoSolidWorksAddin.iss'
$projectPath = Join-Path $root 'ArucoSolidWorksAddin.csproj'
$buildOutput = Join-Path $root "bin\x64\$Configuration\net48"
$outputBaseName = 'SOLIDWORKS_ArUco_Generator_Setup_1.1.0_x64'
$setupPath = Join-Path $distRoot ($outputBaseName + '.exe')

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not $SkipBuild) {
    . (Join-Path $PSScriptRoot 'Resolve-SolidWorksInterop.ps1')
    $interopPath = Resolve-SolidWorksInteropPath
    dotnet build $projectPath -c $Configuration -p:Platform=x64 `
        "-p:SolidWorksInteropPath=$interopPath"
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$requiredFiles = @(
    'ArucoSolidWorksAddin.dll',
    'SolidWorks.Interop.sldworks.dll',
    'SolidWorks.Interop.swconst.dll',
    'SolidWorks.Interop.swpublished.dll'
)
foreach ($name in $requiredFiles) {
    $path = Join-Path $buildOutput $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required installer input is missing: $path"
    }
}

& (Join-Path $installerRoot 'GenerateInstallerIcon.ps1') -OutputPath $iconPath

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
)
$compiler = $compilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
    Select-Object -First 1
if (-not $compiler) {
    throw @'
Inno Setup 7 was not found. Install the official x64 compiler with:
winget install --id JRSoftware.InnoSetup.7 -e --source winget
'@
}

$resolvedInstallerRoot = [IO.Path]::GetFullPath($installerRoot)
$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot)
if (-not $resolvedDistRoot.StartsWith(
    $resolvedInstallerRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected output path: $resolvedDistRoot"
}
if (Test-Path -LiteralPath $resolvedDistRoot) {
    Remove-Item -LiteralPath $resolvedDistRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedDistRoot -Force | Out-Null

& $compiler /Qp $issPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Inno Setup did not create the expected file: $setupPath"
}

$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($setupPath))"
Set-Content -LiteralPath (Join-Path $distRoot ($outputBaseName + '.sha256')) `
    -Value $hashLine -Encoding ascii

$signature = Get-AuthenticodeSignature -LiteralPath $setupPath
$manifest = [ordered]@{
    product = 'SOLIDWORKS ArUco Part Generator'
    version = '1.1.0'
    architecture = 'x64'
    target = 'SOLIDWORKS 2025 or compatible'
    framework = '.NET Framework 4.8'
    file = [IO.Path]::GetFileName($setupPath)
    size_bytes = (Get-Item -LiteralPath $setupPath).Length
    sha256 = $hash.Hash.ToLowerInvariant()
    authenticode_status = [string]$signature.Status
    built_at = (Get-Date).ToString('o')
}
$manifest | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $distRoot 'package-manifest.json') `
        -Encoding utf8

Write-Host ''
Write-Host 'Installer package created.'
Write-Host "Setup:  $setupPath"
Write-Host "SHA256: $($hash.Hash.ToLowerInvariant())"
Write-Host "Signature status: $($signature.Status)"
