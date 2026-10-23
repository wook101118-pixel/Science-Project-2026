전기 바운스 — 기존 sungwook 씬 연결과 학습 기능

실행할 씬
Assets/@Project/@sung_wook/@Game/00_Scenes/sungwook.unity
비교용 연결 씬: 같은 폴더의 sungwook_ElectricBounce.unity

현재 원본 씬 디자인 — Unity에서 직접 편집
원본 sungwook.unity에 금속 캡·암반 이음선·지지대·시설 배경과 한국어 World Space Canvas를 저장했다.
2026-10-19 디자인 정리: 채석장 / 토성 관측창과 낙석 레일 / 청록 전기장 시설 / 구리색 충전탑.
자세한 편집 위치·코드 참고 영상·에셋 라이선스·검증 증거는
@Game/MapPlanning/Presentation_DesignNotes.txt를 참고한다.
Play 전에 이미 존재하는 일반 씬 오브젝트이며, 외형을 생성하는 런타임/에디터 C# 스크립트는 없다.
비교용 sungwook_ElectricBounce 씬과 공용 프리팹은 변경하지 않았다.

2026-10-19 맵 기획·배치
바운스볼 월드 1/7/13/19를 참고해 사이에 5개 테마가 들어가도록 골랐다.
각 월드의 21개 맵을 학습 적합성·동선·재미(5:3:2)로 평가한 3위는
1-19 / 7-3 / 13-5 / 19-11이다. 공식 인기 순위가 아닌 공략 기반 기획 평가이다.
자세한 근거/84개 점수/배치는 @Game/MapPlanning의 BounceMapPlan.txt와
BounceReferenceEvaluation.json에 기록했다.

02 접촉 대전 → 채석장 3단 상승 → 높은 별 → 우측 낙하 → 왼쪽으로 돌아 낮은 출구.
03 기준 저장·조건 변경·비교 발사 → 분리된 섬과 파란 낙석 3개 → 같은 층의 우측 출구.
04 음전하·아래쪽 전기장 → 상층 우측 별 → 오른쪽 하강 → 왼쪽 귀환과 두 번째 하강.
05 50 V 이상 실제 발사 → 높은 탑과 좁은 횡단교 → 깊은 우물의 중간 턱/별 → 바닥 출구.
각 경로의 일반 발판은 Ground 태그의 BoxCollider2D를 사용한다.
이전 공용 타일맵 Platform은 이름에 Previous Shared Course를 표시하고 비활성 보관했다.
기존 두 GearLever 전극 상자는 왼쪽 전시 선반으로 옮겼으며 컴포넌트와 참조는 그대로이다.
실제 마우스 레버, 실험 전극/장 영역, 물리 계수, 학습 조건은 유지했다.
플레이어의 저장 위치를 공용 시작점으로 맞춰 Play 직후에도 접촉 실험에서 시작한다.

Hierarchy에서 Electric Bounce Stages 02-05를 펼쳐 다음 오브젝트를 편집한다.
- Presentation - Shared Hardware (Editable): 공통 제어함, 레버 받침, 표시등.
- Stage 02~05 > Presentation - Stage XX (Editable): 배경, 시설 연결부, 경로 표식.
  Background / Architecture / Route Markings - No Collision은 장식이며 충돌체가 없다.
- Stage 02~05 > Learning UI - Stage XX (Editable World Canvas): 한국어 제목·실험 카드·배지·조작 안내.
  Text / Image / RectTransform으로 글꼴·크기·색·문구·위치를 직접 편집한다.
  ElectricLearningStatusView는 기존 학습 상태를 저장된 문구·배지·효과음에만 연결한다.
- 각 Star > Star Artwork - Editable: 별 모양과 후광. 수집 시 부모와 함께 사라진다.
- 각 Exit > Exit Gate Artwork - Editable: 문틀과 체크무늬 출구.
- 각 전극 > Electrode Housing - Editable: 금속 테두리와 나사. 움직이는 전극을 따라간다.
- Stage 05 > Voltage Gauge - Editable World Canvas: 실험 카드 옆의 세로 전압계와 50 V 목표선.
  기존 VoltageLauncher2D.voltageGauge에 연결되어 충전/발사/재시작을 그대로 반영한다.
