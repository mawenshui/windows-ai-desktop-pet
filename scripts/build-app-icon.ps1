[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

Add-Type -AssemblyName System.Drawing.Common

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$sourcePath = Join-Path $projectRoot 'assets\pets\RGS_8Directional\frames\hero\idle_down_01.png'
$outputDirectory = Join-Path $projectRoot 'assets\icons'
$pngPath = Join-Path $outputDirectory 'windows-ai-desktop-pet.png'
$icoPath = Join-Path $outputDirectory 'windows-ai-desktop-pet.ico'
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-IconBitmap {
    param([int]$Size)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $pad = [Math]::Max(1, [int]($Size * 0.06))
        $radius = [Math]::Max(2, [int]($Size * 0.22))
        $diameter = $radius * 2
        $rect = [System.Drawing.Rectangle]::new($pad, $pad, $Size - 2 * $pad, $Size - 2 * $pad)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $path.AddArc($rect.Left, $rect.Top, $diameter, $diameter, 180, 90)
            $path.AddArc($rect.Right - $diameter, $rect.Top, $diameter, $diameter, 270, 90)
            $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
            $path.AddArc($rect.Left, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 235, 123, 73))
            try { $graphics.FillPath($fill, $path) } finally { $fill.Dispose() }
        } finally { $path.Dispose() }

        $source = [System.Drawing.Image]::FromFile($sourcePath)
        try {
            $petSize = [Math]::Max(12, [int]($Size * 0.78))
            $petX = [int](($Size - $petSize) / 2)
            $petY = [int](($Size - $petSize) / 2 + $Size * 0.025)
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new($petX, $petY, $petSize, $petSize))
        } finally { $source.Dispose() }
    } finally { $graphics.Dispose() }
    return $bitmap
}

$master = New-IconBitmap 256
try { $master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $master.Dispose() }

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$payloads = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $payloads += ,$stream.ToArray()
    } finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$file = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([Byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$payloads[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $payloads[$index].Length
    }
    foreach ($payload in $payloads) { $writer.Write($payload) }
} finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output "Generated app icon: $icoPath"
Write-Output "Generated icon preview: $pngPath"
