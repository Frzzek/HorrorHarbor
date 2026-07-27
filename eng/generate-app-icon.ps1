[CmdletBinding()]
param(
    [string]$OutputDirectory = "src/HorrorHarbor.App/Assets"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-BatPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($X + $Width * 0.04, $Y + $Height * 0.44),
        [System.Drawing.PointF]::new($X + $Width * 0.22, $Y + $Height * 0.31),
        [System.Drawing.PointF]::new($X + $Width * 0.36, $Y + $Height * 0.47),
        [System.Drawing.PointF]::new($X + $Width * 0.43, $Y + $Height * 0.22),
        [System.Drawing.PointF]::new($X + $Width * 0.50, $Y + $Height * 0.36),
        [System.Drawing.PointF]::new($X + $Width * 0.57, $Y + $Height * 0.22),
        [System.Drawing.PointF]::new($X + $Width * 0.64, $Y + $Height * 0.47),
        [System.Drawing.PointF]::new($X + $Width * 0.78, $Y + $Height * 0.31),
        [System.Drawing.PointF]::new($X + $Width * 0.96, $Y + $Height * 0.44),
        [System.Drawing.PointF]::new($X + $Width * 0.79, $Y + $Height * 0.51),
        [System.Drawing.PointF]::new($X + $Width * 0.70, $Y + $Height * 0.72),
        [System.Drawing.PointF]::new($X + $Width * 0.55, $Y + $Height * 0.61),
        [System.Drawing.PointF]::new($X + $Width * 0.50, $Y + $Height * 0.82),
        [System.Drawing.PointF]::new($X + $Width * 0.45, $Y + $Height * 0.61),
        [System.Drawing.PointF]::new($X + $Width * 0.30, $Y + $Height * 0.72),
        [System.Drawing.PointF]::new($X + $Width * 0.21, $Y + $Height * 0.51)
    )
    $path.AddPolygon($points)
    return $path
}

function New-IconFrame {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [Math]::Max(1.0, $Size * 0.035)
        $bounds = [System.Drawing.RectangleF]::new(
            $inset,
            $inset,
            $Size - ($inset * 2),
            $Size - ($inset * 2))
        $tile = New-RoundedRectanglePath $bounds ($Size * 0.205)
        $tileBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 14, 17, 24))
        $borderPen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(255, 185, 35, 61),
            [Math]::Max(1.0, $Size * 0.045))

        try {
            $graphics.FillPath($tileBrush, $tile)
            $graphics.DrawPath($borderPen, $tile)
        }
        finally {
            $borderPen.Dispose()
            $tileBrush.Dispose()
            $tile.Dispose()
        }

        $moonBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 185, 35, 61))
        $cutoutBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 14, 17, 24))
        try {
            $graphics.FillEllipse(
                $moonBrush,
                $Size * 0.19,
                $Size * 0.17,
                $Size * 0.62,
                $Size * 0.68)
            $graphics.FillEllipse(
                $cutoutBrush,
                $Size * 0.37,
                $Size * 0.10,
                $Size * 0.54,
                $Size * 0.62)
        }
        finally {
            $cutoutBrush.Dispose()
            $moonBrush.Dispose()
        }

        $bat = New-BatPath ($Size * 0.22) ($Size * 0.37) ($Size * 0.61) ($Size * 0.38)
        $batBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 244, 188, 97))
        try {
            $graphics.FillPath($batBrush, $bat)
        }
        finally {
            $batBrush.Dispose()
            $bat.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath(
    (Join-Path (Get-Location) $OutputDirectory))
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    [pscustomobject]@{
        Size = $size
        Bytes = New-IconFrame $size
    }
}

$iconPath = Join-Path $resolvedOutput "NocturneHarbor.ico"
$stream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + ($frames.Count * 16)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
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
        $writer.Flush()
        $stream.Write($frame.Bytes, 0, $frame.Bytes.Length)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

$previewPath = Join-Path $resolvedOutput "NocturneHarbor-preview.png"
[System.IO.File]::WriteAllBytes($previewPath, (New-IconFrame 256))

Write-Output "Generated $iconPath"
Write-Output "Generated $previewPath"
