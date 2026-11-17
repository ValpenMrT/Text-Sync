# Bygger Text Sync med C#-kompilatorn som finns i Windows (.NET Framework 4.8)
#   build.ps1          bygger Build\TextSync-<version>.exe (versionen läses ur Program.Version i src\Program.cs)
#   build.ps1 -Test    bygger dessutom testprogrammen i Build\test\ och kör Tests
# Äldre versioner i Build\ ligger kvar, så att en tidigare version går att dela ut igen.
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root 'Build'

# Versionen anges bara i Program.Version. AssemblyVersion och AssemblyFileVersion måste stämma med den.
$prog = [IO.File]::ReadAllText((Join-Path $root 'src\Program.cs'))
$m = [regex]::Match($prog, 'const\s+string\s+Version\s*=\s*"(\d+(?:\.\d+){0,3})"')
if (-not $m.Success) { throw "Hittade inte versionen i src\Program.cs. Där ska det stå till exempel: public const string Version = `"2.1`";" }
$version = $m.Groups[1].Value
$parts = @($version.Split('.')); while ($parts.Count -lt 4) { $parts += '0' }
$full = $parts -join '.'
foreach ($attr in 'AssemblyVersion', 'AssemblyFileVersion') {
    $a = [regex]::Match($prog, $attr + '\("([^"]*)"\)')
    if (-not $a.Success -or $a.Groups[1].Value -ne $full) {
        throw "$attr i src\Program.cs ska vara `"$full`", eftersom Program.Version är `"$version`"."
    }
}
$exe = Join-Path $build "TextSync-$version.exe"
New-Item -ItemType Directory -Force $build | Out-Null
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

$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'
$src = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /optimize+ /target:winexe /codepage:65001 "/out:$exe" "/win32icon:$ico" @refs $src
if ($LASTEXITCODE -ne 0) { throw "Bygget misslyckades" }
Write-Host "Klart: $exe"

if ($Test) {
    # Varje testprogram byggs med alla källfiler i src\ plus testfilen; /main väljer testets Main i stället för Program.Main.
    $bin = Join-Path $build 'test'
    New-Item -ItemType Directory -Force $bin | Out-Null
    foreach ($t in Get-ChildItem (Join-Path $root 'test') -Filter *.cs) {
        & $csc /nologo /target:exe /codepage:65001 "/main:TextSync.$($t.BaseName)" "/out:$(Join-Path $bin "$($t.BaseName).exe")" @refs $src $t.FullName
        if ($LASTEXITCODE -ne 0) { throw "Bygget av $($t.Name) misslyckades" }
    }
    Write-Host "Testprogram: $bin"

    # Tests körs inifrån PowerShell, eftersom Smart App Control ofta blockerar nybyggda exe-filer en stund.
    $tmp = Join-Path $env:TEMP 'TextSyncTests'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $bin 'Tests.exe')))
    $main = $asm.GetType('TextSync.Tests').GetMethod('Main', [Reflection.BindingFlags]'Static,NonPublic,Public')
    try { $rc = $main.Invoke($null, @(, [string[]]@($tmp))) }
    catch {
        # Hos GitHub blir raden en anmärkning på körningen, som går att läsa utan inloggning (se docs\operations.md)
        if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host ("::error::Tests kastade ett undantag: " + ("$($_.Exception.InnerException)" -replace "`r?`n", ' | ')) }
        throw
    }
    # Tests avgör själv om kontrollerna gick igenom och returnerar 1 om någon misslyckades (raderna som börjar med FEL).
    if ($rc -ne 0) { throw "Testerna misslyckades. Se raderna som börjar med FEL ovan." }
    Write-Host "Testerna gick igenom."
}
