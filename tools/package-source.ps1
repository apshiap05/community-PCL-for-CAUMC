param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot
$archivePath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $archivePath) { throw "源码包已存在，请使用新路径：$archivePath" }

$tracked = @(& git -C $sourceRoot -c core.quotepath=false ls-files)
if ($LASTEXITCODE) { throw '无法读取主仓库文件列表' }
$coreFiles = @(& git -C (Join-Path $sourceRoot 'MeloongCore') -c core.quotepath=false ls-files)
if ($LASTEXITCODE -or -not $coreFiles.Count) { throw '缺少已初始化的 MeloongCore 子模块' }
$tracked += @($coreFiles | ForEach-Object { 'MeloongCore/' + $_ })
$untracked = @(& git -C $sourceRoot -c core.quotepath=false ls-files --others --exclude-standard)
if ($LASTEXITCODE) { throw '无法读取新增源文件列表' }
$files = @($tracked + $untracked | Sort-Object -Unique)
$manifest = [ordered]@{
    project = 'PCL for CAUMC'
    projectCommit = (& git -C $sourceRoot rev-parse HEAD).Trim()
    upstreamCommit = '6b9a50d'
    meloongCoreCommit = (& git -C (Join-Path $sourceRoot 'MeloongCore') rev-parse HEAD).Trim()
    files = [ordered]@{}
}
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        $path = Join-Path $sourceRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $fullPath = [IO.Path]::GetFullPath($path)
        if (-not $fullPath.StartsWith(([IO.Path]::GetFullPath($sourceRoot) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw "源文件超出仓库范围：$relative" }
        $manifest.files[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $entry = $zip.CreateEntry($relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
        $input = [IO.File]::OpenRead($path)
        $output = $entry.Open()
        try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
    }
    foreach ($required in @('MeloongCore/Shared/MeloongCore.csproj', 'MeloongCore/Wpf/MeloongCore.Wpf.csproj')) {
        if (-not $manifest.files.Contains($required)) { throw "源码包缺少构建文件：$required" }
    }
    $entry = $zip.CreateEntry('SOURCE-MANIFEST.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($manifest | ConvertTo-Json -Depth 5)) } finally { $writer.Dispose() }
} finally {
    $zip.Dispose()
    $stream.Dispose()
}
Write-Output "源码包：$archivePath（$($manifest.files.Count) 个文件，附逐文件 SHA256）"
