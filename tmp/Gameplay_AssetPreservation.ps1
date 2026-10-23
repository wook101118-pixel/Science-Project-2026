$ErrorActionPreference = 'Stop'
$auditWorkspace = (Get-Location).Path
$auditRoot = 'Assets/@Project/@sung_wook/@Game'
$before = @(Get-Content -LiteralPath 'tmp/Gameplay_BeforeHashes.json' -Raw | ConvertFrom-Json)
$beforeByPath = @{}
foreach ($entry in $before) { $beforeByPath[$entry.path] = $entry.hash }
$changes = @(); $missing = @(); $unchanged = @()
foreach ($entry in $before) {
    if (-not (Test-Path -LiteralPath $entry.path -PathType Leaf)) { $missing += $entry.path; continue }
    $currentHash = (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash
    if ($currentHash -ne $entry.hash) { $changes += [pscustomobject]@{ path=$entry.path; before=$entry.hash; after=$currentHash } }
    else { $unchanged += $entry.path }
}
$allCurrent = @(Get-ChildItem -LiteralPath $auditRoot -File -Recurse | ForEach-Object {
    [IO.Path]::GetRelativePath($auditWorkspace, $_.FullName).Replace('\','/')
})
$added = @($allCurrent | Where-Object { -not $beforeByPath.ContainsKey($_) })
$expectedAdded = @(($auditRoot + '/01_Scripts/ElectricBounce/ElectricStageChallenge2D.cs'), ($auditRoot + '/01_Scripts/ElectricBounce/ElectricStageChallenge2D.cs.meta'))
$unexpectedAdded = @($added | Where-Object { $_ -notin $expectedAdded })
$mediaExtensions = @('.aseprite','.png','.jpg','.jpeg','.webp','.ttf','.otf','.wav','.mp3','.ogg','.mat','.shadergraph')
$media = @($before | Where-Object { [IO.Path]::GetExtension($_.path) -in $mediaExtensions })
$metadata = @($before | Where-Object { $_.path.EndsWith('.meta') })
$changedMedia = @($changes | Where-Object { [IO.Path]::GetExtension($_.path) -in $mediaExtensions })
$changedMetadata = @($changes | Where-Object { $_.path.EndsWith('.meta') })

function Get-VisualFields([string]$path) {
    $lines = [IO.File]::ReadAllLines((Join-Path $auditWorkspace $path))
    $fields = [ordered]@{}; $document = ''
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        if ($line -match '^--- !u!\d+ &(.+)$') { $document = $Matches[1] }
        if ($line -notmatch '^(\s+)([A-Za-z_][A-Za-z_0-9]*):') { continue }
        $indent = $Matches[1].Length; $key = $Matches[2]
        if ($key -notmatch '(?i)(sprite|material|color|fontasset)' -and $key -ne 'm_Font') { continue }
        $block = @($line)
        if ($line.TrimEnd().EndsWith(':')) {
            for ($j = $i + 1; $j -lt $lines.Length; $j++) {
                $child = $lines[$j]
                if ($child -match '^---') { break }
                if ($child.Trim().Length -eq 0) { $block += $child; continue }
                $childIndent = $child.Length - $child.TrimStart().Length
                if ($childIndent -lt $indent -or ($childIndent -eq $indent -and -not $child.TrimStart().StartsWith('-'))) { break }
                $block += $child
            }
        }
        $fields[$document + '|' + $key] = $block -join "`n"
    }
    return $fields
}

$uiResults = @()
foreach ($file in @('Learning_Objective_Panel.prefab','StageHUD.prefab')) {
    $original = 'tmp/before-gameplay-20261021/UI/' + $file
    $current = $auditRoot + '/02_Prefabs/UI/' + $file
    $oldFields = Get-VisualFields $original; $newFields = Get-VisualFields $current
    $different = @()
    foreach ($key in $oldFields.Keys) {
        if (-not $newFields.Contains($key) -or $oldFields[$key] -cne $newFields[$key]) { $different += $key }
    }
    foreach ($key in $newFields.Keys) { if (-not $oldFields.Contains($key)) { $different += $key } }
    $uiResults += [pscustomobject]@{ path=$current; comparedVisualFieldBlocks=$oldFields.Count; unchanged=$different.Count -eq 0; differentVisualFields=$different }
}

function Hash-Utf8Text([string]$text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}
$sceneResults = @()
foreach ($stage in @(2,3,5)) {
    $path = $auditRoot + '/00_Scenes/Stage/Stage_0' + $stage + '.unity'
    $raw = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $auditWorkspace $path)))
    $count = [regex]::Matches($raw, '(?m)^  deceleration: 9\r?$').Count
    $restored = $raw.Replace('  deceleration: 9', '  deceleration: 3')
    $reverseHash = Hash-Utf8Text $restored
    $sceneResults += [pscustomobject]@{ path=$path; reversedDecelerationFields=$count; expectedBeforeHash=$beforeByPath[$path]; reversedHash=$reverseHash; exactBaselineAfterMemoryReversal=($count -eq 1 -and $reverseHash -eq $beforeByPath[$path]) }
}

