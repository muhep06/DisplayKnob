<#
.SYNOPSIS
    Draws the GitHub social preview image (docs/assets/social-preview.png, 1280x640).

.DESCRIPTION
    The card shown when the repository link is shared. Same look as the README banner
    (scripts/Generate-Banner.ps1): the knob on a deep indigo gradient, with the name and tagline, larger so it stays
    readable as a small thumbnail. GitHub can't take this from the repository: upload it in
    Settings > General > Social preview.

    Run from the repository root:  pwsh scripts/Generate-SocialPreview.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\docs\assets\social-preview.png')
)

Add-Type -AssemblyName System.Drawing

$width = 1280; $height = 640
$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
function Hex([string]$hex, [int]$alpha = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B) }

# Background: the whole card, no rounded corners (GitHub crops and rounds it itself).
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $width, $height), (Hex '#1B0F3B'), (Hex '#5B2FC4')
$blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
$blend.Colors = (Hex '#1B0F3B'), (Hex '#3B1D7A'), (Hex '#5B2FC4'); $blend.Positions = 0.0, 0.55, 1.0
$bg.InterpolationColors = $blend
$g.FillRectangle($bg, 0, 0, $width, $height)

# Decorative rings and a level arc, bottom right.
$cx = 1190; $cy = 450
foreach ($ring in @(@(360, 14), @(270, 18), @(180, 22))) {
    $r = $ring[0]
    $pen = New-Object System.Drawing.Pen (Hex '#FFFFFF' $ring[1]), 3
    $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
}
$arcPen = New-Object System.Drawing.Pen (Hex '#C4B5FD' 150), 20
$arcPen.StartCap = 'Round'; $arcPen.EndCap = 'Round'
$g.DrawArc($arcPen, $cx - 180, $cy - 180, 360, 360, 135, 270 * 0.62)

# The knob (same drawing as the application icon), centered vertically on the left.
$size = 260; $u = $size / 128.0; $x0 = 110; $y0 = 150
$sq = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 26 * $u; $inset = 2 * $u; $side = 124 * $u; $dd = 2 * $r
$sq.AddArc($x0 + $inset, $y0 + $inset, $dd, $dd, 180, 90); $sq.AddArc($x0 + $inset + $side - $dd, $y0 + $inset, $dd, $dd, 270, 90)
$sq.AddArc($x0 + $inset + $side - $dd, $y0 + $inset + $side - $dd, $dd, $dd, 0, 90); $sq.AddArc($x0 + $inset, $y0 + $inset + $side - $dd, $dd, $dd, 90, 90); $sq.CloseFigure()
$knobFill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $x0, $y0), (New-Object System.Drawing.PointF ($x0 + $size), ($y0 + $size)), (Hex '#8B5CF6'), (Hex '#6D28D9')
$g.FillPath($knobFill, $sq)
$g.FillEllipse([System.Drawing.Brushes]::White, $x0 + (64 - 47) * $u, $y0 + (64 - 47) * $u, 94 * $u, 94 * $u)
$angle = (135 + 270 * 0.62) * [Math]::PI / 180
$cos = [Math]::Cos($angle); $sin = [Math]::Sin($angle)
$pointer = New-Object System.Drawing.Pen (Hex '#6D28D9'), (16 * $u)
$pointer.StartCap = 'Round'; $pointer.EndCap = 'Round'
$g.DrawLine($pointer, $x0 + (64 + $cos * 5) * $u, $y0 + (64 + $sin * 5) * $u, $x0 + (64 + $cos * 36) * $u, $y0 + (64 + $sin * 36) * $u)

# Text: large, so it still reads as a thumbnail.
$titleFont = New-Object System.Drawing.Font 'Segoe UI Semibold', 104, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tagFont = New-Object System.Drawing.Font 'Segoe UI', 40, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$smallFont = New-Object System.Drawing.Font 'Segoe UI Semibold', 28, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$textX = $x0 + $size + 60
$g.DrawString('DisplayKnob', $titleFont, [System.Drawing.Brushes]::White, $textX, 150)
$lav = New-Object System.Drawing.SolidBrush (Hex '#DDD6FE')
$g.DrawString('A tray hub for your displays.', $tagFont, $lav, $textX + 4, 290)
$g.DrawString('It starts with the volume keys.', $tagFont, $lav, $textX + 4, 342)
$g.DrawString('Windows   ·   DDC/CI   ·   Open source', $smallFont, (New-Object System.Drawing.SolidBrush (Hex '#B9A8F5')), $textX + 4, 440)
$g.Dispose()

$target = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
"Wrote $target ($width x $height, $([math]::Round((Get-Item $target).Length / 1KB)) KB)"
