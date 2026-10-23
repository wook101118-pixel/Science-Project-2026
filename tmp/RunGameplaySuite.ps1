$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$exe = 'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Builds/GameplayRelease/Scince.exe'
$statePath = Join-Path $taskRoot 'Logs/Gameplay_SuiteStatus.json'
$state = [ordered]@{ status = 'running'; startedUtc = [datetime]::UtcNow.ToString('o'); current = ''; playerPid = 0; completed = @(); errors = @() }
function Save-State { $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding utf8 }
function Run-Player([string]$name, [string[]]$flags, [int]$timeout = 180) {
    if (@(Get-Process -Name Scince -ErrorAction SilentlyContinue).Count) { throw 'A player is already running.' }
    $reportPath = Join-Path $taskRoot "Logs/$name.json"
    if (Test-Path -LiteralPath $reportPath) { Copy-Item -LiteralPath $reportPath -Destination (Join-Path $taskRoot "Logs/${name}_Previous.json") -Force }
    $playerArguments = @('-force-d3d11', '-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0') + $flags + @("--qa-report=$reportPath", '-logFile', (Join-Path $taskRoot "Logs/$name.log"))
    $player = Start-Process -FilePath $exe -ArgumentList $playerArguments -WindowStyle Normal -PassThru
    $state.current = $name; $state.playerPid = $player.Id; Save-State
    Write-Output "$name started (PID $($player.Id))"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $player.WaitForExit(1000)) {
        if ($timer.Elapsed.TotalSeconds -gt $timeout) { Stop-Process -Id $player.Id; throw "$name exceeded its watchdog." }
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.status -ne 'passed' -or $player.ExitCode -ne 0) { throw "$name failed; see $reportPath" }
    $count = if ($null -ne $report.checks) { @($report.checks).Count } else { 0 }
    $state.completed += [pscustomobject]@{ name = $name; status = $report.status; exitCode = $player.ExitCode; checks = $count; elapsedSeconds = $timer.Elapsed.TotalSeconds }
    $state.playerPid = 0; Save-State
    Write-Output "$name passed"
}
try {
    Run-Player 'Gameplay_Cards' @('--qa-gameplay-cards', "--qa-backup=$(Join-Path $taskRoot 'tmp/Gameplay_CardsPrefsBackup.json')")
    Run-Player 'Gameplay_Improvements' @('--qa-gameplay-improvements', "--qa-backup=$(Join-Path $taskRoot 'tmp/Gameplay_UiPrefsBackup.json')")
    Run-Player 'Gameplay_MasteryReset' @('--qa-mastery=reset')
    Run-Player 'Gameplay_Routes' @('--qa-player-routes')
    Run-Player 'Gameplay_MasteryRead' @('--qa-mastery=read')
    Run-Player 'Gameplay_Controls' @('--qa-controls')
    Run-Player 'Gameplay_UiAudio' @('--qa-ui-audio', "--qa-backup=$(Join-Path $taskRoot 'tmp/Gameplay_ServicesPrefsBackup.json')")
    Run-Player 'Gameplay_Smoke' @('--qa-scene-smoke')
    $state.status = 'passed'
} catch {
    $state.status = 'failed'; $state.errors += $_.Exception.ToString()
    Write-Output $_.Exception.Message
} finally {
    $state.finishedUtc = [datetime]::UtcNow.ToString('o'); Save-State
}
if ($state.status -ne 'passed') { exit 1 }
