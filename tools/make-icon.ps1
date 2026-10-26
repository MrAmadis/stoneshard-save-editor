<#
.SYNOPSIS
    Builds the app icon, src/StoneshardSaveEditor.App/Icon/app.ico, from its SVG drawings.
.DESCRIPTION
    The Edge that ships with Windows draws each size straight from the SVG, so no design tools are needed.
    icon.svg is used from 32 px up; icon-small.svg, simplified to stay crisp, below that.
    Run it again after changing either drawing: powershell -File tools/make-icon.ps1
#>
param(
    [string]$IconDir = (Join-Path $PSScriptRoot '..\src\StoneshardSaveEditor.App\Icon')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(
    @{ Size = 16; Svg = 'icon-small.svg' }
    @{ Size = 20; Svg = 'icon-small.svg' }
    @{ Size = 24; Svg = 'icon-small.svg' }
    @{ Size = 32; Svg = 'icon.svg' }
    @{ Size = 40; Svg = 'icon.svg' }
    @{ Size = 48; Svg = 'icon.svg' }
    @{ Size = 64; Svg = 'icon.svg' }
    @{ Size = 256; Svg = 'icon.svg' }
)

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found; it draws the icon.' }

$IconDir = (Resolve-Path $IconDir).Path
$work = Join-Path ([IO.Path]::GetTempPath()) "make-icon-$([guid]::NewGuid())"
New-Item -ItemType Directory $work | Out-Null

# A 32-bit DIB as an icon stores it: bottom-up BGRA rows, then a 1-bit mask of the fully transparent pixels.
function ConvertTo-IconDib([Drawing.Bitmap]$bitmap) {
    $w = $bitmap.Width
    $h = $bitmap.Height
    $maskStride = [int][Math]::Ceiling($w / 32) * 4
    $mask = New-Object byte[] ($maskStride * $h)
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    # BITMAPINFOHEADER; its height counts the colour rows and the mask rows.
    $writer.Write([int]40); $writer.Write([int]$w); $writer.Write([int]($h * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($w * $h * 4 + $mask.Length)); $writer.Write([int]0); $writer.Write([int]0)
    $writer.Write([int]0); $writer.Write([int]0)
    for ($row = 0; $row -lt $h; $row++) {
        $y = $h - 1 - $row
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
            if ($c.A -eq 0) {
                $i = $row * $maskStride + [Math]::Floor($x / 8)
                $mask[$i] = $mask[$i] -bor (0x80 -shr ($x % 8))
            }
        }
    }
    $writer.Write($mask)
    $writer.Flush()
    return , $stream.ToArray()
}

try {
    # Lay every size out in one row, draw them in one go, then cut each one out.
    $x = 0
    $images = foreach ($s in $sizes) {
        $uri = ([Uri](Join-Path $IconDir $s.Svg)).AbsoluteUri
        "<img src='$uri' style='position:absolute;left:${x}px;top:0;width:$($s.Size)px;height:$($s.Size)px'>"
        $s.X = $x
        $x += $s.Size + 8
    }
    $page = Join-Path $work 'sheet.html'
    $sheet = Join-Path $work 'sheet.png'
    Set-Content -Encoding UTF8 $page "<!doctype html><html style='background:transparent'><body style='margin:0;background:transparent'>$($images -join '')</body></html>"
    Start-Process -FilePath $edge -Wait -ArgumentList @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        '--default-background-color=00000000', '--virtual-time-budget=5000',
        "--window-size=$x,264", "--user-data-dir=`"$work\profile`"", "--screenshot=`"$sheet`"", ([Uri]$page).AbsoluteUri)
    if (-not (Test-Path $sheet)) { throw 'Edge did not draw the icon sheet.' }

    $drawn = [Drawing.Bitmap]::FromFile($sheet)
    try {
        if ($drawn.GetPixel($x - 4, 260).A -ne 0) { throw 'Edge drew an opaque background; the icon would have a box around it.' }
        $entries = foreach ($s in $sizes) {
            $area = New-Object Drawing.Rectangle $s.X, 0, $s.Size, $s.Size
            $bitmap = $drawn.Clone($area, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                if ($s.Size -ge 256) {
                    # Windows reads the 256 px size as PNG, which keeps the file small.
                    $png = New-Object IO.MemoryStream
                    $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
                    @{ Size = $s.Size; Bytes = $png.ToArray() }
                } else {
                    @{ Size = $s.Size; Bytes = (ConvertTo-IconDib $bitmap) }
                }
            } finally {
                $bitmap.Dispose()
            }
        }
    } finally {
        $drawn.Dispose()
    }

    $ico = Join-Path $IconDir 'app.ico'
    $writer = New-Object IO.BinaryWriter ([IO.File]::Create($ico))
    try {
        $writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$entries.Count)
        $offset = 6 + 16 * $entries.Count
        foreach ($e in $entries) {
            $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
            $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]$e.Bytes.Length); $writer.Write([int]$offset)
            $offset += $e.Bytes.Length
        }
        foreach ($e in $entries) { $writer.Write([byte[]]$e.Bytes) }
    } finally {
        $writer.Dispose()
    }
    Write-Host "Wrote $ico ($(($sizes | ForEach-Object { $_.Size }) -join ', ') px)"
} finally {
    # Edge's helper processes can hold its profile open for a moment; a leftover temp folder is harmless.
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
