param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

function New-RoundedRectanglePath {
    param(
        [double]$X,
        [double]$Y,
        [double]$Width,
        [double]$Height,
        [double]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2

    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    return $path
}

function New-Brush {
    param(
        [string]$Start,
        [string]$End,
        [double]$X1,
        [double]$Y1,
        [double]$X2,
        [double]$Y2
    )

    return [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new([float]$X1, [float]$Y1),
        [System.Drawing.PointF]::new([float]$X2, [float]$Y2),
        [System.Drawing.ColorTranslator]::FromHtml($Start),
        [System.Drawing.ColorTranslator]::FromHtml($End))
}

function Fill-EllipseGlow {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.RectangleF]$Bounds,
        [string]$HexColor,
        [byte]$Alpha
    )

    $graphicsPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $graphicsPath.AddEllipse($Bounds)

    $centerColor = [System.Drawing.Color]::FromArgb($Alpha, [System.Drawing.ColorTranslator]::FromHtml($HexColor))
    $edgeColor = [System.Drawing.Color]::FromArgb(0, [System.Drawing.ColorTranslator]::FromHtml($HexColor))
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($graphicsPath)
    $brush.CenterColor = $centerColor
    $brush.SurroundColors = [System.Drawing.Color[]]@($edgeColor)

    $Graphics.FillEllipse($brush, $Bounds)

    $brush.Dispose()
    $graphicsPath.Dispose()
}

function Draw-PopBadge {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Size
    )

    $scale = $Size / 512.0
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $badgePath = New-RoundedRectanglePath (48 * $scale) (48 * $scale) (416 * $scale) (416 * $scale) (110 * $scale)
    $badgeBrush = New-Brush "#091323" "#1B2A50" (64 * $scale) (56 * $scale) (460 * $scale) (468 * $scale)
    $graphics.FillPath($badgeBrush, $badgePath)

    $state = $graphics.Save()
    $graphics.SetClip($badgePath)
    Fill-EllipseGlow $graphics ([System.Drawing.RectangleF]::new([float](250 * $scale), [float](26 * $scale), [float](228 * $scale), [float](212 * $scale))) "#FB923C" 88
    $graphics.Restore($state)

    $badgePen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#35537E"), [float](8 * $scale))
    $graphics.DrawPath($badgePen, $badgePath)

    $framePath = New-RoundedRectanglePath (110 * $scale) (140 * $scale) (292 * $scale) (228 * $scale) (58 * $scale)
    $frameBrush = New-Brush "#13284B" "#0C1930" (110 * $scale) (140 * $scale) (402 * $scale) (368 * $scale)
    $graphics.FillPath($frameBrush, $framePath)

    $framePen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#E6F1FF"), [float](16 * $scale))
    $framePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($framePen, $framePath)

    $panePath = New-RoundedRectanglePath (132 * $scale) (162 * $scale) (110 * $scale) (184 * $scale) (40 * $scale)
    $paneBrush = New-Brush "#FDBA74" "#F97316" (144 * $scale) (146 * $scale) (248 * $scale) (356 * $scale)
    $graphics.FillPath($paneBrush, $panePath)

    $seamPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#EEF5FF"), [float](13 * $scale))
    $seamPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $seamPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($seamPen, [float](256 * $scale), [float](174 * $scale), [float](256 * $scale), [float](334 * $scale))

    $sparkPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float](334 * $scale), [float](118 * $scale)),
        [System.Drawing.PointF]::new([float](352 * $scale), [float](90 * $scale)),
        [System.Drawing.PointF]::new([float](362 * $scale), [float](108 * $scale)),
        [System.Drawing.PointF]::new([float](390 * $scale), [float](118 * $scale)),
        [System.Drawing.PointF]::new([float](362 * $scale), [float](128 * $scale)),
        [System.Drawing.PointF]::new([float](352 * $scale), [float](146 * $scale)),
        [System.Drawing.PointF]::new([float](342 * $scale), [float](128 * $scale)),
        [System.Drawing.PointF]::new([float](314 * $scale), [float](118 * $scale)),
        [System.Drawing.PointF]::new([float](342 * $scale), [float](108 * $scale))
    )

    $sparkBrush = New-Brush "#FFF5E7" "#FED7AA" (332 * $scale) (90 * $scale) (382 * $scale) (144 * $scale)
    $graphics.FillPolygon($sparkBrush, $sparkPoints)

    $sparkPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#F97316"), [float](10 * $scale))
    $sparkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPolygon($sparkPen, $sparkPoints)

    $arcPen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#F97316"), [float](10 * $scale))
    $arcPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arcPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawBezier(
        $arcPen,
        [System.Drawing.PointF]::new([float](316 * $scale), [float](152 * $scale)),
        [System.Drawing.PointF]::new([float](330 * $scale), [float](141 * $scale)),
        [System.Drawing.PointF]::new([float](344 * $scale), [float](127 * $scale)),
        [System.Drawing.PointF]::new([float](353 * $scale), [float](114 * $scale)))

    $arcPen.Dispose()
    $sparkPen.Dispose()
    $sparkBrush.Dispose()
    $seamPen.Dispose()
    $paneBrush.Dispose()
    $panePath.Dispose()
    $framePen.Dispose()
    $frameBrush.Dispose()
    $framePath.Dispose()
    $badgePen.Dispose()
    $badgeBrush.Dispose()
    $badgePath.Dispose()
}

function New-PngBytes {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Draw-PopBadge -Graphics $graphics -Size $Size

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()

    $stream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()

    return ,([byte[]]$bytes)
}

function Write-PngFile {
    param(
        [string]$Path,
        [int]$Size
    )

    [System.IO.File]::WriteAllBytes($Path, (New-PngBytes -Size $Size))
}

function Write-IcoFile {
    param(
        [string]$Path,
        [int]$Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Draw-PopBadge -Graphics $graphics -Size $Size

    $handle = $bitmap.GetHicon()
    $icon = $null
    $stream = $null

    try {
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        $stream = [System.IO.File]::Create($Path)
        $icon.Save($stream)
    }
    finally {
        if ($stream -is [System.IDisposable]) {
            $stream.Dispose()
        }

        if ($icon -is [System.IDisposable]) {
            $icon.Dispose()
        }

        if ($handle -ne [IntPtr]::Zero) {
            [NativeMethods]::DestroyIcon($handle) | Out-Null
        }

        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$brandingDir = Join-Path $repoRoot "artifacts\branding"
$assetDir = Join-Path $repoRoot "Pop.App\Assets"

New-Item -ItemType Directory -Force -Path $brandingDir | Out-Null
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null

Write-PngFile -Path (Join-Path $brandingDir "pop-badge-1024.png") -Size 1024
Write-PngFile -Path (Join-Path $brandingDir "pop-badge-512.png") -Size 512
Write-IcoFile -Path (Join-Path $assetDir "Pop.ico") -Size 256
