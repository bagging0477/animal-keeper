# 개발 노트

## 마을 맵 (VillageScene) - 격자 배치

VillageScene에 들어갈 때마다 `VillageMapGenerator`(VillageScene의 `MapGenerator` 오브젝트)가 방 프리팹을 골라 맵을 조립한다.
기본 배치는 **격자(Grid)** 다. 같은 Day에 다시 들어오면 같은 시드로 같은 맵이 다시 만들어진다(`GameManager.GetVillageMapSeedForToday`).
자세한 설계 근거와 측정 기록은 `Logs/map_grid_design.md`에 있다(`Logs/`는 git이 무시하므로 이 컴퓨터에만 있다).

### 구조

- **칸**: 격자 `gridColumns x gridRows`(기본 3 x 4)의 칸마다 방 하나를 둔다.
  - 칸 크기는 등록된 방 프리팹 중 가장 큰 바운딩 박스로 자동으로 정해진다(지금 32 x 24, Room_Field). 그래서 더 큰 방을 추가하면 모든 칸이 커진다.
  - 방은 칸 가운데에 놓는다. 칸 (gx, gy)의 시작점은 `(gx * (칸 가로 + 틈), gy * (칸 세로 + 틈))`이다.
  - 방 인스턴스 이름은 `Room_{gx}_{gy}`다. 같은 프리팹이 여러 칸에 나와도 동물/시체 Id(계층 경로)가 겹치지 않게 하려는 것이므로 바꾸면 안 된다.
- **길**: 이웃한 두 칸(상하좌우)을 Z자 길(직선 → 칸 사이 틈에서 한 번 꺾기 → 직선)로 잇는다.
  - 가로 연결은 세로 틈 안에서만, 세로 연결은 가로 틈 안에서만 꺾이고 틈 양쪽에 1칸 이상 여백을 둔다. 그래서 길끼리 만나거나 다른 방 바닥에 닿지 않는다.
  - 그래서 틈은 `길 폭 + 2` 이상이어야 한다. 더 좁게 설정하면 그 맵은 틈을 `폭 + 2`로 넓혀 만들고 경고를 남긴다.
  - 길은 `Corridors` 타일맵에 깔리고 맵 바닥(`mapFloor`)에 들어간다. 그래서 경계 충돌체, 벽 그림자, 바깥 마스크, NavMesh가 방과 같은 함수로 길까지 따라간다.
  - 길 가장자리는 `gridCorridorFadeDepth`칸(기본 1)에 걸쳐 어두워진다. 방 가장자리는 `outsideMaskInnerFadeWidth`(2)다.
- **연결**: 양쪽 변에 포트가 있는 이웃 쌍만 후보다. 섞은 순서의 Kruskal로 모든 방을 잇는 트리를 먼저 만들고, 트리 밖 후보 중 `ceil(남은 수 x extraConnectionRatio)`개를 더해 고리를 만든다.
- **포트(길이 방에 들어가는 자리)**: 방의 한 변에서 `길 폭`줄 연속으로 가장 바깥 바닥 칸이 있고, 줄마다 그 바깥 칸과 안쪽 1칸이 장애물이 아니며, 방 안의 가장 큰 걸을 수 있는 덩어리에 이어진 자리다.
  - 변마다 이런 차선을 찾아, 가장자리가 고른 차선을 먼저, 그다음 변 가운데에 가까운 차선을 고른다.
  - 차선이 하나도 없는 변은 연결 후보에서 빠진다(그 방향으로는 이어지지 않는다).
- **방 선택 규칙** (`GridLayoutPlanner`):
  1. 트럭 칸을 정한다(`truckPlacement`, 기본 Center. 짝수 크기면 가운데 칸 중 하나를 시드로 고른다).
  2. 방 종류를 칸 수만큼 골고루 담는다(종류별 개수 차이 최대 1).
  3. 트럭 칸부터 행 우선으로 칸마다 하나씩 배정한다. 상하좌우 이웃과 같은 종류는 피하고, 트럭 칸에는 `truckRoomPrefabs`(B, C, Field)만 둔다. 막히면 되돌아간다.
  4. 트리가 안 만들어지면 처음부터 다시 한다(`maxLayoutAttempts`, 20회). 그래도 실패하면 에러를 남기고 한 줄 배치로 만든다.
  - 계획기는 `System.Random`만 쓰는 순수 계산이다. 같은 시드면 항상 같은 계획이 나온다.
