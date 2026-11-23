# Animal Keeper 빌드 전 최종 점검 (2026-11-23, main 10ae7d5 기준)

코드 품질과 연속 플레이를 봤다. 앞의 버그 점검(Logs/BugCheck)과 동작 점검(Logs/LogicCheck)에서 다룬 것(21:9 도감 테두리, 카드 뒤 timeScale, 늑대 겹침 등)은 빼고 적었다.
코드·에셋은 바꾸지 않았다. 점검용 임시 에디터 스크립트(TmpFinalCheckBuild.cs, TmpFinalCheckFlow.cs)는 끝난 뒤 지웠다.

- 실제 Windows64 빌드를 batch로 만들었다(성공, 오류 0). 결과: `build_report.txt`. 빌드한 exe를 창 모드로 20초 실행했고 Player 로그 오류 0.
- 연속 플레이·프레임 시험은 batch 에디터 실행 2회: `flow_run1.txt`, `flow_run2.txt`(같은 순서, 같은 결과).
- 진행 기록·항목별 근거: `progress.md`.
- Unity가 실행 중에 바꾼 추적 파일 3개(UIFont Fallback SDF.asset, ProjectSettings.asset, UnityConnectSettings.asset - 줄바꿈과 m_Enabled)는 git으로 되돌렸다.

## 요약

| 항목 | 치명 | 중요 | 사소 |
|---|---|---|---|
| 1 하드코딩 | 0 | 0 | 6 |
| 2 빌드에서만 생기는 문제 | 0 | 0 | 4 |
| 3 초기화 | 0 | 0 | 0 |
| 4 위험한 코드 | 0 | 0 | 4 |
| 5 연속 플레이 | 0 | 0 | 0 |
| 6 빌드 환경 | 0 | 1 | 1 |
| 합계 | 0 | 1 | 15 |

## 문제 표