- Map Layout - Shared Start: 공용 시작 바닥과 기존 전극 상자 전시 선반.
- Stage 02~05 > Map Layout - Stage XX: 번호순 Route 발판과 실제 장애물.
  Obstacles - Solid Scene Objects에는 실제 충돌하는 벽/천장/낙석이 있다.
- 각 Stage 바로 아래의 Star / Exit / Goal Platform: 별/출구/최종 착지대.
이전 Scene Art의 영어 TextMesh, Map Theme, Structure Details는 비활성 보관했다.
Previous Simple Layout / Previous Shared Course도 이전 비활성 레이아웃이다.
SpriteRenderer의 Sprite/Color, 장식 자식의 Transform으로 외형을 수정한다.
현재 안내문은 World Space Canvas의 Text로 편집한다.
4번 전극의 원래 렌더러는 실제 극성에 맞춰 빨강/파랑으로 바뀌며 장식 테두리는 중성색이다.
씬의 기본 활성 스테이지는 02이다. 다른 스테이지를 편집할 때는 Hierarchy에서 해당 Stage를
활성화하고 나머지를 비활성화해 확인한다. Play 중에는 2~5번 키로 선택한다.
기존 플레이어/레버 참조와 실험 전극 이동점은 유지했다. 맵 동선과 카메라 구도는 새 기획에 맞췄다.

기존 기능 수정
- 2~5번의 Learning UI - Stage XX (Editable World Canvas)를 사용해 현재 전하와 Space 안내를 표시한다.
  Live Charge: 현재 전하 0 중성 / + 양전하 / − 음전하. Text와 RectTransform에서 외형·위치를 편집한다.
  Common Controls: Space 전하 전환 (0 → + → −). Inspector의 Text에서 안내 문구를 편집한다.
  ElectricLearningStatusView의 Charge Format은 {0}=기호, {1}=상태 이름으로 런타임 문구를 편집한다.
  UI 배치·복제·텍스트·글자 크기는 Unity 하이어라키/Inspector에서 직접 편집해 원본 씬에 저장했다.
  표시 코드는 기존 Player.CurrentState를 읽을 뿐 입력·전하·학습 판정·디자인을 생성하거나 변경하지 않는다.
- Space를 누를 때마다 기존 플레이어 상태가 중성(0) → 양전하(+) → 음전하(−) → 중성(0)으로 순환한다.
  PlayerInputReader가 누르는 순간을 읽고 PlayerController가 Player.CycleState에 전달한다.
  전하량·위치·속도는 유지하며 기존 전하별 시각 표시와 StateChanged 알림을 재사용한다.
  Space를 길게 눌러도 반복 전환하지 않는다. 2번 접촉 대전 목표는 실제 전극 접촉만 인정한다.
- 런타임 외형 덮어쓰기와 코드로 생성하던 오른쪽 HUD를 제거했다.
  원본 씬의 배경/발판/전극/레버/별/출구 렌더러를 그대로 사용한다.
  학습 판정과 물리는 유지하며, 없어진 버튼의 실험 조작은 단축키로 제공한다.
- 원본 Stage 02 부모의 localPosition만 (4.99, 1.03, 0)에서 (0, 0, 0)으로 복구했다.
  공 시작점은 (-3, -3.35), 전극 접촉점은 (-3.4, -3.7)이다.
  레버는 고정 전극의 전하만 바꾸며, 중성 공이 전극에 닿으면 실제 충돌 콜백으로 대전된다.
- 기존 PlayerMovement, PlayerController, Rigidbody2D, LeverReader/LeverMover를 재사용한다.
  이동/마우스 입력을 중복 생성하지 않고 기존 직렬화 필드와 에셋 GUID를 유지한다.
- 모든 추가/수정 C# 코드는 @sung_wook 아래에 있고 namespace는 sung_wook이다.
- 관련 클래스와 주요 메서드에 한국어 설명, 물리 모형의 가정과 제한을 주석으로 추가했다.

