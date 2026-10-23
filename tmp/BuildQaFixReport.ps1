$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
function Read-Result([string]$name) {
    $path = Join-Path $taskRoot "Logs/$name.json"
    $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($result.status -ne 'passed') { throw "$name did not pass." }
    return $result
}
$build = Read-Result 'QA_Fix_ReleaseBuildReport'
if ($build.developmentBuild -or $build.totalErrors -ne 0) { throw 'A successful nondevelopment Windows build is required.' }
$suiteNames = @('QA_Fix_Controls', 'QA_Fix_DisplaySyncWrite', 'QA_Fix_DisplaySyncRead', 'QA_Fix_DisplayBaselineWrite', 'QA_Fix_DisplayBaselineRead', 'QA_Fix_DisplayBaselineWindowRead', 'QA_Fix_UiAudio')
$expectedCounts = @(63, 34, 9, 58, 16, 14, 33)
$suites = @()
for ($index = 0; $index -lt $suiteNames.Count; $index++) {
    $result = Read-Result $suiteNames[$index]
    $failed = @($result.checks | Where-Object { -not $_.passed })
    if ($failed.Count -or $result.errors.Count -or $result.checks.Count -ne $expectedCounts[$index]) { throw "Unexpected checks in $($suiteNames[$index])." }
    if ([datetime]$result.startedUtc -lt [datetime]$build.finishedUtc) { throw 'A suite predates the final build.' }
    $suites += [pscustomobject]@{ name = $suiteNames[$index]; status = $result.status; checks = $result.checks.Count; report = (Join-Path $taskRoot "Logs/$($suiteNames[$index]).json") }
}
$routes = Read-Result 'QA_Fix_PlayerRoutes'
if ($routes.developmentBuild -or $routes.routes.Count -ne 4 -or -not $routes.finalGameComplete -or $routes.errors.Count) { throw 'Campaign routes did not complete normally.' }
foreach ($route in $routes.routes) {
    if ($route.status -ne 'passed' -or -not $route.physicsSettingsUnchanged -or $route.missingSolidTerrainShapes -or -not $route.realStarEvent -or -not $route.realGoalEvent -or -not $route.learningSatisfied) { throw "Invalid route $($route.stage)." }
}
$stage5 = $routes.routes | Where-Object { $_.stage -eq 5 }
if ([math]::Abs($stage5.launchVoltage - 50) -gt 0.01 -or -not $stage5.realFirstLanding -or -not $stage5.realSecondLanding) { throw 'The 50 V route was not verified.' }
$smoke = Read-Result 'QA_Fix_ReleaseSmoke'
if ($smoke.developmentBuild -or $smoke.campaignErrorCount -or $smoke.legacyErrorCount -or $smoke.diagnosticErrorCount -or $smoke.missingCampaignScriptCount -or $smoke.missingLegacyScriptCount -or -not $smoke.preferencesUnchanged) { throw 'Scene smoke has unresolved failures.' }
if ([datetime]$routes.startedUtc -lt [datetime]$build.finishedUtc -or [datetime]$smoke.startedUtc -lt [datetime]$build.finishedUtc) { throw 'Route or smoke predates the final build.' }
$baseline = Get-Content (Join-Path $taskRoot 'tmp/QA_SourceHashes.json') -Raw | ConvertFrom-Json
$expectedChanges = @('Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/DisplayPreferences.cs', 'Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/DisplaySettingsView.cs', 'Assets/@Project/@sung_wook/@Game/04_InputSystem/Controls.cs', 'Assets/@Project/@sung_wook/@Game/04_InputSystem/Controls.inputactions', 'Assets/@Project/@sung_wook/@Game/09_Tile/Normal_Block_0.asset')
$changed = @()
foreach ($item in $baseline) {
    $path = Join-Path $taskRoot $item.path
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing source $($item.path)." }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.hash) { $changed += $item.path }
}
$unrelatedFontPath = 'Assets/@Project/@sung_wook/@Game/10_font/MyKoreanFont.asset'
$taskChanges = @($changed | Where-Object { $_ -ne $unrelatedFontPath })
if (@(Compare-Object $expectedChanges $taskChanges).Count) { throw 'Unexpected production changes.' }
$unrelatedFontChange = $null
if ($changed -contains $unrelatedFontPath) {
    $sourceFont = Join-Path $taskRoot $unrelatedFontPath
    $cloneFont = Join-Path 'C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild' $unrelatedFontPath
    $sourceFontHash = (Get-FileHash -LiteralPath $sourceFont).Hash
    $cloneFontHash = (Get-FileHash -LiteralPath $cloneFont).Hash
    if ($sourceFontHash -ne $cloneFontHash) { throw 'The independently changed font differs from the tested clone.' }
    $unrelatedFontChange = [pscustomobject]@{
        path = $unrelatedFontPath; before = ($baseline | Where-Object { $_.path -eq $unrelatedFontPath }).hash
        after = $sourceFontHash; cloneMatches = $true
        note = 'A separate font-file change was detected during final auditing. Its writer/cause was not established. Root and agents did not edit this font; it is byte-identical to the tested clone and was preserved rather than overwriting unrelated changes.'
    }
}
$hashes = Get-Content (Join-Path $taskRoot 'Logs/QA_Fix_SourceBuildHashes.json') -Raw | ConvertFrom-Json
foreach ($item in $hashes) {
    if (-not $item.cloneMatches -or (Get-FileHash (Join-Path $taskRoot $item.path)).Hash -ne $item.sha256) { throw 'The source differs from the tested build.' }
}
$artAudit = Get-Content (Join-Path $taskRoot 'Logs/QA_Fix_AssetPreservation.json') -Raw | ConvertFrom-Json
if ($artAudit.changedScenePrefabImageMaterialAudioFiles.Count) { throw 'An existing visual or audio asset changed.' }
$backupNames = @('QA_Fix_RegistryBackup', 'QA_Fix_DisplaySyncPrefsBackup', 'QA_Fix_DisplayBaselinePrefsBackup', 'QA_Fix_UiAudioPrefsBackup')
foreach ($name in $backupNames) {
    $backup = Get-Content (Join-Path $taskRoot "tmp/$name.json") -Raw | ConvertFrom-Json
    if (-not $backup.restored) { throw "Unrestored settings: $name" }
}
if (Test-Path -LiteralPath 'HKCU:/Software/DefaultCompany/Scince') { throw 'The original absent player preference key was not restored.' }
$totalChecks = ($suites | Measure-Object checks -Sum).Sum
$summary = [ordered]@{
    status = 'passed'; date = '2026-10-21'; generatedUtc = [datetime]::UtcNow.ToString('o')
    resolved = @(
        @{ id = 'QA-01'; priority = 'P1'; result = 'resolved'; change = 'Existing Normal_Block tile collider uses Grid instead of missing Sprite outline; sprite, color, placement and scene data unchanged.'; terrainCounts = @(6, 10) },
        @{ id = 'QA-02'; priority = 'P2'; result = 'resolved'; change = 'Unnormalized digital keyboard composite in both action asset and generated wrapper; analog gamepad preserved.'; horizontalVectors = @(1, 1); targetSpeeds = @(7, 7) },
        @{ id = 'QA-03'; priority = 'P2'; result = 'resolved'; change = 'Persistent realtime native display observer distinguishes pending own requests from stable external changes, updates shared UI and durable settings, restores custom sizes, and rebuilds an expanded resolution list safely.' }
    )
    build = $build; executableSha256 = (Get-FileHash -LiteralPath $build.output).Hash
    environment = @{ unity = $smoke.unity; os = $smoke.os; cpu = $smoke.cpu; gpu = $smoke.graphicsDevice; graphicsApi = $smoke.graphicsApi }
    automatedChecksPassed = $totalChecks; automatedChecksFailed = 0; suites = $suites
    campaignRoutes = $routes.routes; finalGameComplete = $routes.finalGameComplete
    sceneSmoke = @{ scenes = $smoke.scenes.Count; campaignErrors = 0; legacyErrors = 0; diagnosticErrors = 0; missingScripts = 0; preferencesUnchanged = $smoke.preferencesUnchanged }
    preservedAssets = $artAudit; sourceBuildHashes = $hashes; preferencesRestored = $true; unrelatedFontChangePreserved = $unrelatedFontChange
    evidence = @('Logs/QA_Fix_PlayerRoutes.json', 'Logs/QA_Fix_ReleaseSmoke.json', 'Logs/QA_Fix_ReleaseBuildReport.json', 'Logs/QA_Fix_AssetPreservation.json', 'Logs/QA_Fix_SourceBuildHashes.json')
    limits = @('Campaign routes use scripted public movement/lesson/lever APIs and actual collision/star/goal callbacks; not a human playthrough.', 'Input regressions use virtual InputSystem keyboard/gamepad devices; external display changes use real Screen APIs and native Win32 window measurements, not physical Alt+Enter.', 'Single Windows PC/Direct3D11, short scene samples; no claim of long-run stability, other hardware, human audio review, or changed inertia tuning.', 'Three existing warnings in legacy apple_wine ElectricCharge code remain; no build errors. Historical probe prose mentioning Development is superseded by developmentBuild=false in actual build and smoke reports.')
}
$summary | ConvertTo-Json -Depth 18 | Set-Content (Join-Path $taskRoot 'Docs/QA_Fixes_2026-10-21.json') -Encoding utf8
$text = @"
전기 바운스 QA 수정 및 Windows 실행 빌드 재검증 | 2026-10-21
판정: 요청한 P1 1건·P2 2건 모두 수정, 최종 비개발 Windows 실행 빌드 재검증 통과