- **트럭/플레이어**: 트럭 칸 방 안에서 트럭 둘레 2.5칸이 모두 바닥인 자리를 먼저 찾는다. 몬스터는 트럭 방에 두지 않는다(`monsterFreeRoomRadius` 0).
- **개발용 검증**: 생성 직후(`Start`) 바닥 연결, 트럭에서 모든 방 중심까지 NavMesh 경로, Id 중복, 방당 상한을 검사해 문제가 있으면 에러 로그를 남긴다.

### 설정값 위치

| 값 | 위치 | 지금 값 |
|---|---|---|
| 배치 방식 `layoutMode` (Grid / Linear) | VillageScene > MapGenerator (`VillageMapGenerator`) | Grid |
| 격자 크기 `gridColumns` x `gridRows` | 〃 | 3 x 4 |
| 추가 연결 비율 `extraConnectionRatio` | 〃 | 0.35 |
| 트럭 칸 위치 `truckPlacement`, 트럭 방 후보 `truckRoomPrefabs` | 〃 | Center / B, C, Field |
| 몬스터 없는 방 반경 `monsterFreeRoomRadius` | 〃 | 0 (트럭 방만) |
| 계획 재시도 `maxLayoutAttempts`, 길 타일 `corridorFloorTile`(비우면 자동) | 〃 | 20 / 자동 |
| 방 목록 `roomPrefabs` | 〃 | 7종 |
| 방 가장자리 그라데이션 `outsideMaskInnerFadeWidth` | 〃 | 2 |
| 길 폭 `gridCorridorWidth` | `Assets/Settings/GameBalanceConfig.asset` | 6 |
| 칸 사이 틈 `gridCorridorGap` | 〃 | 8 |
| 길 가장자리 그라데이션 `gridCorridorFadeDepth` | 〃 | 1 |
| 동물/몬스터 총량 `gridAnimalTotal` / `gridMonsterTotal` | 〃 | 27 / 12 |
| 방당 상한 `gridMaxAnimalsPerRoom` / `gridMaxMonstersPerRoom` | 〃 | 3 / 2 |

- 한 줄 배치의 방 수(`minRoomCount`/`maxRoomCount`)와 방당 스폰 수(`animalsPerRoom`/`monstersPerRoom`)는 MapGenerator에 그대로 있다. 격자에서는 쓰지 않는다.
- 씬 파일에 `corridorWidth`/`corridorGap` 항목이 남아 있지만 예전 필드라 쓰이지 않는다. 길 폭과 틈은 GameBalanceConfig 값만 쓴다.

### 한 줄 배치(Linear)로 되돌리기

- MapGenerator의 `layoutMode`를 **Linear**로 바꾸면 예전처럼 방 3~4개를 동쪽으로 한 줄로 붙인다. 코드 경로와 Random 호출 순서는 격자 작업 전과 같다.
- 다만 **방 프리팹이 바뀌어서 예전 한 줄 맵과 똑같이 나오지는 않는다.** Room_Forest의 남/북 입구와 Room_Forest·Room_Graveyard의 넓힌 입구(격자 길 폭 6~7용) 때문에 그 방의 장애물 배치가 달라졌다.
  - 같은 시드에서 방 순서와 방 위치는 같다.
  - Forest/Graveyard가 나오는 맵은 그 방의 동물·몬스터 위치, NavMesh 카버 수, 바깥 마스크가 다르다.
  - 한 줄 맵에서는 남/북 입구가 맵 경계에 닿는 막다른 홈으로 보인다(경계 충돌체가 막으므로 나갈 수는 없다).
- 회귀 확인: `Tools > Village Rooms > Run Linear Regression Dump (seeds 1-10)` → `Logs/LinearRegressionDump_branch.txt`를 기준 파일과 diff한다.

### 새 방 추가 절차