$stage4Path = $auditRoot + '/00_Scenes/Stage/Stage_04.unity'
$reversed4 = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes((Join-Path $auditWorkspace $stage4Path)))
$stage4Changes = @()
foreach ($spec in @(
    @{id='1404593038'; old='  m_LocalPosition: {x: -3, y: -1.95, z: 0}'; new='  m_LocalPosition: {x: 1.55, y: -1.95, z: 0}'},
    @{id='1404593038'; old='  m_LocalScale: {x: 2.2, y: 3.88, z: 1}'; new='  m_LocalScale: {x: 11.3, y: 3.88, z: 1}'},
    @{id='737160925'; old='  m_LocalPosition: {x: -1.65, y: -1.8, z: 0}'; new='  m_LocalPosition: {x: 7.45, y: -1.8, z: 0}'}
)) {
    $doc = [regex]::Match($reversed4, '(?ms)^--- !u!\d+ &' + $spec.id + '\r?\n.*?(?=^--- !u!|\z)').Value
    if (-not $doc.Contains($spec.new)) { throw ('Stage4 expected property not found: ' + $spec.id + ': ' + $spec.new) }
    $reversed4 = $reversed4.Replace($doc, $doc.Replace($spec.new, $spec.old))
    $stage4Changes += [pscustomobject]@{ document=$spec.id; before=$spec.old; after=$spec.new }
}
$instance4 = [regex]::Match($reversed4, '(?ms)^--- !u!1001 &6883389185584763296\r?\n.*?(?=^--- !u!|\z)').Value
$originalInstance4 = $instance4
foreach ($spec in @(
    @{id='4621037390891271464'; property='m_Offset.x'; value='4.55'},
    @{id='4621037390891271464'; property='m_Size.x'; value='11.3'},
    @{id='6206326342329191123'; property='m_LocalPosition.x'; value='4.55'},
    @{id='6206326342329191123'; property='m_LocalScale.x'; value='11.3'},
    @{id='6008083732987843087'; property='m_LocalPosition.x'; value='4.55'},
    @{id='6008083732987843087'; property='m_LocalScale.x'; value='11.3'}
)) {
    $pattern = '(?m)^    - target: \{fileID: ' + $spec.id + ', guid: 94b0db86c839b054a98e61b5a77c5eaf, type: 3\}\r?\n'
    $pattern += '      propertyPath: ' + [regex]::Escape($spec.property) + '\r?\n      value: ' + [regex]::Escape($spec.value) + '\r?\n      objectReference: \{fileID: 0\}\r?\n'
    if ([regex]::Matches($instance4, $pattern).Count -ne 1) { throw ('Stage4 expected one override: ' + $spec.id + ': ' + $spec.property) }
    $instance4 = [regex]::Replace($instance4, $pattern, '')
    $stage4Changes += [pscustomobject]@{ prefabInstance='6883389185584763296'; target=$spec.id; property=$spec.property; addedValue=$spec.value }
}
$reversed4 = $reversed4.Replace($originalInstance4, $instance4)
$deceleration4Count = [regex]::Matches($reversed4, '(?m)^  deceleration: 9\r?$').Count
$reversed4 = $reversed4.Replace('  deceleration: 9', '  deceleration: 3')
$stage4ReverseHash = Hash-Utf8Text $reversed4
$sceneResults += [pscustomobject]@{ path=$stage4Path; reversedDecelerationFields=$deceleration4Count; fieldWidthAndRailChanges=$stage4Changes; expectedBeforeHash=$beforeByPath[$stage4Path]; reversedHash=$stage4ReverseHash; exactBaselineAfterMemoryReversal=($deceleration4Count -eq 1 -and $stage4ReverseHash -eq $beforeByPath[$stage4Path]) }

