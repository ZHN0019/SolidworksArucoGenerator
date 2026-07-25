function Resolve-SolidWorksInteropPath {
    $requiredFiles = @(
        'SolidWorks.Interop.sldworks.dll',
        'SolidWorks.Interop.swconst.dll',
        'SolidWorks.Interop.swpublished.dll'
    )

    $candidates = New-Object Collections.Generic.List[string]
    if ($env:SOLIDWORKS_INTEROP_PATH) {
        $candidates.Add($env:SOLIDWORKS_INTEROP_PATH)
    }

    $solidWorksRegistryRoot = 'HKLM:\SOFTWARE\SOLIDWORKS'
    if (Test-Path -LiteralPath $solidWorksRegistryRoot) {
        $versionKeys = Get-ChildItem -LiteralPath $solidWorksRegistryRoot |
            Where-Object { $_.PSChildName -match '^SOLIDWORKS \d{4}$' } |
            Sort-Object PSChildName -Descending
        foreach ($versionKey in $versionKeys) {
            $setupPath = Join-Path $versionKey.PSPath 'Setup'
            $installFolder = (Get-ItemProperty -LiteralPath $setupPath `
                -ErrorAction SilentlyContinue).'SolidWorks Folder'
            if ($installFolder) {
                $candidates.Add($installFolder)
                $candidates.Add((Join-Path $installFolder 'api\redist'))
            }
        }
    }

    $standardRoot = Join-Path $env:ProgramFiles 'SOLIDWORKS Corp\SOLIDWORKS'
    $candidates.Add((Join-Path $standardRoot 'api\redist'))
    $candidates.Add($standardRoot)

    foreach ($candidate in $candidates |
        Where-Object { $_ } |
        Select-Object -Unique) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        $allFound = $true
        foreach ($requiredFile in $requiredFiles) {
            if (-not (Test-Path -LiteralPath (Join-Path $resolved $requiredFile) `
                -PathType Leaf)) {
                $allFound = $false
                break
            }
        }
        if ($allFound) {
            return $resolved
        }
    }

    throw @'
SOLIDWORKS interop assemblies were not found. Set SOLIDWORKS_INTEROP_PATH
to a folder containing SolidWorks.Interop.sldworks.dll,
SolidWorks.Interop.swconst.dll, and SolidWorks.Interop.swpublished.dll.
'@
}