학습 목표를 포함한 클리어 조건
모든 스테이지는 학습 목표를 완료하고 별 1개를 모은 뒤 출구에 닿아야 한다.
02: 실제 전극 접촉으로 중성 공이 대전되어야 한다. 전하를 직접 바꾸는 것만으로는 인정하지 않는다.
03: 기준을 저장하고, 같은 고정 출발점과 전극 위치에서 공 전하량/전극 전하량 중 하나를
    바꾼 뒤 실제 발사해야 한다. 기준 없는 발사, 같은 조건 발사, 비행 중 거리 변화는 인정하지 않는다.
04: 음전하로 장 안에서 위쪽 힘을 받은 뒤, 출구에서도 음전하와 아래쪽 전기장을 유지해야 한다.
05: 발사 순간 50 V 이상인 전압으로 성공적으로 발사해야 한다. 충전만 하거나 영역 밖 발사 요청,
    50 V 미만 발사는 목표를 완료하지 않는다. 이미 달성한 기록은 전압 소비 후에도 유지된다.
R/스테이지 재선택은 학습 목표를 초기화한다. 03에서 새 기준을 저장하면 비교 완료 기록도 지운다.
L 키로 Unity Console에서 학습 진행과 관측값/결과 설명을 확인한다.

외형과 학습 기능의 분리
외형 조립/도형 생성/런타임 HUD/테마 스크립트 4개와 해당 meta를 제거했다.
씬 오브젝트를 숨기거나 색상/모양을 덮어쓰는 자동 디자인 연결도 제거했다.
디자인은 씬과 프리팹에서 편집한다. 원래 요청한 전기장 E/전기력 F 관측 화살표는 유지한다.
ElectricObstacleMotion2D는 씬에 이미 저장된 3번 파란 돌의 왕복 이동만 담당한다.
오브젝트/외형을 생성하거나 기존 입력·학습·플레이어·레버를 수정하지 않는다.
낙석의 시작/끝 좌표와 이동/대기 시간은 Inspector에서 수정한다.
ElectricLearningInput2D는 실험 명령과 콘솔 관측만 담당하며 화면/폰트/도형을 만들지 않는다.
ElectricLearningStatusView는 저장된 UI와 음향의 상태 연결만 담당하며 외형·배치를 생성하지 않는다.

조작
스테이지는 결과 화면에서 진행하거나 메인 메뉴의 연습에서 선택한다. R: 재시작. E: 전하/전압 발사.
A/D 또는 방향키: 기존 플레이어 이동. 기존 레버 손잡이를 왼쪽으로 기울이면 입력량 증가.
3번: B 기준 저장, T 실험 위치로 복귀, Z/X/C 공 전하량 +0.5/+1/+2, V/N 전극 전하량 +0.5/+1.
4번: Z/X/C 공 전하 +1/-1/0. L: 현재 관측값/학습 진행/결과를 Unity Console에 한 번 출력.
Awake만으로 공을 옮기지 않는다. 스테이지 선택/재시작 시 공용 시작점으로 이동한다.

02 접촉 대전
중성 공을 시작점에 둔 뒤 레버를 왼쪽으로 움직인다.
접촉 전극이 공에 닿으면 양전하가 되고 같은 전하 사이의 반발을 관찰할 수 있다.

03 고정 거리에서 전하량 비교
스테이지를 시작하면 기존 Rigidbody2D의 constraints만 잠시 고정한다.
전극과 발사 준비 위치는 고정된다. B로 기준을 저장한 뒤 레버로 전극 Q=0.5~1을 바꾼다.
다른 풀이에서는 Q=1을 유지하고 공 q=0.5에서 1로 바꾼다. 한 번에 한 전하량만 바꿔 힘을 비교한다.
E는 고정을 해제하고 기존 PlayerMovement를 통해 발사한다.
T는 실험 위치로 돌아가며 기준과 마지막 실제 발사 기록을 유지한다.
R/스테이지 재선택은 기준과 결과, 전하량을 초기화한다.
최소 거리/최대 힘 제한이 작동하면 이론값과 적용 힘의 차이를 설명한다.

