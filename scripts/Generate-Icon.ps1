$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/TaskIsland.WindowsIntegration/Assets/islet.ico'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$frames = foreach ($size in 16, 20, 24, 32, 48, 64, 256) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(3, 8, 40, 40, 90, 180)
    $shape.AddArc(21, 8, 40, 40, 270, 180)
    $shape.CloseFigure()
    $dark = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#171D28'))
    $accent = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#A6D9B5'), 5)
    $accent.StartCap = $accent.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $accent.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.FillPath($dark, $shape)
    $graphics.DrawLines($accent, [Drawing.PointF[]]@([Drawing.PointF]::new(21,29), [Drawing.PointF]::new(29,37), [Drawing.PointF]::new(43,22)))
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    [PSCustomObject]@{ Size = $size; Data = $stream.ToArray() }
    $stream.Dispose(); $accent.Dispose(); $dark.Dispose(); $shape.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create($destination)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $side = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $file.Dispose() }
Get-Item $destination | Select-Object FullName, Length
