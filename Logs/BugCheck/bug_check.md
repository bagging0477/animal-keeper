# Animal Keeper 빌드 전 버그 점검 (2026-11-21, main b0d1d83 기준)

코드·에셋은 바꾸지 않았다. 점검용 임시 스크립트(`Assets/Scripts/Editor/TmpBugCheck.cs`, `TmpBatchMenuRunner.cs`)는 점검 뒤 지웠다.
원본 결과는 이 폴더에 있다: `audit.txt`(씬·프리팹), `Flow.txt`(게임 흐름), `Resolutions.txt` + `resolutions/`(해상도 40장),
`MapReach.txt`·`MapWalk.txt`·`NavRef.txt`·`VillageMapGenerationTest_bugcheck100.txt` + `walk/`·`map_captures/`(맵 100회), `LongPlay.txt`·`TexLeak.txt`(오래 플레이), `tests_run1/`·`tests_run2/`(전체 테스트 두 번).

> **정정(2026-11-21 수정 작업 중 확인)**: 아래 #1(동물 13% 못 닿음)은 점검 방식이 만든 오탐이었다. 점검이 같은 날 상태로 다른 시드의 맵을 연달아 만들어,
> 이전 맵에서 저장된 동물 자리(GameManager 동물 자리 기록, 같은 Id)가 새 맵에 복원된 것이다. 실제 게임처럼 마을을 떠난 뒤 날을 넘기면 20맵 모두 0이었다.
> 그래도 생성기가 걸어서 닿는 바닥에만 동물을 두도록(저장된 자리 포함) 막았고, 맵 생성 테스트에 걸어서 닿는지 검사를 넣었다. 결과: `fixed/`.

## 요약

| 심각도 | 개수 |
|---|---|
| 치명 (진행 불가·멈춤) | 0 |
| 중요 (눈에 띄는 오류) | 2 |
| 사소 (경고·작은 어긋남) | 9 |

## 문제 표

