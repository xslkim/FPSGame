param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [switch]$Graphics,
    [string[]]$CaseIds = @(),
    [int]$CaseTimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exePath = (Resolve-Path -LiteralPath $Executable).Path
$logRoot = Join-Path $projectRoot ('temp/native-verify/' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$cases = @(
    @{ Id = 'ActualShots'; Scene = 'res://scenes/debug/shot_qa.tscn'; Flags = @('--shot-selftest', '--qa-report'); Marker = '[SHOT-QA] ALL PASS' },
    @{ Id = 'Level2Presentation'; Scene = 'res://scenes/levels/level2.tscn'; Flags = @('--level2-presentation-selftest'); Marker = '[L2-PRESENTATION] ALL PASS' },
    @{ Id = 'InputAndSession'; Scene = 'res://scenes/debug/runtime_qa.tscn'; Flags = @('--runtime-selftest'); Marker = '[RUNTIME-QA] ALL PASS' }
    @{ Id = 'AnimationEvents'; Scene = 'res://scenes/debug/animation_qa.tscn'; Flags = @('--animation-selftest'); Marker = '[ANIMATION-QA] ALL PASS' }
    @{ Id = 'ReportedVisualIssues'; Scene = 'res://scenes/debug/reported_visual_qa.tscn'; Flags = @('--reported-visual-selftest'); Marker = '[REPORTED-VISUAL-QA] ALL PASS' }
    @{ Id = 'OtherLevelsVisual'; Scene = 'res://scenes/debug/other_levels_visual_qa.tscn'; Flags = @('--other-levels-visual-selftest'); Marker = '[OTHER-LEVELS-VISUAL-QA] ALL PASS' }
)
if ($Graphics) {
    $cases += @{ Id = 'RenderedReport'; Scene = 'res://scenes/debug/shot_qa.tscn'; Flags = @('--report-selftest'); Marker = '[SHOT-QA] ALL PASS'; Render = $true }
}
$failed = @()
$caseIndex = 0
foreach ($case in $cases) {
    if ($CaseIds.Count -gt 0 -and $case.Id -notin $CaseIds) { continue }
    $stdout = Join-Path $logRoot ($case.Id + '.out.log')
    $stderr = Join-Path $logRoot ($case.Id + '.err.log')
    $saveDir = (Join-Path $logRoot ($case.Id + '/userdata')).Replace('\', '/')
    $port = 19800 + $caseIndex++
    [string[]]$arguments = @('--headless')
    if ($case.Render) { $arguments = @('--resolution', '1280x720') }
    $arguments += @('--', ('--quick:' + $case.Scene), "--qa-save-dir:$saveDir", "--qa-udp-port:$port") + $case.Flags
    $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit($CaseTimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id
        $exitCode = -1
    } else {
        $process.Refresh()
        $exitCode = $process.ExitCode
    }
    $output = (Get-Content -LiteralPath $stdout -Raw) + (Get-Content -LiteralPath $stderr -Raw)
    $engineErrors = @($output -split "`n" | Where-Object { $_ -match '^ERROR:' -and $_ -notmatch '^ERROR: \d+ resources still in use at exit' })
    $passed = $exitCode -eq 0 -and $engineErrors.Count -eq 0 -and $output.Contains($case.Marker) -and
        $output -notmatch '\[(?:SHOT|RUNTIME|ANIMATION)-QA\] FAIL:|\[L2-PRESENTATION\] FAIL:|Unhandled exception|System\.[A-Za-z]+Exception'
    if ($passed) {
        Write-Host ('PASS native ' + $case.Id)
    } else {
        $failed += $case.Id
        Write-Host ('FAIL native ' + $case.Id + ' exit=' + $exitCode)
        $output -split "`n" | Select-String 'FAIL|ERROR:|Exception' | Select-Object -Last 12
    }
}
Write-Host ('Executable: ' + $exePath)
Write-Host ('Logs: ' + $logRoot)
if ($failed.Count -gt 0) { throw ('Native verification failed: ' + ($failed -join ', ')) }
Write-Host 'All native checks passed.'