$physicsDefaults = @()
foreach ($file in @('DistanceLauncher2D.cs','VoltageLauncher2D.cs')) {
    $old = [IO.File]::ReadAllLines((Join-Path $auditWorkspace ('tmp/before-gameplay-20261021/ElectricBounce/' + $file)))
    $new = [IO.File]::ReadAllLines((Join-Path $auditWorkspace ($auditRoot + '/01_Scripts/ElectricBounce/' + $file)))
    $oldParameters = @($old | Where-Object { $_ -match '\[SerializeField' -and $_ -match 'private (float|bool|Polarity) \w+(\s*=[^;]*)?;' })
    $newParameters = @($new | Where-Object { $_ -match '\[SerializeField' -and $_ -match 'private (float|bool|Polarity) \w+(\s*=[^;]*)?;' })
    $physicsDefaults += [pscustomobject]@{ file=$file; parameterCount=$oldParameters.Count; unchanged=(($oldParameters -join "`n") -ceq ($newParameters -join "`n")); parameters=$newParameters }
}
$oldMovement = @([IO.File]::ReadAllLines((Join-Path $auditWorkspace 'tmp/before-gameplay-20261021/Player/PlayerMovement.cs')) | Where-Object { $_ -match '\[SerializeField.*private float' })
$newMovement = @([IO.File]::ReadAllLines((Join-Path $auditWorkspace ($auditRoot + '/01_Scripts/Player/PlayerMovement.cs'))) | Where-Object { $_ -match '\[SerializeField.*private float' })
$movementRestored = ($newMovement -join "`n").Replace('private float deceleration = 9f;', 'private float deceleration = 3f;')
$movementDefaults = [pscustomobject]@{ parameterCount=$oldMovement.Count; unchangedExceptDeclaredDeceleration=(($oldMovement -join "`n") -ceq $movementRestored); allowedChange='deceleration 3 to 9'; before=$oldMovement; after=$newMovement }
$pass = $missing.Count -eq 0 -and $changedMedia.Count -eq 0 -and $changedMetadata.Count -eq 0 -and $unexpectedAdded.Count -eq 0 `
    -and @($uiResults | Where-Object { -not $_.unchanged }).Count -eq 0 `
    -and @($sceneResults | Where-Object { -not $_.exactBaselineAfterMemoryReversal }).Count -eq 0 `
    -and @($physicsDefaults | Where-Object { -not $_.unchanged }).Count -eq 0 -and $movementDefaults.unchangedExceptDeclaredDeceleration

$report = [ordered]@{
    status=$(if ($pass) { 'passed' } else { 'failed' }); createdUtc=[DateTime]::UtcNow.ToString('O'); scope=$auditRoot
    method='Read-only SHA256 comparison to Gameplay_BeforeHashes.json. Source files are never written. Scene reversals are in memory only. Visual YAML blocks are matched by original fileID and property name.'
    baselineFiles=$before.Count; unchangedFiles=$unchanged.Count; changedFiles=$changes; missingFiles=$missing; addedFiles=$added
    newFilesAudit=[ordered]@{ expectedFunctionalFiles=$expectedAdded; unexpected=$unexpectedAdded; noNewArt=($unexpectedAdded.Count -eq 0) }
    existingMediaAndMaterials=[ordered]@{ count=$media.Count; changed=$changedMedia; allUnchanged=($changedMedia.Count -eq 0); byExtension=@($media | Group-Object { [IO.Path]::GetExtension($_.path) } | Select-Object Name,Count) }
    existingMetadata=[ordered]@{ count=$metadata.Count; changed=$changedMetadata; allUnchanged=($changedMetadata.Count -eq 0) }
    uiVisualResourceFields=$uiResults
    allowedTypographyChange='Learning_Objective_Panel help-toggle label keeps its original font asset/material/colors; m_fontSize and m_fontSizeBase change 85 to 30 to display the Korean open/close label. Font resource preservation does not mean unchanged text size.'
    sceneReversalChecks=$sceneResults; launcherSerializedDefaults=$physicsDefaults; movementSerializedDefaults=$movementDefaults
    unchangedPhysicsSources=@('ElectricMath.cs','ContactCharger2D.cs','UniformField2D.cs','ElectricForceSample.cs') | ForEach-Object {
        $p=$auditRoot + '/01_Scripts/ElectricBounce/' + $_
        [pscustomobject]@{path=$p; hashUnchanged=($p -in $unchanged)}
    }
    explicitBehaviorChanges=@('PlayerMovement no-input/opposite-direction braking deceleration: 3 to 9; jump/acceleration/mass/gravity/launch energy are preserved.', 'Stage04 existing field/plate width is expanded for the polarity traversal; force magnitude, voltage, vertical field extent and plate separation are preserved.', 'Functional checkpoint recovery, mandatory Stage04 traversal records, feedback/help UI and optional challenge persistence are intentional changes.')
    auditLimit='Baseline covers the 391 files under the sung_wook game folder, not every unrelated asset in the Unity project. This is an asset/data preservation audit, not a replacement for runtime play verification.'
}
$report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath 'tmp/Gameplay_AssetPreservation.json' -Encoding utf8
[pscustomobject]$report | Select-Object status,baselineFiles,unchangedFiles,@{n='changedCount';e={@($_.changedFiles).Count}},@{n='addedCount';e={@($_.addedFiles).Count}},@{n='missingCount';e={@($_.missingFiles).Count}} | ConvertTo-Json
