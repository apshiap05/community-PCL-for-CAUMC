param([string]$ExePath, [string]$ScratchRoot, [switch]$WithNetworkChecks, [string]$ModpackPath, [switch]$LaunchOffline, [string]$RegressionInstancePath, [switch]$WithGuideImageChecks, [string]$FabricRequestPath, [string]$SeedGameCachePath, [string]$ModUpdateFixturePath, [AllowEmptyString()][string]$ExpectedMicrosoftClientId, [switch]$WithLayoutChecks)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $sourceRoot 'dist/PCL for CAUMC.exe' }
if (-not $ScratchRoot) { $ScratchRoot = Join-Path $sourceRoot '.build/smoke' }
$testRoot = Join-Path ([IO.Path]::GetFullPath($ScratchRoot)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$testExe = Join-Path $testRoot 'PCL for CAUMC.exe'
Copy-Item -LiteralPath $ExePath -Destination $testExe
if ($WithLayoutChecks) {
    New-Item -ItemType File -Path (Join-Path $testRoot 'layout-tests') | Out-Null
    $expectedPhotos = @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Plain Craft Launcher 2/Resources/Backgrounds') -Filter '*.jpg' | Sort-Object Name | ForEach-Object {
        [ordered]@{file=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
    $expectedPhotos | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'expected-backgrounds.json') -Encoding UTF8
}
if ($PSBoundParameters.ContainsKey('ExpectedMicrosoftClientId')) {
    [IO.File]::WriteAllText((Join-Path $testRoot 'expected-ms-client-id.txt'), $ExpectedMicrosoftClientId)
}
if ($WithGuideImageChecks) { New-Item -ItemType File -Path (Join-Path $testRoot 'guide-image-tests') | Out-Null }
if ($FabricRequestPath) { Copy-Item -LiteralPath $FabricRequestPath -Destination (Join-Path $testRoot 'fabric-install-request.json') }
if ($ModUpdateFixturePath) {
    $modUpdateRequest = Join-Path $ModUpdateFixturePath 'request.json'
    $modUpdateData = Get-Content -LiteralPath $modUpdateRequest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([IO.Path]::GetFileName($modUpdateData.oldFile) -ne $modUpdateData.oldFile) { throw 'Mod 更新样例文件名无效' }
    $updateInstance = Join-Path $testRoot 'CAUMC/.minecraft/versions/caumc-mod-update-regression'
    New-Item -ItemType Directory -Force (Join-Path $updateInstance 'mods') | Out-Null
    Copy-Item -LiteralPath (Join-Path $ModUpdateFixturePath $modUpdateData.oldFile) -Destination (Join-Path $updateInstance 'mods')
    Copy-Item -LiteralPath (Join-Path $ModUpdateFixturePath 'game-profile.json') -Destination (Join-Path $updateInstance 'caumc-mod-update-regression.json')
    Copy-Item -LiteralPath $modUpdateRequest -Destination (Join-Path $testRoot 'mod-update-request.json')
}
if ($SeedGameCachePath) {
    if (-not $FabricRequestPath) { throw '-SeedGameCachePath 需要同时提供 -FabricRequestPath' }
    $targetGame = Join-Path $testRoot 'CAUMC/.minecraft'
    New-Item -ItemType Directory -Force $targetGame | Out-Null
    foreach ($folderName in @('assets', 'libraries')) {
        $seedFolder = Join-Path $SeedGameCachePath $folderName
        if (Test-Path -LiteralPath $seedFolder) { Copy-Item -LiteralPath $seedFolder -Destination (Join-Path $targetGame $folderName) -Recurse }
    }
}
$regressionSourceHashes = @{}
if ($RegressionInstancePath) {
    $regressionSource = Get-Item -LiteralPath $RegressionInstancePath
    $regressionJson = Join-Path $regressionSource.FullName ($regressionSource.Name + '.json')
    if (-not (Test-Path -LiteralPath $regressionJson)) { throw 'Mod 回归实例缺少对应 JSON' }
    $regressionTarget = Join-Path $testRoot 'CAUMC/.minecraft/versions/caumc-mod-regression'
    New-Item -ItemType Directory -Force $regressionTarget | Out-Null
    Copy-Item -LiteralPath $regressionJson -Destination (Join-Path $regressionTarget 'caumc-mod-regression.json')
    $sourceMods = Join-Path $regressionSource.FullName 'mods'
    foreach ($mod in Get-ChildItem -LiteralPath $sourceMods -File -Filter '*.jar') {
        $regressionSourceHashes[$mod.FullName] = (Get-FileHash -LiteralPath $mod.FullName -Algorithm SHA256).Hash
    }
    Copy-Item -LiteralPath $sourceMods -Destination (Join-Path $regressionTarget 'mods') -Recurse
    $sourceLog = Join-Path $regressionSource.FullName 'logs/latest.log'
    if (Test-Path -LiteralPath $sourceLog) {
        Copy-Item -LiteralPath $sourceLog -Destination (Join-Path $testRoot 'regression-game-latest.log')
        $regressionSourceHashes[$sourceLog] = (Get-FileHash -LiteralPath $sourceLog -Algorithm SHA256).Hash
    }
    $regressionSourceHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'source-sha256-before.json') -Encoding UTF8
}
if ($ModpackPath) { Copy-Item -LiteralPath $ModpackPath -Destination (Join-Path $testRoot 'modpack-under-test.zip') }
if ($LaunchOffline) {
    if (-not $ModpackPath) { throw '-LaunchOffline 需要同时提供 -ModpackPath' }
    New-Item -ItemType File -Path (Join-Path $testRoot 'launch-offline-test') | Out-Null
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'tests/fixtures/homepage.html') -Destination (Join-Path $testRoot 'homepage.html')
foreach ($fixtureName in @('guide-mods.html', 'guide-skin.html')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot "tests/fixtures/$fixtureName") -Destination (Join-Path $testRoot $fixtureName)
}
if ($WithNetworkChecks) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'tests/fixtures/minecraft-1.20.1.json') -Destination (Join-Path $testRoot 'minecraft-1.20.1.json')
}
$pythonExe = (Get-Command python -ErrorAction Stop).Source
$fixture = Join-Path $sourceRoot 'tests/fake_auth_server.py'
$urlFile = Join-Path $testRoot 'auth-test-url.txt'
$server = Start-Process -FilePath $pythonExe -ArgumentList @(('"' + $fixture + '"'), ('"' + $urlFile + '"')) -WindowStyle Hidden -PassThru
$process = $null
try {
    for ($attempt = 0; $attempt -lt 50 -and -not (Test-Path -LiteralPath $urlFile); $attempt++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $urlFile)) { throw '本地认证模拟服务未就绪' }
    $process = Start-Process -FilePath $testExe -ArgumentList '--caumc-selftest' -WindowStyle Hidden -PassThru
    $timeout = if ($LaunchOffline -or $FabricRequestPath) { 2000000 } elseif ($ModpackPath) { 1250000 } elseif ($ModUpdateFixturePath) { 600000 } elseif ($RegressionInstancePath -or $WithGuideImageChecks -or $WithLayoutChecks) { 120000 } else { 30000 }
    Write-Output "自检进程：PID=$($process.Id)，目录=$testRoot"
    if (-not $process.WaitForExit($timeout)) { throw "自检超时，PID=$($process.Id)" }
    Get-Content -LiteralPath (Join-Path $testRoot 'selftest-result.txt') -Encoding UTF8
    if ($process.ExitCode -ne 0) { throw "自检失败，退出码=$($process.ExitCode)" }
    foreach ($sourceFile in $regressionSourceHashes.Keys) {
        if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $regressionSourceHashes[$sourceFile]) {
            throw "回归输入源文件发生变化：$sourceFile"
        }
    }
    if ($RegressionInstancePath) { Write-Output "PASS 回归输入源文件 SHA256 未变化：$($regressionSourceHashes.Count) 个文件" }
    Write-Output "自检记录：$testRoot"
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}