1. 수정 결과
[P1] QA-01 — 3·4번 지형 충돌 형상 누락: 해결
  기존 Normal_Block 타일의 collider type만 Sprite(1)→Grid(2)로 변경했다.
  3번 고체 TilemapCollider 6개와 4번 10개 모두 실제 형상·bounds가 생성된다.
  Sprite outline generation failed 오류 0건. 4번 무입력 관찰 종료 y=$($smoke.scenes | Where-Object { $_.path -like '*Stage_04.unity' } | ForEach-Object { $_.playerY })로 바닥 아래 추락이 사라졌다.
  스프라이트·색상·타일 배치·씬·프리팹은 그대로다.

[P2] QA-02 — 수직 키 동시 입력에 따른 좌우 감속: 해결
  키보드 Player.Move의 Dpad를 mode=1로 변경하고 생성된 Controls.cs도 일치시켰다.
  실제 InputSystem 결과: D x=1, D+W x=1, 좌우 목표 속도 각각 7 / 7.
  WASD·방향키 대각선, 반대 방향 상쇄, 수직 입력, 게임패드 아날로그/데드존 회귀 검사 통과.
  최고속도·가속도·제동·점프·중력·질량과 기존 관성은 변경하지 않았다.

[P2] QA-03 — 외부 화면 변경 후 표시·저장 불일치: 해결
  씬을 넘어서 유지되는 realtime 관찰자가 실제 화면 모드·크기를 공유 설정과 동기화한다.
  자체 SetResolution의 프레임 지연은 pending 요청으로 구분하고 안정된 외부 변경만 저장한다.
  일시정지 중에도 동작하며 custom 창 크기도 목록에 추가하고 다음 실행에서 복원한다.
  펼쳐진 해상도 목록에 새 크기가 삽입되면 기존 팝업을 즉시 정리해 항목/인덱스 불일치를 방지한다.
  실제 Screen 값·Win32 창 geometry·캐시·메인/일시정지 드롭다운·PlayerPrefs 일치 확인.

