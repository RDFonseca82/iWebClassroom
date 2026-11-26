[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IWebClassroomNativeIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@

$directory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(7, 22, 45))
$mint = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(86, 227, 192))
$check = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(86, 227, 192)), 4
$rounded = New-Object System.Drawing.Drawing2D.GraphicsPath
$iconHandle = [IntPtr]::Zero
$icon = $null
$stream = $null

try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $rounded.AddArc(0, 0, 32, 32, 180, 90)
    $rounded.AddArc(32, 0, 32, 32, 270, 90)
    $rounded.AddArc(32, 32, 32, 32, 0, 90)
    $rounded.AddArc(0, 32, 32, 32, 90, 90)
    $rounded.CloseFigure()
    $graphics.FillPath($dark, $rounded)

    $outer = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(12, 18), [System.Drawing.PointF]::new(32, 9),
        [System.Drawing.PointF]::new(52, 18), [System.Drawing.PointF]::new(52, 46),
        [System.Drawing.PointF]::new(32, 56), [System.Drawing.PointF]::new(12, 46))
    $inner = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(21, 25), [System.Drawing.PointF]::new(32, 20),
        [System.Drawing.PointF]::new(43, 25), [System.Drawing.PointF]::new(43, 40),
        [System.Drawing.PointF]::new(32, 45), [System.Drawing.PointF]::new(21, 40))
    $graphics.FillPolygon($mint, $outer)
    $graphics.FillPolygon($dark, $inner)
    $check.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $check.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $check.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($check, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(26, 32), [System.Drawing.PointF]::new(30, 36),
        [System.Drawing.PointF]::new(38, 27)))

    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle).Clone()
    $stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
    $icon.Save($stream)
}
finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $icon) { $icon.Dispose() }
    if ($iconHandle -ne [IntPtr]::Zero) { [IWebClassroomNativeIcon]::DestroyIcon($iconHandle) | Out-Null }
    $rounded.Dispose()
    $check.Dispose()
    $mint.Dispose()
    $dark.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
