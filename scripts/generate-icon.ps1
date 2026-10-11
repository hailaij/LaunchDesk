$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/LaunchDesk.App/Assets/LaunchDesk.ico'
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
$images = @()
foreach ($size in 16, 24, 32, 48, 64, 256) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#13795B'))
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    foreach ($arc in @(@(2,2,16,16,180,90),@(46,2,16,16,270,90),@(46,46,16,16,0,90),@(2,46,16,16,90,90))) {
        $path.AddArc([single]$arc[0],[single]$arc[1],[single]$arc[2],[single]$arc[3],[single]$arc[4],[single]$arc[5])
    }
    $path.CloseFigure(); $graphics.FillPath($brush,$path)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White,4)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(18,23),[Drawing.PointF]::new(28,32),[Drawing.PointF]::new(18,41)))
    $graphics.DrawLine($pen,35,41,47,41)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{Size=$size; Bytes=$stream.ToArray()}
    $stream.Dispose(); $pen.Dispose(); $path.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create($destination)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output $destination
