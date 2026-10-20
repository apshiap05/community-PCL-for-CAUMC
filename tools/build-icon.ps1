param([string]$ImagePath = (Join-Path (Split-Path $PSScriptRoot) 'Plain Craft Launcher 2/Images/CAUMC.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outputPath = Join-Path (Split-Path $PSScriptRoot) 'Plain Craft Launcher 2/Images/icon.ico'
$splashPath = Join-Path (Split-Path $PSScriptRoot) 'Plain Craft Launcher 2/Images/Splash.png'
$source = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($ImagePath))
$frames = @()
try {
    foreach ($size in @(16,24,32,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            If ($size -eq 256) { [IO.File]::WriteAllBytes($splashPath,$stream.ToArray()) }
            $frames += @{size=$size; bytes=$stream.ToArray()}
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$file = [IO.File]::Create($outputPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.size -eq 256) { 0 } else { $frame.size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output 'Built icon.ico (16–256 px, RGBA)'
