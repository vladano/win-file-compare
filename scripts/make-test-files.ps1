<#
.SYNOPSIS
  Creates a folder of sample files for the manual checks in docs\TESTING.md.

.DESCRIPTION
  The folder holds pairs for every view (text, folders, hex, images), names with spaces, "&" and
  non-ASCII letters, and mixed items for the drag and drop checks. It only writes inside -Path.
  Running it again recreates the folder, but only if this script made it (it leaves a marker file).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\make-test-files.ps1 -Open
#>
param(
    [string]$Path = (Join-Path $env:TEMP 'FileCompare test files'),
    [switch]$Open
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$marker = '.filecompare-test-files'

if (Test-Path $Path) {
    if (-not (Test-Path (Join-Path $Path $marker))) {
        throw "$Path exists and was not made by this script. Choose another -Path."
    }
    Remove-Item $Path -Recurse -Force
}
New-Item -ItemType Directory -Force $Path | Out-Null
Set-Content (Join-Path $Path $marker) 'Made by scripts\make-test-files.ps1. Safe to delete.'

function Write-Text([string]$relative, [string[]]$lines) {
    $file = Join-Path $Path $relative
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [IO.File]::WriteAllText($file, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
}

# --- Text: a changed, an inserted, a deleted, a whitespace-only and a case-only line ----------------
$base = 1..30 | ForEach-Object { "Line $_ of the sample text." }
$left = [System.Collections.Generic.List[string]]$base
$right = [System.Collections.Generic.List[string]]$base
$right[4] = 'Line 5 of the sample text, changed on the right.'
$right[9] = '    Line 10 of the sample text.'          # only indentation: a "minor" difference
$right[14] = 'LINE 15 OF THE SAMPLE TEXT.'             # only case
$right.Insert(20, 'A line that exists only on the right.')
$right.RemoveAt(26)                                     # one line only on the left
Write-Text 'text\left.txt' $left
Write-Text 'text\right.txt' $right

# Names that need care in menus and paths.
Write-Text 'text\notes & ideas (v1).txt' @('First idea', 'Second idea')
Write-Text 'text\notes & ideas (v2).txt' @('First idea', 'Second idea, improved')
$zurich = 'Z' + [char]0x00FC + 'rich'                  # "Zurich" with u-umlaut, kept ASCII in this script
Write-Text "text\$zurich 1.txt" @("Gr$([char]0x00FC)ezi", 'one')
Write-Text "text\$zurich 2.txt" @("Gr$([char]0x00FC)ezi", 'two')

# --- Folders ---------------------------------------------------------------------------------------
Write-Text 'folders\left\same.txt' @('same on both sides')
Write-Text 'folders\right\same.txt' @('same on both sides')
Write-Text 'folders\left\changed.txt' @('left version')
Write-Text 'folders\right\changed.txt' @('right version')
Write-Text 'folders\left\only-left.txt' @('only on the left')
Write-Text 'folders\right\only-right.txt' @('only on the right')
Write-Text 'folders\left\sub\deep\nested.txt' @('nested, left')
Write-Text 'folders\right\sub\deep\nested.txt' @('nested, right')
Write-Text 'folders\left\node_modules\ignored.js' @('ignored by the default rules')
New-Item -ItemType Directory -Force (Join-Path $Path 'folders\left\empty-left-only') | Out-Null

# --- Binary: a few changed bytes, and the second file 100 bytes shorter ---------------------------------
$bytes = New-Object byte[] 4096
[System.Random]::new(42).NextBytes($bytes)
$changed = [byte[]]$bytes.Clone()
foreach ($i in 100, 101, 2050) { $changed[$i] = [byte](255 - $changed[$i]) }
New-Item -ItemType Directory -Force (Join-Path $Path 'binary') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $Path 'binary\app-v1.bin'), $bytes)
[IO.File]::WriteAllBytes((Join-Path $Path 'binary\app-v2.bin'), $changed[0..3995])

# --- Pictures: a box that moved, and one of another size ---------------------------------------------
function Write-Picture([string]$relative, [int]$width, [int]$height, [int]$boxX) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::FromArgb(70, 130, 180))
    $g.FillEllipse([System.Drawing.Brushes]::Gold, 20, 20, 70, 70)
    $g.FillRectangle([System.Drawing.Brushes]::White, $boxX, 90, 60, 40)
    $g.Dispose()
    $file = Join-Path $Path $relative
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}
Write-Picture 'images\before.png' 240 150 140
Write-Picture 'images\after.png' 240 150 160
Write-Picture 'images\other-size.png' 300 150 140

# --- Mixed items for "wrong kind" drops ----------------------------------------------------------------
Write-Text 'mixed\a file.txt' @('a file')
Write-Text 'mixed\another file.txt' @('another file')
New-Item -ItemType Directory -Force (Join-Path $Path 'mixed\a folder'), (Join-Path $Path 'mixed\another folder') | Out-Null

Write-Host "Test files are in: $Path"
Get-ChildItem $Path -Directory | ForEach-Object { Write-Host ("  {0,-8} {1} items" -f $_.Name, @(Get-ChildItem $_.FullName -Recurse).Count) }
if ($Open) { Start-Process explorer.exe $Path }