04 음전하 퍼즐
양전하 비교 상태에서 시작한다. X 키로 음전하를 선택한다.
전하 선택은 같은 출발점으로 돌아가 별/출구 진행을 초기화한다.
레버를 되돌려 전기장을 아래로 향하게 하면 음전하는 위로 힘을 받는다.
장 안에서 음전하로 위쪽 힘을 받은 기록과 별이 있어야 출구를 통과한다.
출구에서도 음전하와 아래쪽 전기장을 유지해야 한다.
Z(+1), C(0)로 같은 장 안에서 힘의 방향과 0을 비교할 수 있다.

05 전위차
레버를 기울여 충전하고 전극 사이에서 E로 발사한다. 목표는 50 V이다.
발사가 성공할 때만 전압을 소비하며 기존 플레이어에 발사 속도를 전달한다.

표시와 모형
보라 E는 전기장, 주황 F는 공이 받는 전기력이다.
화살표는 방향만 표시하고 크기는 L 키로 출력한 콘솔 수치로 비교한다.
거리 발사 스테이지의 F*는 발사에 사용할 힘의 예측값이다.
ContactChargingStation은 SI 단위의 쿨롱 법칙 F = k qQ / (εr r²)을 사용한다.
기본 축척은 Unity 거리 1 = 1 m, Rigidbody2D 질량 1 = 1 kg, 전하량 1 = 30 μC이다.
전극의 부호와 충전량은 StageLever2D.SignedValue(-1~1)를 따르고 이동량은 절댓값을 따른다.
중앙은 무전하이며, 접촉한 공은 전원이 연결된 전극의 부호와 충전량을 따르는 단순화된 모형이다.
공이 떨어진 뒤에는 레버를 바꿔도 공의 전하가 유지된다.
쿨롱 상수는 2022 CODATA ε0에서 얻은 약 8.9875517862×10^9 N·m²/C²이며 εr 기본값은 1이다.
HUD와 L 콘솔에 m, μC, N(및 콘솔 N/C)을 표시한다. 거리 2배는 힘 1/4, 전하 2배는 힘 2배이다.
influenceRadius = 0은 무제한으로, 먼 거리에서도 역제곱 법칙을 적용한다.
수치 발산을 막는 기본 최소 중심 거리 0.5 m와 최대 힘 60 N은 Inspector에서 변경할 수 있다.
안전 제한이 걸리면 HUD에 표시한다. 두 물체의 중심 전하를 사용하는 점전하 근사이므로,
금속판 표면의 정밀한 전하 분포나 유도/분극, 공기의 절연 파괴는 계산하지 않는다.
거리 발사대/평행판/전압 발사대의 기존 힘/거리/전하량은 게임 단위다.
F = k|qQ|/r²의 힘 배수를 점프 높이의 배수로 해석하지 않는다.
중성은 순전하 0인 점전하 모형이다. 실제 금속의 유도/분극은 이번 범위에 포함하지 않는다.

코드 구조와 책임
ElectricStageSelector: 스테이지 선택/재시작과 기존 컴포넌트 조립.
ElectricLearningController2D: 실험 준비/전하 선택/학습 목표 판정.
ElectricLearningInput2D: 화면을 생성하지 않는 실험 단축키와 콘솔 관측.
ElectricComparisonRecord: 씬과 입력에 의존하지 않는 기준/결과 저장 및 비율 설명.
ElectricVectorDisplay2D: 관측값으로 화살표를 표시하며 힘을 추가하지 않음.
IElectricForceProvider: 접촉 전극/거리 발사대/균일장이 공유하는 읽기 전용 표본 계약.
IElectricStageClearRule: 출구에서 사용하는 추가 학습 조건 계약.
ElectricForceSample / ElectricMath: 물리 관측 데이터와 공통 계산식.
ElectricBall2D / StageLever2D: 기존 플레이어 전하/이동 및 레버 입력에 연결하는 어댑터.
ContactCharger2D: 양방향 레버에 따른 전극 이동/충전, 접촉 대전, SI 인력/척력.
ContactChargeVisual2D: 공급 전하의 부호/크기에 따른 기존 전극 색상과 +/− 표시.
DistanceLauncher2D: 고정 전극과 전하량에 따른 발사. 레버는 위치 대신 Q를 바꾼다. 성공한 발사 표본만 이벤트로 전달.
UniformField2D: 레버 극성에 따른 E와 qE 적용.
VoltageLauncher2D: 충전/목표 알림/전압 소비/발사.
ElectricStar2D / ElectricGoal2D: 별 수집과 출구의 일회성 완료 이벤트.

