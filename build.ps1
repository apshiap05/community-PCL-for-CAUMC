param(
    [string]$MSBuildPath,
    [string]$PackagesPath,
    [switch]$PortableMSBuild,
    [switch]$RunTests,
    [string]$MicrosoftClientId = $env:PCL_BUILD_MS_CLIENT_ID,
    [string]$CurseForgeKeyFile
)
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
if (-not $CurseForgeKeyFile) { $CurseForgeKeyFile = Join-Path $sourceRoot '.build/curseforge-api-key.txt' }
$cfKey = $env:PCL_BUILD_CF_API_KEY
if (-not $cfKey -and (Test-Path -LiteralPath $CurseForgeKeyFile)) { $cfKey = [IO.File]::ReadAllText($CurseForgeKeyFile).Trim() }
$credentialResource = Join-Path $sourceRoot '.build/curseforge-key.bin'
if ($cfKey) {
    if ($cfKey.Length -lt 32 -or $cfKey -match '\s') { throw 'CurseForge API 凭据格式无效。' }
    New-Item -ItemType Directory -Force (Split-Path $credentialResource) | Out-Null
    $keyBytes = [Text.Encoding]::UTF8.GetBytes($cfKey)
    $mask = New-Object byte[] $keyBytes.Length
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($mask); $random.Dispose()
    $encoded = New-Object byte[] $keyBytes.Length
    for ($index = 0; $index -lt $keyBytes.Length; $index++) { $encoded[$index] = $keyBytes[$index] -bxor $mask[$index] }
    $writer = New-Object IO.BinaryWriter([IO.File]::Create($credentialResource))
    try { $writer.Write([int]$keyBytes.Length); $writer.Write($mask); $writer.Write($encoded) } finally { $writer.Dispose() }
} elseif (Test-Path -LiteralPath $credentialResource) { Remove-Item -LiteralPath $credentialResource }
$cfKey = $null
& (Join-Path $sourceRoot 'tools/compile-glass-shader.ps1') -SourceRoot $sourceRoot
if (-not $PSBoundParameters.ContainsKey('MicrosoftClientId') -and [string]::IsNullOrWhiteSpace($MicrosoftClientId)) {
    [xml]$publicConfiguration = Get-Content -LiteralPath (Join-Path $sourceRoot 'tools/CaumcPublicConfiguration.props') -Raw
    $MicrosoftClientId = [string]$publicConfiguration.Project.PropertyGroup.CaumcDefaultMicrosoftClientId
}
$MicrosoftClientId = $MicrosoftClientId.Trim()
if ($MicrosoftClientId -and $MicrosoftClientId -notmatch '^(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{16})$') {
    throw 'MicrosoftClientId 必须是微软应用的公开 Client ID（GUID 或 16 位十六进制），不能填 Client Secret。'
}
if ($PackagesPath) { $env:NUGET_PACKAGES = [IO.Path]::GetFullPath($PackagesPath) }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (-not (Test-Path (Join-Path $sourceRoot 'MeloongCore/Shared/MeloongCore.csproj'))) {
    throw '缺少 MeloongCore 子模块。Git 检出请先运行 git submodule update --init；源码包须包含该目录。'
}
if (-not $MSBuildPath) {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($command) { $MSBuildPath = $command.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path $vswhere) {
            $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
        }
    }
}
if (-not $MSBuildPath -or -not (Test-Path $MSBuildPath)) { throw '需要完整 MSBuild（Visual Studio 桌面构建工具，或使用 -MSBuildPath -PortableMSBuild 指定便携工具）。' }
& dotnet build (Join-Path $sourceRoot 'PCLCS/PCLCS.csproj') -c Release -v:minimal
if ($LASTEXITCODE) { throw 'C# 依赖构建失败' }
$buildArgs = @((Join-Path $sourceRoot 'Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj'), '-t:Build', '-p:Configuration=Release', '-v:minimal')
$buildArgs += "-p:CaumcMicrosoftClientId=$MicrosoftClientId"
if ($PortableMSBuild) {
    $packages = if ($PackagesPath) { [IO.Path]::GetFullPath($PackagesPath) } elseif ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $reference = Get-ChildItem (Join-Path $packages 'microsoft.netframework.referenceassemblies.net48') -Recurse -Filter mscorlib.dll | Select-Object -First 1
    if (-not $reference) { throw '找不到 .NET Framework 4.8 目标程序集' }
    $referenceRoot = Split-Path (Split-Path $reference.Directory.FullName)
    $buildArgs += "-p:TargetFrameworkRootPath=$referenceRoot\"
    $buildArgs += "-p:CustomBeforeMicrosoftCommonTargets=$(Join-Path $sourceRoot 'tools/PrebuiltDependencies.targets')"
    $buildArgs += "-p:VbcToolPath=$(Join-Path (Split-Path $MSBuildPath) 'Roslyn')"
    $buildArgs += '-p:CodeAnalysisRuleSet='
}
& $MSBuildPath @buildArgs
if ($LASTEXITCODE) { throw 'WPF 主程序构建失败' }
$dist = Join-Path $sourceRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot 'Plain Craft Launcher 2/bin/Plain Craft Launcher 2.exe') -Destination (Join-Path $dist 'PCL for CAUMC.exe') -Force
if ($RunTests) {
    & dotnet build (Join-Path $sourceRoot 'tests/CAUMC.Tests.csproj') -c Release -v:minimal
    if ($LASTEXITCODE) { throw '测试工程构建失败' }
    & (Join-Path $sourceRoot 'tests/bin/Release/net48/CAUMC.Tests.exe') (Join-Path $sourceRoot 'tests/fixtures/homepage.html')
    if ($LASTEXITCODE) { throw '文章测试失败' }
}
Get-FileHash -LiteralPath (Join-Path $dist 'PCL for CAUMC.exe') -Algorithm SHA256
