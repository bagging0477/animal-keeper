소리 조사 표 페이지: https://claude.ai/artifact/BMZEUmBhbbZxBtvTMyq6Gj

# Animal Keeper 소리 상태 조사 (2026-11-24, main 9d9aa18)

조사만 함 — 프로젝트 파일 변경 없음. `Logs/`는 .gitignore 대상.

## 기준

- 모든 소리는 `AudioManager`(TitleScene.unity:3685~3721, TruckScene.unity:717~753, 두 씬 값 같음)를 거쳐 믹서 그룹으로 나감. 씬·프리팹에 AudioSource 컴포넌트는 없음.
- 믹서 그룹 기본값(GameAudioMixer.mixer 스냅샷): Master 0dB, SFX +3, UI +3, BGM −8, Chase(BGM 아래) +6.
- 임시음(코드 합성 비프음, `AudioManager.cs:447` EnsurePlaceholderClips)은 클립 칸이 비었을 때만 생기는데, 지금 두 씬 모두 9칸이 다 채워져 있어 **실제로 재생되는 임시음은 없음**.
- 크기 측정: 파일을 디코드해 50ms 창 RMS를 잼(maxRMS = 가장 큰 창, 유효 길이 = maxRMS−20dB 이상인 창의 합). **실효 = maxRMS + 20·log10(볼륨)** (믹서 그룹 dB 제외). 효과음 대부분이 약 **−19dB**로 맞춰져 있어(AudioManager.cs:13 주석) 이보다 **6dB 이상 작으면 약함**으로 표시.
- 상태는 하나만 적고(없음 > 임시음 > 약함 > 출처 미확인 > 정상 순), 겹치는 건 비고에 적음.
- 출처 미확인 = CREDITS.md "효과음·배경음 16개 — 출처 확인 필요"에 해당하는 16개 파일(Kenney 파일 8개만 출처 확인됨).

## 파일별 측정값

| 파일 | 길이 | 유효 길이 | peak | maxRMS | 평균RMS | 출처 |
|---|---|---|---|---|---|---|
| VillageScene.mp3 | 21.72s | 13.15s | −0.9 | −6.9 | −15.2 | 미확인 |
| cozy ambient.mp3 | 30.04s | 23.30s | −3.1 | −11.4 | −17.5 | 미확인 |
| alert stinger.mp3 | 6.01s | 3.65s | −8.5 | −16.5 | −22.2 | 미확인 |
| air gun shot.mp3 | 1.10s | 0.20s | −3.5 | −18.3 | −22.0 | 미확인 |
| damage grunt.mp3 | 0.39s | 0.35s | −11.6 | −18.9 | −22.5 | 미확인 |
| whoosh swing.mp3 | 1.22s | 0.15s | −2.0 | −12.7 | −17.1 | 미확인 |
| success chime.mp3 | 1.62s | 0.40s | −0.1 | −11.1 | −14.0 | 미확인 |
| fail buzzer.mp3 | 3.53s | 1.85s | 0.0 | −4.6 | −9.6 | 미확인 |
| animal_cat_pickup.wav.mp3 | 1.22s | 0.90s | −0.6 | −7.1 | −11.4 | 미확인 |
| animal_wombat_pickup.wav.mp3 | 1.10s | 0.40s | −4.7 | −20.1 | −23.1 | 미확인 |
| animal_redpanda_barking.mp3 | 0.86s | 0.20s | −3.1 | −14.1 | −18.1 | 미확인 |
| animal_redpanda_pickup.mp3 | 1.61s | 0.55s | −7.2 | −17.0 | −21.3 | 미확인 |
| animal_turtle_hide and emerge.wav | 1.03s | 0.10s | −0.1 | −17.5 | −20.5 | 미확인 |
| animal_turtle_pickup.wav | 10.03s | 3.05s | −12.4 | −25.0 | −29.5 | 미확인 |
| animal_goose_fly.mp3 (0~0.8s 구간) | 0.80s | 0.60s | −6.7 | −14.5 | −18.0 | 미확인 |
| animal_goose_hit.mp3 | 1.61s | 0.90s | −10.2 | −22.3 | −26.9 | 미확인 |
| HitFeel/impactSoft_medium_000.ogg | 0.12s | 0.05s | −1.0 | −10.6 | −10.6 | Kenney CC0 |
| HitFeel/dropLeather.ogg | 0.42s | 0.20s | −8.0 | −17.4 | −21.3 | Kenney CC0 |
| HitFeel/lowFrequency_explosion_001.ogg | 1.00s | 0.35s | −0.8 | −7.1 | −11.1 | Kenney CC0 |
| HitFeel/impactSoft_heavy_000.ogg | 0.51s | 0.15s | −0.9 | −8.0 | −12.0 | Kenney CC0 |
| HitFeel/impactWood_heavy_000.ogg | 0.31s | 0.10s | −0.9 | −11.8 | −14.7 | Kenney CC0 |
| Kenney/handleSmallLeather.ogg | 0.34s | 0.15s | −14.7 | −30.6 | −34.7 | Kenney CC0 |
| Kenney/cloth4.ogg | 0.38s | 0.35s | −16.5 | −32.4 | −36.3 | Kenney CC0 |