단일 책임에 따라 입력/실험 흐름/비교 해석/물리를 분리했다.
관측은 구체적 발사대 대신 IElectricForceProvider의 관측 계약을 사용한다.
새 힘 공급자를 추가해도 화살표와 학습 표본 조회에 장치별 분기를 추가할 필요가 없다.
Unity의 기존 직렬화 연결은 유지하고, 출구의 조건 판정은 작은 인터페이스로 위임한다.
기존 이동 담당자는 PlayerMovement 하나이며 외부 전기력은 명시적인 메서드로 전달한다.

열린 씬의 접촉 배치 복구
ElectricStageContactRepair는 원본 경로/루트/앵커/기존 참조와 알려진 오류 위치를 확인한다.
열린 씬에 옛 위치가 남아 있으면 Edit Mode의 초기화가 끝난 뒤 한 번 복구한다.
원래 미저장 편집이 있던 씬은 자동 저장하지 않으며 Undo가 가능하다.
다른 위치로 직접 편집한 레벨은 변경하지 않는다.
수동 메뉴: Tools > sung_wook > Electric Bounce > Repair Stage 02 Contact Layout

설치/학습 연결
이미 연결된 씬은 Play 시 학습 컨트롤러/실험 입력/관측 화살표를 한 개씩만 사용한다.
학습 연결만 저장하려면:
Tools > sung_wook > Electric Bounce > Connect Learning To Current Scene
저장된 연결을 읽기 전용으로 확인하려면:
Tools > sung_wook > Electric Bounce > Validate Existing Scene Connections
이전 전체 재설치 메뉴의 생성·재배치·자동 설치 코드는 제거했다.
이 검사는 씬 오브젝트나 참조를 수정하거나 씬을 저장하지 않는다.

Unity 검색 인덱스 오류 수정
검증 복사본에 UserSettings/Search.index가 없을 때, 검증 도구가 에디터의 시작
delayCall보다 먼저 Play Mode를 요청해 기본 인덱스 생성을 건너뛰는 문제가 있었다.
ElectricBatchPlayMode가 시작 콜백 이후로 Play 진입을 미뤄 Unity가 기본 인덱스를 생성하게 한다.
검색 기능을 끄거나 예외를 무시하지 않으며, 오류는 계속 검증 실패로 기록한다.
Unity 6000.3 참조 코드:
https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/QuickSearch/Editor/SearchInit.cs
https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/QuickSearch/Editor/Indexing/SearchDatabase.cs

검증 도구
일반 에디터에서는 검증 때문에 자동으로 Play Mode에 들어가거나 씬을 교체하지 않는다.
별도 Unity 배치 프로세스의 -executeMethod로 명시적으로 실행한다.
- sung_wook.ElectricBouncePlayModeCheck.RunBatch: 원본 씬의 실제 접촉/발사 검사
- sung_wook.ElectricLearningPlayModeCheck.RunBatch: 비교 씬의 학습 기능 검사
- sung_wook.ElectricLearningPlayModeCheck.RunOriginalBatch: 원본 씬의 같은 학습 기능 검사
- sung_wook.ElectricLearningPlayModeCheck.RunOriginalVisualBatch: 원본 씬 2~5번 Game View 화면 캡처
- sung_wook.ElectricGoalGatePlayModeCheck.RunBatch: 원본 씬의 새 학습 목표/출구 조건 검사
- sung_wook.ElectricMapRoutePlayModeCheck.RunBatch: 기존 좌우 이동/학습 명령으로 새 맵 실제 완주 검사
- sung_wook.ElectricRepairValidation.RunBatch: 복구 범위/미저장 보존과 비교 기록 검사
보고서는 프로젝트 Logs 폴더의 Electric*.json에 저장된다.

