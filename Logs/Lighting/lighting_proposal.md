# 빛 효과 체계 시안 (조사 + 시안, 프로젝트 원본 변경 없음)

비교 이미지: `Logs/Lighting/lighting_proto_compare.png` (Field·숲 × 지금 / A / B, 확대 x4 포함)
시안 렌더 원본: `Logs/Lighting/proto_Room_*_{current,A,B}.png`, 기록: `proto_log.txt`
시안 코드(복사본에만): `C:\Users\Main\Documents\A_Game\animal-keeper_lightproto\Assets\LightProto\`

## 1. 지금 조명 구조
- 파이프라인: URP 2D (`Assets/Settings/UniversalRP.asset` → `Renderer2D.asset`). HDR 켜짐, MSAA 없음, 라이트 텍스처 0.5배, 블렌드 스타일 4개(Multiply 기본).
- 후처리: 모든 씬 카메라에서 꺼짐(`renderPostProcessing 0`), 씬에 Volume 없음. 전역 기본 프로필(`DefaultVolumeProfile.asset`)은 블룸 세기 0 등 모두 중립.
- Pixel Perfect Camera: 없음(Canvas의 pixelPerfect 플래그만). 그림은 Point 필터.
- 마을(VillageScene) Light2D 3개: `Global Light 2D` 세기 **0**(시야 밖은 완전히 검정), `AmbientVisionLight`(원형, 그림자 끔), `FocusedVisionLight`(부채꼴 손전등, 그림자 켬). 색은 모두 흰색, 세기는 `GameBalanceConfig.visionLightIntensity`(1).
- 마을 시간 구간별 밝기 변화: **없음**(구간은 소환·자막에만 쓰임). 다른 씬: 트럭 실내(따뜻한 전체 0.85 + 천장등), 클래스 선택(푸른 전체 0.25 + 스탠드등).
- 몬스터·동물·플레이어는 모두 Sprite-Lit 재질 → 빛이 없으면 검게 그려져 보이지 않는다. 장애물은 `ShadowCaster2D`(맵 생성 때 붙임)로 손전등 그림자를 만든다.

## 2. 시야 판정과 조명의 연결, 위험
- `PlayerVision.IsPointVisible`은 두 라이트의 실제 값(반경·각도·방향·켜짐·그림자 여부 + 벽 판정)을 읽는 **기하 계산**이다. 화면에 보이는 것 = 라이트가 비춘 곳이라 둘이 일치한다.
- 게임 쪽 사용처: 박쥐 굳음(`IsInFocusedVision`), 시간 소환 위치(`IsVisibleToPlayer`로 시야 밖만).
- 위험(시안에서 실제로 확인):
  1. 스스로 빛나는 부위(Unlit/가산)나 전체 조명을 몬스터 레이어에 주면 **시야 밖 몬스터가 보인다**.
  2. 바닥·벽 레이어만 밝히는 밤 조명: 몬스터·나무가 밝은 바닥 위 **검은 실루엣**으로 드러난다 → 시안에서 버림. 대신 화면 전체에 같은 남색을 더한다(보이는 곳/안 보이는 곳 차이가 없어 정보가 새지 않음).
  3. 발 위치로 판정하면 몸이 빛 경계에 걸친 몬스터(코뿔소)의 **눈만 어둠 속에 떠 보인다** → 눈 위치로 판정해야 한다.
  4. 발광을 별도 렌더러로 그리면 정렬이 어긋나 **나무 뒤 몬스터의 눈이 잎 위로 비친다** → 몸과 같은 재질 안에서(보조 텍스처) 그려야 한다.
  5. 블룸 기준 밝기가 낮으면 원형+부채꼴이 겹치는 플레이어 주변(밝기 최대 약 2)이 번진다 → 기준 밝기를 2.6 이상으로.

## 3. 빛 효과 규칙
| 무엇 | 빛 | 색 | 밝기 단계 | 블룸 |
|---|---|---|---|---|
| 손전등(부채꼴) | Light2D | 따뜻한 흰색 #FFDEA8 | L2 (1.0) | 안 번짐 |
| 원형 시야 | Light2D | 차가운 흰색 #DBEBFF | L2 (1.0) | 안 번짐 |
| 몬스터 눈·얼굴 | 발광 마스크 | 늑대 #9FE8FF, 소리반응형 #FF5A8A, 박쥐·호저·코뿔소 #FFC04D | L4 (HDR 3~5) | 번짐 |
| 트럭 헤드라이트 | 발광 마스크 + 바닥 전용 Light2D | #FFE9B0 | 램프 L4, 바닥 L1 | 램프만 |
| 지뢰·폭탄 버튼 | 발광 마스크(깜빡임) | 경고 빨강 #FF4B3A | L3 (HDR 2.8, 깜빡일 때만 넘김) | 살짝 |
| 시체 회수 표시 | 발광 마스크 | 회수 청록 #6FFFD2 | L3 | 살짝 |
| 어둠(배경) | 화면 전체 가산 | 남색 #050812 | L0 | - |
- 밝기 단계: L0 어둠(+0.02~0.07) / L1 은은함(≤0.6, 바닥 전용) / L2 조명(≤1.0, 겹치면 ≤2) / L3 표시(2.8) / L4 발광(3~5).
- 블룸: 기준 밝기(threshold) **2.6**, 세기 **1.8**, 퍼짐(scatter) 0.6, 고품질 필터 끔. L3·L4만 넘는다. 도트 자체는 Point 그대로 두고 번짐만 위에 더한다(확대 x4에서 경계 유지 확인).
- 마을 시간 구간별 어둠 색(화면 전체 가산, 몬스터 정보는 새지 않음): 1구간 해질녘 #0A0810(보라), 2구간 밤 #050812(남색), 3구간 깊은 밤 #03050C, 4구간 새벽 전 #020306(가장 어두움), 5구간 #0C0306(붉은 경고). 손전등 색은 구간과 무관하게 고정.

## 4. 시야 밖 몬스터 빛: 시안 A / B
- **A. 시야 밖에서는 빛도 안 보임**: 눈은 그 몬스터의 눈 위치가 시야 안일 때만 켠다. 지금 규칙(보이는 것 = 비춘 곳)을 그대로 지켜 난이도·박쥐 굳음·소환 규칙과 어긋나지 않는다. 대신 긴장감은 그대로이고, 빛이 "보이는 몬스터를 더 잘 보이게" 하는 역할만 한다.
- **B. 시야 밖에서는 눈만 희미하게(경고)**: 눈 위치가 시야 밖이면 LDR 0.55로 희미하게(블룸 없음). 등 뒤·벽 너머 몬스터의 방향과 종류(눈 색)를 알 수 있어 난이도가 내려가고, 박쥐(정면 시야에 들어오면 굳음)를 미리 찾아 돌아볼 수 있다. 소리반응형·늑대의 기습 공포가 줄어든다. 쓰려면 눈 밝기·거리 제한(예: 8칸 안만), 종류별 켜기(예: 박쥐만)로 조절하는 것을 권장한다.

## 5. 적용할 때 바꿀 것(정리만)
- 설정: 마을 카메라 후처리 켜기(`VillageScene` Main Camera `renderPostProcessing`), 마을 전용 Volume 프로필 새 파일(블룸·비네트), 손전등·원형 시야 색(`GameBalanceConfig`에 색 값 추가 → `PlayerVision`이 대입).
- 그림 발광 마스크: 몬스터 5종 프레임마다 눈 위치만 칠한 마스크 PNG(같은 크기). 색 하나로 자동 추출은 안 된다 - 늑대·소리반응형 시트는 압축되어 색이 바뀌고, 코뿔소 대기 프레임에는 눈 색 픽셀이 없고, 호저는 같은 색이 가시에도 있다. 스프라이트의 보조 텍스처(`_EmissionTex`)로 연결.
- 코드: 발광 셰이더(Sprite-Lit 기반 + 보조 텍스처 가산, 몸과 같은 정렬), `MonsterBrain`에 눈 위치 기준 시야 판정과 A/B 전환(`GameBalanceConfig`), 시간 구간별 어둠 색(`GameManager.OnVillagePhaseChanged` 구독), 트럭·지뢰·폭탄·시체 표시 마스크.
- 파일(예상): `Assets/Shaders/SpriteLitEmission.shader`, `Assets/Scripts/Common/EmissionPartVisibility.cs`, `Assets/Settings/VillageVolumeProfile.asset`, `Assets/Sprites/Monster/**/Emission/*.png`(+meta), `GameBalanceConfig.cs/.asset`, `PlayerVision.cs`, `VillageScene.unity`, 해당 프리팹 5개 + 트럭·지뢰·폭탄·시체 표시 프리팹.
- 확인할 테스트: PlayerVisionQueryTest, BatMonsterTest(굳음), VillageSpawnTest(시야 밖 소환), Monster Trace(행동 그대로인지).

## 6. 성능 영향(예상)
- 블룸: 1080p에서 GPU 약 0.3~0.6ms(다운샘플 6~7단계). 저사양은 반 해상도 시작(downscale Half)으로 절반.
- 발광 마스크: 몬스터당 텍스처 1장 더 샘플(같은 드로우), 추가 드로우 없음. 별도 렌더러 방식(시안)은 몬스터당 드로우 +1.
- 눈 위치 시야 판정: 몬스터당 프레임마다 각도·거리 계산 + 벽 레이캐스트 1회(지금 박쥐가 하는 것과 같음) - 18~60마리 기준 무시할 수준.
- 화면 전체 어둠 색: 전체 화면 가산 1회(아주 작음). 후처리 색보정으로 대신하면 추가 비용 없음.
