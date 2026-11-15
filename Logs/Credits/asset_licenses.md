# Animal Keeper 에셋 출처·라이선스 목록

조사일 2026-11-15. "사용"은 빌드 씬(EditorBuildSettings)에서 출발해 씬·프리팹·에셋이 GUID로 참조하거나, 코드가 `Resources.Load`로 읽는 파일입니다.
"안 씀"은 그런 참조가 없는 파일입니다. 지우지 않았고, 상태만 표시했습니다.

## 1. 외부 에셋

| 에셋 | 위치 | 출처 | 라이선스 | 표기 필요 | 상태 |
|---|---|---|---|---|---|
| RV(마을 트럭) | `Assets/Sprites/Truck/RV.png` | Pixel Vehicles by MinZinn — https://minzinn.itch.io/pixelvehicles | CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/) | **필수**(수정 사실도 표기: 색 보정함, 커밋 1fe0093, `Logs/RV/notes.txt`) | 사용(VillageScene TruckPoint) |
| 동물 그림(코알라·고양이·레서판다·거북·거위) | `Assets/Sprites/Animals/koala_sheet.png`, `cat_sheet.png`, `redpanda_sheet.png`, `Turtle Sprite Sheet (1).png`, `Seagull Sprite Sheet.png` | Elthen — https://elthen.itch.io/ | 제작자 조건(이미 확인됨: 표기 필수 아님) | 권장 | 사용(동물 프리팹 5종) |
| 늑대 그림(순찰 몬스터) | `Assets/Sprites/Monster/Wolf Cub Sprite Sheet.png` | Elthen | 위와 같음 | 권장 | 사용(PatrolMonster). XMP: Photoshop CC 2015.5, 2018-03-14 +03:00 |
| 마을 바닥·풀·벽·나무·소품, 클래스 선택 풀 타일 | `Assets/Sprites/Tiles/Outdoor/TX *.png`, 여기서 잘라 낸 `Outdoor/Props/*`, `Assets/Tiles/Outdoor*Sprite.png`, `Assets/Tiles/StoneWall/*`(TX Tileset Stone Ground 조각, 커밋 de9c036), `Assets/Tiles/ClassSelect/*` | Cainos "Pixel Art Top Down - Basic" — https://cainos.itch.io/pixel-art-top-down-basic | 제작자 조건(이미 확인됨: 표기 필수 아님) | 권장 | 사용. 안 씀: `TX Player`, `TX Shadow`, `TX Shadow Plant`, `TX Struct`, `TX Tileset Wall`, `TX Tileset Stone Ground`(시트 자체, 조각은 사용), `Extra/*` 2개, `Props/OutdoorStatueSprite`, `Assets/Tiles/OutdoorWallStoneSprite.png`, StoneWall 13조각 |
| 인벤토리 칸·선택 칸, 트럭·보호소 메뉴 버튼, 상점 아이콘 | `Assets/Sprites/UI/DEMO_Cozy_UI_Pack_doboui/` (ItemSlot1, ItemSlotSelected1, RoundButton2_wood, IconsDefault19_64px) | Cozy UI Pack (DEMO) by dobo_ui — https://dobo-ui.itch.io/cozy-ui | 페이지 표기: "Use / rework the assets with personal and commercial projects", 재배포·재판매·NFT/AI 학습 항목 별도(금지로 보임 — 렌더된 페이지에서 확인 권장). 데모용 별도 조건은 페이지에 없음 | 필수 아님(권장) | 76개 중 4개 사용, 72개 안 씀 |
| 글꼴(게임 글씨) | `Assets/Fonts/NotoSansKR-Bold.otf` (+ `NotoSansKR-OFL.txt`) | Noto Sans KR (Google) | SIL OFL 1.1 | 권장(OFL 원문을 함께 배포) | 사용(UIFont SDF·Fallback) |
| 글꼴(TMP 기본) | `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` (+ OFL 원문) | Liberation Sans — Copyright (c) 2012 Red Hat, Inc. / 2010 Google | SIL OFL 1.1 | 권장(OFL 원문을 함께 배포) | 빌드 포함(TMP Resources의 LiberationSans SDF) |
| 던전 타일·촛불·가시·횃불 | `Assets/Sprites/Tiles/mainlevbuild.png`, `decorative.png`, `candle*`, `spike_*`, `torch_*`, `PSD/*` | Szadi art (`public-license.txt` 동봉) | 퍼블릭 도메인, 상업 사용 가능, 표기 불필요(권장), 원본·수정본 재판매 금지 | 필요 없음 | **안 씀**(20 PNG + 6 PSD) |
| 소리반응형 몬스터(Blood Monster_A) | `Assets/Sprites/Monster/Blood Monster_A_*.png` 6장 | **출처 확인 필요** — 단서 아래 2.1 | 미확인 | 미확인 | 사용(SoundReactiveMonster), 눈 발광 마스크 `Resources/MonsterEmission/SoundReactive/*`도 이 그림에서 만듦 |
| Demon_A 몬스터 | `Assets/Sprites/Monster/Demon_A_*.png` 6장 | **출처 확인 필요**(Blood Monster와 같은 묶음으로 보임) | 미확인 | 미확인 | **안 씀**(커밋 b494ebb에서 늑대로 교체) |
| 트럭·보호소 실내 타일(TopDownHouse) | `Assets/Sprites/Tiles/Truck/TopDownHouse_*.png` 6장, 이를 쓰는 `Assets/Tiles/RV/*`, `Assets/Tiles/Shelter/*` | **출처 확인 필요** — 단서 아래 2.2 | 미확인 | 미확인 | 5장 사용(TruckScene·ShelterScene), `TopDownHouse_FloorsAndWalls_OpenDoors.png` 안 씀 |
| 무기 칸 모서리 UI | `Assets/Sprites/UI/All.png`(1312x304 픽셀 GUI 시트, All_326/327/381/382) | **출처 확인 필요** — 단서 아래 2.3 | 미확인 | 미확인 | 사용(VillageScene PromptCanvas/WeaponSlotFrame 모서리 4개) |
| 예전 트럭 그림 | `Assets/Sprites/Truck/Truck.png`(300x300) | **출처 확인 필요**(커밋 b84712c "real art"로만 기록) | 미확인 | — | **안 씀**(RV로 교체) |
| 효과음·배경음 16개 | `Assets/Audio/*` | **출처 확인 필요** — 단서 아래 2.4 | 미확인 | 미확인 | 16개 모두 사용 |

