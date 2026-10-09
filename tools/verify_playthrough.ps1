param(
    [ValidateSet('level1','level2','level3','level4')]
    [string[]]$Levels = @('level2','level1','level3','level4'),
    [ValidateSet('easy','hard','hell')]
    [string]$Difficulty = 'easy',
    [UInt64]$Seed = 144006,
    [string]$Executable = '',
    [switch]$SkipBuild,
    [switch]$Graphics,
    [switch]$CaptureScreenshots,
    [int]$UdpPort = 19917,
    [int]$TimeoutSeconds = 4500
)

$ErrorActionPreference = 'Stop'
if ($CaptureScreenshots -and -not $Graphics) { throw 'CaptureScreenshots requires Graphics.' }
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gameRoot = Join-Path $projectRoot 'game'
$logRoot = Join-Path $projectRoot ('temp/playthrough/' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$saveDir = (Join-Path $logRoot 'userdata').Replace('\','/')
if ($Executable) {
    $exePath = (Resolve-Path -LiteralPath $Executable).Path
} else {
    $exePath = Join-Path $projectRoot 'godot/bin/godot.windows.editor.x86_64.mono.console.exe'
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $gameRoot 'FPSGame.csproj') -c Debug -v q *> (Join-Path $logRoot 'build.log')
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $logRoot/build.log" }
    }
}
[string[]]$arguments = @('--headless')
if ($Graphics) { $arguments = @('--resolution','1280x720') }
if (-not $Executable) { $arguments += @('--path',$gameRoot,'res://scenes/ui/menu.tscn') }
$arguments += @('--',"--qa-save-dir:$saveDir","--qa-udp-port:$UdpPort",
    ('--playthrough:' + ($Levels -join ',') + ':' + $Difficulty + ':' + $Seed))
if ($CaptureScreenshots) {
    $shotDir = (Join-Path $logRoot 'screenshots').Replace('\','/')
    $arguments += "--playthrough-shot-dir:$shotDir"
}
$stdout = Join-Path $logRoot 'out.log'
$stderr = Join-Path $logRoot 'err.log'
$process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
Write-Host "Normal playthrough PID $($process.Id). Logs: $logRoot"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
    if ($process.WaitForExit(1000)) { break }
    $process.Refresh()
}
if (-not $process.HasExited) {
    # The console launcher has an engine child. Stop only this verified QA process tree.
    Get-CimInstance Win32_Process | Where-Object {
        $_.ParentProcessId -eq $process.Id -and $_.CommandLine -like "*--qa-save-dir:$saveDir*"
    } | ForEach-Object { Stop-Process -Id $_.ProcessId }
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw "Playthrough timed out. Logs: $logRoot"
}
$process.WaitForExit()
$process.Refresh()
$output = (Get-Content -LiteralPath $stdout -Raw) + (Get-Content -LiteralPath $stderr -Raw)
$errors = @($output -split "`n" | Where-Object { $_ -match '^ERROR:' -and $_ -notmatch '^ERROR: \d+ resources still in use at exit' })
Get-Content -LiteralPath $stdout | Select-String '\[PLAYTHROUGH-QA\] (PASS|FAIL|SUMMARY|ALL PASS|FAILED)'
if ($process.ExitCode -ne 0 -or $errors.Count -gt 0 -or -not $output.Contains('[PLAYTHROUGH-QA] ALL PASS') -or
    $output -match '\[PLAYTHROUGH-QA\] FAIL:|Unhandled exception|System\.[A-Za-z]+Exception') {
    $output -split "`n" | Select-String 'FAIL:|^ERROR:|Exception' | Select-Object -Last 15
    throw "Normal playthrough failed. Logs: $logRoot"
}
Write-Host "Normal playthrough passed. Reports: $saveDir"
