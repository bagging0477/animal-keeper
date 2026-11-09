# 트럭 납품 직원 시안 (2026-11-09)

프로젝트 원본은 바꾸지 않았다. 그림은 Python으로 직접 찍었다(생성 스크립트는 세션 스크래치 폴더에 있다).
렌더는 batch Play 모드(타이틀 → 클래스 선택(스카우트) → 트럭)에서 PNG를 실행 중에 읽어 만든 임시 스프라이트로 했다. 임시 에디터 스크립트는 지웠다.

## 지금 납품 자리 구조
- TruckScene 루트의 `AnimalCage` 오브젝트: 위치 (2, -1), 배율 1. 구성은 SpriteRenderer + AnimalDeliveryPoint, 충돌체 없음.
  - 그림은 `Assets/Sprites/RescuePointSquare.png`(16x16 초록 네모, PPU 16 → 1x1칸), Sprite-Lit-Default, 정렬 1(플레이어·가구 2보다 아래).
- `AnimalDeliveryPoint.cs`: interactionRange 2.3. 플레이어와의 거리는 transform.position 기준이다. InteractionFocus로 가장 가까운 지점만 반응한다.
  - 선택한 숫자 칸이 동물일 때만 자막 "E를 눌러 동물 납품하기"(SubtitleManager, 화면 아래 가운데)를 띄운다.
  - E를 누르면 `GameManager.TryDeliverSelectedAnimal()`을 부른다. 동물 칸이 아니면 안내도 동작도 없다.
- 참고하는 곳: `Editor/InventorySlotTest.cs` 414행(FindAnyObjectByType<AnimalDeliveryPoint>), `SceneFlow/TruckPoint.cs` 주석.
- 크기 기준: 카메라 ortho 5.6, 1080p에서 1칸 ≈ 96px, 가구 1텍셀 ≈ 6px. 플레이어(스카우트) 1.25칸 ≈ 120px.

## 시안 (모두 32x32 텍셀, PPU 16 = 2x2칸, 외곽선 #2F2F2E, 알파 0/255만, 대기 3장 + 받기 3장)
| 시안 | 의도 | 그림 영역 | 색 |
|---|---|---|---|
| A 밤 접수원 | 후드를 쓴 밤 접수원이 작은 접수대 뒤에서 장부에 도장을 찍는다. 서랍장과 같은 나무색이라 트럭 안에 녹아든다. | 26x30 (≈1.6x1.9칸) | 9색 |
| B 떠돌이 상인 | 챙 넓은 모자 그늘에 두 점 눈만 보이는 상인과 철장. 다크큐트 쪽으로 가장 기울인 "수상하지만 친절한" 상인. | 29x28 (≈1.8x1.75칸) | 10색 |
| C 막내 수의사 | 플레이어처럼 동글동글한 몸의 막내 수의사, 반창고와 클립보드. 귀여움 쪽이고, 받으면 하트가 뜬다. | 25x27 (≈1.6x1.7칸) | 9색 |

- 대기: 1 기본, 2 숨쉬기(머리·몸 1텍셀 내려감), 3 눈 깜빡임.
- 받기: 1 손 내밂, 2 크게 내밂, 3 마무리(A 도장 쾅, B 케이지 문 들고 모자 살짝 들기, C 팔 벌리고 하트).
- 파일: `{시안}_idle_1~3.png`, `{시안}_take_1~3.png`, `{시안}_sheet.png`(6프레임 가로), `render/*.png`(실제 렌더 1080p), `compare_truck_clerk.png`(비교).
- 가져올 때 설정: Sprite(Multiple 또는 낱장), PPU 16, Pivot 아래 가운데, Filter Point, Compression None, Mip Map 끔.

## 적용할 때 바꿀 것 (정리만, 바꾸지 않음)
- **오브젝트**: `AnimalCage`를 `DeliveryClerk`로 바꾼다.
  - 루트는 (2, -1)에 그대로 두고 AnimalDeliveryPoint를 유지한다. 거리 기준점이 그대로라 interactionRange 2.3을 바꾸지 않아도 된다.
  - 자식 `Visual`에 SpriteRenderer(직원 그림, 아래 가운데 피벗, 로컬 (0, -0.6))와 SimpleFrameAnimator(대기 3장 반복)를 둔다.
  - 정렬은 2로 올리고 FootSortOrder를 붙여 플레이어 앞뒤가 발 기준으로 맞게 한다.
  - 선택: 접수대·철장에 BoxCollider2D(약 1.4x0.4칸, 발 높이)를 둬 플레이어가 통과하지 않게 한다. 지금 케이지는 충돌체가 없다.
- **상호작용 범위**: 2.3 그대로. 직원 그림이 케이지보다 0.6칸 아래·0.3칸 위로 넓어지지만, 플레이어가 직원 바로 옆(1.2~1.4칸)에 서면 범위 안이다(비교 이미지 3번).
- **받기 동작**: `TryDeliverSelectedAnimal()`이 성공하면 받기 3장(약 0.4초)을 한 번 재생하고 대기로 돌아간다(AnimalDeliveryPoint에 한 줄 + 애니메이터 호출).
- **안내 문구**: "E를 눌러 동물 납품하기"를 "E를 눌러 맡기기"로 바꾼다.
  - 위치: 지금처럼 화면 아래 가운데 자막(SubtitleManager, 인벤토리 바 위)을 유지한다. 대안은 직원 머리 위 월드 말풍선(그림 위 끝 + 0.3칸, 월드 y ≈ 0.3)이다.
  - 동물 칸을 안 골랐을 때도 "숫자키로 동물 칸을 고르세요"를 띄우는 것을 추천한다(지금은 아무것도 안 뜸).
- **튜토리얼 7단계 문구**(Logs/Tutorial/notes.md): "숫자로 동물 칸을 고르고, 초록 케이지 앞에서 E" → "**숫자**로 동물 칸을 고르고, 직원에게 **E**로 맡기기".
  - 시안 이미지 tutorial_02의 "맡기는 곳" 화살표 대상도 직원으로 바뀐다.
- **바뀌는 파일**: `Assets/Scenes/TruckScene.unity`, `Assets/Scripts/Animals/AnimalDeliveryPoint.cs`(문구·받기 재생), 새 `Assets/Sprites/Truck/Clerk/*.png`(+meta).
  - 테스트 `InventorySlotTest`는 컴포넌트로 찾으므로 그대로 동작한다(오브젝트 이름은 쓰지 않음).
