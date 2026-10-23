$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Read-Json([string]$path) { Get-Content -LiteralPath (Join-Path $root $path) -Raw | ConvertFrom-Json }
function Absolute([string]$path) { (Join-Path $root $path).Replace('\','/') }
$route2 = Read-Json 'Logs/QA_Stage2Route.json'
$route34 = Read-Json 'Logs/QA_Stages34Route.json'
$route5 = Read-Json 'Logs/QA_Stage5FiftyVoltRoute.json'
$progress = Read-Json 'Logs/QA_Progression.json'
$controls = Read-Json 'Logs/QA_Controls.json'
$ui = Read-Json 'Logs/QA_UiAudio.json'
$smoke = Read-Json 'Logs/QA_SceneSmoke.json'
$release = Read-Json 'Logs/QA_ReleaseSceneSmoke.json'
$build = Read-Json 'Logs/QA_ReleaseBuildReport.json'
$registry = Read-Json 'tmp/QA_RegistryBackup.json'
$sourceBefore = Read-Json 'tmp/QA_SourceHashes.json'
$changed = @($sourceBefore | Where-Object { $_.hash -ne (Get-FileHash -LiteralPath (Join-Path $root $_.path) -Algorithm SHA256).Hash } | ForEach-Object { $_.path })
if ($changed.Count -gt 0 -or -not $registry.restored) { throw 'Source or preferences have not been restored.' }
if ($route2.status -ne 'passed' -or $route34.status -ne 'passed' -or $route5.status -ne 'passed' -or $progress.status -ne 'passed') { throw 'Unexpected route/progression result.' }
$s3 = $release.scenes | Where-Object { $_.path -like '*Stage_03.unity' }
$s4 = $release.scenes | Where-Object { $_.path -like '*Stage_04.unity' }
function Missing-Terrain($scene) { @($scene.colliders | Where-Object { $_.enabled -and $_.activeInHierarchy -and -not $_.isTrigger -and $_.type -eq 'TilemapCollider2D' -and $_.compositeOperation -eq 'None' -and $_.shapeCount -eq 0 }) }
$missing3 = Missing-Terrain $s3
$missing4 = Missing-Terrain $s4
if ($missing3.Count -ne 6 -or $missing4.Count -ne 10 -or $s4.playerY -ge -20) { throw 'Review release terrain evidence before publishing.' }
$issues = @(
    [ordered]@{
        id='QA-01'; priority='P1'; status='confirmed-in-development-and-release-player'; title='Windows 빌드에서 3·4번 지형 충돌 형상 누락';
        actual="3번 고체 TilemapCollider2D 6개, 4번 10개가 enabled/active/non-trigger/composite=None인데 shapeCount=0, bounds=(0,0,0). Release 4번 무입력 관찰 종료 y=$($s4.playerY). 자연 로드 오류 6+10회, 진단 호출 오류 0회.";
        expected='화면에 보이는 시작 바닥·경로 발판·벽·출구 발판에 실제 충돌 형상이 있어야 한다.';
        reproduction=@('동일 씬을 Windows 실행 빌드로 연다. QA는 공유 비동기 로더를 통해 Stage_03 및 Stage_04에 진입했다.','Stage_04를 로드한 뒤 입력 없이 약 3초 관찰한다. 공이 바닥을 통과하여 화면 밖으로 추락한다.','3번/4번 지형의 실제 Collider shapeCount와 bounds 및 Player.log의 Sprite outline generation failed 오류를 확인한다.');
        impact='정상 발판 경로와 장애물 충돌이 성립하지 않는다. 현재 Windows 빌드는 출시 전 수정이 필요하다. 에디터 경로 통과로 이 결함을 면제할 수 없다.';
        sources=@('Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules/Terrain/Stage03_Terrain.prefab:1256','Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules/Terrain/Stage04_Terrain.prefab:1010','Assets/@Project/@sung_wook/@Game/09_Tile/Normal_Block_0.asset:36','Assets/@Project/@sung_wook/@Game/05_Texture/Normal_Block.aseprite.meta:27','Assets/@Project/@sung_wook/@Game/05_Texture/Normal_Block.aseprite.meta:298');
        rootCauseBoundary='Normal_Block 타일은 Sprite 충돌 타입이고, 원본 텍스처는 읽기 불가·물리 형상 생성 꺼짐이며 실제 sprite physicsShapeCount=0이다. Grid 셀 override도 저장되어 있으므로 정확한 importer/build 재생성 원인은 추가 확인 대상이다. 충돌 누락 현상과 영향은 실측으로 확정했다.';
        recommendation='기존 에셋을 유지해 Grid 충돌을 빌드에 보존하거나 기존 스프라이트의 물리 형상을 생성한다. 수정 후 Windows 빌드에서 3·4번 실제 경로 및 지형 충돌을 재검증한다.';
        evidence=@(Absolute 'Logs/QA_ReleaseSceneSmoke.json'; Absolute 'Logs/QA_SceneSmoke.json'; Absolute 'Logs/QA_UiAudio.json')
    },
    [ordered]@{
        id='QA-02'; priority='P2'; status='confirmed-runtime'; title='수직 키 동시 입력이 좌우 이동 속도를 약 29% 낮춤';
        actual='D 단독 MoveVector.x=1, D+W=0.707107. 수평 목표 속도 7 → 4.949749 world units/s.';
        expected='수직 이동을 사용하지 않는 플레이어는 W/S 추가 입력으로 동일한 좌우 이동 의도가 약해지지 않아야 한다.';
        reproduction=@('오른쪽 D를 누른 상태에서 W 또는 위쪽 키를 추가로 누른다.','좌우 입력은 같지만 정규화된 Dpad 벡터의 x 값이 0.707로 줄어든다.');
        impact='의도하지 않은 수직 키 입력이 점프 도달 거리와 좌우 조작감을 바꾼다.';
        sources=@('Assets/@Project/@sung_wook/@Game/04_InputSystem/Controls.inputactions:34','Assets/@Project/@sung_wook/@Game/01_Scripts/Player/PlayerController.cs:24');
        recommendation='키보드 이동을 1차원 수평 축으로 구성하거나 디지털 입력의 정규화를 조정한다. 게임패드의 아날로그 입력은 별도로 보존한다.';
        evidence=@(Absolute 'Logs/QA_Controls.json')
    },
    [ordered]@{
        id='QA-03'; priority='P2'; status='confirmed-by-direct-equivalent-mode-api'; title='설정창 밖의 화면 모드 변경 후 설정 표시·저장값 불일치';
        actual='실제 Screen.fullScreen=True인데 DisplayPreferences.IsFullscreen=False, 드롭다운은 창 모드(1)를 표시했다.';
        expected='지원하는 외부 화면 모드 변경 이후 실제 모드와 설정 UI·저장값이 일치해야 한다.';
        reproduction=@('설정에서 창 모드를 선택한다.','설정창을 통하지 않고 Screen.fullScreenMode를 전체화면으로 변경한다.','설정창은 기존 창 모드를 계속 표시하며 같은 값 재선택은 즉시 적용되지 않는다.');
        impact='실제 창 모드와 설정 표시가 다르고, 현재 표시값과 같은 모드로 되돌리려면 다른 항목을 거쳐야 한다.';
        sources=@('Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/DisplayPreferences.cs:23','Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/DisplayPreferences.cs:90','Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/DisplaySettingsView.cs:41','ProjectSettings/ProjectSettings.asset:112');
        recommendation='화면 변경 적용 완료와 외부 변경을 구분해 공유 상태·드롭다운·저장값을 실제 모드와 동기화한다.';
        limitation='직접 Screen API 변경으로 재현했다. 실제 OS Alt+Enter 입력은 보내지 않았으므로 해당 단축키 자체의 동작 검증으로 확장하지 않는다.';
        evidence=@(Absolute 'Logs/QA_UiAudio.json')
    }
)
$observations = @([ordered]@{
    id='OBS-01'; priority='P3-tuning-observation'; title='큰 제동 거리와 방향 반전 지연';
    measurement='기본 최고속도 7에서 무입력 정지까지 2.34초 / 약 8.10 world units. 반대 방향 입력 후 0 통과 1.42초, -7 도달 2.80초.';
    assessment='짧은 발판에서는 일찍 제동해야 하는 관성 설계다. 의도된 전기 발사 관성 보존과 구분했으며, 결함으로 단정하지 않는다. 사람의 초회 플레이로 제동·공중 제어 튜닝 여부를 평가할 것을 권한다.';
    evidence=Absolute 'Logs/QA_Controls.json'
})
$coverage = @(
    [ordered]@{area='에디터 기본 물리 경로';result='2/3/4/5번 4개 통과';method='A/D에 해당하는 -1/0/+1 이동 명령과 공개 실험/레버 명령. 실제 수집·출구·착지 이벤트. 경로 중 위치·속도·중력·점프 강화 없음. 마우스 수동 완주 아님.';evidence=@('QA_Stage2Route.json','QA_Stages34Route.json','QA_Stage5FiftyVoltRoute.json')},
    [ordered]@{area='학습 조건·진행·최종 재시작';result='52/52 통과';method='별만/학습만/조건 미달 출구 거부, 실제 trigger callback, 2→3→4→5→완료→2 및 입력 잠금. 위치/중력 fixture를 쓰는 조건 시험이며 경로 완주와 구분.';evidence=@('QA_Progression.json')},
    [ordered]@{area='입력·전하·전기력·시각·충돌';result='40/41 통과, QA-02 재현';method='실제 저장 플레이어와 전극을 격리한 fixture. 가상 InputSystem 입력을 실제 Controller/Mover 코드에 전달. 레버 5항목, Space edge/hold, 1m/3m SI 힘, 접촉 유지/분리, Plus/Minus fade, 바닥·벽·천장 검사.';evidence=@('QA_Controls.json')},
    [ordered]@{area='메뉴·설정·SFX·씬 전환';result='32/33 통과, 오류 검사는 QA-01로 실패; 별도 QA-03 재현';method='실제 저장 버튼 이벤트·드롭다운, 6회 설정 재개방, 저해상도, 일시정지 고정, Esc popup 정리, 중복 시작/재시작, 잘못된 경로 거부, 반복 비동기 이동. 음량/mute/AudioSource 상태 확인이며 인간 청취 아님.';evidence=@('QA_UiAudio.json')},
    [ordered]@{area='Windows 지형·씬 안정성';result='Development와 비개발 빌드에서 QA-01 확인';method='입력·위치·속도·힘 변경 없이 씬별 짧은 idle 관찰. 실제 고체 Collider 형상, 메모리 참고값, 로그와 화면 캡처. 비활성 legacy와 Composite 원본 shape=0을 오판에서 제외.';evidence=@('QA_SceneSmoke.json','QA_ReleaseSceneSmoke.json')},
    [ordered]@{area='화면 설정 재실행 유지';result='직전 동일 소스 Windows 3회 실행 / 88개 검증 통과';method='이전 턴의 정상 표시 실행 결과 재사용. 창 모드 1280×720/1600×900/1920×1080, 전체화면 2개 렌더 크기, 두 모드 각각 재실행. 이번 QA의 외부 모드 변경은 별도 QA-03.';evidence=@('DisplaySettingsStandaloneValidationSummary.json')}
)
$perf = @($release.scenes | ForEach-Object { [ordered]@{scene=$_.path;scope=$_.scope;width=$_.width;height=$_.height;sampleSeconds=$_.sampleSeconds;frames=$_.frameCount;averageFrameMs=$_.averageFrameMs;p95FrameMs=$_.p95FrameMs;maxFrameMs=$_.maxFrameMs;missingScripts=$_.missingScriptCount;errors=$_.errorCount;screenshot=$_.screenshot;playerY=$_.playerY} })
$summary = [ordered]@{
    title='전기 바운스 전체 QA 보고';date='2026-10-21';completedUtc=[DateTime]::UtcNow.ToString('o');verdict='release-blocked-by-QA-01';
    scope='현재 메뉴 캠페인 02→03→04→05와 공유 플레이어·레버·메뉴·설정·SFX. Stage_01/apple_wine은 일반 메뉴에 없는 레거시 씬으로 분리 smoke 확인.';
    environment=[ordered]@{unity=$release.unity;os=$release.os;cpu=$release.cpu;gpu=$release.graphicsDevice;api=$release.graphicsApi;releaseBuild=$build.output;targetFrameRate=60;fixedTimestep=.02};
    productionSourceModified=$false;sourceFilesChecked=$sourceBefore.Count;sourceChanges=$changed;originalPlayerPreferencesRestored=$registry.restored;
    issueCounts=[ordered]@{P0=0;P1=1;P2=2};issues=$issues;observations=$observations;coverage=$coverage;releaseSceneSamples=$perf;
    routeSeconds=[ordered]@{stage2=$route2.seconds;stage3=$route34.routes[0].seconds;stage4=$route34.routes[1].seconds;stage5=$route5.routes[0].seconds;stage5Voltage=$route5.routes[0].launchVoltage};
    limitations=@('사람이 직접 처음부터 연속 완주한 난이도·재미 평가는 아님. 실제 OS 단축키/하드웨어 입력 지연과 인간 청취는 미검증.','게임패드만으로 전체 완주, 다른 GPU·운영체제·다중 모니터·주사율, 장시간 안정성/메모리 누수/최악 부하 성능은 미검증.','씬별 성능은 목표 60 FPS/vSync 0에서 3초(레거시 1초) idle 표본. 3·4번 충돌 누락으로 부하 대표성이 낮으므로 수정 후 다시 측정해야 함.','Grid 셀 override가 있는데 빌드에서 스프라이트 형상 생성이 요청되는 정확한 원인은 미확정. 결함 현상·충돌 누락·무입력 추락은 실측 확정.');
    recommendedOrder=@('QA-01 지형 충돌을 먼저 수정하고 Windows에서 3·4번 경로 재검증','QA-02 키보드 수평 입력 정규화 수정','QA-03 실제 화면 모드와 설정 동기화','수동 초회 플레이로 OBS-01 제동 거리와 발판 난이도 평가');
    fixtureNotes=@('기존 progression 검증기는 새 비동기 페이드 완료 전에 timeScale을 검사했다. QA 복제본에 fade busy 대기만 추가하여 52/52 재검증.','첫 Ground 단위 fixture가 전극 Collider와 겹쳤다. fixture 위치를 분리한 뒤 실제 바운스 7 units/s 확인. 두 항목은 게임 결함으로 집계하지 않음.','비활성 Canvas의 옛 2~5 선택 문구와 호출되지 않는 legacy 배열 Setup 경고는 현재 캠페인 결함에서 제외. 레거시 2개 씬 smoke 오류/누락 스크립트 0.');
}
$jsonPath = Join-Path $root 'Docs/QA_Summary_2026-10-21.json'
$summary | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$text = [Collections.Generic.List[string]]::new()
$text.Add('전기 바운스 QA 보고서 | 2026-10-21')
$text.Add('판정: 출시 전 우선 수정 필요 — P1 1건, P2 2건, 조작감 개선 관찰 1건')
$text.Add('게임 코드·씬·에셋·색상은 유지했다. QA 도구는 격리 복제본에서만 실행했으며 로컬 설정도 원복했다.')
$text.Add('')
$text.Add('1. 핵심 결론')
$text.Add('에디터의 02~05 기본 경로는 통과했지만 Windows 실행 빌드의 03/04 지형 충돌이 생성되지 않는다. 현재 빌드의 정상 진행을 승인할 수 없다.')
$text.Add("검증 환경: Unity $($release.unity), $($release.os), $($release.cpu), $($release.graphicsDevice), $($release.graphicsApi). Development와 비개발 Windows 빌드를 모두 확인했다.")
$text.Add('')
$text.Add('2. 확정 결함과 재현')
foreach ($issue in $issues) {
    $text.Add("[$($issue.priority)] $($issue.id) — $($issue.title)")
    $text.Add("  실제: $($issue.actual)"); $text.Add("  기대: $($issue.expected)")
    $step = 0; foreach ($repro in $issue.reproduction) { $step++; $text.Add("  재현 $step. $repro") }
    $text.Add("  영향: $($issue.impact)"); $text.Add("  권장: $($issue.recommendation)")
    if ($issue.rootCauseBoundary) { $text.Add("  원인 확정 범위: $($issue.rootCauseBoundary)") }
    if ($issue.limitation) { $text.Add("  한계: $($issue.limitation)") }
    foreach ($source in $issue.sources) { $text.Add("  소스: $(Absolute $source)") }
    foreach ($evidence in $issue.evidence) { $text.Add("  증거: $evidence") }
    $text.Add('')
}
$text.Add('3. 조작감 관찰 — 결함 수에 포함하지 않음')
$text.Add($observations[0].measurement); $text.Add($observations[0].assessment); $text.Add('')
$text.Add('4. 검증 범위와 결과')
foreach ($row in $coverage) { $text.Add("- $($row.area): $($row.result)"); $text.Add("  방법: $($row.method)"); foreach ($file in $row.evidence) { $text.Add("  증거: $(Absolute ('Logs/' + $file))") } }
$text.Add('')
$text.Add(('에디터 실제 경로 시간(시뮬레이션): 02 {0:N2}s, 03 {1:N2}s, 04 {2:N2}s, 05 {3:N2}s / 발사 {4:N3}V.' -f $route2.seconds,$route34.routes[0].seconds,$route34.routes[1].seconds,$route5.routes[0].seconds,$route5.routes[0].launchVoltage))
$text.Add('5번은 50V±0.01V 범위의 실제 발사와 두 발판 착지·별·출구 콜백을 확인했다. 에디터 경로 시간은 사람의 플레이 난이도 점수가 아니다.')
$text.Add('')
$text.Add('5. 비개발 실행 빌드 성능·안정성 표본')
foreach ($row in $perf) { $name = [IO.Path]::GetFileNameWithoutExtension($row.scene); $text.Add(('{0}: {1}x{2}, {3} frames / {4:N1}s, 평균 {5:N2}ms, p95 {6:N2}ms, 최대 {7:N2}ms, 오류 {8}, 누락 스크립트 {9}.' -f $name,$row.width,$row.height,$row.frames,$row.sampleSeconds,$row.averageFrameMs,$row.p95FrameMs,$row.maxFrameMs,$row.errors,$row.missingScripts)); $text.Add("  실제 화면: $($row.screenshot)") }
$text.Add('짧은 idle 표본이며 충돌이 누락된 03/04의 성능은 정상 플레이 부하를 대표하지 않는다. 장시간 메모리 누수나 다른 하드웨어 성능을 보장하지 않는다.')
$text.Add('640x480 메인·일시정지 설정 화면의 실제 캡처와 활성 UI geometry에서는 항목 잘림·화면 경계 이탈을 발견하지 못했다. 비활성 legacy UI는 검사에서 제외했다.')
$text.Add('')
$text.Add('6. 아직 실시하지 않은 항목')
foreach ($limit in $summary.limitations) { $text.Add('- ' + $limit) }
$text.Add('')
$text.Add('7. 검증 신뢰성 및 보존')
foreach ($note in $summary.fixtureNotes) { $text.Add('- ' + $note) }
$text.Add("- 게임/설정 파일 $($sourceBefore.Count)개의 SHA-256 비교: 변경 0개. 원래 Windows PlayerPrefs 키 및 값 복구 완료.")
$text.Add('- 외형 생성·색상 변경·스테이지 수정·수정 커밋은 수행하지 않았다. 현재 요청은 QA와 보고다.')
$text.Add('')
$text.Add('8. 수정 권장 순서')
$index = 0; foreach ($item in $summary.recommendedOrder) { $index++; $text.Add("$index. $item") }
$text.Add('')
$text.Add('구조화된 전체 결과: ' + $jsonPath.Replace('\','/'))
$textPath = Join-Path $root 'Docs/QA_Report_2026-10-21.txt'
[IO.File]::WriteAllLines($textPath,$text,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Report=$textPath;Summary=$jsonPath;Issues=$issues.Count;SourceChanges=$changed.Count} | ConvertTo-Json