## 순간별 상태

| # | 순간 | 상태 | 연결된 파일 | 볼륨 (실효 dB) | 위치 파일:줄 | 비고 |
|---|---|---|---|---|---|---|
| **플레이어·무기·도구** |||||||
| 1 | 걷기 발소리 | 없음 | — | — | Assets/Scripts/Player/PlayerMovement.cs:69 | |
| 2 | 달리기 | 없음 | — | — | Assets/Scripts/Player/PlayerMovement.cs:75 | 몬스터용 SoundEvents만 냄(:89) |
| 3 | 플레이어 피격(모든 몬스터 공격·가시·돌진 공통) | 출처 미확인 | damage grunt.mp3 | 1.0 (−18.9) | Assets/Scripts/Core/GameManager.cs:335 | 클립: TruckScene.unity:722, 볼륨 :736 |
| 4 | 포획망 휘두르기 | 출처 미확인 | whoosh swing.mp3 | 0.48 (−19.1) | Assets/Scripts/Player/PlayerWeaponController.cs:60 | |
| 5 | 포획망 명중 | 약함 | HitFeel/dropLeather.ogg | 0.31 (−27.6) | Assets/Scripts/Common/HitFeel.cs:33 | 기준보다 약 8.5dB 작음 |
| 6 | 마취총 발사 | 출처 미확인 | air gun shot.mp3 | 0.9 (−19.2) | Assets/Scripts/Player/PlayerWeaponController.cs:125 | |
| 7 | 마취총 탄약 없음(빈 방아쇠) | 없음 | — | — | Assets/Scripts/Player/PlayerWeaponController.cs:109 | 로그만 찍음 |
| 8 | 마취 다트 명중 | 약함 | HitFeel/impactSoft_medium_000.ogg | 0.16 (−26.5) | Assets/Scripts/Common/HitFeel.cs:25 | 7.5dB 작고 유효 길이 0.05s |
| 9 | 지뢰 설치 | 없음 | — | — | Assets/Scripts/Player/PlayerGadgetController.cs:50 | |
| 10 | 지뢰 폭발 | 없음 | — | — | Assets/Scripts/Items/MineTrap.cs:59 | 붉은 섬광만 |
| 11 | 폭탄 폭발 | 정상 | HitFeel/lowFrequency_explosion_001.ogg | 0.38 (−15.5) | Assets/Scripts/Common/HitFeel.cs:46 | 의도적으로 조금 큼 |
| 12 | 무기·인벤토리 칸 바꾸기 | 없음 | — | — | Assets/Scripts/Core/GameManager.cs:622 | SelectWeapon :629 |
| 13 | 지뢰·폭탄 줍기 | 없음 | — | — | Assets/Scripts/Items/GadgetPickup.cs:53 | |
| 14 | 가방 가득 참 알림 | 없음 | — | — | Assets/Scripts/Items/InventoryFullNotice.cs:22 | 자막만 |
| 15 | 몬스터 시체 내려놓기 | 없음 | — | — | Assets/Scripts/Player/PlayerItemDropper.cs:81 | |
| **몬스터** |||||||
| 16 | 늑대 추격 시작 | 출처 미확인 | alert stinger.mp3 (PatrolMonster chaseClip 비어 기본값) | 0.56 (−21.5, Chase 그룹) | Assets/Scripts/Common/MonsterBrain.cs:64 | 재생: AudioManager.cs:308, 반복 꺼짐 |
| 17 | 늑대 공격(물기) | 없음 | — (플레이어 피격음만) | — | Assets/Scripts/Common/MonsterBrain.cs:192 | |
| 18 | 소리반응형 소리 들음(조사 시작) | 없음 | — | — | Assets/Scripts/Monsters/SoundReactiveMonsterAI.cs:131 | |
| 19 | 소리반응형 추격 | 출처 미확인 | alert stinger.mp3 (기본값) | 0.56 (−21.5) | Assets/Scripts/Common/MonsterBrain.cs:64 | 늑대와 같은 소리 |
| 20 | 소리반응형 공격 | 없음 | — | — | Assets/Scripts/Common/MonsterBrain.cs:192 | |
| 21 | 박쥐 추격 경고음 | 약함 | Kenney/cloth4.ogg | 1.0 (−32.4) | Assets/Scripts/Monsters/BatMonsterAI.cs:504 | 13dB 작음, 클립: BatMonster.prefab:384 |
| 22 | 박쥐 공격(닿음) | 없음 | — | — | Assets/Scripts/Common/MonsterBrain.cs:192 | |
| 23 | 박쥐 도망 | 없음 | — | — | Assets/Scripts/Monsters/BatMonsterAI.cs:344 | |
| 24 | 호저 가시 준비 | 없음 | — | — | Assets/Scripts/Monsters/PorcupineMonsterAI.cs:176 | |
| 25 | 호저 가시 발사 | 없음 | — | — | Assets/Scripts/Monsters/PorcupineMonsterAI.cs:194 | |
| 26 | 코뿔소 돌진 준비 | 없음 | — | — | Assets/Scripts/Monsters/RhinoMonsterAI.cs:290 | |
| 27 | 코뿔소 돌진 | 없음 | — | — | Assets/Scripts/Monsters/RhinoMonsterAI.cs:318 | |
| 28 | 코뿔소 벽 충돌 | 정상 | HitFeel/impactWood_heavy_000.ogg | 0.48 (−18.2) | Assets/Scripts/Common/HitFeel.cs:63 | |
| 29 | 몬스터 마취·기절 | 없음 | — | — | Assets/Scripts/Monsters/MonsterHealth.cs:121 | |
| 30 | 몬스터 쓰러짐 | 정상 | HitFeel/impactSoft_heavy_000.ogg | 0.30 (−18.5) | Assets/Scripts/Common/HitFeel.cs:54 | |
| 31 | 몬스터 시체 줍기(5종 공통) | 약함 | Kenney/handleSmallLeather.ogg (기본 줍기음) | 0.32 (−40.5) | Assets/Scripts/Monsters/MonsterCorpsePickup.cs:192 | 21dB 작음 — 사실상 안 들림. 볼륨 0.32는 이전 임시음 기준값으로 보임 |
| **동물** |||||||
| 32 | 고양이 도망 시작 | 없음 | — | — | Assets/Scripts/Animals/AnimalFlee.cs:378 | |
| 33 | 고양이 줍기 | 출처 미확인 | animal_cat_pickup.wav.mp3 | 0.25 (−19.1) | Assets/Prefabs/Animals/AnimalFlee.prefab:132 | 재생: AnimalRescue.cs:234 |
| 34 | 코알라 줍기 | 출처 미확인 | animal_wombat_pickup.wav.mp3 | 1.0 기본값 (−20.1) | Assets/Prefabs/Animals/AnimalWander.prefab:224 | 파일 이름이 웜뱃 |
| 35 | 동물 마취돼 잠듦·깸(5종 공통) | 없음 | — | — | Assets/Scripts/Animals/AnimalSleep.cs:48 | |
| 36 | 레서판다 울음(도망 시작) | 출처 미확인 | animal_redpanda_barking.mp3 | 1.0 (−14.1) | Assets/Prefabs/Animals/AnimalSoundFlee.prefab:167 | 재생: AnimalSoundFlee.cs:16, 교체 예정 |
| 37 | 레서판다 줍기 | 출처 미확인 | animal_redpanda_pickup.mp3 | 1.0 기본값 (−17.0) | Assets/Prefabs/Animals/AnimalSoundFlee.prefab:133 | |
| 38 | 거북 숨기 | 출처 미확인 | animal_turtle_hide and emerge.wav | 0.58 (−22.2) | Assets/Prefabs/Animals/AnimalTurtle.prefab:182 | 재생: AnimalTurtle.cs:263, 유효 0.10s로 짧음 |
| 39 | 거북 나오기 | 출처 미확인 | 같은 파일 | 0.58 (−22.2) | Assets/Prefabs/Animals/AnimalTurtle.prefab:188 | 재생: AnimalTurtle.cs:270, 숨기와 같은 소리 |
| 40 | 거북 숨은 채 줍기 거절 | 출처 미확인 | 같은 파일(hideSound 재사용) | 0.58 (−22.2) | Assets/Scripts/Animals/AnimalTurtle.cs:263 | |
| 41 | 거북 줍기 | 약함 | animal_turtle_pickup.wav | 0.48 (−31.4) | Assets/Prefabs/Animals/AnimalTurtle.prefab:132 | 12dB 작음 + 출처 미확인, 파일 10초 |
| 42 | 거위 날아오르기 | 출처 미확인 | animal_goose_fly.mp3 (0~0.8s 구간) | 1.0 (−14.5) | Assets/Prefabs/Animals/AnimalGoose.prefab:175 | 재생: AnimalGoose.cs:141 |
| 43 | 거위 피격 | 출처 미확인 | animal_goose_hit.mp3 | 1.0 (−22.3) | Assets/Prefabs/Animals/AnimalGoose.prefab:169 | 재생: AnimalGoose.cs:317 |
| 44 | 거위 줍기 | 없음 | — (playPickupSound 꺼짐, 의도) | — | Assets/Prefabs/Animals/AnimalGoose.prefab:137 | |
| **맡기기·트럭·상점** |||||||
| 45 | 보호소에 동물 맡기기 | 없음 | — | — | Assets/Scripts/Animals/AnimalDeliveryPoint.cs:52 | |
| 46 | 상점 열기·닫기 | 없음 | — | — | Assets/Scripts/Shop/ShopMenuUI.cs:78 | Close :88 |
| 47 | 상점 구매 | 없음 | — | — | Assets/Scripts/Core/GameManager.cs:306 | 탄약 구매 :569 |
| 48 | 트럭 메뉴 열기 | 없음 | — | — | Assets/Scripts/SceneFlow/TruckHubPoint.cs:107 | |
| 49 | 트럭에서 포기 확인 → 게임오버 | 출처 미확인 | fail buzzer.mp3 | 0.19 (−19.0, UI 그룹) | Assets/Scripts/SceneFlow/TruckHubPoint.cs:175 | |
| **UI** |||||||
| 50 | 타이틀 버튼(시작·설정·크레딧·종료) | 없음 | — | — | Assets/Scripts/UI/TitleMenu.cs:28 | :33~55 |
| 51 | 직업 선택 안내 카드 | 없음 | — | — | Assets/Scripts/UI/ClassInfoPopup.cs:31 | |
| 52 | 몬스터 첫 만남 카드 | 없음 | — | — | Assets/Scripts/UI/MonsterEncounterCard.cs:262 | |
| 53 | 도감 열기·닫기·넘기기 | 없음 | — | — | Assets/Scripts/UI/BestiaryUI.cs:581 | Close :592, Select :604 |
| 54 | 설정 창 효과음 슬라이더 미리듣기 | 약함 | HitFeel/impactSoft_medium_000.ogg | 0.16 (−26.5) | Assets/Scripts/UI/VolumeSettingsPanel.cs:43 | 다트 명중음 재사용 |
| 55 | 일시정지 메뉴 | 없음 | — | — | Assets/Scripts/UI/PauseMenu.cs:134 | |
| 56 | 크레딧 창 | 없음 | — | — | Assets/Scripts/UI/CreditsPanel.cs:34 | |
| 57 | 튜토리얼 단계 완료 | 없음 | — | — | Assets/Scripts/UI/TutorialGuide.cs:648 | |
| **날·장면** |||||||
| 58 | 마을 시간대 바뀜(해질녘→밤·경고 구간 알림) | 없음 | — | — | Assets/Scripts/UI/VillagePhaseNotice.cs:18 | |
| 59 | 하루 성공(Day 클리어) | 출처 미확인 | success chime.mp3 | 0.4 (−19.1, UI) | Assets/Scripts/SceneFlow/ShelterSettlement.cs:65 | |
| 60 | 승리(게임 클리어) | 출처 미확인 | success chime.mp3 | 0.4 (−19.1, UI) | Assets/Scripts/SceneFlow/ShelterSettlement.cs:53 | 하루 성공과 같은 소리 |
| 61 | 목표 미달 게임오버 | 출처 미확인 | fail buzzer.mp3 | 0.19 (−19.0, UI) | Assets/Scripts/SceneFlow/ShelterSettlement.cs:60 | |
| 62 | 체력 0 게임오버 | 출처 미확인 | fail buzzer.mp3 | 0.19 (−19.0, UI) | Assets/Scripts/Core/GameManager.cs:354 | |
| 63 | 게임오버 밤 트럭 장면 | 없음 | — | — | Assets/Scripts/UI/GameOverTruckScene.cs:67 | |
| 64 | 장면 이동(문·포털) | 없음 | — | — | Assets/Scripts/SceneFlow/ScenePortal.cs:29 | |
| **배경음** |||||||
| 65 | 타이틀 배경음 | 없음 | — | — | Assets/Scripts/Core/AudioManager.cs:231 | 씬 이름이 목록에 없어 정지 |
| 66 | 직업 선택 씬 배경음 | 없음 | — | — | Assets/Scripts/Core/AudioManager.cs:231 | |
| 67 | 트럭 배경음 | 출처 미확인 | cozy ambient.mp3 | 0.33 (−21.0, BGM −8) | Assets/Scripts/Core/AudioManager.cs:229 | 마을보다 RMS 12.5dB 작지만 귀로 맞춘 값이라는 주석(:62) 있음 |
| 68 | 마을 배경음 | 출처 미확인 | VillageScene.mp3 | 0.83 (−8.5, BGM −8) | Assets/Scripts/Core/AudioManager.cs:230 | |
| 69 | 보호소 배경음 | 출처 미확인 | VillageScene.mp3 (마을과 같음) | 0.83 | Assets/Scripts/Core/AudioManager.cs:230 | |

## 상태별 개수 (69개 순간)

| 상태 | 개수 |
|---|---|
| 정상 | 3 (11, 28, 30) |
| 없음 | 38 |
| 임시음 | 0 (합성음 코드는 있지만 모든 칸이 채워져 재생 안 됨) |
| 약함 | 6 (5, 8, 21, 31, 41, 54) |
| 출처 미확인 | 22 |

## 레서판다 울음

- 지금 파일: `Assets/Audio/animal_redpanda_barking.mp3` (GUID 67a55558d587a4c4683ad97fdb5cc905, 0.86s, 출처 미확인)
- 연결: `Assets/Prefabs/Animals/AnimalSoundFlee.prefab:167` (cryClip, cryVolume 1 :168) → 재생 `Assets/Scripts/Animals/AnimalSoundFlee.cs:16`
- 후보: `C:\Users\Main\Documents\A_Game\SoundCandidates\RedPanda\` (아래 CANDIDATES.md 참고)
