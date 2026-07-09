# Generates Assets\app.ico (PNG-compressed ICO with 16/32/48/256 sizes):
# rounded accent square + white check mark, matching the tray icon.
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $m = [Math]::Max(1, [int]($size * 0.06))          # margin
    $r = [int]($size * 0.28)                          # corner radius
    $w = $size - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point(0, $size)),
        [System.Drawing.Color]::FromArgb(255, 0x6F, 0xAC, 0xFF),
        [System.Drawing.Color]::FromArgb(255, 0x3F, 0x7D, 0xE8))
    $g.FillPath($brush, $path)

    $penW = [Math]::Max(1.6, $size * 0.10)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pts = @(
        (New-Object System.Drawing.PointF(($size * 0.28), ($size * 0.52))),
        (New-Object System.Drawing.PointF(($size * 0.44), ($size * 0.68))),
        (New-Object System.Drawing.PointF(($size * 0.74), ($size * 0.34)))
    )
    $g.DrawLines($pen, $pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = @(16, 32, 48, 256)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-IconPng $s }

$outPath = "F:\TODO\src\GlassTodo\Assets\app.ico"
New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    [byte[]]$data = $pngs[$s]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)      # width
    $bw.Write([byte]$dim)      # height
    $bw.Write([byte]0)         # palette
    $bw.Write([byte]0)         # reserved
    $bw.Write([uint16]1)       # planes
    $bw.Write([uint16]32)      # bpp
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write([byte[]]$pngs[$s]) }
$bw.Close(); $fs.Close()
Write-Output ("wrote $outPath (" + (Get-Item $outPath).Length + " bytes)")
