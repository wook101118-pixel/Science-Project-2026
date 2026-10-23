$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskArchive = Join-Path $taskRoot ('tmp/rolled-back-science-campaign-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$taskBackup = Join-Path $taskRoot 'tmp/before-science-campaign-20261022'
$taskManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'Logs/ScienceCampaign/SourceExport.json') -Raw | ConvertFrom-Json
$taskGame = 'Assets/@Project/@sung_wook/@Game/'
function Assert-WorkspacePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Target outside workspace: $resolved" }
    return $resolved
}
function Ensure-Parent([string]$Path) { New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null }
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
$taskRestore = @{}
1..5 | ForEach-Object { $name = 'Stage_{0:D2}.unity' -f $_; $taskRestore[$taskGame + '00_Scenes/@InGame/Stage/' + $name] = Join-Path $taskBackup ('Stage/' + $name) }
foreach ($name in @('TutorialController2D','ElectricStageHudView','ElectricGameCompleteView')) {
    foreach ($extension in @('.cs','.cs.meta')) { $taskRestore[$taskGame + '01_Scripts/ElectricBounce/' + $name + $extension] = Join-Path $taskBackup ('ElectricBounce/' + $name + $extension) }
}
# All target files still match this task's export. Refuse to overwrite later edits.
foreach ($item in $taskManifest.assets) {
    $target = Assert-WorkspacePath (Join-Path $taskRoot $item.path)
    if ((Hash $target) -ne $item.sha256) { throw "Changed since campaign export: $target" }
    if ($taskRestore.ContainsKey($item.path) -and -not (Test-Path -LiteralPath $taskRestore[$item.path])) { throw "Missing previous file: $($item.path)" }
}
$taskPreserved = @($taskManifest.preservedBgmAndMenu | ForEach-Object { [pscustomobject]@{ path=$_.path; hash=(Hash (Join-Path $taskRoot $_.path)) } })
$taskPlan = $taskGame + 'MapPlanning/BounceMapPlan.txt'
$taskOldPlan = 'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/tmp/BeforeScienceCampaign20261022/' + $taskPlan
$taskPlanContent = Get-Content -LiteralPath (Join-Path $taskRoot $taskPlan) -Raw
$taskMarker = '11. 발표·촬영용 실험실 전면 재구성 (2026-10-22)'
$taskMarkerIndex = $taskPlanContent.IndexOf($taskMarker)
if ($taskMarkerIndex -lt 0 -or $taskPlanContent.Substring(0,$taskMarkerIndex).TrimEnd() -ne (Get-Content -LiteralPath $taskOldPlan -Raw).TrimEnd()) { throw 'Map plan contains unrelated changes; review before restoring.' }
$taskArchivedBuild = Assert-WorkspacePath (Join-Path $taskRoot 'Builds/ScienceProject-Final-2026-10-22')
$taskArchivedBuildDestination = Assert-WorkspacePath (Join-Path $taskArchive 'Builds/ScienceProject-Final-2026-10-22')
$taskRunningBuild = @(Get-Process -Name ScienceProject -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($taskArchivedBuild + '\', [StringComparison]::OrdinalIgnoreCase) })
if ($taskRunningBuild.Count -gt 0) { throw 'The rejected campaign executable is still open; close it before archiving.' }
New-Item -ItemType Directory -Path (Assert-WorkspacePath $taskArchive) | Out-Null
$taskRestored = @(); $taskMoved = @()
foreach ($item in $taskManifest.assets) {
    $target = Assert-WorkspacePath (Join-Path $taskRoot $item.path)
    $archive = Assert-WorkspacePath (Join-Path $taskArchive $item.path)
    Ensure-Parent $archive
    if ($taskRestore.ContainsKey($item.path)) {
        Copy-Item -LiteralPath $target -Destination $archive
        Copy-Item -LiteralPath $taskRestore[$item.path] -Destination $target -Force
        if ((Hash $target) -ne (Hash $taskRestore[$item.path])) { throw "Restore mismatch: $target" }
        $taskRestored += [pscustomobject]@{path=$item.path;sha256=(Hash $target);matchesBeforeCampaign=$true}
    } else {
        Move-Item -LiteralPath $target -Destination $archive
        $taskMoved += $item.path
    }
}
$taskArtFolder = Assert-WorkspacePath (Join-Path $taskRoot ($taskGame + '08_Presentation/ScienceLab'))
if ((Test-Path -LiteralPath $taskArtFolder) -and @(Get-ChildItem -LiteralPath $taskArtFolder -Force).Count -eq 0) { Remove-Item -LiteralPath $taskArtFolder }
$taskPlanArchive = Assert-WorkspacePath (Join-Path $taskArchive $taskPlan)
Ensure-Parent $taskPlanArchive
Copy-Item -LiteralPath (Join-Path $taskRoot $taskPlan) -Destination $taskPlanArchive
Copy-Item -LiteralPath $taskOldPlan -Destination (Join-Path $taskRoot $taskPlan) -Force
foreach ($relative in @('Docs/ScienceCampaign-PresentationGuide-2026-10-22.txt','Docs/SciencePresentationReview-2026-10-22.md')) {
    $source = Assert-WorkspacePath (Join-Path $taskRoot $relative)
    $destination = Assert-WorkspacePath (Join-Path $taskArchive $relative)
    Ensure-Parent $destination
    Move-Item -LiteralPath $source -Destination $destination
}
# Resolve and verify both directory targets before the native recursive move.
if (Test-Path -LiteralPath $taskArchivedBuild) {
    Ensure-Parent $taskArchivedBuildDestination
    Move-Item -LiteralPath $taskArchivedBuild -Destination $taskArchivedBuildDestination
}
foreach ($item in $taskPreserved) { if ((Hash (Join-Path $taskRoot $item.path)) -ne $item.hash) { throw "Unrelated BGM/menu change: $($item.path)" } }
$taskRuntime = Join-Path $taskRoot ($taskGame + '01_Scripts/ElectricBounce')
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $taskBackup 'ElectricBounce') -File) {
    if ((Hash (Join-Path $taskRuntime $file.Name)) -ne (Hash $file.FullName)) { throw "Runtime differs from before-campaign backup: $($file.Name)" }
}
$taskReport = [ordered]@{status='passed';rolledBackUtc=[datetime]::UtcNow.ToString('o');archive=$taskArchive;restored=$taskRestored;newAssetsArchived=$taskMoved;bgmAndMenuPreserved=$taskPreserved;runtimeMatchesBeforeCampaign=$true;mapPlanMatchesBeforeCampaign=((Hash (Join-Path $taskRoot $taskPlan)) -eq (Hash $taskOldPlan));previousRelease=(Join-Path $taskRoot 'Builds/ScienceProject-BGM-2026-10-22/ScienceProject.exe')}
$taskReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRoot 'Logs/ScienceCampaign/Rollback.json') -Encoding utf8
[pscustomobject]@{status='passed';restoredFiles=$taskRestored.Count;archivedNewAssets=$taskMoved.Count;archive=$taskArchive;bgmAndMenuPreserved=$true} | ConvertTo-Json
