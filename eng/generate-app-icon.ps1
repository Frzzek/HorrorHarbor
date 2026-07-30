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

function New-LighthousePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($X + $Width * 0.36, $Y + $Height * 0.18),
        [System.Drawing.PointF]::new($X + $Width * 0.64, $Y + $Height * 0.18),
        [System.Drawing.PointF]::new($X + $Width * 0.72, $Y + $Height),
        [System.Drawing.PointF]::new($X + $Width * 0.28, $Y + $Height)
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
            [System.Drawing.Color]::FromArgb(255, 190, 28, 58))
        try {
            $graphics.FillEllipse(
                $moonBrush,
                $Size * 0.54,
                $Size * 0.15,
                $Size * 0.28,
                $Size * 0.28)
        }
        finally {
            $moonBrush.Dispose()
        }

        $beamPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $beamPath.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($Size * 0.08, $Size * 0.43),
            [System.Drawing.PointF]::new($Size * 0.44, $Size * 0.35),
            [System.Drawing.PointF]::new($Size * 0.44, $Size * 0.52),
            [System.Drawing.PointF]::new($Size * 0.08, $Size * 0.49)
        ))
        $beamPath.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($Size * 0.56, $Size * 0.35),
            [System.Drawing.PointF]::new($Size * 0.92, $Size * 0.43),
            [System.Drawing.PointF]::new($Size * 0.92, $Size * 0.49),
            [System.Drawing.PointF]::new($Size * 0.56, $Size * 0.52)
        ))
        $beamBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 244, 181, 72))
        try {
            $graphics.FillPath($beamBrush, $beamPath)
        }
        finally {
            $beamBrush.Dispose()
            $beamPath.Dispose()
        }

        $lighthouse = New-LighthousePath ($Size * 0.35) ($Size * 0.37) ($Size * 0.30) ($Size * 0.42)
        $lighthouseBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 232, 225, 208))
        try {
            $graphics.FillPath($lighthouseBrush, $lighthouse)
            $graphics.FillRectangle(
                $lighthouseBrush,
                $Size * 0.39,
                $Size * 0.33,
                $Size * 0.22,
                $Size * 0.09)
        }
        finally {
            $lighthouseBrush.Dispose()
            $lighthouse.Dispose()
        }

        $lanternBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 244, 181, 72))
        try {
            $graphics.FillEllipse(
                $lanternBrush,
                $Size * 0.46,
                $Size * 0.35,
                $Size * 0.08,
                $Size * 0.08)
        }
        finally {
            $lanternBrush.Dispose()
        }

        $waterPen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(255, 39, 132, 157),
            [Math]::Max(1.0, $Size * 0.045))
        $waterPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $waterPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            $graphics.DrawArc($waterPen, $Size * 0.14, $Size * 0.70, $Size * 0.42, $Size * 0.20, 15, 150)
            $graphics.DrawArc($waterPen, $Size * 0.44, $Size * 0.70, $Size * 0.42, $Size * 0.20, 15, 150)
            $graphics.DrawArc($waterPen, $Size * 0.28, $Size * 0.78, $Size * 0.44, $Size * 0.14, 15, 150)
        }
        finally {
            $waterPen.Dispose()
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