| # | 항목 | 심각도 | 문제 | 위치(파일:줄) | 영향 | 고치는 방법 제안 |
|---|---|---|---|---|---|---|
| 1 | 6 | 중요 | 타이틀 SETTING 버튼이 빈 설정 창을 연다(머리글·닫기 버튼만, 글자·조작 UI 없음). 이 창은 ESC로 닫히지 않는다(크레딧은 ESC로 닫힘). | `Assets/Scenes/TitleScene.unity` SettingsPanel, `Assets/Scripts/UI/TitleMenu.cs:45-53` | 출시 빌드에서 플레이어가 누르면 미완성 화면이 보이고, ESC를 눌러도 반응이 없다. | 출시 전 SETTING 버튼을 숨기거나(볼륨 등 최소 기능을 넣거나), TitleMenu에 Update를 두어 ESC로 설정 창도 닫는다. |
| 2 | 6 | 사소 | 창 포커스를 잃을 때 처리(OnApplicationFocus/Pause)가 없다. runInBackground=false라 빌드에서는 포커스를 잃으면 게임이 통째로 멈추고, 돌아오면 일시정지 메뉴 없이 바로 이어진다. | 코드 전체(해당 처리 없음), `ProjectSettings/ProjectSettings.asset` runInBackground | Alt+Tab 뒤 돌아오는 순간 몬스터가 바로 움직인다(멈춘 동안 피해는 없음). 키 눌림은 Input System이 초기화해 남지 않는다. | PauseMenu에 `OnApplicationFocus(false)`이면 (마을·트럭에서, 다른 멈춤 화면이 없을 때) Pause()를 부르도록 추가. |
| 3 | 2 | 사소 | 게임이 쓰지 않는 `com.unity.ai.inference`(Sentis)가 빌드에 들어간다: DirectML.dll 14MB + Resources 셰이더, 빌드 경고 488건 중 대부분. `com.unity.ai.assistant`도 의존. | `Packages/manifest.json` | 빌드 크기 증가(전체 약 178MB), 빌드 로그가 경고로 덮여 진짜 경고를 놓치기 쉽다. | 쓰지 않으면 manifest에서 ai.inference·ai.assistant를 빼고 다시 빌드한다. |
| 4 | 2 | 사소 | 출시 빌드에도 Debug.Log 22곳이 남는다. 특히 마을에 들어갈 때마다 동물·몬스터마다 한 줄씩(약 60줄), 도망치는 레서판다가 0.5초마다 한 줄. | `Assets/Scripts/Map/VillageMapGenerator.cs:654`, `:2394`, `Assets/Scripts/Animals/AnimalSoundFlee.cs:15`, `Assets/Scripts/Animals/AnimalRescue.cs:260` 외 | Player.log가 커지고 로그마다 스택 문자열을 만든다(체감 성능 영향은 작음). | ShelterSettlement처럼 `[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]` 로그 함수로 감싸거나, Player Settings의 Stack Trace(Log)를 None으로 둔다. |
| 5 | 2 | 사소 | 런타임 코드 컴파일 경고 2종: 곧 사라질 API `FindObjectsByType(..., FindObjectsSortMode)`, 쓰지 않는 필드. | `Assets/Scripts/Core/GameManager.cs:365`, `Assets/Scripts/Items/MineTrap.cs:21` | 지금은 동작함. 다음 Unity 업데이트에서 컴파일 오류가 될 수 있다. | `FindObjectsByType<GameOverPanel>(FindObjectsInactive.Include)`로 바꾸고, flashSortingOrderOffset을 쓰거나 지운다. |
| 6 | 2 | 사소 | 설정 이름이 "디버그용·테스트용"인데 실제 시작 지급량으로 쓰인다(지뢰 2·폭탄 1). 또 쓰지 않는 에셋 샘플 InstantButtonBomb2D가 Space 키로 터지는 코드를 갖고 빌드에 컴파일된다(씬·프리팹에서는 안 씀). | `Assets/Scripts/Core/GameBalanceConfig.cs:1106-1110`, `Assets/Scripts/Core/GameManager.cs:764-765`, `Assets/Sprites/Gadget/instant-button-bomb-unity/InstantBombUnity/Scripts/InstantButtonBomb2D.cs:20` | 나중에 "디버그 값"으로 보고 0으로 바꾸면 시작 도구가 사라진다. 샘플은 붙이는 순간 Space(튜토리얼 닫기 키)와 겹친다. | `startingMineCount`/`startingBombCount`로 이름과 설명을 바꾸고(FormerlySerializedAs), 샘플 스크립트는 지우거나 enableSpaceKey 기본값을 false로. |
| 7 | 1 | 사소 | 설정 에셋 값과 GameBalanceConfig 코드 기본값이 16개 다르다. 예: 목표 3/7, 날마다 +2/+6, 늑대 발견 4.05/3.44, 포획망 피해 1.2/0.6·쿨 0.75/1.5, 폭탄 피해 3/2, 수면 지연 1/0.6, 주변 시야 3/4.8. | `Assets/Scripts/Core/GameBalanceConfig.cs:27, 35, 222, 233, 241, 249, 447, 464, 602, 605, 669, 1042, 1051, 1085` | 지금은 에셋 값이 쓰여 영향 없음. 새 설정 에셋을 만들거나 Reset하면 예전 밸런스로 돌아간다. | 코드 기본값을 지금 에셋 값으로 맞춘다. |
| 8 | 1 | 사소 | config가 비었을 때 쓰는 코드 대체값 23곳이 에셋 값과 다르다(스태미나 70/25/12 vs 85/18/15, 맵 동물 27/41·몬스터 12/18, 늑대 포기 거리 10/5.5 등). config 참조는 씬·프리팹 15곳 모두 연결돼 있어 지금은 안 쓰인다. | `GameManager.cs:38-39`, `VillageMapGenerator.cs:100,103`, `PlayerMovement.cs:24,102,117`, `PlayerWeaponController.cs:59,64,91-92,115-117`, `PlayerGadgetController.cs:64,96-97`, `MonsterAI.cs:47-48`, `SoundReactiveMonsterAI.cs:44-45`, `BatMonsterAI.cs:107`, `RhinoMonsterAI.cs:154` | 참조가 빠지면 조용히 다른 밸런스로 동작한다. | 대체값을 없애고 config가 없으면 경고를 한 번 남기거나, 대체값을 GameBalanceConfig의 기본 인스턴스(같은 값)에서 읽는다. |
| 9 | 1 | 사소 | config 밖에 있는 게임 값: 동물 무게 1~10(마일리지 경제, 프리팹 5종), 동물 놀람 거리 3·포기 5, 다트 속도 12·판정 0.3, 상호작용 거리 8종(1.6~2.7), 늑대 순찰 대기 2~4초, 소리반응형 조사 속도 2, 거북이 숨음 자막 2초, 몬스터 근접 판정 CatchRange×3. | `AnimalRescue.cs:8-9`, `AnimalFlee.cs:22-24`, `PlayerWeaponController.cs:12`, `TranquilizerDart.cs:5`, `MonsterAI.cs:22-23`, `SoundReactiveMonsterAI.cs:19`, `AnimalTurtle.cs:255`, `MonsterBrain.cs:172,184` | 밸런스를 config 한 곳에서 조절할 수 없다. | 동물 무게·놀람 거리·다트 속도처럼 밸런스에 닿는 값부터 GameBalanceConfig로 옮긴다. |
| 10 | 1 | 사소 | 씬 이름을 문자열로 직접 비교하는 곳이 많다(TutorialGuide 12곳, BestiaryUI, MonsterEncounterCard, AudioManager, VillageMonsterSpawner, TruckNightLighting, EndScreenButtons + 직렬화 기본값 11곳). 튜토리얼은 오브젝트 이름 "TopInfoPanel"·"TruckPoint"·"Sofa"를 Find로 찾는다. | `TutorialGuide.cs:584-674, 741, 768, 774`, `BestiaryUI.cs:30, 529`, `MonsterEncounterCard.cs:228`, `AudioManager.cs:179-180`, `VillageMonsterSpawner.cs:77-79`, `TruckNightLighting.cs:16` | 씬·오브젝트 이름을 바꾸면 튜토리얼·도감·음악이 조용히 멈춘다(오류 없음). | `SceneNames` 상수 클래스 하나로 모으고, 이름 Find 대신 컴포넌트 타입으로 찾는다. |
| 11 | 1 | 사소 | Resources 경로 문자열 9곳(TutorialHint/*, AnimalKeeperNight/, AnimalKeeperHUD/, AnimalKeeperTitle/ 등)과 태그 "Player" FindGameObjectWithTag 여러 곳. | `TutorialGuide.cs:232-236`, `GameOverTruckScene.cs:105`, `AnimalKeeperHUD.cs:99`, `AnimalKeeperNight.cs:65`, `AnimalKeeperTitle.cs:63` | 폴더 이름을 바꾸면 그림이 빠진다(경고만). | 경로를 상수로 모으거나 직렬화 참조로 바꾼다. |
| 12 | 1 | 사소 | UI 기준 해상도 2240x1260이 6곳에 따로 적혀 있다. 타이틀 그림은 480x300·비율 1.6 고정. | `GameManager.cs:378`, `ClerkSpeechBubble.cs:66`, `ShopMenuUI.cs:27`, `SubtitleManager.cs:27`, `TutorialGuide.cs:33`, `AnimalKeeperTitle.cs:44,112` | 기준을 바꿀 때 한 곳이라도 빠지면 그 화면만 크기가 어긋난다. | `UIReference.Resolution` 같은 공용 상수 하나를 쓴다. |
| 13 | 4 | 사소 | 매 프레임 검색: 튜토리얼이 마을에 있는 동안 매 프레임 FindGameObjectWithTag("Player")+GetComponent(1일차가 끝난 뒤에도), 트럭에서 timeScale 0일 때 매 프레임 FindAnyObjectByType<TruckHubPoint>, 0.25초마다 FindObjectsByType<FootSortOrder>(마을 오브젝트 약 1000개), 튜토리얼 멈춤 카드 동안 매 프레임 FindObjectsByType<Graphic>. | `TutorialGuide.cs:602-603, 1395`, `GameManager.cs:146`, `ObstacleOccluderFade.cs:64` | 지금 성능 문제는 보이지 않았다(앞 점검 LongPlay). 저사양에서 작은 끊김 원인이 될 수 있다. | 플레이어·허브 참조를 씬 로드 때 한 번 찾아 캐시하고, FootSortOrder는 Active 목록(MonsterHealth.Active처럼)으로 관리한다. |
| 14 | 4 | 사소 | 매 프레임 `SceneManager.GetActiveScene().name`(문자열 할당) 4곳. | `GameManager.cs:109`, `BestiaryUI.cs:528`, `MonsterEncounterCard.cs:219`, `TutorialGuide.cs:493` | 프레임마다 작은 GC 할당. | sceneLoaded에서 이름을 한 번 저장해 쓴다. |
| 15 | 4 | 사소 | `Time.timeScale == 0f` float 같음 비교 3곳. | `ClerkSpeechBubble.cs:144`, `ShopMerchant.cs:32`, `PauseMenu.cs:90` | 지금은 0f만 넣으므로 문제 없음. 나중에 느린 화면(0.01 등)을 쓰면 판정이 어긋난다. | `<= 0f`로 통일(다른 곳은 이미 그렇게 씀). |
| 16 | 4 | 사소 | null 검사 없는 참조: 목표 미달 확인에서 `GameManager.Instance.FinishCycle()`(같은 파일 다른 곳은 null 검사). 설정 실수로 playerMoveSpeed가 0이면 0으로 나눔. | `Assets/Scripts/SceneFlow/TruckHubPoint.cs:166`, `Assets/Scripts/Core/GameBalanceConfig.cs:1216` | GameManager 없이 트럭 씬을 바로 열 때만 NullReference. | `GameManager.Instance?.FinishCycle()` / `Mathf.Max(0.01f, playerMoveSpeed)`. |

## 항목별 결과

### 1. 하드코딩
- 밸런스 값은 거의 모두 GameBalanceConfig를 거친다. config 참조는 씬·프리팹 15곳 모두 연결(빈 참조 0).
- 위 #7~#12. 레이어는 직렬화 이름 1곳(VillageMapGenerator:1889)뿐. Shader.Find(VillageMapGenerator:2253)는 머티리얼이 씬에 연결돼 대체 경로로만 쓰이고, 셰이더는 빌드에 들어간다.

### 2. 빌드에서만 생기는 문제
- 실제 빌드 성공(오류 0), exe 실행 오류 0.
- `#if UNITY_EDITOR` 없이 UnityEditor를 쓰는 런타임 코드 0. UNITY_EDITOR 블록 6곳은 OnValidate·종료 버튼·테스트용 덮어쓰기.
- 에디터에서만 채워지는 값(OnValidate로 고르는 인벤토리 아이콘 sheetFirstFrameIcon): 프리팹 9개 모두 저장돼 있어 빌드에서도 같다.
- 테스트 스위치(TutorialGuide.IntrosSuppressed, MonsterEncounterCard.Suppressed)는 Editor 폴더 스크립트가 batchmode + -executeMethod일 때만 켠다 → 빌드에서는 늘 꺼짐. VillageMapGenerator.EditorLayoutOverride는 #if UNITY_EDITOR 안. 맵 검증은 개발 빌드 전용.
- 치트 키 0.

### 3. 초기화 (문제 0)
- PlayerPrefs를 쓰지 않는다.
- DontDestroyOnLoad 8개(GameManager, AudioManager, PauseMenu, TutorialGuide, MonsterEncounterCard, BestiaryUI, SubtitleManager, AppUIUpdater)는 판이 바뀌어도 늘 같은 8개였다(중복 생성 없음). 상점 메뉴는 열 때 만든다.
- ResetGame이 판 상태를 모두 비우고, 튜토리얼·첫 만남·도감은 "시작 직업 선택 대기"를 보고 비운다. 이벤트 구독 14쌍 모두 해제 짝이 있다.
- timeScale은 멈추는 곳 9곳 모두 닫을 때 되돌리고, 씬을 불러올 때마다 PauseMenu가 1로 되돌린다.

### 4. 위험한 코드
- 이동은 FixedUpdate(플레이어·동물)·NavMeshAgent(몬스터)·deltaTime(돌진·가시)이라 프레임 의존이 없고, 6번 실측도 같았다.
- 0으로 나누기 후보는 #16 한 곳 외에 모두 가드돼 있다. 멈추지 않는 코루틴 없음.

### 5. 연속 플레이 (문제 0)
순서: 새 게임(스카우트) → 승리 → 타이틀로 → 새 게임(트래퍼) → 체력 0 게임오버(마을) → 다시 시작(트래퍼) → 목표 미달 게임오버(트럭) → 타이틀로 → 새 게임(거너) → 승리 → 다시 시작(스카우트).
판마다 동물을 줍고, 패시브(러닝화)를 사고, 마을에서 20~70초를 보내 상태를 더럽힌 뒤 다음 판으로 넘어갔다. 튜토리얼·첫 만남 카드는 켠 채 Space로 닫았다(24번).

| 상태 | 판1 | 판2 | 판3 | 판4 | 판5 |
|---|---|---|---|---|---|
| 직업 / 체력 | 스카우트 100 | 트래퍼 130 | 트래퍼 130 | 거너 50 | 스카우트 100 |
| 날 / 목표 / 구조 / 마일리지 | 1 / 7 / 0 / 0 | 같음 | 같음 | 같음 | 같음 |
| 마을 시간 / 구간 / 시간소환 기록 | 0 / 1 / 비어 있음 | 같음 | 같음 | 같음 | 같음 |
| 가방 | 5칸 [지뢰 지뢰 폭탄 빈 빈] | 같음 | 같음 | 같음 | 같음 |
| 패시브 / 거너 탄 | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 3(직업 시작 탄) | 0 / 0 |
| 동물·시체 자리 기록 | 0 / 0 | 같음 | 같음 | 같음 | 같음 |
| 마을 첫 진입 동물 / 몬스터 | 41 / 9 | 41 / 9 | 41 / 9 | 41 / 9 | 41 / 9 |
| 튜토리얼 | 2단계(1단계 완료) | 같음 | 같음 | 같음 | 같음 |
| 첫 만남 카드 / 도감 기록 | 0 / 0 | 같음 | 같음 | 같음 | 같음 |
| timeScale / 일시정지 / 입력 막힘 | 1 / 아님 / 아님 | 같음 | 같음 | 같음 | 같음 |
| 소리 | 트럭 음악 재생, 추격음 0 | 같음 | 같음 | 같음 | 같음 |

- 판마다 다른 것은 직업에 따른 값(체력 100/130/50, 걷기 4.2/3.75, 거너 시작 탄 3)뿐이다. 마을 첫 진입 때 도감 동물 2~3은 그 순간 시야에 들어온 동물 수(맵마다 다름)다.
- 승리·게임오버 화면은 timeScale 0이고, [다시 시작]·[타이틀로] 뒤 1로 돌아왔다. 콘솔 경고·오류·예외 0.

### 6. 빌드 환경
- 프레임 속도(1/30·1/60·1/144초 고정 프레임, 마을):

| | 30fps | 60fps | 144fps |
|---|---|---|---|
| 걷기 속도(칸/초) | 4.200 | 4.200 | 4.200 |
| 마을 시간 증가 = deltaTime 합 | 같음 | 같음 | 같음 |
| 달리기 탈진까지 | 3.97초 · 19.57칸 | 3.95초 · 19.58칸 | 3.94초 · 19.56칸 |
| 탈진 뒤 2초 회복 | +30.5 | +30.3 | +30.1 |
| 늑대 공격 간격 | 1.50초 x5 | 1.50초 x5 | 1.50초 x5 |
| 호저 가시 간격 | 2.50초 | 2.50초 | 2.51초 |

  차이는 한 프레임 길이 안이다(프레임 의존 없음). 빌드 품질 설정(Standalone 기본 Ultra)은 vSync 1이라 모니터 주사율(60/144Hz)로 돈다.
- 포커스: #2. 한 프레임은 최대 0.333초로 잘린다.
- 타이틀: ESC는 크레딧을 닫고, 설정 창은 못 닫는다(#1), 아무것도 없을 때는 반응 없음(일시정지는 타이틀에서 꺼 둔 설계). EXIT 버튼은 TitleMenu.OnExit → 빌드에서 Application.Quit.
- 처음 실행: PlayerPrefs를 안 써서 처음 실행과 다시 실행이 같다. 처음 타이틀 상태가 새 게임 직후 상태와 같다.

## 확인하지 못한 것
- 실제 창 포커스 전환과 실제 144Hz 모니터는 batch에서 재현할 수 없어, 고정 프레임 시험과 코드·설정으로 판단했다.
- 종료 버튼의 실제 종료는 에디터에서 플레이 모드가 끝나 버려 누르지 않았다(연결과 코드만 확인).

## 수정 결과 (2026-11-23, 커밋 전)

| # | 처리 | 내용 |
|---|---|---|
| 1 | 고침 | 타이틀 설정 창에 배경음·효과음 슬라이더(0~100%). PlayerPrefs 저장(처음 실행 100%), 믹서 BGM/SFX·UI 그룹에 적용(모든 씬·모든 소리), 0%면 -80dB. ESC·CLOSE로 닫힘. 효과음 슬라이더는 미리듣기(다트 맞는 소리). 타이틀 카메라에 없던 AudioListener 추가. 캡처: `settings_volume.png` |
| 2 | 고침 | 마을·트럭에서 포커스를 잃거나 최소화하면 일시정지 메뉴를 연다. 튜토리얼 멈춤·첫 만남 카드·도감·게임오버·승리·상점·소파 메뉴가 떠 있으면 열지 않는다. 대화형 에디터에서는 열지 않는다. |
| 3 | 고침 | `com.unity.ai.inference`(Sentis) 제거. 게임 코드·에셋은 쓰지 않았다. 에디터 전용 AI Assistant의 AI Search가 있을 때만 켜는 선택 기능(`SENTIS_AVAILABLE`)이라 같이 꺼진다. 빌드 185.7MB → 160.0MB, 빌드 경고 488 → 3. |

- 실제 출력 크기(창 모드 에디터): 배경음 경로 40% → 0.399배, 실제 트럭 배경음 0.397배, 효과음 70% → 0.697배, 0% → 0.
- 테스트: 전체 30개 batch 통과(이전 28개 + 새 2개)(`tests/summary.txt`, 새 테스트 VolumeSettingsTest·FocusPauseTest 포함). MonsterTrace·LinearRegressionDump는 기준 기록과 같다.