| # | 심각도 | 문제 | 재현 방법 | 위치(파일:줄) | 고치는 방법 제안 |
|---|---|---|---|---|---|
| 1 | 중요 | **마을 동물의 약 13%가 걸어서 닿을 수 없는 곳에 생긴다.** 방 바닥 타일 중 맵 경계벽(MapBoundary)으로 잘려 나간 조각(바깥 마스크로 어둡게 가려진 곳)에 놓인다. 100맵 4,100마리 중 542마리(맵당 0~8마리), NavMesh 기준으로도 같은 결과. 보이지도 않고 잡을 수도 없어 실제로 잡을 수 있는 동물 수가 줄어든다(2일차 목표 13마리). | 격자 3x4·시드 1003(2일차)에서 `Room_0_0/AnimalTurtle_2` @ (13.9, 11.6), 시드 1008(1일차) `Room_0_3/AnimalWander_2` @ (3.8, 116.4). 위에서 찍은 그림: `walk/seed1003_12_14.png`, `walk/seed1008_2_114.png`(초록 = 걸어서 닿는 곳, 빨강 = 못 닿는 동물). 목록: `MapWalk.txt` | `Assets/Scripts/Map/VillageMapGenerator.cs:835` (PickGridSpawnCell - 방 FloorCells에서 후보를 고름), `:822` (IsOnMainNavMesh - 기준점은 매번 정상인데도 이 조각을 걸러내지 못함. NavMesh는 방 NavGround로 굽고 경계벽은 2D 충돌체라 서로 어긋나는 것으로 추정), 테스트 빈틈 `Assets/Scripts/Editor/VillageRooms/VillageMapGenerationTest.cs:451` (방 바닥 안인지만 봄) | 스폰 후보를 "플레이어 시작 칸에서 mapFloor(장애물 제외)로 칸 단위 flood fill해 닿는 칸"으로 제한한다(MapBoundary를 만드는 같은 mapFloor 기준). VillageMapGenerationTest에 같은 연결 검사(동물·몬스터가 시작 칸과 이어진 칸인지)를 넣어 다시 생기지 않게 한다. 몬스터도 같은 PickGridSpawnCell을 쓰므로 함께 확인한다. |
| 2 | 중요 | **거너는 코뿔소 돌진 한 번에 체력이 가득 차 있어도 쓰러진다.** 돌진 피해 60 > 거너 최대 체력 50. 2일차(코뿔소 등장)에 거너로 하면 반응할 틈 없이 바로 게임오버. 의도라면 무시해도 되지만 다른 위험(늑대 47, 박쥐 80% 비율)은 한 번에 죽이지 않는다. | 거너로 2일차 마을, 체력을 매 프레임 가득 채운 채 돌아다니면 266초에 "코뿔소 몬스터에게 당함! 데미지: 60%, 남은 체력: 0%" → 게임오버(`LongPlay.log` 2536줄) | `Assets/Settings/GameBalanceConfig.asset:210` (rhinoDashDamage 60), `:111` (gunnerMaxHealthPercent 50), `Assets/Scripts/Monsters/RhinoMonsterAI.cs:153` | 돌진 피해를 최대 체력 비율로 바꾸거나(박쥐처럼, 예: 60%), 한 번 피해 상한을 "최대 체력 - 1"로 둔다. 또는 거너 체력을 61 이상으로. |
| 3 | 사소 | 21:9(2560x1080)에서 도감 판의 위아래 테두리가 화면 밖으로 14px씩 잘린다(내용은 다 보임). 판 높이 1120(기준 단위)이 그 해상도의 화면 높이보다 크다. | 트럭에서 Tab, 2560x1080. `resolutions/06_bestiary_2560x1080.png` | `Assets/Scripts/UI/BestiaryUI.cs:35`, `:183` (PanelSize 고정) | 판 크기를 Canvas 높이에 맞춰 줄이거나(예: min(1120, 화면 높이 - 40)) 판 전체에 축소 배율을 준다. |
| 4 | 사소 | 첫 만남 카드가 떠 있는 동안 게임오버가 되면 카드가 닫히면서 시간을 다시 1로 돌린다(게임오버 화면 뒤에서 몬스터가 계속 움직임). 카드가 멈춘 동안에는 피해가 거의 안 들어와 드물다(같은 프레임 경쟁). | 마을에서 박쥐 첫 만남 카드가 뜬 상태로 `TakeDamage(10000)` → 게임오버, timeScale 1 (`Flow.txt` D 항목). 카드 없이 게임오버면 0 | `Assets/Scripts/UI/MonsterEncounterCard.cs:228`, `:280` (같은 형태: `TutorialGuide.cs:576`, `BestiaryUI.cs:581`) | Close에서 게임오버·승리 중이면 timeScale을 되돌리지 않는다(`if (!gm.IsGameOver && !gm.IsGameWon)`), 또는 savedTimeScale 대신 "내가 0으로 만든 경우에만" 되돌린다. |
| 5 | 사소 | 승리 화면·게임오버 화면에서 타이틀로 바로 갈 방법이 없다. 버튼은 "다시 시작"(→ 새 게임 클래스 선택)뿐이고, 화면이 시간을 멈춰서 ESC 일시정지 메뉴(나가기)도 안 열린다. | 2일차 목표 달성 → 보호소 출구 → 승리 화면에서 ESC: 일시정지 안 열림(`Flow.txt` A 항목) | `Assets/Scripts/UI/GameClearPanel.cs:29`, `Assets/Scripts/UI/GameOverPanel.cs:29`, `Assets/Scripts/UI/PauseMenu.cs:90` | 두 패널에 "타이틀로" 버튼을 더하거나, 패널이 떠 있을 때 ESC로 일시정지 메뉴를 허용한다. |
| 6 | 사소 | 동물 중심이 장애물·경계 충돌체 안에 놓인 경우가 100맵 중 43번(같은 방 같은 자리 반복, 예: Room_0_0 (7.2, 17.1)). 대부분은 옆에서 주울 수 있지만 그림이 장애물과 겹쳐 보인다. 스폰 검사(GridSpawnSpotFits)는 충돌체 겹침을 보는데도 생겨 원인은 확인하지 못했다. | 시드 1001·1006, `MapReach.txt`의 "충돌체 … 안" 줄 | `Assets/Scripts/Map/VillageMapGenerator.cs:639` (스폰 위치), GridSpawnSpotFits(약 800줄) | 스폰 뒤 한 프레임 후 OverlapPoint로 다시 검사해 옮기거나, 원인(검사 시점에 장애물 충돌체가 아직 없거나 크기가 다른지)을 확인한다. |
| 7 | 사소 | 기본 줍기 소리가 합성 "삐" 소리다. 게임 내내 살아남는 AudioManager(타이틀 씬 것)의 pickupClip이 비어 있어 자리표시 소리를 만든다. 몬스터 시체 4종(pickupSound 없음)을 주울 때 들린다. | 늑대 시체를 E로 줍기 | `Assets/Scenes/TitleScene.unity:2314` (pickupClip 비어 있음), `Assets/Scripts/Core/AudioManager.cs:399` , `Assets/Scripts/Monsters/MonsterCorpsePickup.cs:188` | 타이틀·트럭 씬 AudioManager에 실제 줍기 소리를 넣는다. |
| 8 | 사소 | 박쥐 경고음(warningClip)이 비어 있어 박쥐가 달려들 때 경고음이 없다(코드는 null이면 건너뜀). | 마을에서 박쥐 접근 | `Assets/Prefabs/Monsters/BatMonster.prefab` (BatMonsterAI.warningClip), `Assets/Scripts/Monsters/BatMonsterAI.cs:482` | 경고음을 넣거나, 의도면 필드를 지운다. |
| 9 | 사소 | 트럭 씬 인벤토리에 무기 칸이 없다(InventoryUI의 weaponSlotRoot·fill·frame·label이 비어 있음). 트래퍼·거너도 트럭에서는 무기 칸이 안 보인다. 트럭에서 무기를 쓰지 않으니 의도일 수 있다. | 거너로 트럭 진입(`resolutions/02_truck_hud_*.png`) | `Assets/Scenes/TruckScene.unity` Canvas/InventoryBar (InventoryUI), `Assets/Scripts/UI/InventoryUI.cs:222` | 의도면 그대로, 아니면 마을 씬의 무기 칸을 트럭에도 연결. |
| 10 | 사소 | URP 2D 렌더러 설정에 끊긴 참조 3개(probeVolume 디버그 셰이더·메시). 2D 렌더러에서는 쓰지 않아 실행 영향 없음. | `audit.txt` | `Assets/Settings/Renderer2D.asset` (probeVolumeResources) | 렌더러 에셋을 한 번 다시 저장하거나 그대로 둔다. |
| 11 | 사소 | 씬 템플릿 `Lit2DSceneTemplate`의 의존성 참조가 끊겨 있다(에디터 전용, 빌드 영향 없음). | `audit.txt` | `Assets/Settings/Lit2DSceneTemplate.scenetemplate` | 쓰지 않으면 지운다. |

