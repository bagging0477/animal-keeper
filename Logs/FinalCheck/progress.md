# 빌드 전 최종 점검 진행 기록 (2026-11-23 시작, main 10ae7d5)

이어서 할 때: 아래 상태에서 [ ]인 첫 항목부터. 앞 점검(Logs/BugCheck, Logs/LogicCheck)에서 본 것은 제외.
- Logs 이미지 원본: git이 추적하는 이미지는 끝에 `git status Logs`로 바뀐 것 확인 후 `git checkout -- <파일>`로 복원. 추적 안 되는 새 이미지는 점검 전 목록과 비교해 지움.
- 임시 스크립트: Assets/Scripts/Editor/TmpFinalCheck*.cs (+ .meta) — 끝나면 지움.
- Unity 원본 로그: Logs/FinalCheck/unity_*.log — 끝나면 지움.

## 상태
- [x] 1 하드코딩
- [x] 2 빌드에서만 생기는 문제
- [x] 3 초기화 (코드; 실제 확인은 5번 결과로)
- [x] 4 위험한 코드
- [x] 5 연속 플레이 (batch)
- [x] 6 빌드 환경 (batch + 코드)
- [x] 정리 (임시 스크립트·로그 삭제, 이미지 복원, git status)

## 항목별 결과

### 1 하드코딩 (완료)
- 밸런스 값은 거의 모두 GameBalanceConfig 경유. config 참조는 씬·프리팹 15곳 모두 연결됨(빈 참조 0) → 코드 대체값은 지금은 안 쓰임.
- 설정 에셋 vs 코드 필드 기본값 다름 16개(GameBalanceConfig.cs: patrolMonsterDetectRange 4.05/3.44, targetRescueCount 3/7, targetRescueIncreasePerDay 2/6, netDamage 1.2/0.6, netCooldown 0.75/1.5, bombDamage 3/2, sleepDelay 1/0.6, ambientVisionRadius 3/4.8 등).
- config가 없을 때 쓰는 코드 대체값이 에셋과 다름 23곳(GameManager:38-39 목표 3+2 vs 7+6, VillageMapGenerator:100,103 동물 27/41·몬스터 12/18, PlayerMovement:24,102,117 스태미나 70/25/12 vs 85/18/15, PlayerWeaponController:59,64,91-92,115-117, PlayerGadgetController:64,96-97, MonsterAI:47-48, SoundReactiveMonsterAI:44-45, BatMonsterAI:107, RhinoMonsterAI:154).
- config 밖 게임 값: 동물 무게 1~10(AnimalRescue 프리팹 5종, 마일리지 경제), 동물 놀람 거리 3·포기 5(AnimalFlee 직렬화), 다트 속도 12·판정 0.3, 상호작용 거리 8종(1.6~2.7), 늑대 순찰 대기 2~4, 소리반응형 조사 속도 2, 거북이 숨음 자막 2초(AnimalTurtle:255), MonsterBrain:172,184 CatchRange*3, MonsterAI:164 시야 잃음 감소 x2.
- 씬 이름 문자열 직접 비교: TutorialGuide:584-674(12곳), MonsterEncounterCard:228, BestiaryUI:30,529, AudioManager:179-180, VillageMonsterSpawner:78-79, TruckNightLighting:16, EndScreenButtons:17 (+직렬화 기본값 11곳). 오브젝트 이름 Find: TutorialGuide:741,768,774("TopInfoPanel","TruckPoint","Sofa"), VillageMapGenerator "Ground"/"NavGround" 다수, GameObject.Find 6곳. 태그 "Player": MineFlash2D(미사용 샘플)·AnimalRescue 등 FindWithTag. Resources 경로 문자열 9곳(TutorialGuide:232-236, GameOverTruckScene:105, AnimalKeeperHUD:99, AnimalKeeperNight:65, AnimalKeeperTitle:63 등). 레이어 NameToLayer 1곳(VillageMapGenerator:1889, 직렬화 이름).
- 해상도: 기준 2240x1260이 6곳에 따로 적힘(GameManager:378, ClerkSpeechBubble:66, ShopMenuUI:27, SubtitleManager:27, TutorialGuide:33, 기타). 타이틀 그림 480x300·비율 1.6 고정(AnimalKeeperTitle:44,112, AnimalKeeperNight). 21:9 도감은 BugCheck #3에서 이미 다룸.
- Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") VillageMapGenerator:2253 - 머티리얼이 씬에 연결돼 있어 대체 경로만, 빌드엔 포함됨.