2. 최종 실행 빌드 검증
  Unity $($smoke.unity), Windows Standalone x64, Direct3D11, developmentBuild=false.
  GPU: $($smoke.graphicsDevice)
  빌드 오류 0, 기존 apple_wine 실험 코드 경고 3개 유지.
  입력·전하·힘·시각·충돌: 63/63 통과.
  외부 화면 변경·pending 요청·popup·새 프로세스 복원: 43/43 통과.
  기존 전체화면/창 모드·해상도·재실행 검사: 88/88 통과.
  메뉴·Esc·일시정지·설정·SFX·비동기 페이드 회귀: 33/33 통과.
  합계: $totalChecks/$totalChecks 체크 통과, 실패 0.
  기본 물리로 2→3→4→5 자동 경로 검증 및 실제 별·출구·학습 조건, 최종 완료 통과.
  5번 실제 발사 $($stage5.launchVoltage)V, 필수 두 발판 실제 윗면 접촉 통과.
  7개 기존 씬 smoke: 캠페인/legacy/진단 오류 0, 누락 스크립트 0.
  실제 새 프로세스에서 1600×900 전체화면, 1280×720 창 모드, 외부 변경한 1440×810 창 모드 복원.

3. 에셋 및 설정 보존
  QA 전 393개 파일의 SHA-256과 비교: 수정 대상 기존 파일 5개 변경, 삭제 0.
  씬·프리팹·이미지·재질·오디오 68개 모두 동일. 타일 파일 차이는 collider type 한 줄뿐이다.
  새 생산 파일은 기능 전용 DisplayPreferencesMonitor.cs와 meta 두 개다.
  변경한 생산 파일 7개의 소스/검증 클론 해시가 모두 일치한다.
  QA helper는 격리 클론에만 포함했고 production Assets에 추가하지 않았다.
  테스트가 사용한 표시/SFX 설정 및 전체 PlayerPrefs 레지스트리를 실행 전 상태로 원복했다.
  별도 변경: MyKoreanFont.asset의 해시 변경 1건도 발견했다. 변경 주체/원인은 확정하지 못했다.
  이번 수정에서 이 폰트를 편집하지 않았으며, 현재 검증 클론과 바이트 단위로 동일하다. 관계없는 변경을 덮어쓰지 않고 보존했다.