## 2. 출처를 모르는 에셋의 단서

### 2.1 Blood Monster_A (소리반응형 몬스터)
- 파일: `Blood Monster_A_{Idle,Walk,Attack01,Attack02,Hurt,Death}.png`, 높이 100px·프레임 100x100(가로 400~800px). PNG에는 sRGB 청크만 있고 제작 도구·저작자 정보 없음.
- 같은 폴더에 같은 형식의 `Demon_A_*` 6장이 함께 들어옴(커밋 168c4a6, 2026-09-21, 출처 기록 없음).
- **유력 후보**: "Tiny RPG Character Asset Pack 02" by Zerie — https://zerie.itch.io/tiny-rpg-character-asset-pack-02
  - 무료 데모 파일 이름이 `...v1.01-Free Demon_A&Blood Monster_A.zip`으로, 이 프로젝트에 있는 두 몬스터(Demon_A, Blood Monster_A)와 정확히 같음. 페이지 설명도 "타일 크기 100×100".
  - 페이지 조건: 개인·상업 게임 사용 가능, 수정 가능, 크레딧은 "appreciated but not required", 원본·수정본 재배포·재판매·재업로드 금지, AI 학습·NFT 금지.
  - 확인 방법: 위 데모 zip을 받아 파일 이름·픽셀이 같은지 비교.

