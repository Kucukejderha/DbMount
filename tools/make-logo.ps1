# ASCOS DbMount logo uretimi
# Cikti: ASCOS-DbMount.png (256, saydam), ASCOS-DbMount.ico (16-256),
#        logo-1024.png (web kaynagi)
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 1024.0
    function V([float]$v) { $v * $s }

    $Navy = [System.Drawing.Color]::FromArgb(255, 25, 48, 78)
    $Blue = [System.Drawing.Color]::FromArgb(255, 43, 111, 184)
    $Tier = [System.Drawing.Color]::FromArgb(255, 61, 95, 138)
    $White = [System.Drawing.Color]::White
    $TileBorder = [System.Drawing.Color]::FromArgb(255, 228, 234, 242)

    # beyaz yuvarlak karo
    $tile = New-RoundedPath (V 24) (V 24) (V 976) (V 976) (V 190)
    $tileBrush = New-Object System.Drawing.SolidBrush($White)
    $g.FillPath($tileBrush, $tile)
    $tilePen = New-Object System.Drawing.Pen($TileBorder, (V 6))
    $g.DrawPath($tilePen, $tile)
    $tilePen.Dispose(); $tileBrush.Dispose(); $tile.Dispose()

    # veritabani silindiri: ust elips + govde + alt elips + katman cizgileri
    $cylX = V 230; $cylY = V 250; $cylW = V 390; $cylH = V 85
    $navyBrush = New-Object System.Drawing.SolidBrush($Navy)
    $blueBrush = New-Object System.Drawing.SolidBrush($Blue)
    $navyPen = New-Object System.Drawing.Pen($Navy, (V 10))

    $g.FillRectangle($navyBrush, $cylX, (V 292), $cylW, (V 300))           # govde
    $g.FillEllipse($navyBrush, $cylX, (V 550), $cylW, $cylH)               # alt elips
    $g.DrawEllipse($navyPen, $cylX, (V 550), $cylW, $cylH)
    $g.FillEllipse($blueBrush, $cylX, $cylY, $cylW, $cylH)                 # ust elips
    $g.DrawEllipse($navyPen, $cylX, $cylY, $cylW, $cylH)

    $tierPen = New-Object System.Drawing.Pen($Tier, (V 12))
    $g.DrawEllipse($tierPen, $cylX, (V 385), $cylW, $cylH)                 # 1. katman
    $g.DrawEllipse($tierPen, $cylX, (V 480), $cylW, $cylH)                 # 2. katman
    $tierPen.Dispose()

    # mavi rozet + beyaz cift yonlu ok (mount/unmount)
    $badgeX = V 535; $badgeY = V 480; $badgeD = V 360
    $g.FillEllipse($blueBrush, $badgeX, $badgeY, $badgeD, $badgeD)
    $ringPen = New-Object System.Drawing.Pen($White, (V 16))
    $g.DrawEllipse($ringPen, $badgeX, $badgeY, $badgeD, $badgeD)
    $ringPen.Dispose()

    $whiteBrush = New-Object System.Drawing.SolidBrush($White)
    $up = New-Object -TypeName "System.Drawing.PointF[]" -ArgumentList 3
    $up[0] = New-Object System.Drawing.PointF (V 715), (V 572)
    $up[1] = New-Object System.Drawing.PointF (V 640), (V 662)
    $up[2] = New-Object System.Drawing.PointF (V 790), (V 662)
    $down = New-Object -TypeName "System.Drawing.PointF[]" -ArgumentList 3
    $down[0] = New-Object System.Drawing.PointF (V 640), (V 688)
    $down[1] = New-Object System.Drawing.PointF (V 790), (V 688)
    $down[2] = New-Object System.Drawing.PointF (V 715), (V 778)
    $g.FillPolygon($whiteBrush, $up)
    $g.FillPolygon($whiteBrush, $down)

    $whiteBrush.Dispose(); $blueBrush.Dispose(); $navyBrush.Dispose(); $navyPen.Dispose()
    $g.Dispose()
    return $bmp
}

function Downscale($src, [int]$size) {
    $out = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $out
}

$big = Render-Logo 4096

$logo1024 = Downscale $big 1024
$logo1024.Save("$root\logo-1024.png", [System.Drawing.Imaging.ImageFormat]::Png)
$logo1024.Dispose()

$logo256 = Downscale $big 256
$logo256.Save("$root\ASCOS-DbMount.png", [System.Drawing.Imaging.ImageFormat]::Png)
$logo256.Dispose()

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $b = Downscale $big $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    $pngs += , $ms.ToArray()
    $ms.Dispose()
}
$big.Dispose()

$fs = [System.IO.File]::Create("$root\ASCOS-DbMount.ico")
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $sz = $sizes[$i]
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
$fs.Close()

Write-Output "Uretildi:"
Write-Output "  $root\ASCOS-DbMount.png (256)"
Write-Output "  $root\ASCOS-DbMount.ico ($($sizes -join ', '))"
Write-Output "  $root\logo-1024.png"
