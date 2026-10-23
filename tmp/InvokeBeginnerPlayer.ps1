param([Parameter(Mandatory=$true)][string]$Name,[string[]]$Flags=@(),[int]$Timeout=240)
$ErrorActionPreference='Stop'
$qaTaskRoot=Split-Path $PSScriptRoot -Parent
$qaExe='C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Builds/BeginnerRelease/Scince.exe'
if(@(Get-Process -Name Scince -ErrorAction SilentlyContinue).Count){throw 'A standalone player is already running.'}
$qaReport=Join-Path $qaTaskRoot "Logs/$Name.json"
$qaProcessState=Join-Path $qaTaskRoot 'Logs/Beginner_CurrentRun.json'
if(Test-Path -LiteralPath $qaReport){Copy-Item -LiteralPath $qaReport -Destination (Join-Path $qaTaskRoot "Logs/${Name}_Previous.json") -Force}
$qaArguments=@('-force-d3d11','-screen-width','1600','-screen-height','900','-screen-fullscreen','0')+$Flags+@("--qa-report=$qaReport",'-logFile',(Join-Path $qaTaskRoot "Logs/$Name.log"))
# Actual screenshot QA needs the interactive Unity render window to be visible; a hidden swap chain produced black captures.
$qaPlayer=Start-Process -FilePath $qaExe -ArgumentList $qaArguments -WindowStyle Normal -PassThru
[ordered]@{name=$Name;pid=$qaPlayer.Id;status='running';startedUtc=[datetime]::UtcNow.ToString('o');report=$qaReport} | ConvertTo-Json | Set-Content -LiteralPath $qaProcessState -Encoding utf8
Write-Output "$Name started (PID $($qaPlayer.Id))"
$qaClock=[Diagnostics.Stopwatch]::StartNew()
while(-not $qaPlayer.WaitForExit(1000)){
    if($qaClock.Elapsed.TotalSeconds -gt $Timeout){Stop-Process -Id $qaPlayer.Id;throw "$Name exceeded watchdog."}
}
if(-not(Test-Path -LiteralPath $qaReport)){throw "$Name produced no report. See player log."}
$qaResult=Get-Content -Raw -LiteralPath $qaReport | ConvertFrom-Json
$qaCheckCount=if($null -ne $qaResult.checks){@($qaResult.checks).Count}else{0}
$qaRun=[ordered]@{name=$Name;status=$qaResult.status;exitCode=$qaPlayer.ExitCode;seconds=$qaClock.Elapsed.TotalSeconds;checks=$qaCheckCount;report=$qaReport}
$qaRun | ConvertTo-Json | Set-Content -LiteralPath $qaProcessState -Encoding utf8
$qaRun | ConvertTo-Json
if($qaResult.status -ne 'passed' -or $qaPlayer.ExitCode -ne 0){exit 1}
