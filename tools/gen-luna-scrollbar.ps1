# Renders the Luna scrollbar sprites from XP.css's per-pixel crispEdges SVG dumps,
# then 9-slices them to the 21px "large" metric the way msstyles SizingMargins did.
Add-Type -AssemblyName System.Drawing

$cssPath = "$env:TEMP\xp.css"
$outDir = "C:\Users\Loris\Projects\Cooldown\src\Cooldown\Assets\Xp"
$css = Get-Content $cssPath -Raw
$luna = $css.Substring($css.IndexOf("[role=tabpanel]"))

function Get-Rule([string]$selector) {
    $i = $luna.IndexOf($selector + "{")
    if ($i -lt 0) { throw "missing $selector" }
    $j = $luna.IndexOf("}", $i)
    return $luna.Substring($i + $selector.Length + 1, $j - $i - $selector.Length - 1)
}

function Render-PixelSvg([string]$body) {
    $m = [regex]::Match($body, 'url\("data:image/svg\+xml;charset=utf-8,(.*?)"\)')
    if (-not $m.Success) { return $null }
    $svg = [System.Uri]::UnescapeDataString($m.Groups[1].Value)

    $vb = [regex]::Match($svg, "viewBox='([-0-9. ]+)'").Groups[1].Value -split ' '
    $w = [int][double]$vb[2]
    $h = [int][double]$vb[3]
    $bmp = New-Object System.Drawing.Bitmap $w, $h

    foreach ($p in [regex]::Matches($svg, "<path stroke='([^']+)' d='([^']+)'")) {
        $hex = $p.Groups[1].Value.TrimStart('#')
        if ($hex.Length -eq 3) { $hex = "$($hex[0])$($hex[0])$($hex[1])$($hex[1])$($hex[2])$($hex[2])" }
        $col = [System.Drawing.Color]::FromArgb(
            [Convert]::ToInt32($hex.Substring(0, 2), 16),
            [Convert]::ToInt32($hex.Substring(2, 2), 16),
            [Convert]::ToInt32($hex.Substring(4, 2), 16))

        $x = 0; $y = 0
        foreach ($t in [regex]::Matches($p.Groups[2].Value, '([MmHhVvLl])\s*(-?[\d.]+)(?:[ ,]+(-?[\d.]+))?')) {
            $cmd = $t.Groups[1].Value
            $a = [double]$t.Groups[2].Value
            $b = if ($t.Groups[3].Success) { [double]$t.Groups[3].Value } else { 0 }
            switch -CaseSensitive ($cmd) {
                'M' { $x = [int]$a; $y = [int][Math]::Round($b) }
                'm' { $x += [int]$a; $y += [int][Math]::Round($b) }
                'h' {
                    $n = [int]$a
                    for ($k = 0; $k -lt [Math]::Abs($n); $k++) {
                        $px = if ($n -gt 0) { $x + $k } else { $x - 1 - $k }
                        if ($px -ge 0 -and $px -lt $w -and $y -ge 0 -and $y -lt $h) { $bmp.SetPixel($px, $y, $col) }
                    }
                    $x += $n
                }
                'H' {
                    $n = [int]$a
                    $from = [Math]::Min($x, $n); $to = [Math]::Max($x, $n)
                    for ($px = $from; $px -lt $to; $px++) {
                        if ($px -ge 0 -and $px -lt $w -and $y -ge 0 -and $y -lt $h) { $bmp.SetPixel($px, $y, $col) }
                    }
                    $x = $n
                }
            }
        }
    }
    return $bmp
}

function NineSlice([System.Drawing.Bitmap]$src, [int]$newW, [int]$newH, [int]$m) {
    $sw = $src.Width; $sh = $src.Height
    $out = New-Object System.Drawing.Bitmap $newW, $newH
    for ($y = 0; $y -lt $newH; $y++) {
        if ($sh -eq $newH) { $sy = $y }
        elseif ($y -lt $m) { $sy = $y }
        elseif ($y -ge $newH - $m) { $sy = $sh - ($newH - $y) }
        else { $sy = $m + [int][Math]::Floor(($y - $m) * ($sh - 2 * $m) / ($newH - 2 * $m)) }
        for ($x = 0; $x -lt $newW; $x++) {
            if ($sw -eq $newW) { $sx = $x }
            elseif ($x -lt $m) { $sx = $x }
            elseif ($x -ge $newW - $m) { $sx = $sw - ($newW - $x) }
            else { $sx = $m + [int][Math]::Floor(($x - $m) * ($sw - 2 * $m) / ($newW - 2 * $m)) }
            $out.SetPixel($x, $y, $src.GetPixel($sx, $sy))
        }
    }
    return $out
}

$targets = @{
    'scroll-track'       = '::-webkit-scrollbar-track:vertical'
    'scroll-arrow-up'    = '::-webkit-scrollbar-button:vertical:start'
    'scroll-arrow-down'  = '::-webkit-scrollbar-button:vertical:end'
    'scroll-arrow-left'  = '::-webkit-scrollbar-button:horizontal:start'
    'scroll-arrow-right' = '::-webkit-scrollbar-button:horizontal:end'
    'scroll-grip'        = '::-webkit-scrollbar-thumb:vertical'
    'scroll-grip-h'      = '::-webkit-scrollbar-thumb:horizontal'
    'scroll-track-h'     = '::-webkit-scrollbar-track:horizontal'
}

