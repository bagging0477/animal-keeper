# Animal Keeper 손맛(타격감) 조사와 시안

작성 2026-11-17. 코드·에셋은 바꾸지 않았다. 아래 내용은 코드와 프리팹·씬 값을 읽고 정리한 것이다.
기준 화면: 카메라 직교 크기 5.6(세로 11.2칸). 1920x1080에서 1칸은 약 96px이다.

## 1. 지금 순간마다 있는 반응

표기: ○ 있음, △ 일부 대상만 있음, ✕ 없음.

| 순간 | 깜빡임 | 피격/쓰러짐 그림 | 소리 | 파티클·그림 효과 | 화면 흔들림 | 히트스톱 | 근거 |
|---|---|---|---|---|---|---|---|
| 마취총 다트가 몬스터에 맞음 | △ 빨강 #FF4040 0.15초. 늑대는 깜빡임만, 박쥐·호저·코뿔소는 그림+깜빡임, 소리반응형은 그림만(4장) | △ (위와 같음) | ✕ 맞을 때 소리 없음. 쏠 때만 `air gun shot` | ✕ 다트가 사라지기만 함 | ✕ | ✕ | `TranquilizerDart.Update` → `MonsterHealth.TakeDamage` → `SimpleFrameAnimator.PlayHurt`. 1초 뒤 수면(`PutToSleep`) |
| 다트가 벽에 맞음 | ✕ | ✕ | ✕ | ✕ 그 자리에서 사라짐 | ✕ | ✕ | `TranquilizerDart.Update` (`StaticBlockerLine`) |
| 폭탄이 터짐 | 범위 안 몬스터는 위의 피격 깜빡임/그림 | △ | ✕ 폭발음 없음 | ○ 화염 그림 0.45초(커지며 흔들리다 흐려짐, unscaled 시간). 화염 그림이 없으면 반투명 빨간 원 | ✕ | ✕ | `PlayerGadgetController.UseBomb`, `BombExplosionEffect`, `FireBurstEffect2D` |
| 지뢰가 터짐(참고) | 밟은 몬스터 피격 깜빡임/그림 | △ | ✕ | ○ 빨간 섬광 0.25초(커지며 흐려짐) | ✕ | ✕ | `MineTrap.Detonate/ShowFlash` |
| 그물이 맞음 | 맞은 몬스터 피격 깜빡임/그림. 거위는 쓰러지는 동작 | △ | △ 휘두를 때 `whoosh swing`(맞든 안 맞든 같음). 맞을 때 몬스터 소리 없음. 거위만 `animal_goose_hit` | ○ 그물 펼침 그림 6장 0.30초(휘두를 때) | ✕ | ✕ | `PlayerWeaponController.SwingNet`, `NetSwingEffect` |
| 몬스터가 쓰러짐 | ✕ (깜빡임을 끊고 회색 #595959로 바뀜) | ○ 쓰러짐 그림 2~4장(늑대 3, 박쥐·호저·코뿔소 2, 소리반응형 4) 뒤 시체로 남음 | ✕ | ✕ | ✕ | ✕ | `MonsterHealth.Die` → `PlayDeath`, `deadColor` |
| 플레이어가 맞음 | ✕ 플레이어 그림 변화 없음 | ✕ (몬스터는 공격 그림 `PlayRandomAttack`) | ○ `damage grunt` | ✕ | ✕ | ✕ | `GameManager.TakeDamage` → `PlayHit`. HUD 체력바는 부드럽게 줄어듦. 0이면 바로 트럭으로 |
| 코뿔소가 벽에 부딪힘 | ✕ | △ 돌진을 멈추고 기절 그림(`knockedOutSprite`) 2초 | ✕ | ✕ | ✕ | ✕ | `RhinoMonsterAI.UpdateDash` → `EndDash(Wall)` + `Stun(rhinoWallStunDuration)` |

요약: 지금 맞는 순간의 반응은 **몬스터 쪽 빨간 깜빡임(0.15초)과 피격 그림**이 거의 전부다. **맞았다는 소리**는 플레이어 피격(`damage grunt`)과 거위(`animal_goose_hit`)뿐이다. **화면 흔들림·히트스톱·파티클은 하나도 없다**(폭탄·지뢰의 그림 효과는 있음).

## 2. 지금 쓰는 효과음 연결

| 파일 (Assets/Audio) | 연결 위치 | 울리는 순간 |
|---|---|---|
| `air gun shot.mp3` | AudioManager.tranquilizerShotClip (볼륨 0.9) | 마취총을 쏠 때 (`PlayerWeaponController.FireTranquilizerGun`) |
| `whoosh swing.mp3` | AudioManager.netSwingClip (0.48) | 그물을 휘두를 때 (`SwingNet`, 맞든 안 맞든) |
| `damage grunt.mp3` | AudioManager.hitClip (1.0) | 플레이어가 맞을 때 (`GameManager.TakeDamage`: 늑대·소리반응형·박쥐 물기, 호저 가시, 코뿔소 돌진) |
| `alert stinger.mp3` | AudioManager.monsterChaseClip (0.56) | 몬스터가 추격을 시작할 때 (`MonsterBrain` → `SetChasing`, 몬스터마다 겹침) |
| `success chime.mp3` / `fail buzzer.mp3` | daySuccessClip / dayFailClip (UI) | 하루 성공 / 실패 |
| `cozy ambient.mp3` / `VillageScene.mp3` | truckMusicClip / villageMusicClip | 트럭·보호소 / 마을 배경음 |
| `animal_goose_hit.mp3` | AnimalGoose.hitSound | 거위가 다트·폭탄·그물에 맞아 쓰러지기 시작할 때 |
| `animal_goose_fly.mp3` | AnimalGoose.flySound (0~0.8초 구간) | 거위가 도망치기 시작할 때 |
| `animal_turtle_hide and emerge.wav` | AnimalTurtle | 거북이가 숨거나 나올 때 |
| `animal_*_pickup` (고양이·레서판다·거북이·웜뱃) | 각 동물 프리팹 pickupSound | 동물을 주울 때 |
| `animal_redpanda_barking.mp3` | AnimalSoundFlee.cryClip | 레서판다가 울 때 |
| (없음) pickupClip | 비어 있어 코드가 만든 임시 비프음 | 개별 줍는 소리가 없는 것을 주울 때 |
| (없음) 박쥐 warningClip, 늑대 chaseClip | 비어 있음 | 박쥐 경고음 없음, 늑대는 기본 추격음 |

- AudioManager는 TitleScene·TruckScene에 있고 두 씬의 연결은 같다. 효과음은 모두 sfxSource 하나로 `PlayOneShot`처럼 겹쳐 재생된다(앞 무음은 잘라서 재생).
- `CREDITS.md`에 "효과음·배경음 16개 — 출처 확인 필요"로 적혀 있다. 새 효과음을 CC0로 넣으면 출처 문제도 일부 줄어든다.

## 3. 시안: 순간마다 넣을 반응

### 화면 흔들림 두 단계(약·중)
흔들림은 카메라 위치에 더하는 작은 오프셋이다. 방향은 무작위이고, 시간에 따라 선형으로 줄어든다.

| 단계 | 세기(칸) | 화면 px(1080p) | 시간 | 진동 |
|---|---|---|---|---|
| 약 | 0.04 | 약 4px | 0.10초 | 30Hz |
| 중 | 0.09 | 약 9px | 0.22초 | 25Hz |

- 같은 프레임에 여러 번 걸리면 큰 쪽만 쓴다(더하지 않는다). 최대 0.12칸.
- 설정 메뉴에 "화면 흔들림 끄기/약하게"를 두는 것을 권한다(멀미 대응).

### 순간별 시안

| 순간 | 화면 흔들림 | 히트스톱 | 파티클 | 피격음 |
|---|---|---|---|---|
| 다트가 몬스터에 맞음 | 없음(연사·원거리라 피로함). 원하면 약 | 0.04초, 맞은 몬스터만 | 맞은 자리에 흰 점 3~4개(0.2초, 0.3칸 퍼짐) | 짧고 가벼운 "퍽"(작은 충격음, 0.1초 안팎) |
| 다트가 벽에 맞음 | 없음 | 없음 | 먼지 2~3개(회색 #77736B, 0.15초) | 아주 작은 "틱"(볼륨 0.3) |
| 폭탄 | **중** | 0.06초, 범위 안 몬스터만 | 연기 8~12개(크림 그림자 #BBAE9A → 투명, 0.5초) + 파편 4~6개 | 낮은 "쿵" 폭발음(0.4~0.6초) |
| 지뢰(참고) | 약 | 0.05초 | 연기 5~6개 | 폭탄보다 작은 폭발음 |
| 그물이 맞음 | 약(맞았을 때만) | 0.05초, 맞은 몬스터만 | 풀잎·털 3~4개(0.2초) | 천·밧줄 "툭". 기존 `whoosh swing`은 그대로 두고 맞을 때만 더한다 |
| 몬스터가 쓰러짐 | 약 | 0.08초(마지막 타격과 겹치면 긴 쪽 하나) | 흙먼지 6개(발 아래, 0.4초) | 둔탁한 "털썩" |
| 플레이어가 맞음 | **중** | 0.05초(전체, 아래 주의 참고) | 없음. 대신 플레이어 그림 빨강 깜빡임 0.15초(몬스터와 같은 #FF4040), 화면 가장자리 붉은빛 0.2초 | 기존 `damage grunt` 유지 + 짧은 타격음을 겹침 |
| 코뿔소가 벽에 부딪힘 | 중(시야 안일 때만), 시야 밖이면 없음 | 0.06초, 코뿔소만 | 먼지·돌 조각 6~8개(부딪힌 벽 쪽) | 무거운 "쾅"(낮은 충격음) |

- **히트스톱은 "맞은 대상만 멈춤"을 기본으로 한다.** 그 대상의 프레임 애니메이션과 그림 위치만 unscaled 시간으로 잠깐 멈추고, `Time.timeScale`은 건드리지 않는다(아래 4번 이유).
- 피격 깜빡임은 지금의 빨강 0.15초를 유지한다. 손맛을 더하려면 맞은 첫 0.05초만 흰색 → 그 뒤 빨강으로 바꾸는 방식을 권한다(흰 깜빡임은 어두운 밤에도 잘 보인다).

## 4. 새 효과음 CC0 후보 (다운로드·추가는 하지 않음)

| 사이트 | 팩/검색 | 쓸 곳 | 라이선스 확인 |
|---|---|---|---|
| Kenney (kenney.nl) | Impact Sounds (130개, 태그 impact·foley) | 다트 명중 "퍽", 그물 "툭", 쓰러짐 "털썩", 코뿔소 "쾅", 다트 벽 "틱" | 페이지에 CC0 표기 확인 (https://kenney.nl/assets/impact-sounds) |
| Kenney | RPG Audio (50개, 태그 foley·weapon·footstep) | 그물(천·가죽 소리), 플레이어 피격 타격음 | CC0 확인 (https://kenney.nl/assets/rpg-audio) |
| Kenney | Sci-fi Sounds (70개) | 폭발음 후보. 팩 안에 쓸 만한 폭발음이 있는지 들어 보고 정한다 | CC0 확인 (https://kenney.nl/assets/sci-fi-sounds) |
| Kenney | Interface Sounds (100개) | (필요하면) 탄약 없음·줍기 비프음 대체 | CC0 확인 (https://kenney.nl/assets/interface-sounds) |
| Freesound (freesound.org) | 라이선스 필터 "Creative Commons 0"로 "explosion", "body fall" 검색 | 폭발음·쓰러짐을 Kenney에서 못 찾았을 때 | 파일마다 라이선스가 다르다. CC0인 파일만, 페이지 주소를 CREDITS에 적는다 |
| OpenGameArt (opengameart.org) | 라이선스 필터 CC0 | 위와 같음 | 파일마다 확인 |

- Kenney 네 팩은 이번에 페이지에서 CC0 표기를 확인했다. 개별 파일 이름은 페이지에 없어서, 실제 고를 때 들어 보고 정한다.

## 5. 시야 판정(화면 = 시야)과 기존 테스트에 영향을 줄 수 있는 부분

1. **파티클·흰 깜빡임은 시야 밖에서 보이면 안 된다.** LightingTest(시야 밖 몬스터 "있을 때·없을 때 화면 차이 ≤3")와 DuskTest(반경 밖은 같은 그림)가 바로 실패한다. 파티클은 몬스터 몸과 같은 Lit 재질(빛을 받아야 보임)로 그리거나, `PlayerVision.IsVisibleToPlayer(위치)`일 때만 만든다. 가산(Additive)·Unlit 재질은 쓰지 않는다. 눈 발광 재질(`Sprite-Lit-Emission`)처럼 어둠에서 빛나는 효과도 시야 밖에서는 끈다.
2. **화면 흔들림은 시야 안에서 일어난 일에만 건다.** 시야 밖 코뿔소가 벽에 부딪혀 화면이 흔들리면 보이지 않는 정보를 준다. 흔들림은 `CameraFollow`의 마지막 위치에 더하는 오프셋으로 넣는다. DuskTest·LightingTest·PorcupineTest는 `CameraFollow`를 끄고 카메라를 직접 놓으므로 영향이 없다. 반면 PlayerStatusHudTest·TutorialGuideTest 등 카메라가 따라가는 상태로 캡처하는 테스트는 흔들림이 남은 프레임을 찍을 수 있다. GameBalanceConfig에 전체 끄기 값(예: 흔들림 배율 0)을 두고 테스트에서 끈다.
3. **히트스톱을 `Time.timeScale`로 하면 위험하다.** 마을 시간(`VillageTime`), 몬스터 AI·NavMesh, 코뿔소 돌진 시간(RhinoTest), 그물 그림 시간(NetSwingTest)이 함께 멈춘다. 일시정지·상점·트럭 메뉴·하루 정산(`TruckHubPoint`, `ShopMenuUI`, `DayClearPanel`)과 여러 테스트(DuskTest·VillageTimeTest 등)가 `timeScale`을 직접 0/1로 바꾸므로, 히트스톱이 끝나며 1로 되돌리면 메뉴의 멈춤을 풀어 버릴 수 있다. 그래서 "맞은 대상만 멈춤"을 권한다. 전체 멈춤이 꼭 필요하면 `timeScale`을 한 곳에서만 관리하고, 메뉴가 연 멈춤을 덮어쓰지 않게 한다.
4. **Monster Trace 기준 기록이 바뀔 수 있다.** MonsterTraceTest는 `MonsterHealth.TakeDamage`·`Stun`을 직접 부르고 다트 프리팹도 실제로 쏜다. 히트스톱이나 반응을 `MonsterHealth`·`TranquilizerDart` 안에서 몬스터 이동·타이머에 영향을 주게 넣으면 기록이 달라진다. 반응은 "그림·소리만 바꾸는 별도 컴포넌트"(예: 피격 이벤트를 받는 HitFeel)로 두고, 몬스터의 이동·체력·기절 타이머는 건드리지 않는다.
5. **새 소리에 `SoundEvents.Emit`을 붙이지 않는다.** 소리반응형 몬스터가 그 소리에 반응해 AI가 바뀐다(Monster Trace·MonsterPacingTest 영향). 폭발음을 몬스터가 듣게 하려면 게임 설계 변경으로 따로 정한다. 또 시야 밖에서 일어난 타격음(예: 멀리 있는 코뿔소의 "쾅")이 들리면 정보를 준다. 거리 감쇠를 두거나 시야 안일 때만 울리는 것을 권한다.
6. **깜빡임 색은 `SimpleFrameAnimator`가 매 프레임 `spriteRenderer.color`를 덮어쓴다.** 흰 깜빡임을 넣을 때도 같은 곳(`UpdateHurtFlash`)에서 처리해야 쓰러짐 회색(`deadColor`)이나 장애물 가림 반투명과 부딪히지 않는다. BatMonsterTest·PorcupineTest·RhinoTest는 피격 그림 재생 여부(`IsPlayingHurt`)를 보므로 피격 그림 시간을 바꾸면 확인이 필요하다.
7. **파티클 오브젝트에 `FootSortOrder`를 붙이지 않는다.** 붙이면 `ObstacleOccluderFade`가 가림 대상으로 보고 나무를 흐리게 할 수 있다. 정렬은 폭탄처럼 `FootSortOrder.EffectOrder`를 쓴다.
8. **플레이어 무적 시간(맞은 뒤 잠깐)은 넣으면 손맛이 좋아지지만 밸런스가 바뀐다.** 연속 피격 데미지가 줄어 피격 횟수·체력을 보는 테스트(MonsterPacingTest 등)를 다시 확인해야 한다. 이번 시안에서는 제안만 하고 기본값은 끈다.
