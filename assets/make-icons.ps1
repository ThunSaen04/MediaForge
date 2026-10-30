# Builds every icon the app and installer use from assets\logo.png (square, transparent):
#   src\MediaForge\Assets\MediaForge.ico   exe / window / installer icon (16-256 px, PNG-compressed)
#   src\MediaForge\Assets\logo.png         title bar logo (128 px)
#   installer\wizard-small.bmp             Inno Setup wizard header image
# Run with PowerShell 7: pwsh .\assets\make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$source = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'logo.png'))
$appAssets = Join-Path $root 'src\MediaForge\Assets'
New-Item -ItemType Directory -Force $appAssets | Out-Null

function Resize([int]$size, [System.Drawing.Color]$background = [System.Drawing.Color]::Transparent) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($background)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    $bmp
}

function PngBytes($bmp) {
    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    # Leading comma stops PowerShell from unrolling the array into single bytes.
    , $ms.ToArray()
}

# ICO container with PNG-encoded entries (supported since Windows Vista).
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($s in $sizes) { $b = Resize $s; $images.Add((PngBytes $b)); $b.Dispose() }
$ico = [IO.MemoryStream]::new()
$w = [IO.BinaryWriter]::new($ico)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $appAssets 'MediaForge.ico'), $ico.ToArray())

$logo = Resize 128
$logo.Save((Join-Path $appAssets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# Wizard header image: BMP has no alpha, so flatten onto white (the modern wizard header is white).
$wizard = Resize 110 ([System.Drawing.Color]::White)
$wizard.Save((Join-Path $root 'installer\wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$wizard.Dispose()

$source.Dispose()
Write-Host 'Icons written.'
