param([string]$Executable='C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Builds/FixedChargeRelease/ScienceProject.exe')
$ErrorActionPreference='Stop'
$fixedTaskRoot=Split-Path $PSScriptRoot -Parent
$fixedSuitePath=Join-Path $fixedTaskRoot 'Logs/FixedChargeRouteSuite.json'
$fixedSuite=[ordered]@{status='running';startedUtc=[datetime]::UtcNow.ToString('o');current='';pid=0;completed=@();errors=@()}
function Save-FixedSuite { $fixedSuite | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixedSuitePath -Encoding utf8 }
function Run-FixedCase([string]$name,[string[]]$flags,[int]$timeout=300) {
    $fixedReportPath=Join-Path $fixedTaskRoot "Logs/$name.json"
    if(Test-Path -LiteralPath $fixedReportPath){Copy-Item -LiteralPath $fixedReportPath -Destination (Join-Path $fixedTaskRoot "Logs/${name}_Previous.json") -Force}
    $fixedArguments=@('-batchmode','-nographics','-screen-width','1600','-screen-height','900','-screen-fullscreen','0')+$flags+@("--qa-report=$fixedReportPath",'-logFile',(Join-Path $fixedTaskRoot "Logs/$name.log"))
    $fixedPlayer=Start-Process -FilePath $Executable -ArgumentList $fixedArguments -WindowStyle Hidden -PassThru
    $fixedSuite.current=$name;$fixedSuite.pid=$fixedPlayer.Id;Save-FixedSuite
    $fixedWatch=[Diagnostics.Stopwatch]::StartNew()
    while(-not $fixedPlayer.WaitForExit(1000)){
        if($fixedWatch.Elapsed.TotalSeconds -gt $timeout){
            Stop-Process -Id $fixedPlayer.Id
            $fixedBackupPath=$fixedReportPath+'.ProgressBackup.json'
            if(Test-Path -LiteralPath $fixedBackupPath){
                $fixedRecovery=Start-Process -FilePath $Executable -ArgumentList @('-batchmode','-nographics','--qa-restore-progress',"--qa-progress-backup=$fixedBackupPath",'-logFile',(Join-Path $fixedTaskRoot "Logs/${name}_ProgressRecovery.log")) -WindowStyle Hidden -PassThru
                if(-not $fixedRecovery.WaitForExit(30000)){Stop-Process -Id $fixedRecovery.Id;throw 'QA progress recovery timed out'}
            }
            throw "$name exceeded its watchdog"
        }
    }
    $fixedReport=Get-Content -Raw -LiteralPath $fixedReportPath | ConvertFrom-Json
    $fixedProgress=Get-Content -Raw -LiteralPath ($fixedReportPath+'.ProgressBackup.json') | ConvertFrom-Json
    $fixedSuite.completed += [pscustomobject]@{name=$name;status=$fixedReport.status;exitCode=$fixedPlayer.ExitCode;seconds=$fixedWatch.Elapsed.TotalSeconds;progressRestored=($fixedProgress.restored -and $fixedProgress.verified);report=$fixedReportPath}
    $fixedSuite.pid=0;Save-FixedSuite
    if($fixedReport.status -ne 'passed' -or $fixedPlayer.ExitCode -ne 0 -or -not($fixedProgress.restored -and $fixedProgress.verified)){throw "$name failed; see report"}
}
try {
    Save-FixedSuite
    Run-FixedCase 'FixedCharge_TutorialRoute' @('--qa-fixed-charge-tutorial') 180
    Run-FixedCase 'FixedCharge_Stage2ContactRoute' @('--qa-fixed-charge-routes','--qa-route-stage=2') 150
    Run-FixedCase 'FixedCharge_Stage3SourceChargePlan' @('--qa-fixed-charge-routes','--qa-route-stage=3') 150
    Run-FixedCase 'FixedCharge_Stage3BallChargePlan' @('--qa-fixed-charge-routes','--qa-route-stage=3','--qa-plan-alt') 150
    $fixedSuite.status='passed'
} catch {
    $fixedSuite.status='failed';$fixedSuite.errors += $_.Exception.ToString()
} finally {
    $fixedSuite.finishedUtc=[datetime]::UtcNow.ToString('o');Save-FixedSuite
}
$fixedSuite | ConvertTo-Json -Depth 8
if($fixedSuite.status -ne 'passed'){exit 1}