이전 접촉 배치/리팩터링 검증 (2026-10-18, Unity 6000.3.19f1)
- 원본 씬 실제 Play Mode: 7/7 통과. 실제 충돌에 의한 접촉 대전 포함.
- 학습 Play Mode: 원본/비교 씬 각각 29/29 통과.
  양전하·음전하·중성 힘, 거리·전하량 비교, 실제 출구 조건,
  반복 연결의 단일 HUD 및 학습 종료 시 공의 물리 제약 복구를 확인했다.
- 접촉 복구/비교 기록: 9/9 통과. 기존 참조, 미저장 편집, 씬 파일 해시 보존 확인.
- 검색 인덱스가 없던 검증 복사본에서 시작해 인덱스 자동 생성과 오류 0건을 확인했다.
- C# 44개 파일의 sung_wook namespace, meta 존재/32자리 GUID/중복 여부 확인.
- 실제 Unity 검증은 분리된 프로젝트 복사본에서 실행하여 원본 씬을 저장하지 않았다.
- Game View 캡처 2장: 한글 패널, 거리 비교 수치, 음전하의 E/F 반대 방향을 확인했다.

이전 학습 목표 강화와 디자인 변경 검증 (Unity 6000.3.19f1, 디자인 제거 전 기록)
- 원본 씬 새 학습 목표 검사: 35/35 통과, Unity 오류 0건.
  실제 접촉 전/후, 기준 없음/동일 조건/비행 중 재발사/조건 변경,
  충전만 함/영역 밖 요청/50 V 미만 발사/50 V 이상 성공 발사를 구분한다.
  학습 목표가 완료되어도 별이 필요하며 재시작 시 기록이 초기화되는지 검사했다.
- 기존 학습 회귀 검사: 원본 씬 29/29 통과, 오류 0건.
- 원본 씬의 2~5번 Game View 4장을 캡처하여 HUD, 전극 극성, 전압 눈금, 별/출구 표시를 확인했다.
- 추가/변경 코드는 @sung_wook 폴더에 있고 namespace sung_wook을 유지한다.
  48개 C# 파일의 namespace/meta/GUID를 확인했다.
- 씬 파일과 기존 물리 배치는 수정하지 않았다. 디자인은 Play Mode에 적용된다.
보고서: Logs/ElectricGoalGatePlayModeReport.json, Logs/ElectricOriginalLearningReport.json,
        Logs/ElectricLearningVisualReport.json

코드 기반 디자인 제거 검증 (Unity 6000.3.19f1)
- 분리된 프로젝트 복사본에서 원본 sungwook 씬으로 검사했다. 원본 씬 파일은 변경하지 않았다.
- 학습/입력/원본 외형 검사 39/39 통과, Unity 오류 0건.
  기준 저장과 전하 선택 단축키, 비행 중 조건 변경 차단, 비활성 컨트롤러 입력 무시,
  기존 학습/화살표, 디자인 오브젝트 부재와 원본 배경/발판 렌더러 표시를 확인했다.
- 2/3/5번 실제 학습 목표/출구 검사 35/35 통과, Unity 오류 0건.
- 남은 C# 45개 파일의 sung_wook namespace와 meta/GUID를 확인했다.
- 기존 이동/레버/발사 연결과 직렬화 필드는 유지했다.
보고서: Logs/ElectricDesignRemovalLearningReport.json, Logs/ElectricDesignRemovalGoalReport.json

원본 씬 직접 배치 디자인 검증 (Unity 6000.3.19f1)
- 원본 씬을 분리된 프로젝트에서 열어 학습/입력/디자인 영속성/전압계 41/41 검사 통과.
- 2/3/5번 실제 접촉/비교 발사/50 V 발사와 출구 조건 35/35 검사 통과.
- 두 실행 모두 Unity 오류 0건. 2~5번의 실제 Game View를 캡처해 확인했다.
- Play 전부터 디자인 그룹과 연결된 native Slider가 존재하며, 55.5 V에서 게이지가 0.555를 표시했다.
- 원본 267개 직렬화 블록을 모두 유지했다. 기존 물리 컴포넌트 25개는 완전히 동일하다.
- 기존 동작 컴포넌트의 유일한 변경은 전압계 참조 연결이다. 별도 디자인 생성 코드는 추가하지 않았다.
- 원래 플레이어/레버/스폰/전극/발사 영역/출구 충돌체의 위치와 크기는 유지했다.
- 새로운 외형은 일반 씬 오브젝트로 저장되어 Inspector에서 편집할 수 있다.
보고서: Logs/ElectricAuthoredSceneLearningReport.json, Logs/ElectricAuthoredSceneGoalReport.json,
        Logs/ElectricAuthoredSceneStructureReport.json, Logs/ElectricAuthoredSceneVisualReport.json