### 2.2 TopDownHouse (트럭·보호소 실내)
- 파일: `TopDownHouse_DoorsAndWindows`(288x160), `FloorsAndWalls`(288x144), `FloorsAndWalls_OpenDoors`(128x48), `FurnitureState1`/`FurnitureState2`(208x288), `SmallItems`(128x128). 모두 16px 격자, PNG에는 pHYs 청크만 있음(제작 도구·저작자 없음). 라이선스·readme 파일 없음.
- 커밋 8ce1925(2026-09-29) "Add TopDownHouse tileset sprites for the truck interior"에 출처 기록 없음.
- 웹 검색("TopDownHouse" + 시트 이름)으로는 같은 이름의 팩을 찾지 못함. 원래 받은 곳(itch.io 다운로드 기록, 브라우저 다운로드 폴더의 zip 이름)을 확인하는 것이 가장 빠름.

### 2.3 All.png (무기 칸 모서리)
- 1312x304 픽셀 GUI 시트(버튼·바·별·프레임·원형 로딩 등), 자동 분할 이름 `All_0`~. PNG에는 sRGB 청크만 있음.
- Cozy UI 데모와 같은 커밋(4a1bf27, 2026-10-03, "Adds the Cozy UI pack sprites")에 들어왔지만 Cozy UI 데모 폴더 밖에 있고 그림체·크기가 다름 → 다른 팩으로 보임.

### 2.4 효과음·배경음 16개
- 16개 모두 ID3 태그·ID3v1·제목·저작자 정보 없음. 인코더 표시는 모두 `LAME3.100`뿐(받은 뒤 다시 인코딩한 것으로 보임).
- `animal_cat_pickup.wav.mp3`, `animal_wombat_pickup.wav.mp3`는 이름에 `.wav`가 남아 있고, `animal_turtle_*.wav` 2개는 확장자는 .wav지만 실제 내용은 MP3 → 변환기를 거친 흔적.
- git 추가 기록(출처 기록 없음): 2026-09-13 0a189fe(배경음·효과음 8개), 2026-09-13 160c45c(고양이·웜뱃 줍기), 2026-10-03 5227a6e(레서판다 2개), 2026-10-14 5ba7272·9a0e34c(거북 2개), 2026-10-17 bef83c0(거위 2개).

| 파일 | 쓰는 곳 | 길이 | 샘플레이트·채널 | 비고 |
|---|---|---|---|---|
| `VillageScene.mp3` | 마을·보호소 배경음 | 21.7초 | 48 kHz 스테레오 | |
| `cozy ambient.mp3` | 트럭 배경음 | 30.0초 | 44.1 kHz 스테레오 | |
| `alert stinger.mp3` | 몬스터 추격 경고 | 6.0초 | 44.1 kHz 스테레오 | |
| `air gun shot.mp3` | 마취총 | 1.1초 | 48 kHz 스테레오 | |
| `damage grunt.mp3` | 피격 | 0.4초 | 44.1 kHz 모노 | |
| `whoosh swing.mp3` | 포획망 휘두르기 | 1.2초 | 48 kHz 스테레오 | |
| `success chime.mp3` | 하루 성공 | 1.6초 | 44.1 kHz 스테레오 | |
| `fail buzzer.mp3` | 하루 실패 | 3.5초 | 44.1 kHz 모노 | |
| `animal_cat_pickup.wav.mp3` | 고양이 줍기 | 1.2초 | 48 kHz 스테레오 | 이름에 .wav |
| `animal_wombat_pickup.wav.mp3` | 코알라 줍기 | 1.1초 | 44.1 kHz 스테레오 | 이름에 .wav |
| `animal_redpanda_pickup.mp3` | 레서판다 줍기 | 1.6초 | 24 kHz 스테레오 | 160 kbps |
| `animal_redpanda_barking.mp3` | 레서판다 울음 | 0.9초 | 24 kHz 스테레오 | 160 kbps |
| `animal_turtle_pickup.wav` | 거북 줍기(앞 1초) | 10.0초 | 44.1 kHz 스테레오 | 내용은 MP3 |
| `animal_turtle_hide and emerge.wav` | 거북 숨기·나오기 | 1.0초 | 48 kHz 스테레오 | 내용은 MP3 |
| `animal_goose_fly.mp3` | 거위 날기 | 30.1초 | 48 kHz 모노 | |
| `animal_goose_hit.mp3` | 거위 맞음 | 1.6초 | 24 kHz 스테레오 | 160 kbps |

