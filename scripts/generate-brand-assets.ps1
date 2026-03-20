param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function New-SizedBitmapFromSource {
    param(
        [string]$SourcePath,
        [int]$Size,
        [switch]$TrimToContent,
        [int]$AlphaThreshold = 96,
        [double]$PaddingScale = 0.08
    )

    $source = [System.Drawing.Image]::FromFile($SourcePath)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $sourceRect = [System.Drawing.RectangleF]::new(0, 0, $source.Width, $source.Height)
        if ($TrimToContent) {
            $minX = $source.Width
            $minY = $source.Height
            $maxX = -1
            $maxY = -1

            $sourceBitmap = [System.Drawing.Bitmap]$source
            for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
                for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
                    if ($sourceBitmap.GetPixel($x, $y).A -ge $AlphaThreshold) {
                        if ($x -lt $minX) { $minX = $x }
                        if ($y -lt $minY) { $minY = $y }
                        if ($x -gt $maxX) { $maxX = $x }
                        if ($y -gt $maxY) { $maxY = $y }
                    }
                }
            }

            if ($maxX -ge 0) {
                $contentWidth = $maxX - $minX + 1
                $contentHeight = $maxY - $minY + 1
                $padding = [Math]::Ceiling([Math]::Max($contentWidth, $contentHeight) * $PaddingScale)
                $cropX = [Math]::Max(0, $minX - $padding)
                $cropY = [Math]::Max(0, $minY - $padding)
                $cropRight = [Math]::Min($source.Width - 1, $maxX + $padding)
                $cropBottom = [Math]::Min($source.Height - 1, $maxY + $padding)
                $sourceRect = [System.Drawing.RectangleF]::new(
                    [float]$cropX,
                    [float]$cropY,
                    [float]($cropRight - $cropX + 1),
                    [float]($cropBottom - $cropY + 1))
            }
        }

        $scale = [Math]::Min($Size / $sourceRect.Width, $Size / $sourceRect.Height)
        $drawWidth = [float]($sourceRect.Width * $scale)
        $drawHeight = [float]($sourceRect.Height * $scale)
        $offsetX = [float](($Size - $drawWidth) / 2)
        $offsetY = [float](($Size - $drawHeight) / 2)
        $destination = [System.Drawing.RectangleF]::new($offsetX, $offsetY, $drawWidth, $drawHeight)
        $graphics.DrawImage($source, $destination, $sourceRect, [System.Drawing.GraphicsUnit]::Pixel)

        return $bitmap
    }
    finally {
        $graphics.Dispose()
        $source.Dispose()
    }
}

function New-PngBytes {
    param(
        [string]$SourcePath,
        [int]$Size,
        [switch]$TrimToContent
    )

    $bitmap = New-SizedBitmapFromSource -SourcePath $SourcePath -Size $Size -TrimToContent:$TrimToContent

    try {
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,([byte[]]$stream.ToArray())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Write-PngFile {
    param(
        [string]$Path,
        [string]$SourcePath,
        [int]$Size,
        [switch]$TrimToContent
    )

    [System.IO.File]::WriteAllBytes($Path, (New-PngBytes -SourcePath $SourcePath -Size $Size -TrimToContent:$TrimToContent))
}

function Write-IcoFile {
    param(
        [string]$Path,
        [string]$SourcePath,
        [int[]]$Sizes
    )

    $frames = foreach ($size in $Sizes | Sort-Object -Unique) {
        [pscustomobject]@{
            Size = $size
            Bytes = New-PngBytes -SourcePath $SourcePath -Size $size -TrimToContent
        }
    }

    $stream = $null
    $writer = $null

    try {
        $stream = [System.IO.File]::Create($Path)
        $writer = [System.IO.BinaryWriter]::new($stream)

        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -ge 256) { 0 } else { [byte]$frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write([byte[]]$frame.Bytes)
        }
    }
    finally {
        if ($writer -is [System.IDisposable]) {
            $writer.Dispose()
        }

        elseif ($stream -is [System.IDisposable]) {
            $stream.Dispose()
        }
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$brandingDir = Join-Path $repoRoot "artifacts\branding"
$assetDir = if (Test-Path (Join-Path $repoRoot "src\Pop.App.Windows")) {
    Join-Path $repoRoot "src\Pop.App.Windows\Assets"
}
else {
    Join-Path $repoRoot "Pop.App\Assets"
}
$iconSourcePath = Join-Path $repoRoot "official_icon.png"

if (-not (Test-Path $iconSourcePath)) {
    throw "Missing icon source image: $iconSourcePath"
}

New-Item -ItemType Directory -Force -Path $brandingDir | Out-Null
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null

Write-PngFile -Path (Join-Path $brandingDir "pop-badge-1024.png") -SourcePath $iconSourcePath -Size 1024
Write-PngFile -Path (Join-Path $brandingDir "pop-badge-512.png") -SourcePath $iconSourcePath -Size 512
Write-IcoFile -Path (Join-Path $assetDir "Pop.ico") -SourcePath $iconSourcePath -Sizes @(16, 20, 24, 32, 40, 48, 64, 128, 256)
