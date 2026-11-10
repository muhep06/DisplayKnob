<#
.SYNOPSIS
    Draws the DisplayKnob application icon (src/DisplayKnob.App/Assets/DisplayKnob.ico).

.DESCRIPTION
    A white knob on a violet rounded square, the pointer straight up (the tray icon's idle look).
    The geometry (a 128-unit canvas) is the same as src/DisplayKnob.App/Tray/KnobIconSet.cs; change both together.
    The .ico holds PNG images at several sizes, which Windows 10/11 read natively.

    Run from the repository root:  pwsh scripts/Generate-AppIcon.ps1
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\DisplayKnob.App\Assets\DisplayKnob.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$supersample = 8
$level = 50 # Pointer straight up.

function New-KnobBitmap([int]$size) {
    $big = $size * $supersample
    $u = $big / 128.0
    $canvas = New-Object System.Drawing.Bitmap $big, $big
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = 'AntiAlias'

    $top = [System.Drawing.ColorTranslator]::FromHtml('#8B5CF6')
    $bottom = [System.Drawing.ColorTranslator]::FromHtml('#6D28D9')
    $accent = [System.Drawing.ColorTranslator]::FromHtml('#6D28D9')

    $r = 26 * $u; $x = 2 * $u; $w = 124 * $u; $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $x, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $x, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $x + $w - $d, $d, $d, 0, 90)
    $path.AddArc($x, $x + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $big, $big), $top, $bottom
    $g.FillPath($fill, $path)

    $g.FillEllipse([System.Drawing.Brushes]::White, (64 - 47) * $u, (64 - 47) * $u, 94 * $u, 94 * $u)

    # The pointer sweeps 270 degrees: 7:30 for 0%, 4:30 for 100%.
    $angle = (135 + 270 * $level / 100.0) * [Math]::PI / 180
    $cos = [Math]::Cos($angle); $sin = [Math]::Sin($angle)
    $pen = New-Object System.Drawing.Pen $accent, (16 * $u)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, (64 + $cos * 5) * $u, (64 + $sin * 5) * $u, (64 + $cos * 36) * $u, (64 + $sin * 36) * $u)
    $g.Dispose()

    $scaled = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($scaled)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.PixelOffsetMode = 'HighQuality'
    $g2.DrawImage($canvas, 0, 0, $size, $size)
    $g2.Dispose(); $canvas.Dispose()
    return $scaled
}

$images = foreach ($size in $sizes) {
    $bitmap = New-KnobBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
}

# ICONDIR (6 bytes), one ICONDIRENTRY (16 bytes) per image, then the PNG data.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size } # 0 means 256 in an ICO header.
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$image.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $w.Write($image.Bytes) }
$w.Flush()

$target = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
"Wrote $target ($($images.Count) sizes, $($out.Length) bytes)"
