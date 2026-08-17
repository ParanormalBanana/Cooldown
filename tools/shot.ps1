# Captures the running Cooldown window with PrintWindow (works even when occluded)
# and writes a full shot plus zoomed caption / scrollbar crops for review.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$exe = "C:\Users\Loris\Projects\Cooldown\src\Cooldown\bin\Release\net8.0-windows\win-x64\Cooldown.exe"
Get-Process Cooldown -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 7
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq 0) { throw "no main window" }

$r = New-Object Cap+RECT
[void][Cap]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[void][Cap]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save("$env:TEMP\xp-shot.png", [System.Drawing.Imaging.ImageFormat]::Png)

function Zoom($src, $sx, $sy, $sw, $sh, $z, $path) {
    $o = New-Object System.Drawing.Bitmap ($sw * $z), ($sh * $z)
    $gg = [System.Drawing.Graphics]::FromImage($o)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $gg.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, ($sw * $z), ($sh * $z)),
                        (New-Object System.Drawing.Rectangle $sx, $sy, $sw, $sh),
                        [System.Drawing.GraphicsUnit]::Pixel)
    $gg.Dispose()
    $o.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $o.Dispose()
}

Zoom $bmp 0 0 300 34 3 "$env:TEMP\xp-caption.png"
Zoom $bmp ($w - 320) 0 320 34 3 "$env:TEMP\xp-caption-right.png"
Zoom $bmp ($w - 40) 55 40 260 3 "$env:TEMP\xp-scrollbar.png"
$bmp.Dispose()
"captured ${w}x${ht}"
