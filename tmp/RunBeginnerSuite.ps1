param([int]$BuildProcessId=0,[switch]$Resume)
$ErrorActionPreference='Stop'
$beginnerTaskRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $beginnerTaskRoot
$beginnerSuitePath=Join-Path $beginnerTaskRoot 'Logs/Beginner_SuiteStatus.json'
$beginnerState=[ordered]@{status='running';startedUtc=[datetime]::UtcNow.ToString('o');current='build';completed=@();errors=@()}
if($Resume -and (Test-Path -LiteralPath $beginnerSuitePath)){
    $previousSuite=Get-Content -Raw -LiteralPath $beginnerSuitePath | ConvertFrom-Json
    $beginnerState.completed=@($previousSuite.completed)
    $beginnerState.resumedAfterTestFixtureCorrection=$true
    $beginnerState.originalStartedUtc=$previousSuite.startedUtc
}
function Save-BeginnerState {$beginnerState | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $beginnerSuitePath -Encoding utf8}
function Run-BeginnerCase([string]$name,[string[]]$flags){
    if(@($beginnerState.completed | Where-Object {$_.name -eq $name -and $_.status -eq 'passed'}).Count){return}
    $beginnerState.current=$name;Save-BeginnerState
    & (Join-Path $PSScriptRoot 'InvokeBeginnerPlayer.ps1') -Name $name -Flags $flags
    $caseResult=Get-Content -Raw -LiteralPath (Join-Path $beginnerTaskRoot "Logs/$name.json") | ConvertFrom-Json
    if($caseResult.status -ne 'passed'){throw "$name failed"}
    $caseCount=if($null -ne $caseResult.checks){@($caseResult.checks).Count}else{0}
    $beginnerState.completed += [pscustomobject]@{name=$name;status=$caseResult.status;checks=$caseCount;report="Logs/$name.json"}
    Save-BeginnerState
}
try{
    Save-BeginnerState
    $buildWatch=[Diagnostics.Stopwatch]::StartNew()
    while($BuildProcessId -gt 0 -and (Get-Process -Id $BuildProcessId -ErrorAction SilentlyContinue)){
        if($buildWatch.Elapsed.TotalSeconds -gt 240){throw 'Build watchdog exceeded'}
        Start-Sleep -Seconds 1
    }
    $buildResult=Get-Content -Raw -LiteralPath 'Logs/Beginner_BuildReport.json' | ConvertFrom-Json
    if($buildResult.status -ne 'passed'){throw 'Final release build failed'}
    if($BuildProcessId -gt 0 -and ([datetime]$buildResult.finishedUtc).ToUniversalTime() -lt ([datetime]$beginnerState.startedUtc).ToUniversalTime().AddSeconds(-2)){
        throw 'Release report is stale; the requested build did not complete successfully'
    }
    $cloneRoot='C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild'
    $sourceHashes=@(Get-ChildItem -LiteralPath 'Assets/@Project/@sung_wook/@Game' -Recurse -File | ForEach-Object {
        $relative=$_.FullName.Substring($beginnerTaskRoot.Length+1).Replace('\','/')
        $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        $copyHash=(Get-FileHash -LiteralPath (Join-Path $cloneRoot $relative) -Algorithm SHA256).Hash
        [pscustomobject]@{path=$relative;sha256=$hash;cloneMatches=($hash -eq $copyHash)}
    })
    $sourceHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'Logs/Beginner_SourceBuildHashes.json' -Encoding utf8
    if(@($sourceHashes | Where-Object {-not $_.cloneMatches}).Count){throw 'Source files differ from release build clone'}
    Run-BeginnerCase 'Beginner_Locked' @('--qa-beginner-menus')
    Run-BeginnerCase 'Beginner_Tutorial' @('--qa-tutorial-route')
    Run-BeginnerCase 'Beginner_Routes' @('--qa-beginner-routes')
    Run-BeginnerCase 'Beginner_Alt2' @('--qa-beginner-routes','--qa-route-stage=2','--qa-plan-alt')
    Run-BeginnerCase 'Beginner_Alt3' @('--qa-beginner-routes','--qa-route-stage=3','--qa-plan-alt')
    Run-BeginnerCase 'Beginner_Alt5' @('--qa-beginner-routes','--qa-route-stage=5','--qa-plan-alt')
    Run-BeginnerCase 'Beginner_MenusRead' @('--qa-beginner-menus=read')
    Run-BeginnerCase 'Beginner_Hud' @('--qa-beginner-hud')
    Run-BeginnerCase 'Beginner_Controls' @('--qa-beginner-controls')
    Run-BeginnerCase 'Beginner_UiAudio' @('--qa-beginner-ui-audio',"--qa-backup=$(Join-Path $beginnerTaskRoot 'tmp/Beginner_UiServicesPrefsBackup.json')")
    Run-BeginnerCase 'Beginner_MasteryRead' @('--qa-mastery=read')
    Run-BeginnerCase 'Beginner_Smoke' @('--qa-beginner-smoke')
    $beginnerState.status='passed'
}catch{
    $beginnerState.status='failed';$beginnerState.errors += $_.Exception.ToString();Write-Output $_.Exception.Message
}finally{
    $beginnerState.finishedUtc=[datetime]::UtcNow.ToString('o');Save-BeginnerState
}
if($beginnerState.status -ne 'passed'){exit 1}
