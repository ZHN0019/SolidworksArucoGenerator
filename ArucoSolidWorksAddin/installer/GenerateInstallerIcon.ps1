param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$size = 256
$bitmap = New-Object Drawing.Bitmap($size, $size)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
$graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
$graphics.Clear([Drawing.Color]::White)

$borderPen = New-Object Drawing.Pen(
    [Drawing.Color]::FromArgb(214, 45, 54), 12)
$graphics.DrawRectangle($borderPen, 6, 6, 243, 243)

$marker = @(
    '111111',
    '110011',
    '110011',
    '111111',
    '111111',
    '111111'
)
$module = 32
$offset = 32
$blackBrush = [Drawing.Brushes]::Black

for ($row = 0; $row -lt 6; $row++) {
    for ($column = 0; $column -lt 6; $column++) {
        if ($marker[$row][$column] -eq '1') {
            $graphics.FillRectangle(
                $blackBrush,
                $offset + ($column * $module),
                $offset + ($row * $module),
                $module,
                $module)
        }
    }
}

$pngStream = New-Object IO.MemoryStream
$bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()

$fileStream = [IO.File]::Open(
    $outputFullPath,
    [IO.FileMode]::Create,
    [IO.FileAccess]::Write,
    [IO.FileShare]::None)
$writer = New-Object IO.BinaryWriter($fileStream)

try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
}
finally {
    $writer.Dispose()
    $graphics.Dispose()
    $borderPen.Dispose()
    $bitmap.Dispose()
    $pngStream.Dispose()
}

Write-Host "Installer icon created: $outputFullPath"
