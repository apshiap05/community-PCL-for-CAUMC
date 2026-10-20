param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [int]$MaxEdge = 1600,
    [int]$Quality = 80,
    [string]$EvidencePath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path (Split-Path $PSScriptRoot) 'Plain Craft Launcher 2/Resources/Backgrounds'
New-Item -ItemType Directory -Force $assetDirectory | Out-Null
$sources = @(Get-ChildItem -LiteralPath $SourceDirectory -File | Where-Object { $_.Extension -match '^\.(png|jpe?g)$' } | Sort-Object Name)
if ($sources.Count -eq 0) { throw '截图目录没有可用图片' }
$jpeg = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$parameters = [Drawing.Imaging.EncoderParameters]::new(1)
$parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality, [long]$Quality)
$manifest = @()
$index = 0
foreach($source in $sources) {
    $index++
    $beforeHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
    $inputImage = [Drawing.Image]::FromFile($source.FullName)
    try {
        $ratio = [Math]::Min(1.0, $MaxEdge / [double][Math]::Max($inputImage.Width,$inputImage.Height))
        $width = [Math]::Max(1,[int][Math]::Round($inputImage.Width * $ratio))
        $height = [Math]::Max(1,[int][Math]::Round($inputImage.Height * $ratio))
        $bitmap = [Drawing.Bitmap]::new($width,$height,[Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($inputImage,0,0,$width,$height)
            } finally { $graphics.Dispose() }
            $file = 'scene-{0:D2}.jpg' -f $index
            $target = Join-Path $assetDirectory $file
            $bitmap.Save($target,$jpeg,$parameters)
            $manifest += [ordered]@{source=$source.Name; sourceBytes=$source.Length; sourceSha256=$beforeHash; originalWidth=$inputImage.Width; originalHeight=$inputImage.Height; file=$file; width=$width; height=$height; bytes=(Get-Item -LiteralPath $target).Length; sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash}
        } finally { $bitmap.Dispose() }
    } finally { $inputImage.Dispose() }
    if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne $beforeHash) { throw '原图发生变化' }
}
$parameters.Dispose()
[IO.File]::WriteAllLines((Join-Path $assetDirectory 'index.txt'),[string[]]@($manifest | ForEach-Object { $_.file }),[Text.UTF8Encoding]::new($false))
if ($EvidencePath) { $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8 }
[pscustomobject]@{Count=$manifest.Count; OriginalBytes=($manifest | ForEach-Object { [long]$_.sourceBytes } | Measure-Object -Sum).Sum; EmbeddedBytes=($manifest | ForEach-Object { [long]$_.bytes } | Measure-Object -Sum).Sum}