1. **프리팹 규칙**
   - 루트에 `Grid` 컴포넌트, 그 아래 `Ground` 타일맵이 있어야 한다. Ground에 타일이 있는 칸만 바닥(걸을 수 있는 곳)이다. Ground가 비어 있으면 생성기가 경고하고 뺀다.
   - 벽은 두지 않는다. 바닥 밖은 생성기가 경계 충돌체와 어두운 마스크로 막는다.
   - 막는 장애물은 트리거가 아닌 2D 충돌체(보통 `Obstacles` 아래 BoxCollider2D)다. 타일맵/복합 충돌체는 장애물로 세지 않는다. 충돌체 없는 장식은 `Decorations`, 길 그림은 `Path` 타일맵에 둔다(Ground가 아니므로 바닥 변형 패치에 덮이지 않는다).
   - Ground 원점은 기존 방처럼 로컬 칸 (-1, -1)에서 시작한다.
   - 장애물을 뺀 걸을 수 있는 바닥은 한 덩어리여야 한다.
   - 바운딩 박스가 32 x 24보다 크면 모든 격자 칸이 그만큼 커져 맵 전체가 넓어진다.
   - 텍스트 레이아웃으로 만들 때는 `Assets/Scripts/Editor/VillageRooms/Layouts/{이름}.txt`를 쓰고 `Tools > Village Rooms > Validate Layouts` → `Build Room Prefabs`를 실행한다. 같은 경로에 덮어써서 GUID가 유지된다.
2. **roomPrefabs 등록**: `Build Room Prefabs`가 VillageScene MapGenerator의 `roomPrefabs`에 자동으로 등록한다(`Register Rooms in VillageScene`으로 다시 할 수 있다). 손으로 만든 프리팹은 Inspector에서 `roomPrefabs`에 끌어 넣는다. 트럭을 둘 수 있는 넓은 방이면 `truckRoomPrefabs`에도 넣는다(트럭 둘레 6.8 x 9.65가 바닥이고 치워지는 장애물이 적은 방).
3. **포트 점검**: `Tools > Village Rooms > Grid > Run Stress Test` → `Logs/MapPreview/grid_stress.txt`의 "방 종류" 줄에서 새 방의 `열린 차선 서/동/남/북`을 확인한다. 0인 변으로는 길이 붙지 않는다. 한 변만 0이면 그 방은 연결이 줄어 막다른 방이 되기 쉽다. 아래 표에 새 방 줄을 추가해 둔다.
4. **확인**: `Grid > Save Preview PNGs`로 그림을 보고, `Run Map Generation Test - Grid 3x4 (30x)`에서 경로·Id·스폰 검사가 통과하는지 본다. `Run Grid Day Cycle Test (4 days)`는 씬 설정 그대로 Day를 넘기며 재진입·납품 기록까지 확인한다. 한 줄 배치도 쓸 거라면 `Run Linear Regression Dump`도 돌린다(아래 NavMesh y 오프셋 문제 참고).

**포트 점검 표** (길 폭 6 기준, 변마다 쓸 수 있는 차선 수. 0 = 그 변으로 연결 안 됨)

| 방 | 서 | 동 | 남 | 북 | 트럭 방 | 메모 |
|---|---|---|---|---|---|---|
| RoomA_Corridor | 2 | 2 | 7 | 15 | - | 높이 7칸이라 동/서 차선이 적다 |
| RoomB_Square | 15 | 15 | 19 | 19 | 가능 | |
| RoomC_LShape | 15 | 15 | 15 | 15 | 가능 | |
| RoomD_Connected | 16 | 16 | 17 | 17 | - | |
| Room_Field | 19 | 19 | 27 | 27 | 가능 | |
| Room_Forest | 14 | 14 | 2 | 2 | - | 남/북 입구는 폭 7까지 열려 있다(폭 7: 1/1) |
| Room_Graveyard | 12 | 12 | 19 | 5 | - | 북쪽 입구는 폭 7까지(폭 7: 4) |

(2026-10-10 `Grid > Run Stress Test` 결과. 폭 7로 바꾸면 모든 변이 1씩 줄고, 0이 되는 변은 아직 없다.)

### 격자 크기와 스폰 밀도

- 동물/몬스터 수는 **격자 크기와 상관없이 맵 전체 총량이 고정**이다(`gridAnimalTotal` 27, `gridMonsterTotal` 12).
  - 동물은 방마다 먼저 한 마리씩 돌아가고, 남는 수는 스폰 가능 칸이 넓은 방에 더 간다(방당 상한까지).
  - 몬스터는 트럭 방을 뺀 방에 같은 규칙으로 나눈다.
- 그래서 **격자를 키우면 밀도가 낮아진다.** 크기를 바꿀 때는 총량도 같이 조정해야 체감 난이도가 유지된다.
- 반대로 줄이면 상한에 걸릴 수 있다. 총량이 `방 수 x 방당 상한`보다 크면 경고를 남기고 상한까지만 둔다(예: 3x3 = 9방 x 3 = 동물 27까지).

