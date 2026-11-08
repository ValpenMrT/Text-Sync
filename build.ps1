# Bygger TextSync.exe med C#-kompilatorn som finns i Windows (.NET Framework 4.8)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ico = Join-Path $root 'src\TextSync.ico'

if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 256, 48, 32, 16
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
        $r = [single]($s * 0.18)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $w = [single]($s - 1)
        $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90); $path.AddArc($w - $r * 2, 0, $r * 2, $r * 2, 270, 90)
        $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90); $path.AddArc(0, $w - $r * 2, $r * 2, $r * 2, 90, 90)
        $path.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 103, 192))), $path)
        $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
        $light = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
        # två rader "undertext" som linjeras upp
        $h = [single]($s * 0.13)
        $g.FillRectangle($light, [single]($s * 0.16), [single]($s * 0.30), [single]($s * 0.52), $h)
        $g.FillRectangle($white, [single]($s * 0.30), [single]($s * 0.30 + $h * 1.6), [single]($s * 0.54), $h)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single][Math]::Max(1, $s * 0.05))
        $g.DrawLine($pen, [single]($s * 0.30), [single]($s * 0.18), [single]($s * 0.30), [single]($s * 0.82))
        $g.FillRectangle($white, [single]($s * 0.16), [single]($s * 0.66), [single]($s * 0.68), [single]($s * 0.07))
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $out
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    [System.IO.File]::WriteAllBytes($ico, $out.ToArray())
}

$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /target:winexe /codepage:65001 "/out:$(Join-Path $root 'TextSync.exe')" "/win32icon:$ico" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $src
if ($LASTEXITCODE -ne 0) { throw "Bygget misslyckades" }
Write-Host "Klart: $(Join-Path $root 'TextSync.exe')"