## 3. 자체 제작

| 에셋 | 위치 | 근거 | 상태 |
|---|---|---|---|
| 박쥐·호저·코뿔소 몬스터 | `Assets/Sprites/Monster/bat-monster-32-unity`, `porcupine-monster-32x32-unity`, `rhino-monster-48x32-unity`, 눈 발광 마스크 `Resources/MonsterEmission/{Bat,Porcupine,Rhino}` | 자체 제작(확인됨) | 사용. 아틀라스 3장, 박쥐 shadow.png·프레임 1장(freeze-01), 호저 프레임 4장(walk-01~04)은 안 씀 |
| 수의사 | `Assets/Sprites/Truck/Clerk/*` | 자체 제작(확인됨) | 사용 |
| 플레이어·포획망 | `Assets/Sprites/Player/*.png`, `Player/NetSwing/*` | 자체 제작(확인됨) | 사용 |
| HUD | `Assets/Sprites/Player/AnimalKeeperHUD/` | 자체 제작(확인됨) | 사용 |
| 튜토리얼 | `Assets/Resources/TutorialHint/*`, `Assets/Sprites/UI/Tutorial/objective_chip.png` | 자체 제작(확인됨) | 사용 |
| 트럭 실내 외벽 | `Assets/Sprites/Truck/RVShell.png` | `Logs/RV/notes.txt`에 "자체 제작" | 사용 |
| 그림자 | `Assets/Sprites/Shadows/*` | 커밋 9610da3에서 생성 | 7개 사용, 3개 안 씀 |
| 타이틀 화면(낮·밤), 타이틀 버튼 | `Assets/Sprites/Title/*` | 자체 제작으로 보임(확인 필요): 다른 자체 제작물과 같은 형식의 한국어 `사용방법.txt` | 낮·밤 세트·버튼·background 사용. `animal-keeper-unity-png`의 title·truck 3장, 밤 preview 안 씀 |
| 지뢰·폭탄 | `Assets/Sprites/Gadget/*` | 자체 제작으로 보임(확인 필요): 같은 형식의 `사용방법.txt`, 원본 그림 `탑다운 원형 불꽃 폭발 스프라이트.png`에 C2PA 기록(ChatGPT gpt-image, OpenAI, 2026-10-05) | 사용. `fire-explosion-32.png` 안 씀 |
| 임시 그림 | `Assets/Sprites/*Square.png`, `Items/*_placeholder.png`, `TruckFloor.png`, `Assets/Tiles/Floor*/WallTileSprite.png` | 프로젝트 초기 임시 도형 | 일부 사용(AnimalSquare, SoundAnimalSquare, TruckFloor, 아이템 3개) |

## 4. 요약
- 외부 에셋 묶음 13개(오디오 16개는 한 묶음으로 셈).
- 출처를 아는 것 7개: MinZinn, Elthen, Cainos, Cozy UI, Noto Sans KR, Liberation Sans, Szadi(안 씀).
- 출처 확인 필요 6개: Blood Monster_A(유력: Zerie), Demon_A(안 씀), TopDownHouse, All.png, Truck.png(안 씀), 효과음·배경음 16개.
- 표기 필수: RV(CC BY 4.0). 나머지 알려진 에셋은 표기 필수 아님(권장).
