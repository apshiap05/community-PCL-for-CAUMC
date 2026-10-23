param([string]$ExePath, [string]$ScratchRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
if (-not $ExePath) { $ExePath = Join-Path $repoRoot 'dist/PCL for CAUMC.exe' }
if (-not $ScratchRoot) { $ScratchRoot = Join-Path ([IO.Path]::GetTempPath()) 'CAUMC-updater-tests' }
$testRoot = Join-Path ([IO.Path]::GetFullPath($ScratchRoot)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$fixture = Join-Path $repoRoot 'tests/fixtures/UpdateTarget.cs'
foreach ($mode in @('success', 'rollback', 'replacement-failure')) {
    $caseRoot = Join-Path $testRoot $mode
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    $target = Join-Path $caseRoot '原启动器名字.exe'
    $package = Join-Path $caseRoot 'package.exe'
    $helper = Join-Path $caseRoot 'updater.exe'
    & $compiler /nologo /target:winexe "/out:$target" $fixture
    if ($LASTEXITCODE) { throw '旧版样例编译失败' }
    & $compiler /nologo /target:winexe /define:NEW_VERSION "/out:$package" $fixture
    if ($LASTEXITCODE) { throw '新版样例编译失败' }
    Copy-Item -LiteralPath $ExePath -Destination $helper
    $oldHash = (Get-FileHash -LiteralPath $target).Hash
    $record = Join-Path $caseRoot 'transaction.json'
    if ($mode -eq 'rollback') { Set-Content -LiteralPath ($record + '.fail') -Value 'simulate initialization failure' }
    New-Item -ItemType Directory -Path (Join-Path $caseRoot '.minecraft') | Out-Null
    $gameMarker = Join-Path $caseRoot '.minecraft/player-data.txt'
    Set-Content -LiteralPath $gameMarker -Value 'game-data-unchanged'
    $parent = Start-Process -FilePath $target -ArgumentList '--parent' -WindowStyle Hidden -PassThru
    $tx = [ordered]@{Target=$target; Package=$package; Version='99.0.0.0'; Sha256=(Get-FileHash -LiteralPath $package).Hash;
        Nonce=[guid]::NewGuid().ToString('N'); ParentId=$parent.Id; ParentStarted=$parent.StartTime.ToUniversalTime().Ticks; Status='prepared'; Error=''}
    $tx | ConvertTo-Json | Set-Content -LiteralPath $record -Encoding UTF8
    $lock = $null
    if ($mode -eq 'replacement-failure') { $lock = [IO.File]::Open($target, 'Open', 'Read', 'Read') }
    $worker = Start-Process -FilePath $helper -ArgumentList @('--caumc-update-helper', ('"' + $record + '"')) -WindowStyle Hidden -PassThru
    try {
        $accepted = $false
        for ($attempt = 0; $attempt -lt 200; $attempt++) {
            Start-Sleep -Milliseconds 25
            try { $current = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json } catch { continue }
            if ($current.Status -eq 'waiting') {
                $current.Status = 'approved'
                $current | ConvertTo-Json | Set-Content -LiteralPath ($record + '.approve') -Encoding UTF8
                [IO.File]::Replace(($record + '.approve'), $record, ($record + '.previous'))
                $accepted = $true
                break
            }
            if ($worker.HasExited) { break }
        }
        if (-not $accepted) { throw "辅助进程未就绪：$record" }
        Stop-Process -Id $parent.Id -Force
        $parent.WaitForExit()
        $parent.Dispose()
        $parent = $null
        if (-not $worker.WaitForExit(120000)) { throw "更新测试超时：$record" }
        $result = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
        if ($mode -eq 'success') {
            if ($result.Status -ne 'succeeded' -or [Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion -ne '99.0.0.0') { throw '真实进程替换失败' }
            if ((Test-Path -LiteralPath ($target + '.caumc-' + $tx.Nonce + '.bak')) -or (Test-Path -LiteralPath $package)) { throw '成功后未清理备份和安装包' }
            Write-Output 'PASS 独立辅助进程等待旧进程退出，替换原文件名 EXE，新进程确认初始化后清理备份'
        } else {
            if ((Get-FileHash -LiteralPath $target).Hash -ne $oldHash) { throw '失败后原 EXE 不一致' }
            if ($mode -eq 'rollback') {
                for ($attempt = 0; $attempt -lt 40 -and -not (Test-Path -LiteralPath ($record + '.old-started')); $attempt++) { Start-Sleep -Milliseconds 50 }
                if ($result.Status -ne 'rolledback' -or -not (Test-Path -LiteralPath ($record + '.old-started'))) { throw '未恢复并重新打开旧版' }
                Write-Output 'PASS 新进程初始化失败后，辅助进程恢复旧 EXE 并重新打开旧版'
            } else {
                if ($result.Status -ne 'failed') { throw '替换失败状态未保存' }
                for ($attempt = 0; $attempt -lt 40 -and -not (Test-Path -LiteralPath ($record + '.old-started')); $attempt++) { Start-Sleep -Milliseconds 50 }
                if (-not (Test-Path -LiteralPath ($record + '.old-started'))) { throw '替换失败后没有重新打开原程序' }
                Write-Output 'PASS EXE 被占用导致替换失败时，保留原程序、安装包和错误记录'
            }
        }
        if ((Get-Content -LiteralPath $gameMarker -Raw).Trim() -ne 'game-data-unchanged') { throw '游戏数据发生变化' }
        Write-Output "PASS $mode 游戏目录保持原内容"
    } finally {
        if ($lock) { $lock.Dispose() }
        if (-not $worker.HasExited) { Stop-Process -Id $worker.Id -Force }
        if ($parent) {
            if (-not $parent.HasExited) { Stop-Process -Id $parent.Id -Force }
            $parent.Dispose()
        }
        $worker.Dispose()
    }
}
Write-Output "更新进程验证：$testRoot"