| 격자 | 방 수 | 방당 평균 동물 | 몬스터 대상 방당 평균 몬스터 (트럭 방 제외) | 동물 1마리당 바닥 칸 |
|---|---|---|---|---|
| 한 줄 (참고) | 3~4 | 2 (고정) | 1 (고정, 트럭 방 포함) | 약 216 |
| 3x3 | 9 | 3.0 (상한에 꽉 참) | 1.50 | 약 174 |
| **3x4 (기본)** | 12 | 2.25 | 1.09 | 약 235 |
| 4x4 | 16 | 1.69 | 0.80 | 약 314 |
| 4x5 | 20 | 1.35 | 0.63 | 약 392 |

(총량 동물 27 / 몬스터 12 기준. 바닥 칸은 방 + 길, 30회 평균.)

### 격자 크기별 맵 크기와 생성 시간

2026-10-10 측정. 길 폭 6, 틈 8, 크기마다 시드 1~30, 에디터 Play 기준(`Run Map Generation Test - Grid NxM (30x)`). 시간은 30회 평균이고 괄호 안은 최대다.

| 격자 | 맵 외곽 크기(칸) | 바닥 칸(방 + 길) | 길 칸 | 생성 합계 (Awake) | 그중 바깥 마스크 | NavMesh 굽기 | 씬 로드 | NavMesh 카버 | 마스크 정점 |
|---|---|---|---|---|---|---|---|---|---|
| 한 줄 (참고, 0단계) | 약 75~105 x 24 | 1,512 | 0 | 83 ms (122) | 58 ms | 4 ms | 402 ms | 95 | 18,336 |
| 3x3 | 108 x 86 | 4,711 | 977 | 318 ms (495) | 252 ms | 7 ms | 640 ms | 222 | 63,794 |
| **3x4 (기본)** | 109 x 118 | 6,345 | 1,362 | 450 ms (603) | 374 ms | 8 ms | 771 ms | 281 | 85,568 |
| 4x4 | 150 x 118 | 8,486 | 1,862 | 585 ms (853) | 493 ms | 10 ms | 906 ms | 389 | 114,085 |
| 4x5 | 150 x 150 | 10,590 | 2,346 | 698 ms (948) | 598 ms | 13 ms | 1,019 ms | 483 | 142,909 |

- 생성 시간의 약 80~86%가 바깥 마스크다. 한 줄 행의 맵 크기는 설계 단계의 계산값이고, 나머지 값은 0단계 측정값이다. NavMesh 굽기는 4x5에서도 13 ms다.
- Play 중 프레임 시간(플레이어 정지, AI는 동작)은 크기와 상관없이 평균 2.4~2.6 ms였다.
- 원본 보고서: `Logs/VillageMapGenerationTest_grid{3x3,3x4,4x4,4x5}.txt`.

### 알려진 문제

- **한 줄 배치의 NavMesh y 오프셋 (잠재 버그, 고치지 않음)**
  - 위치: `VillageMapGenerator.BuildNavGround`(한 줄 경로만 쓴다).
  - 원인: 바닥 메시를 방 루트의 자식으로 만든다. 방 루트는 2D 좌표 `(x, y, 0)`에 있어서, 방의 2D y 오프셋이 NavMesh 평면(x, z)의 z가 아니라 높이(y)로 들어간다.
  - 영향: 방이 y = 0이 아닌 곳에 붙으면 그 방의 NavMesh가 공중에 뜨고 위치가 어긋난다. 몬스터 스폰이 "no in-room NavMesh" 경고와 함께 실패하거나 몬스터가 움직이지 못한다.
  - 지금 드러나지 않는 이유: 7종 모두 동/서 입구 줄이 같아서 한 줄 배치의 y 오프셋이 거의 항상 0이다.
  - 새 방의 동/서 입구 높이가 다르면 드러날 수 있다. 격자 경로는 월드 칸으로 메시를 만드는 `BuildGridRoomNavGround`를 따로 써서 이 문제가 없다.
- **길 가운데의 옅은 어두움 (고치지 않음)**: 포트 입구 근처 길 칸 몇 개에 바깥 마스크가 최대 알파 0.207로 남는다. 맵 생성 테스트에서 "길 가운데 칸 N칸이 어둡다 (최대 알파 0.207)"로 잡히는 것은 이 항목이다. 원인 후보와 영향은 `Logs/map_grid_design.md`에 적었다.
- **바깥 마스크 생성 시간**: 생성 시간의 대부분(4x5에서 약 600 ms)이 `BuildOutsideMask`다. 격자를 키울 때 씬 로드가 느려지는 주원인이다.
