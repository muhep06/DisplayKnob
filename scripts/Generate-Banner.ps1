<#
.SYNOPSIS
    Draws the README banner (docs/assets/banner.png).

.DESCRIPTION
    The DisplayKnob icon on a deep indigo gradient, with the name and tagline. The knob is the same drawing as the
    application icon (scripts/Generate-AppIcon.ps1, src/DisplayKnob.App/Tray/KnobIconSet.cs), pointer at 62%.

    Run from the repository root:  pwsh scripts/Generate-Banner.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\docs\assets\banner.png')
)

Add-Type -AssemblyName System.Drawing

$width = 1600; $height = 400
$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
function Hex([string]$hex, [int]$alpha = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B) }

# Background: rounded rectangle with a diagonal gradient.
$radius = 36
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d = $radius * 2
$path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($width - $d, 0, $d, $d, 270, 90)
$path.AddArc($width - $d, $height - $d, $d, $d, 0, 90); $path.AddArc(0, $height - $d, $d, $d, 90, 90); $path.CloseFigure()
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $width, $height), (Hex '#1B0F3B'), (Hex '#5B2FC4')
$bgBlend = New-Object System.Drawing.Drawing2D.ColorBlend 3
$bgBlend.Colors = (Hex '#1B0F3B'), (Hex '#3B1D7A'), (Hex '#5B2FC4'); $bgBlend.Positions = 0.0, 0.55, 1.0
$bg.InterpolationColors = $bgBlend
$g.SetClip($path)
$g.FillPath($bg, $path)

# Decorative rings and a level arc on the right.
$cx = 1380; $cy = 200
foreach ($ring in @(@(330, 14), @(250, 18), @(170, 22))) {
    $r = $ring[0]
    $pen = New-Object System.Drawing.Pen (Hex '#FFFFFF' $ring[1]), 3
    $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
}
$arcPen = New-Object System.Drawing.Pen (Hex '#C4B5FD' 150), 18
$arcPen.StartCap = 'Round'; $arcPen.EndCap = 'Round'
$g.DrawArc($arcPen, $cx - 170, $cy - 170, 340, 340, 135, 270 * 0.62)
$g.ResetClip()

# The knob (128-unit canvas scaled to 240 px).
$size = 240; $u = $size / 128.0; $x0 = 110; $y0 = ($height - $size) / 2
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

# Text.
$titleFont = New-Object System.Drawing.Font 'Segoe UI Semibold', 88, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$tagFont = New-Object System.Drawing.Font 'Segoe UI', 34, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$textX = $x0 + $size + 64
$g.DrawString('DisplayKnob', $titleFont, [System.Drawing.Brushes]::White, $textX, 104)
$g.DrawString('A tray hub for your displays.', $tagFont, (New-Object System.Drawing.SolidBrush (Hex '#DDD6FE')), $textX + 4, 214)
$g.DrawString('It starts with the volume keys.', $tagFont, (New-Object System.Drawing.SolidBrush (Hex '#DDD6FE')), $textX + 4, 258)
$g.Dispose()

$target = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
$bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
"Wrote $target ($width x $height)"