4. 검증 범위
  경로 검증은 공개 이동/레버 API와 실제 충돌·수집·출구 callback을 사용한 자동 검사다. 사람의 수동 완주가 아니다.
  입력은 가상 InputSystem 장치로 검증했다. 외부 화면 변경은 실제 Screen API/Win32 창으로 확인했으며 물리적 Alt+Enter 입력은 시험하지 않았다.
  단일 Windows PC의 짧은 씬 관찰이다. 타 하드웨어·장시간 안정성·사람의 청취·초회 플레이 체감까지 보장하지 않는다.
  이전 QA의 관성 관찰은 이번 요청의 3개 결함에 포함하지 않았고 튜닝하지 않았다.
  일부 기존 임시 helper 설명의 Development 문구와 달리 실제 최종 build/smoke의 developmentBuild는 false다.

5. 증거 파일
  Docs/QA_Fixes_2026-10-21.json — 전체 결과 및 원본 검사 연결
  Logs/QA_Fix_ReleaseBuildReport.json
  Logs/QA_Fix_PlayerRoutes.json
  Logs/QA_Fix_Controls.json
  Logs/QA_Fix_DisplaySyncWrite.json / QA_Fix_DisplaySyncRead.json
  Logs/QA_Fix_DisplayBaselineWrite.json / QA_Fix_DisplayBaselineRead.json / QA_Fix_DisplayBaselineWindowRead.json
  Logs/QA_Fix_UiAudio.json
  Logs/QA_Fix_ReleaseSmoke.json
  Logs/QA_Fix_AssetPreservation.json / QA_Fix_SourceBuildHashes.json
  최종 테스트 실행 파일: $($build.output)
  과거 QA_Report_2026-10-21.txt / QA_Summary_2026-10-21.json은 수정 전 이력으로 보존했다.
"@
$text | Set-Content (Join-Path $taskRoot 'Docs/QA_Fixes_2026-10-21.txt') -Encoding utf8
[pscustomobject]@{ status = 'passed'; checks = $totalChecks; routes = $routes.routes.Count; scenes = $smoke.scenes.Count; assetsUnchanged = $artAudit.scenePrefabImageMaterialAudioFilesCompared; report = (Join-Path $taskRoot 'Docs/QA_Fixes_2026-10-21.txt') } | ConvertTo-Json
