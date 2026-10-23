param([string]$Executable='C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Builds/Stage3UxQa/ScienceProject.exe')
$ErrorActionPreference='Stop'
$tilemapTaskRoot=Split-Path $PSScriptRoot -Parent
$tilemapLogRoot=Join-Path $tilemapTaskRoot 'Logs/Stage3Guidance'
New-Item -ItemType Directory -Path $tilemapLogRoot -Force | Out-Null
$tilemapSuitePath=Join-Path $tilemapLogRoot 'RouteSuite.json'
$tilemapSuite=[ordered]@{status='running';startedUtc=[datetime]::UtcNow.ToString('o');executable=$Executable;current='';pid=0;completed=@();errors=@()}
function Save-TilemapSuite { $tilemapSuite | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tilemapSuitePath -Encoding utf8 }
function Restore-TilemapProgress([string]$backupPath,[string]$caseName) {
    if(-not(Test-Path -LiteralPath $backupPath)){throw "$caseName has no progress backup for recovery"}
    $tilemapRecovery=Start-Process -FilePath $Executable -ArgumentList @('-batchmode','-nographics','--qa-restore-progress',"--qa-progress-backup=$backupPath",'-logFile',(Join-Path $tilemapLogRoot "${caseName}_ProgressRecovery.log")) -WindowStyle Hidden -PassThru
    if(-not $tilemapRecovery.WaitForExit(30000)){
        Stop-Process -Id $tilemapRecovery.Id
        throw "$caseName progress recovery timed out"
    }
    $tilemapRecovered=Get-Content -Raw -LiteralPath $backupPath | ConvertFrom-Json
    if($tilemapRecovery.ExitCode -ne 0 -or -not($tilemapRecovered.restored -and $tilemapRecovered.verified)){
        throw "$caseName progress recovery was not verified"
    }
}
function Run-TilemapCase([string]$name,[string[]]$flags,[int]$timeout=180) {
    $tilemapReportPath=Join-Path $tilemapLogRoot "$name.json"
    if(Test-Path -LiteralPath $tilemapReportPath){Copy-Item -LiteralPath $tilemapReportPath -Destination (Join-Path $tilemapLogRoot "${name}_Previous.json") -Force}
    $tilemapArguments=@('-batchmode','-nographics','-screen-width','1600','-screen-height','900','-screen-fullscreen','0')+$flags+@("--qa-report=$tilemapReportPath",'-logFile',(Join-Path $tilemapLogRoot "$name.log"))
    $tilemapPlayer=Start-Process -FilePath $Executable -ArgumentList $tilemapArguments -WindowStyle Hidden -PassThru
    $tilemapSuite.current=$name;$tilemapSuite.pid=$tilemapPlayer.Id;Save-TilemapSuite
    $tilemapWatch=[Diagnostics.Stopwatch]::StartNew()
    while(-not $tilemapPlayer.WaitForExit(1000)){
        if($tilemapWatch.Elapsed.TotalSeconds -gt $timeout){
            Stop-Process -Id $tilemapPlayer.Id
            Restore-TilemapProgress ($tilemapReportPath+'.ProgressBackup.json') $name
            throw "$name exceeded its watchdog; original progress was restored and verified"
        }
    }
    $tilemapReport=Get-Content -Raw -LiteralPath $tilemapReportPath | ConvertFrom-Json
    $tilemapBackupPath=$tilemapReportPath+'.ProgressBackup.json'
    $tilemapProgress=Get-Content -Raw -LiteralPath $tilemapBackupPath | ConvertFrom-Json
    if(-not($tilemapProgress.restored -and $tilemapProgress.verified)){
        Restore-TilemapProgress $tilemapBackupPath $name
        $tilemapProgress=Get-Content -Raw -LiteralPath $tilemapBackupPath | ConvertFrom-Json
    }
    $tilemapSuite.completed += [pscustomobject]@{name=$name;status=$tilemapReport.status;exitCode=$tilemapPlayer.ExitCode;seconds=$tilemapWatch.Elapsed.TotalSeconds;progressRestored=($tilemapProgress.restored -and $tilemapProgress.verified);report=$tilemapReportPath}
    $tilemapSuite.pid=0;Save-TilemapSuite
    if($tilemapReport.status -ne 'passed' -or $tilemapPlayer.ExitCode -ne 0 -or -not($tilemapProgress.restored -and $tilemapProgress.verified)){throw "$name failed; see report"}
}
try {
    if(-not(Test-Path -LiteralPath $Executable)){throw "Player executable not found: $Executable"}
    Save-TilemapSuite
    Run-TilemapCase 'TutorialRoute' @('--qa-fixed-charge-tutorial') 180
    Run-TilemapCase 'Stage2ContactRoute' @('--qa-fixed-charge-routes','--qa-route-stage=2') 150
    Run-TilemapCase 'Stage3SourceChargePlan' @('--qa-fixed-charge-routes','--qa-route-stage=3') 150
    Run-TilemapCase 'Stage3BallChargePlan' @('--qa-fixed-charge-routes','--qa-route-stage=3','--qa-plan-alt') 150
    Run-TilemapCase 'Stage4FieldRoute' @('--qa-fixed-charge-routes','--qa-route-stage=4') 180
    Run-TilemapCase 'Stage5FiftyVoltRoute' @('--qa-fixed-charge-routes','--qa-route-stage=5') 180
    $tilemapSuite.status='passed'
} catch {
    $tilemapSuite.status='failed';$tilemapSuite.errors += $_.Exception.ToString()
} finally {
    $tilemapSuite.finishedUtc=[datetime]::UtcNow.ToString('o');Save-TilemapSuite
}
$tilemapSuite | ConvertTo-Json -Depth 8
if($tilemapSuite.status -ne 'passed'){exit 1}
