param(
    [string]$Executable = 'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Builds/Stage4UxQa/ScienceProject.exe',
    [ValidateSet('default','keyboard','recovery')] [string]$Case = 'default',
    [string]$Tag = '',
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$scienceTaskRoot = Split-Path $PSScriptRoot -Parent
$scienceLogRoot = Join-Path $scienceTaskRoot 'Logs/Stage4Guidance'
New-Item -ItemType Directory -Path $scienceLogRoot -Force | Out-Null
$scienceName = 'Stage4-' + $Case
if ($Tag) { $scienceName += '-' + $Tag }
if ($Visible) { $scienceName += '-Visible' }
$scienceReportPath = Join-Path $scienceLogRoot ($scienceName + '.json')
$scienceProcessPath = Join-Path $scienceLogRoot ($scienceName + '-Process.json')
$scienceBackupPath = $scienceReportPath + '.ProgressBackup.json'
$scienceAudit = [ordered]@{ status='running'; startedUtc=[datetime]::UtcNow.ToString('o'); executable=$Executable; report=$scienceReportPath; pid=0; errors=@() }
function Save-ScienceAudit { $scienceAudit | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $scienceProcessPath -Encoding utf8 }
function Restore-ScienceProgress {
    if (-not (Test-Path -LiteralPath $scienceBackupPath)) { throw 'No progress backup exists for recovery.' }
    $scienceRecovery = Start-Process -FilePath $Executable -ArgumentList @('-batchmode','-nographics','--qa-restore-progress',"--qa-progress-backup=$scienceBackupPath",'-logFile',(Join-Path $scienceLogRoot ($scienceName + '-Recovery.log'))) -WindowStyle Hidden -PassThru
    if (-not $scienceRecovery.WaitForExit(30000)) { Stop-Process -Id $scienceRecovery.Id; throw 'Progress recovery exceeded 30 seconds.' }
    $scienceRecovered = Get-Content -Raw -LiteralPath $scienceBackupPath | ConvertFrom-Json
    if ($scienceRecovery.ExitCode -ne 0 -or -not ($scienceRecovered.restored -and $scienceRecovered.verified)) { throw 'Progress recovery was not verified.' }
}
try {
    if (-not (Test-Path -LiteralPath $Executable)) { throw "QA executable not found: $Executable" }
    if (Test-Path -LiteralPath $scienceBackupPath) {
        $sciencePreviousBackup = Get-Content -Raw -LiteralPath $scienceBackupPath | ConvertFrom-Json
        if (-not ($sciencePreviousBackup.restored -and $sciencePreviousBackup.verified)) { Restore-ScienceProgress }
    }
    if (Test-Path -LiteralPath $scienceReportPath) { Copy-Item -LiteralPath $scienceReportPath -Destination ($scienceReportPath + '.previous.json') -Force }
    $scienceArguments = @('-screen-width','1600','-screen-height','900','-screen-fullscreen','0','--qa-stage4-ux',"--qa-report=$scienceReportPath",'-logFile',(Join-Path $scienceLogRoot ($scienceName + '.log')))
    if ($Visible) { $scienceArguments += '-force-d3d11' } else { $scienceArguments += @('-batchmode','-nographics') }
    $scienceArguments += "--qa-case=$Case"
    if ($Visible) { $sciencePlayer = Start-Process -FilePath $Executable -ArgumentList $scienceArguments -PassThru }
    else { $sciencePlayer = Start-Process -FilePath $Executable -ArgumentList $scienceArguments -WindowStyle Hidden -PassThru }
    $scienceAudit.pid = $sciencePlayer.Id; Save-ScienceAudit
    $scienceWatch = [Diagnostics.Stopwatch]::StartNew()
    while (-not $sciencePlayer.WaitForExit(1000)) {
        if ($scienceWatch.Elapsed.TotalSeconds -gt 170) {
            Stop-Process -Id $sciencePlayer.Id
            Restore-ScienceProgress
            throw 'QA watchdog exceeded; original progress restored.'
        }
    }
    $scienceReport = Get-Content -Raw -LiteralPath $scienceReportPath | ConvertFrom-Json
    $scienceBackup = Get-Content -Raw -LiteralPath $scienceBackupPath | ConvertFrom-Json
    if (-not ($scienceBackup.restored -and $scienceBackup.verified)) { Restore-ScienceProgress; $scienceBackup = Get-Content -Raw -LiteralPath $scienceBackupPath | ConvertFrom-Json }
    $scienceAudit.exitCode = $sciencePlayer.ExitCode; $scienceAudit.seconds = $scienceWatch.Elapsed.TotalSeconds
    $scienceAudit.progressRestored = $scienceBackup.restored -and $scienceBackup.verified
    if ($scienceReport.status -ne 'passed' -or $sciencePlayer.ExitCode -ne 0 -or -not $scienceAudit.progressRestored) { throw 'Stage 4 UX validation failed; see detailed report.' }
    $scienceAudit.status = 'passed'
} catch {
    $scienceAudit.status = 'failed'; $scienceAudit.errors += $_.Exception.ToString()
} finally {
    $scienceAudit.finishedUtc = [datetime]::UtcNow.ToString('o'); Save-ScienceAudit
}
$scienceAudit | ConvertTo-Json -Depth 8
if ($scienceAudit.status -ne 'passed') { exit 1 }