### 2 빌드에서만 생기는 문제 (완료)
- 실제 Windows64 빌드(batch, 임시 TmpFinalCheckBuild): 성공, 오류 0, 경고 488(대부분 com.unity.ai.inference(Sentis) 셰이더 경고). 결과: build_report.txt. 빌드 크기 약 178MB, DirectML.dll 14MB·Sentis 셰이더가 들어감(게임 코드에서 안 씀).
- 빌드한 exe를 창 모드로 20초 실행: Player 로그 오류·예외 0, 타이틀 로드 정상.
- 런타임 코드 컴파일 경고: GameManager:365 FindObjectsSortMode(곧 사라질 API), MineTrap:21 flashSortingOrderOffset 안 씀.
- #if 없이 UnityEditor 쓰는 런타임 코드 0. UNITY_EDITOR 블록 6곳 모두 OnValidate/종료 버튼/테스트 덮어쓰기. OnValidate로만 채우는 sheetFirstFrameIcon은 프리팹 9개 모두 저장돼 있음(빌드 정상).
- 테스트 스위치: TutorialGuide.IntrosSuppressed·MonsterEncounterCard.Suppressed는 Editor 폴더 TutorialIntroTestSwitch(batchmode + -executeMethod)만 켬 → 빌드에선 늘 false. VillageMapGenerator.EditorLayoutOverride는 #if UNITY_EDITOR 안.
- 개발 검증 VillageMapGenerator:1053은 UNITY_EDITOR||DEVELOPMENT_BUILD → 출시 빌드 제외. ShelterSettlement 로그는 Conditional.
- 치트 키 0. 미사용 샘플 InstantButtonBomb2D(Space로 터짐, 씬·프리팹에서 안 씀).
- Debug.Log 22곳 빌드에 남음: VillageMapGenerator:654,2394 맵 만들 때 동물·몬스터마다(마을 들어갈 때마다 약 60줄), AnimalSoundFlee:15 도망 중 0.5초마다, AnimalRescue:260 씬 떠날 때 동물마다.
- config 이름 debugStartingMineCount/debugStartingBombCount(설명 "디버그용·테스트용")가 실제 시작 지급량으로 쓰임(GameManager:764-765).
- PlayerSettings: runInBackground=false, 창 크기 변경 불가, 전체화면 창, vSync 0(Quality), targetFrameRate 설정 코드 없음.

### 3 초기화 (코드 점검 완료)
- PlayerPrefs 사용 0 → 처음 실행/저장 문제 없음.
- DontDestroyOnLoad 7종: GameManager·AudioManager(씬 오브젝트 싱글톤), PauseMenu·TutorialGuide·MonsterEncounterCard·BestiaryUI·SubtitleManager·ShopMenuUI(스스로 생성).
- ResetGame(GameManager:269)이 비우는 것: 날·구조·동물/시체 자리·마일리지·화물·해금·직업·체력·탄약·게임오버/승리·시드·마을시간·시간소환 기록·패시브·가방·정산. 튜토리얼·첫만남·도감은 NeedsStartingClassSelection을 매 프레임 보고 비움(TutorialGuide:497, MonsterEncounterCard:213, BestiaryUI:525).
- timeScale: PauseMenu.OnSceneLoaded(PauseMenu:64-72)가 씬을 불러올 때마다 1로 되돌림(안전망). 멈추는 곳 9곳 모두 닫을 때 되돌림.
- static: SceneTransitionGuard.transitionStarted(sceneLoaded에서 풀림), InteractionFocus(프레임 기준), ShopPurchase.nextClassNoticeTime(실제 시간 쿨다운), VillageMonsterSpawner.villageEntries(새 게임에서도 계속 증가 - 난수 씨앗에만 쓰임, 영향 없음).
- 이벤트 구독 14쌍 모두 해제 짝 있음(SceneTransitionGuard 람다는 static 생성자 1회).

### 4 위험한 코드 (완료)
- 이동: 플레이어·동물 FixedUpdate+fixedDeltaTime, 몬스터 NavMeshAgent, 코뿔소 돌진·가시 deltaTime → 프레임 의존 이동 0(6번 실측으로 확인 예정).
- 0으로 나누기: 위험 후보 모두 가드됨(Mathf.Max, >0 검사, [Min]). GameBalanceConfig:1216 ScoutMoveSpeed/playerMoveSpeed만 설정 0일 때.
- float ==: Time.timeScale == 0f 3곳(ClerkSpeechBubble:144, ShopMerchant:32, PauseMenu:90) - 값을 0f로만 넣으므로 실제 문제 없음, <= 0f로 통일 권장.
- 매 프레임 검색: TutorialGuide:602 FindGameObjectWithTag("Player")+GetComponent 마을에 있는 동안 매 프레임(1일차 이후에도), GameManager:146 트럭에서 timeScale 0일 때 매 프레임 FindAnyObjectByType<TruckHubPoint>, ObstacleOccluderFade:64 0.25초마다 FindObjectsByType<FootSortOrder>(마을 오브젝트 약 1000개), TutorialGuide:1395 멈춤 카드 동안 FindObjectsByType<Graphic> 매 프레임, StaminaUI:26 플레이어 없을 때 매 프레임, ClerkSpeechBubble:142,149 Camera.main·GetComponent 매 프레임(가벼움).
- 씬 이름 비교: GameManager:109, BestiaryUI:528, MonsterEncounterCard:219, TutorialGuide:493 매 프레임 GetActiveScene().name(문자열 할당).
- 코루틴: MineTrap:70(오브젝트와 함께 사라짐), VillageMapGenerator.Start(검증, 개발 빌드만). 멈추지 않는 코루틴 없음.
- null: TruckHubPoint:166 GameManager.Instance.FinishCycle() null 검사 없음(다른 곳은 ?. 사용) - GameManager가 없을 때만.

