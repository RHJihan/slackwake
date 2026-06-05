# Generates SlackWake's application icon (app.ico) from the same bell silhouette
# used by the in-tray icons, so the brand stays consistent across the tray, the
# taskbar, Explorer, and the window title bar.
#
# Design: a minimal "squircle" tile in the brand green (#2EB67D, the same green as
# the "active / armed" status dot and tray icon) with a centered white bell glyph.
# Rendered at every size Windows asks for (16-256px) and packed into a single
# PNG-compressed .ico.
#
# Re-run after changing the artwork:
#   powershell -ExecutionPolicy Bypass -File .\generate-app-icon.ps1
#
# No third-party tooling — pure GDI+ via System.Drawing.

Add-Type -AssemblyName System.Drawing

$OutPath = Join-Path $PSScriptRoot 'app.ico'
$Sizes   = 16, 24, 32, 48, 64, 128, 256

# Brand palette. A subtle top->bottom gradient gives the flat tile a little depth
# without reading as skeuomorphic.
$GreenTop    = [System.Drawing.Color]::FromArgb(0x3A, 0xCC, 0x90)
$GreenBottom = [System.Drawing.Color]::FromArgb(0x1E, 0x9E, 0x6A)

# Bell silhouette in its native 32x32 design space (identical geometry to
# TrayIconFactory.BuildBellPath so the two icons read as the same mark).
function New-BellPath {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.StartFigure()
    $p.AddLine(14.0, 4.0, 18.0, 4.0)
    $p.AddLine(18.0, 4.0, 18.0, 7.0)
    $p.AddBezier(18.0, 7.0, 23.0, 7.5, 24.0, 12.0, 24.0, 16.0)
    $p.AddLine(24.0, 16.0, 24.0, 22.0)
    $p.AddLine(24.0, 22.0, 27.0, 24.0)
    $p.AddLine(27.0, 24.0, 27.0, 25.5)
    $p.AddLine(27.0, 25.5, 5.0, 25.5)
    $p.AddLine(5.0, 25.5, 5.0, 24.0)
    $p.AddLine(5.0, 24.0, 8.0, 22.0)
    $p.AddLine(8.0, 22.0, 8.0, 16.0)
    $p.AddBezier(8.0, 16.0, 8.0, 12.0, 9.0, 7.5, 14.0, 7.0)
    $p.AddLine(14.0, 7.0, 14.0, 4.0)
    $p.CloseFigure()
    $p.AddEllipse(14.5, 26.5, 3.0, 3.0)
    return $p
}

# Rounded-rectangle ("squircle"-ish) path with a per-corner radius.
function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2.0
    $p.AddArc($x,           $y,           $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y,           $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d,   0, 90)
    $p.AddArc($x,           $y + $h - $d, $d, $d,  90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap($n, $n, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Tile: small uniform margin, generous corner radius for the modern squircle look.
    $margin = [single]([Math]::Max(1.0, $n * 0.06))
    $tile   = [single]($n - 2 * $margin)
    $radius = [single]($tile * 0.235)
    $tilePath = New-RoundedRectPath $margin $margin $tile $tile $radius

    $rect = New-Object System.Drawing.RectangleF($margin, $margin, $tile, $tile)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, $GreenTop, $GreenBottom, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $tilePath)
    $brush.Dispose()

    # Bell: scale the 32x32 design to ~52% of the canvas and center it, nudged up
    # a hair so the visual weight sits in the optical center of the tile.
    $bell = New-BellPath
    $bounds = $bell.GetBounds()             # ~ x:5..27, y:4..29.5
    $target = [single]($n * 0.52)
    $scale  = [single]($target / [Math]::Max($bounds.Width, $bounds.Height))

    $m = New-Object System.Drawing.Drawing2D.Matrix
    $cx = $bounds.X + $bounds.Width  / 2.0
    $cy = $bounds.Y + $bounds.Height / 2.0
    $m.Translate([single]($n / 2.0), [single]($n / 2.0 - $n * 0.015))
    $m.Scale($scale, $scale)
    $m.Translate([single](-$cx), [single](-$cy))
    $bell.Transform($m)
    $m.Dispose()

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $bell)
    $white.Dispose()
    $bell.Dispose()
    $tilePath.Dispose()
    $g.Dispose()
    return $bmp
}

# Render every size to PNG bytes (Vista+ icons store entries as PNG — Win10/11
# reads them natively and it keeps the file tiny).
$pngs = @()
foreach ($s in $Sizes) {
    $bmp = New-IconBitmap $s
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

# Assemble the .ico container by hand: ICONDIR header + one ICONDIRENTRY per
# image, then the PNG payloads.
$fs = [System.IO.File]::Create($OutPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$count = $pngs.Count
$bw.Write([uint16]0)        # reserved
$bw.Write([uint16]1)        # type = icon
$bw.Write([uint16]$count)   # image count

$offset = 6 + 16 * $count
foreach ($entry in $pngs) {
    $size = [int]$entry[0]
    $data = [byte[]]$entry[1]
    $dim  = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in the spec
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # palette size (none)
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # color planes
    $bw.Write([uint16]32)            # bits per pixel
    $bw.Write([uint32]$data.Length)  # bytes in resource
    $bw.Write([uint32]$offset)       # offset to data
    $offset += $data.Length
}
foreach ($entry in $pngs) {
    $bw.Write([byte[]]$entry[1])
}
$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Wrote $OutPath ($count sizes: $($Sizes -join ', '))"
