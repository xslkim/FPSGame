param(
    [ValidateSet('all', 'ui', 'levels')]
    [string]$Suite = 'all',
    [switch]$SkipBuild,
    [string[]]$CaseIds = @(),
    [int]$CaseTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gameRoot = Join-Path $projectRoot 'game'
$godotExe = Join-Path $projectRoot 'godot/bin/godot.windows.editor.x86_64.mono.console.exe'
$logRoot = Join-Path $projectRoot ('temp/verify/' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

if (-not (Test-Path -LiteralPath $godotExe)) {
    throw "Godot executable not found: $godotExe"
}

if (-not $SkipBuild) {
    $buildLog = Join-Path $logRoot 'build.log'
    & dotnet build (Join-Path $gameRoot 'FPSGame.csproj') -c Debug -v q *> $buildLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $buildLog | Select-Object -Last 40
        throw "C# build failed. Log: $buildLog"
    }
    Write-Host 'PASS build'
}

$cases = @(
    @{ Id = 'UI/Menu'; Scene = 'res://scenes/ui/menu.tscn'; Flag = '--menu-selftest'; Marker = 'MENU SELFTEST PASS'; Group = 'ui' },
    @{ Id = 'UI/LevelChoose'; Scene = 'res://scenes/ui/level_choose.tscn'; Flag = '--levelchoose-selftest'; Marker = 'LEVELCHOOSE SELFTEST PASS'; Group = 'ui' },
    @{ Id = 'UI/DeviceConnection'; Scene = 'res://scenes/ui/device_connection.tscn'; Flag = '--deviceconnection-selftest'; Marker = 'DEVICECONNECTION SELFTEST PASS'; Group = 'ui' },
    @{ Id = 'UI/MenuToLevelChoose'; Scene = 'res://scenes/ui/menu.tscn'; Flag = '--e2e-mouse-flow'; Marker = 'E2E MOUSE FLOW PASS'; Group = 'ui' },
    @{ Id = 'UI/Loading'; Scene = 'res://scenes/ui/loading.tscn'; Flag = '--loading-selftest'; Marker = 'LOADING SELFTEST PASS'; Group = 'ui' },
    @{ Id = 'UI/LoadingRecovery'; Scene = 'res://scenes/ui/loading.tscn'; Flag = '--loading-selftest-stuck'; Marker = 'LOADING SELFTEST PASS'; Group = 'ui' },
    @{ Id = 'Runtime/InputAndSession'; Scene = 'res://scenes/debug/runtime_qa.tscn'; Flag = '--runtime-selftest'; Marker = '[RUNTIME-QA] ALL PASS'; Group = 'levels' },
    @{ Id = 'Debug/Report'; Script = 'res://tools/debug_smoke.gd'; Marker = '[DEBUG-SMOKE] PASS'; Group = 'ui' },
    @{ Id = 'Port/Geometry'; Script = 'res://tools/port_geometry_regression.gd'; Marker = '[PORT-GEOMETRY] PASS'; Group = 'levels' },
    @{ Id = 'Port/Effects'; Script = 'res://tools/effect_regression.gd'; Marker = '[EFFECT-REGRESSION] PASS'; Group = 'levels' },
    @{ Id = 'Visual/ReportedIssues'; Scene = 'res://scenes/debug/reported_visual_qa.tscn'; Flag = '--reported-visual-selftest'; Marker = '[REPORTED-VISUAL-QA] ALL PASS'; Group = 'levels' },
    @{ Id = 'Visual/OtherLevels'; Scene = 'res://scenes/debug/other_levels_visual_qa.tscn'; Flag = '--other-levels-visual-selftest'; Marker = '[OTHER-LEVELS-VISUAL-QA] ALL PASS'; Group = 'levels' },
    @{ Id = 'L1/Story'; Scene = 'res://scenes/levels/level1_story.tscn'; Flag = '--story-selftest'; Marker = '[STORY-TEST] done, failed=False'; Group = 'levels' },
    @{ Id = 'L1/Battle'; Scene = 'res://scenes/levels/level1_battle.tscn'; Flag = '--level1-selftest'; Marker = '[L1-TEST] done, failed=False'; Group = 'levels' },
    @{ Id = 'L2/Battle'; Scene = 'res://scenes/levels/level2.tscn'; Flag = '--level2-selftest'; Marker = '[L2-TEST] done, failed=False'; Group = 'levels' },
    @{ Id = 'L2/Presentation'; Scene = 'res://scenes/levels/level2.tscn'; Flag = '--level2-presentation-selftest'; Marker = '[L2-PRESENTATION] ALL PASS'; Group = 'levels' },
    @{ Id = 'Combat/WeaponsAndHealth'; Scene = 'res://scenes/levels/level2.tscn'; Flag = '--combat-presentation-selftest'; Marker = '[COMBAT-PRESENTATION] ALL PASS'; Group = 'levels' },
    @{ Id = 'Combat/PauseShot'; Scene = 'res://scenes/levels/level2.tscn'; Flag = '--panel-click-test'; Marker = '[PANEL-TEST] done'; Group = 'levels' },
    @{ Id = 'Combat/ActualShots'; Scene = 'res://scenes/debug/shot_qa.tscn'; Flag = '--shot-selftest'; Marker = '[SHOT-QA] ALL PASS'; Group = 'levels' },
    @{ Id = 'Combat/AnimationEvents'; Scene = 'res://scenes/debug/animation_qa.tscn'; Flag = '--animation-selftest'; Marker = '[ANIMATION-QA] ALL PASS'; Group = 'levels' },
    @{ Id = 'L3/Ground'; Scene = 'res://scenes/levels/level3.tscn'; Flag = '--level3-ground-selftest'; Marker = '[L3-GROUND] ALL PASS'; Group = 'levels' },
    @{ Id = 'L3/Battle'; Scene = 'res://scenes/levels/level3.tscn'; Flag = '--level3-selftest'; Marker = '[L3-TEST] done, failed=False'; Group = 'levels' },
    @{ Id = 'L4/Battle'; Scene = 'res://scenes/levels/level4.tscn'; Flag = '--level4-selftest'; Marker = '[L4-TEST] done, failed=False'; Group = 'levels' },
    @{ Id = 'Debug/LevelReport'; Script = 'res://tools/debug_level_smoke.gd'; Marker = '[DEBUG-LEVEL-SMOKE] PASS'; Group = 'levels' },
    @{ Id = 'Debug/StoryReport'; Script = 'res://tools/debug_story_smoke.gd'; Marker = '[DEBUG-STORY-SMOKE] PASS'; Group = 'levels' }
)

$failed = @()
$caseIndex = 0
foreach ($case in $cases) {
    if ($Suite -ne 'all' -and $case.Group -ne $Suite) { continue }
    if ($CaseIds.Count -gt 0 -and $case.Id -notin $CaseIds) { continue }
    $safeId = $case.Id.Replace('/', '-')
    $stdout = Join-Path $logRoot ($safeId + '.out.log')
    $stderr = Join-Path $logRoot ($safeId + '.err.log')
    $saveDir = Join-Path $logRoot ($safeId + '/userdata')
    $port = 19000 + $caseIndex++
    $arguments = @('--headless', '--path', $gameRoot)
    if ($case.ContainsKey('Script')) {
        $arguments += @('-s', $case.Script)
    } else {
        $arguments += @($case.Scene)
    }
    $arguments += @('--', "--qa-save-dir:$saveDir", "--qa-udp-port:$port")
    if ($case.ContainsKey('Flag')) { $arguments += $case.Flag }
    $process = Start-Process -FilePath $godotExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $timedOut = -not $process.WaitForExit($CaseTimeoutSeconds * 1000)
    if ($timedOut) { Stop-Process -Id $process.Id; $exitCode = -1 } else { $process.Refresh(); $exitCode = $process.ExitCode }
    $output = (Get-Content -LiteralPath $stdout -Raw) + (Get-Content -LiteralPath $stderr -Raw)
    # Exit-time engine resource cleanup diagnostics are tracked separately;
    # parse errors, failed methods and runtime engine errors must fail the gate.
    $engineErrors = @($output -split "`n" | Where-Object { $_ -match '^ERROR:' -and $_ -notmatch '^ERROR: \d+ resources still in use at exit' })
    $passed = $exitCode -eq 0 -and
        $engineErrors.Count -eq 0 -and
        $output.Contains($case.Marker) -and $output -notmatch '(?m)^FAIL |\[[A-Z0-9-]+(?:-TEST|-QA)?\] FAIL:|Unhandled exception|System\.[A-Za-z]+Exception'
    if ($passed) {
        Write-Host ('PASS ' + $case.Id)
    } else {
        $failed += $case.Id
        Write-Host ('FAIL ' + $case.Id + '  exit=' + $exitCode + ' marker=' + $output.Contains($case.Marker))
        $output -split "`n" | Select-String 'FAIL|ERROR:|Exception|SELFTEST|done, failed' | Select-Object -Last 12
    }
}

Write-Host ('Logs: ' + $logRoot)
if ($failed.Count -gt 0) {
    throw ('Verification failed: ' + ($failed -join ', '))
}
Write-Host 'All selected scenes passed.'