### 5 연속 플레이 (완료) - flow_run1.txt, flow_run2.txt (같은 순서 두 번 실행, 결과 같음)
- 임시 TmpFinalCheckFlow: 실제 경로(가상 E·Space, 버튼 onClick, 직업대 E, 트럭 문·소파·수의사·보호소 출구), 튜토리얼·첫만남 카드 켬, 1/30초 프레임.
- 순서: 판1 스카우트 승리→타이틀로 / 판2 트래퍼 체력0(마을)→다시 시작 / 판3 트래퍼 목표미달(트럭)→타이틀로 / 판4 거너 승리→다시 시작 / 판5 스카우트. 판마다 동물 줍기·패시브 구매·마을 20~70초.
- 판 시작(트럭)·마을 첫 진입 비교: 날 1, 목표 7, 구조 0, 마일리지 0, 마을시간 0, 구간 1, 가방 5칸[지뢰2 폭탄1], 패시브 0, 동물·시체 자리 기록 0, 시간소환 기록 0, 튜토리얼 2단계(완료 1단계), 첫만남 0, 도감 0, timeScale 1, 일시정지 false, 전환 잠금 false, DDOL 루트 8개 같음, 음악 트럭 cozy ambient·마을 VillageScene, 추격음 0, 마을 동물 41·몬스터 9 → 모두 같음.
- 다른 것은 직업에 따른 값뿐(체력 100/130/50, 걷기 4.2/3.75, 거너 탄 3). 도감 동물 2~3은 마을 들어간 순간 시야에 들어온 동물 수(맵마다 다름). 판3 시작에 이전 게임오버 효과음(UI 원샷)이 아직 재생 중 - 테스트가 실제 시간으로 1초 안에 넘어가서 생긴 것.
- 승리·게임오버 화면 timeScale 0 → 다시 시작/타이틀로 뒤 1. 타이틀로 가면 튜토리얼·카드·도감·마을시간 모두 0.
- 콘솔 경고·오류·예외 0(두 번째 실행).

### 6 빌드 환경 (완료)
- 프레임별(1/30·1/60·1/144): 걷기 4.200칸/초 셋 다 같음, 마을시간 증가 = deltaTime 합, 달리기 탈진 3.97/3.95/3.94초·19.57/19.58/19.56칸, 탈진 뒤 2초 회복 +30.5/30.3/30.1(한 프레임 차), 늑대 공격 간격 1.50초 x5 셋 다, 호저 2.50/2.50/2.51초 → 차이 없음.
- 포커스: OnApplicationFocus/Pause 처리 코드 없음, runInBackground=false → 빌드에서 포커스 잃으면 게임 전체 멈춤, 돌아오면 일시정지 메뉴 없이 바로 이어짐. Input System 기본(포커스 잃으면 키 초기화)이라 키 눌림 남지 않음. 한 프레임 최대 0.333초(Maximum Allowed Timestep).
- 타이틀 ESC: 크레딧은 닫힘, 설정 창은 안 닫힘(닫기 버튼만), 아무것도 없을 때 ESC 무반응(일시정지 안 열림 - 설계). 설정 창은 머리글·닫기 버튼만 있고 글자·조작 UI 없음(빈 창).
- 게임 종료 버튼: ExitButton → TitleMenu.OnExit → 빌드에선 Application.Quit(코드 확인).
- 처음 실행: PlayerPrefs 안 씀. 처음 타이틀 상태 = 새 게임 직후 상태.
- 빌드 exe 실행 20초: 오류 0.

### 정리 (완료)
- 임시 스크립트 TmpFinalCheckBuild.cs·TmpFinalCheckFlow.cs(+meta) 삭제, Unity 원본 로그 unity_build/flow1~3.log 삭제.
- Logs 이미지 1892장: 점검 전 해시와 모두 같음(새로 생긴 것·바뀐 것 0) → 복원할 것 없음.
- Unity가 바꾼 추적 파일 3개(UIFont Fallback SDF.asset, ProjectSettings.asset, UnityConnectSettings.asset) git checkout으로 되돌림.
- 결과표: final_check.md. git status 깨끗함.