$raw = @{}
foreach ($k in $targets.Keys) {
    $bmp = Render-PixelSvg (Get-Rule $targets[$k])
    if ($null -eq $bmp) { "SKIP $k"; continue }
    $raw[$k] = $bmp
    "$k = $($bmp.Width)x$($bmp.Height)"
}

# Luna draws the arrow chevron in a single flat colour over otherwise smooth button chrome.
$GLYPH = 0x4D6185
$BAR = 21
$MARGIN = 5

function Is-Glyph([System.Drawing.Bitmap]$b, [int]$x, [int]$y) {
    $c = $b.GetPixel($x, $y)
    return (([int]$c.R -shl 16) -bor ([int]$c.G -shl 8) -bor [int]$c.B) -eq $GLYPH
}

# Returns the button chrome with the chevron painted out, plus the chevron mask.
function Split-Glyph([System.Drawing.Bitmap]$src) {
    $w = $src.Width; $h = $src.Height
    $clean = New-Object System.Drawing.Bitmap $w, $h
    $mask = New-Object 'System.Collections.Generic.List[int[]]'
    for ($y = 0; $y -lt $h; $y++) {
        for ($x = 0; $x -lt $w; $x++) {
            if (Is-Glyph $src $x $y) {
                $mask.Add(@($x, $y))
                $fill = $null
                for ($d = 1; $d -lt $w -and $null -eq $fill; $d++) {
                    if ($x - $d -ge 0 -and -not (Is-Glyph $src ($x - $d) $y)) { $fill = $src.GetPixel($x - $d, $y) }
                    elseif ($x + $d -lt $w -and -not (Is-Glyph $src ($x + $d) $y)) { $fill = $src.GetPixel($x + $d, $y) }
                }
                $clean.SetPixel($x, $y, $fill)
            }
            else { $clean.SetPixel($x, $y, $src.GetPixel($x, $y)) }
        }
    }
    return @{ Clean = $clean; Mask = $mask }
}

function Crop([System.Drawing.Bitmap]$src, [int]$x, [int]$y, [int]$w, [int]$h) {
    $out = New-Object System.Drawing.Bitmap $w, $h
    for ($j = 0; $j -lt $h; $j++) { for ($i = 0; $i -lt $w; $i++) { $out.SetPixel($i, $j, $src.GetPixel($x + $i, $y + $j)) } }
    return $out
}

function Save([System.Drawing.Bitmap]$b, [string]$name) {
    $b.Save("$outDir\$name.png", [System.Drawing.Imaging.ImageFormat]::Png)
    "  -> $name.png $($b.Width)x$($b.Height)"
}

$off = ($BAR - 17) / 2

# Arrow buttons: 9-slice the chrome, then re-stamp the chevron at the new centre.
foreach ($dir in @('up', 'down', 'left', 'right')) {
    $split = Split-Glyph $raw["scroll-arrow-$dir"]
    $big = NineSlice $split.Clean $BAR $BAR $MARGIN
    $gc = [System.Drawing.Color]::FromArgb(($GLYPH -shr 16) -band 0xFF, ($GLYPH -shr 8) -band 0xFF, $GLYPH -band 0xFF)
    foreach ($px in $split.Mask) { $big.SetPixel($px[0] + $off, $px[1] + $off, $gc) }
    Save $big "scroll-arrow-$dir"
    if ($dir -eq 'up') { $vClean = $split.Clean }
    if ($dir -eq 'left') { $hClean = $split.Clean }
}

# Thumb: identical chrome to a button, split at the SizingMargins so the middle tiles.
$vBig = NineSlice $vClean $BAR 17 $MARGIN
Save (Crop $vBig 0 0 $BAR $MARGIN) "scroll-thumb-top"
Save (Crop $vBig 0 8 $BAR 1) "scroll-thumb-mid"
Save (Crop $vBig 0 (17 - $MARGIN) $BAR $MARGIN) "scroll-thumb-bot"

$hBig = NineSlice $hClean 17 $BAR $MARGIN
Save (Crop $hBig 0 0 $MARGIN $BAR) "scroll-thumb-left"
Save (Crop $hBig 8 0 1 $BAR) "scroll-thumb-mid-h"
Save (Crop $hBig (17 - $MARGIN) 0 $MARGIN $BAR) "scroll-thumb-right"

Save (NineSlice $raw['scroll-track'] $BAR 1 $MARGIN) "scroll-track"
Save (NineSlice $raw['scroll-track-h'] 1 $BAR $MARGIN) "scroll-track-h"
Save (NineSlice $raw['scroll-grip'] 9 8 1) "scroll-grip"
Save (NineSlice $raw['scroll-grip-h'] 8 9 1) "scroll-grip-h"
