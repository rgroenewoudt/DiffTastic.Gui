# Renders src/DiffTastic.Gui/Assets/app.ico (two panes with removed/added lines).
# Windows only: uses System.Drawing.
param([string]$OutFile = "$PSScriptRoot/../src/DiffTastic.Gui/Assets/app.ico")

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function Get-IconPng([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    $bg = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    $g.FillPath($bg, (New-RoundedRect (8*$s) (8*$s) (240*$s) (240*$s) (48*$s)))

    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $g.FillPath($paper, (New-RoundedRect (36*$s) (44*$s) (84*$s) (168*$s) (12*$s)))
    $g.FillPath($paper, (New-RoundedRect (136*$s) (44*$s) (84*$s) (168*$s) (12*$s)))

    $grey = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 148, 163, 184))
    $red = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 220, 38, 38))
    $green = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 22, 163, 74))

    # rows: y, left brush, right brush
    $rows = @(
        @(70, $grey, $grey),
        @(102, $red, $green),
        @(134, $red, $green),
        @(166, $grey, $grey)
    )
    if ($size -lt 32) { $rows = @(@(82, $grey, $grey), @(118, $red, $green), @(154, $grey, $grey)) }
    $barH = if ($size -lt 32) { 22 } else { 18 }
    foreach ($r in $rows) {
        $g.FillRectangle($r[1], 48*$s, $r[0]*$s, 60*$s, $barH*$s)
        $g.FillRectangle($r[2], 148*$s, $r[0]*$s, 60*$s, $barH*$s)
    }

    $g.Dispose()
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (Get-IconPng $size) }

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
$fs = [System.IO.File]::Create($OutFile)
$w = [System.IO.BinaryWriter]::new($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()
Write-Host "Wrote $OutFile"
