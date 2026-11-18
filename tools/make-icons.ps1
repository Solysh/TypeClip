# Generates Assets\typeclip.ico (active) and Assets\typeclip-off.ico (disabled). Re-run to change the artwork.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function New-Frame([int]$size, [string]$hex) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square
    $r = [Math]::Max(2, $size * 0.2); $d = $r * 2; $w = $size - 1
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))), $path)

    # white "T" with a text caret to its right
    $white = [System.Drawing.Brushes]::White
    $font = New-Object System.Drawing.Font 'Segoe UI', ($size * 0.5), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('T', $font, $white, (New-Object System.Drawing.RectangleF(($size * -0.06), ($size * -0.02), $size, $size)), $sf)
    $g.FillRectangle($white, [single]($size * 0.70), [single]($size * 0.26), [single][Math]::Max(1, $size * 0.07), [single]($size * 0.48))
    $g.Dispose()
    return $bmp
}

function Write-Ico([string]$file, [string]$hex) {
    $sizes = 16, 24, 32, 48, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Frame $s $hex; $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($file); $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close(); $fs.Close()
}

Write-Ico (Join-Path $out 'typeclip.ico')     '#2563EB'
Write-Ico (Join-Path $out 'typeclip-off.ico') '#6B7280'
"Wrote icons to $out"
