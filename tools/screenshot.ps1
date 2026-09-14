# Captures a screen region and saves it scaled up, so pixel detail is reviewable.
# Usage: powershell.exe -File screenshot.ps1 <x> <y> <width> <height> <outputPath> [scale]
Add-Type -AssemblyName System.Drawing
$x = [int]$args[0]; $y = [int]$args[1]; $w = [int]$args[2]; $h = [int]$args[3]
$out = $args[4]; $scale = if ($args.Count -gt 5) { [int]$args[5] } else { 4 }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()

$big = New-Object System.Drawing.Bitmap ($w * $scale), ($h * $scale)
$gb = [System.Drawing.Graphics]::FromImage($big)
$gb.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$gb.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$gb.DrawImage($bmp, 0, 0, ($w * $scale), ($h * $scale))
$gb.Dispose()
$big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "saved $out"