## 점검별 결과

### 1. 전체 테스트 두 번 (batch)
- 28개 × 2회 모두 통과, 두 번의 결과 줄이 같다(`tests_run1/summary.txt`, `tests_run2/summary.txt`). 간헐 실패 없음.
- MonsterTraceTest 기록은 두 번 서로 같고 기준 기록(`Logs/monster_trace_before.txt`)과도 같다. LinearRegressionDump도 두 번 같다.
- VillageMapGenerationTest(30회)는 두 번 모두 검사 실패 0.

### 2. 씬·프리팹
- 빠진 스크립트 0, 빠진 원본 프리팹 0.
- 끊긴 참조 4(위 #10·#11, 모두 설정 에셋).
- 그림 없는 SpriteRenderer 1(VillageScene `NetSwingEffect`) - 실행 중 NetSwingEffect가 그림을 넣으므로 문제 아님.
- 비어 있는 참조 필드 32종은 코드에서 하나씩 확인했다. 대부분은 비어 있으면 다른 값을 쓰도록 처리돼 있다(aimCamera → Camera.main, corridorFloorTile → 방 바닥 타일, mileageText → 배지, 몬스터 추격음 → 공용 추격음, 하루 계획의 pickFromDayTypes 항목 등). 문제로 남긴 것은 #7·#8·#9.

### 3. 게임 흐름 자동 진행 (`Flow.txt`, 콘솔 경고·오류·예외 0건)
- 스카우트·트래퍼·거너 각각: 타이틀 → 석상 E → 시작 E → 트럭 → 마을(실제 E로 줍기) → 트럭 케이지 E → 1일차 7/7(왕복 4번) → 소파 [다음 날로] → 보호소 → 출구 → Day 클리어 → 2일차 13/13(왕복 7번) → 승리 화면 → 일시정지 메뉴 나가기 → 타이틀: 모두 진행됨. 직업은 코드상 3개뿐이다(PlayerClass: Scout·Trapper·Gunner).
- 마을 체력 0 → 게임오버 화면 → 다시 시작 → 새 게임(클래스 선택부터, 날 1·마일리지 0·체력 가득): 정상.
- 목표 미달: [다음 날로] → 확인 창(E = 취소 정상) → [확인] → 게임오버 → 다시 시작: 정상. 게임오버 중 Tab으로 도감이 열리지 않음.
- 겹침: 일시정지 중 Tab(도감 안 열림), 도감 중 ESC(도감만 닫힘), 소파 메뉴 중 Tab(안 열림), 도감 열린 채 씬 이동(도감 닫힘), 튜토리얼 멈춤 중 ESC(일시정지 안 열림), 튜토리얼을 닫으면 첫 만남 카드가 이어서 뜸, 일시정지 중 새 몬스터 → 풀면 카드: 모두 정상. 예외: #4.

### 4. 해상도 (`resolutions/`, 화면 8종 × 5해상도 = 40장)

| 화면 | 1920×1080 | 1600×900 | 1366×768 | 1920×1200 | 2560×1080 |
|---|---|---|---|---|---|
| 타이틀 | 정상 | 정상 | 정상 | 정상 | 정상(설계대로 좌우 여백) |
| 크레딧 | 정상 | 정상 | 정상 | 정상 | 정상 |
| 트럭 HUD·인벤토리 | 정상 | 정상 | 정상 | 정상 | 정상 |
| 마을 HUD·인벤토리·안내 줄 | 정상 | 정상 | 정상 | 정상 | 정상 |
| 튜토리얼 멈춤 카드 | 정상 | 정상 | 정상 | 정상 | 정상 |
| 첫 만남 카드 | 정상 | 정상 | 정상 | 정상 | 정상 |
| 도감 | 정상 | 정상 | 정상 | 정상 | 판 위아래 테두리 잘림(#3) |
| 게임오버 | 정상 | 정상 | 정상 | 정상 | 정상 |

타이틀·크레딧·게임오버의 "화면 밖 그래픽"은 화면을 꽉 채우려고 일부러 넘치게 둔 배경 그림층이라 문제가 아니다.

### 5. 맵 생성 100회
- 공식 검사(VillageMapGenerationTest -mapGenRuns 100): 검사 실패 0/100, 경고·에러 0, 방 연결 경로·스폰 위치·트럭 주변 모두 통과(`VillageMapGenerationTest_bugcheck100.txt`).
- 보충 검사(격자 3x4, 시드 1000~1099, 1·2일차 번갈아): 생성 실패 0, 시작부터 플레이어 시야 안에 있는 몬스터 0, 동물끼리·몬스터끼리 겹침 0, 걸어서 못 닿는 동물 542/4100(#1), 충돌체 안 동물 43건(#6).

### 6. 오래 플레이 (`LongPlay.txt`, `TexLeak.txt`)
- 거너로 마을 1일차 600초 + 2일차 266초(코뿔소에 쓰러짐, #2). 오브젝트 수는 시간 스폰된 몬스터 수만큼만 늘었다(1078 → 1097, 몬스터 9 → 16). 파티클 0개 유지, Canvas 6개, DontDestroyOnLoad 루트 8개로 그대로였다. 관리 메모리 955MB, 전체 할당 676 → 691MB로 거의 평평하다.
- 트럭으로 돌아오면 오브젝트 209개로 정리된다(몬스터·동물 0).
- 늘어난 텍스처는 처음 등장한 몬스터의 애니메이션 그림(코뿔소 charge/dash, 호저 raise/shoot 등)을 처음 불러온 것과, 런타임에 한 번만 만드는 HitFeelPixel·DizzyStar 각 1장이다. 같은 일을 반복해도 다시 늘지 않아 누수로 보지 않는다.
- 시간 스폰 중 플레이어 시야 안에서 생긴 몬스터 0.
