# Renders build/icon.png (512x512) and build/icon.ico (16-256 px) from the same geometry as build/icon.svg.
# Usage:  powershell -ExecutionPolicy Bypass -File .\scripts\generate-icon.ps1
# If you change build/icon.svg, update the shapes below to match, then run this script.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$buildDir = Join-Path $PSScriptRoot '..\build'
$buildDir = (Resolve-Path $buildDir).Path

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $d = $r * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Fill-Round($g, $brush, $x, $y, $w, $h, $r) {
    $path = New-RoundedRectPath $x $y $w $h $r
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function Render-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 1024.0, $size / 1024.0)

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF 48, 48), (New-Object System.Drawing.PointF 976, 976),
        [System.Drawing.ColorTranslator]::FromHtml('#059669'), [System.Drawing.ColorTranslator]::FromHtml('#2563eb'))
    $white = [System.Drawing.Brushes]::White
    $gray = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#cbd5e1'))
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#f97316'))

    Fill-Round $g $gradient 48 48 928 928 210
    Fill-Round $g $white 150 230 340 564 44
    Fill-Round $g $white 534 230 340 564 44

    foreach ($s in @(@(200,300,240), @(200,480,190), @(200,660,240), @(584,300,240), @(584,480,190), @(584,660,240))) {
        Fill-Round $g $gray $s[0] $s[1] $s[2] 44 22
    }
    foreach ($s in @(@(200,390,200), @(584,390,240), @(200,570,240), @(584,570,150))) {
        Fill-Round $g $orange $s[0] $s[1] $s[2] 44 22
    }

    $g.Dispose(); $gradient.Dispose(); $gray.Dispose(); $orange.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

# build/icon.png (512x512)
$big = Render-Icon 512
$big.Save((Join-Path $buildDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

# build/icon.ico with PNG-compressed images (supported on Windows Vista and later)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $b = Render-Icon $s
    ,@($s, (Get-PngBytes $b))
    $b.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $bytes = $img[1]
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the ICO format
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $buildDir 'icon.ico'), $out.ToArray())

Write-Host "Wrote $buildDir\icon.png and $buildDir\icon.ico"