이전 2발판 맵 적용 검증 기록 (2026-10-19, Unity 6000.3.19f1)
- 최신 원본 씬과 동일한 분리 검증 프로젝트에서 학습 41/41, 목표 판정 35/35 통과.
- 기존 수평 이동과 학습 명령만 사용해 실제 별/출구에 도달하는 완주 검사 4/4 통과.
  3번은 강한 발사로 두 번째 발판에 먼저 착지하는 경로도 가능하며, 실제 기록을 보존했다.
  5번은 67.5 V 실제 발사로 완주했다. 낙사 복귀는 기존 R 키를 사용한다.
- 모든 실행의 Unity 오류 0건, 2~5번 화면 4장 확인. 화면 구도는 16:9 기준이다.
- 기존 44개 동작/UI 컴포넌트와 25개 물리/Grid 블록의 직렬화 내용 유지.
  기존 Transform과 활성 상태 일부 변경, Ground 발판 10개를 씬에 저장했다.
- 46개 C#의 sung_wook namespace/meta, 씬의 내부 참조 및 에셋 GUID를 검증했다.
- 런타임 디자인 생성 스크립트를 추가하지 않았다. 새 C#은 Editor 전용 완주 검사다.
보고서: Logs/ElectricMapLearningReport.json, ElectricMapGoalReport.json,
        ElectricMapRouteReport.json, ElectricMapStructureReport.json, ElectricMapVisualReport.json.
화면: Logs/ElectricMapStage02.png ~ ElectricMapStage05.png.

스테이지별 동선·장애물 개편 검증 (2026-10-19, Unity 6000.3.19f1)
- 2번 채석장 상승·하층 귀환, 3번 세 낙석과 분리된 섬,
  4번 상층 우측 이동·두 번의 하강·왼쪽 귀환, 5번 충전탑·횡단교·깊은 우물을 적용했다.
- 기존 학습/입력 검사 41/41, 목표 판정 35/35, 실제 완주 4/4 통과. Unity 오류 0건.
- 3번은 두 번째 섬 왼쪽에서 낙석이 올라갈 때를 기다린 후 다음 바운스로 건넌다.
  검사에서도 2.84초 실제 대기 후 통과했고, 세 돌 모두 저장된 주기대로 이동했다.
- 5번은 별도 50.5 V 발사로도 네 경로 발판·별·출구까지 도달했다.
  실제 충전의 물리 갱신 단위가 0.5 V이며 정확히 50.0 V를 직접 설정한 검사는 아니다.
- 기존 플레이어·레버·학습 컴포넌트와 참조를 유지했다. 모든 C# 47개의 namespace/meta 정상.
- 새 런타임 스크립트 ElectricObstacleMotion2D는 저장된 낙석의 이동만 담당한다.
  디자인 오브젝트는 Play 전부터 원본 씬에 존재한다. 일회성 파일 배치 도구는 제거했다.
- 최종 화면 4장을 확인하고 2번 출구/5번 경로 안내의 겹침을 조정했다.
  5번 교량 위 블록은 교량 아래 회복용 턱으로 옮겼다. 다시 확인한 67.5 V 경로는
  공 전체가 화면 상단 안에서 이동하며, 50.5 V 경로도 함께 다시 검증했다.
  상세 배치, 테스트 해시와 결과는 MapPlanning/BounceMapPlan.txt를 참고한다.
보고서: Logs/ElectricMapReworkLearningReport.json, ElectricMapReworkGoalReport.json,
        ElectricMapReworkRouteReport.json, ElectricMapReworkMinimumVoltageReport.json,
        ElectricMapReworkStructureReport.json, ElectricMapReworkVisualReport.json.
화면: Logs/ElectricMapReworkStage02.png ~ ElectricMapReworkStage05.png.
