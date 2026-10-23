using UnityEngine;

/// <summary>발 높이(Y)로 같은 정렬 레이어 안의 앞뒤 순서(sortingOrder)를 정한다. 발이 아래(화면 앞)일수록 Order가 크다.
/// 그림·피벗·위치·충돌체는 건드리지 않고 Order만 바꾼다. 같은 Order끼리는 기존처럼 2D 렌더러의 Y축 정렬(그림 가운데)이 가른다.
/// - 캐릭터(동물·몬스터·플레이어): 매 프레임(LateUpdate - 몬스터 Visual이 NavMesh 위치로 옮겨진 뒤) 다시 계산한다.
/// - 움직이지 않는 것(소품·트럭·설치한 지뢰): 처음 한 번만 계산한다(isStatic, 맵 소품은 ApplyStatic).
/// 발 위치는 CharacterShadowBuilder에 재어 둔 값(몸 피벗 기준 텍셀)을 쓴다. 그림자(자식 "Shadow" 또는 지정한 렌더러)는 몸보다 1 낮게 따라간다.
/// Order = -2 x (발 Y를 0.02칸 단위로 반올림): 그림자 칸(-1)이 다른 물체의 Order와 겹치지 않도록 2씩 띄운다. 발 Y ±320칸까지 쓸 수 있다.</summary>
[DisallowMultipleComponent]
public class FootSortOrder : MonoBehaviour
{
    public const int StepsPerUnit = 50;
    public const int MaxOrder = 32000;
    /// <summary>폭발·지뢰 섬광처럼 모든 세계 그림 위에 보여야 하는 효과의 Order(발 정렬 범위 밖).</summary>
    public const int EffectOrder = 32700;

    [Tooltip("정렬할 몸 렌더러. 비우면 같은 오브젝트의 SpriteRenderer.")]
    [SerializeField] private SpriteRenderer body;
    [Tooltip("몸 피벗 기준 발(보이는 맨 아랫줄의 아래 변) 높이, 몸 텍셀 단위. CharacterShadowBuilder의 발 값과 같다.")]
    [SerializeField] private float footTexels;
    [Tooltip("footTexels의 텍셀 크기(PPU). 월드 높이 = footTexels / texelsPerUnit x 몸 배율.")]
    [SerializeField] private float texelsPerUnit = 32f;
    [Tooltip("켜면 footTexels 대신 그림 아래 끝(렌더러 bounds 바닥)을 발로 쓴다.")]
    [SerializeField] private bool useBoundsBottom;
    [Tooltip("켜면 world 오프셋(footWorldOffset, 몸 위치 기준 월드 칸)을 쓴다. 회전하는 플레이어처럼 배율·회전과 무관한 발 높이용.")]
    [SerializeField] private bool useWorldOffset;
    [SerializeField] private float footWorldOffset;
    [Tooltip("움직이지 않으면 켠다 - 처음 한 번만 계산하고 멈춘다.")]
    [SerializeField] private bool isStatic;
    [Tooltip("몸보다 1 낮게 따라갈 그림자. 비우면 몸 아래 자식 \"Shadow\".")]
    [SerializeField] private SpriteRenderer shadow;

    /// <summary>발 Y(월드)에 해당하는 Order.</summary>
    public static int OrderForFootY(float footY) => Mathf.Clamp(-2 * Mathf.RoundToInt(footY * StepsPerUnit), -MaxOrder, MaxOrder);

    /// <summary>지금 몸의 발 Y(월드).</summary>
    public float FootY
    {
        get
        {
            if (body == null) return transform.position.y;
            if (useBoundsBottom) return body.bounds.min.y;
            Transform t = body.transform;
            if (useWorldOffset) return t.position.y + footWorldOffset;
            return t.position.y + footTexels / Mathf.Max(0.0001f, texelsPerUnit) * t.lossyScale.y;
        }
    }

    private void Awake() => Resolve();

    private void Resolve()
    {
        if (body == null) body = GetComponent<SpriteRenderer>();
        if (shadow == null && body != null)
        {
            Transform s = body.transform.Find("Shadow");
            if (s != null) shadow = s.GetComponent<SpriteRenderer>();
        }
    }

    private void Start()
    {
        Apply();
        if (isStatic) enabled = false;
    }

    private void LateUpdate() => Apply();

    /// <summary>지금 발 위치로 몸·그림자 Order를 맞춘다.</summary>
    public void Apply()
    {
        if (body == null) return;
        int order = OrderForFootY(FootY);
        body.sortingOrder = order;
        if (shadow != null) shadow.sortingOrder = order - 1;
    }

    /// <summary>실행 중에 붙인다(플레이어: 씬 파일을 바꾸지 않고 맵 생성기가 붙인다). 이미 있으면 그것을 쓴다.</summary>
    public static FootSortOrder AttachWorldOffset(GameObject go, SpriteRenderer body, float footWorldOffset, SpriteRenderer shadow)
    {
        FootSortOrder f = go.GetComponent<FootSortOrder>();
        if (f == null) f = go.AddComponent<FootSortOrder>();
        f.body = body; f.useWorldOffset = true; f.footWorldOffset = footWorldOffset; f.shadow = shadow; f.useBoundsBottom = false; f.isStatic = false;
        f.Apply();
        return f;
    }

    /// <summary>실행 중에 붙인다: 그림 아래 끝을 발로 쓰는 물체(설치한 지뢰 등).</summary>
    public static FootSortOrder AttachBoundsBottom(GameObject go, SpriteRenderer body, bool isStatic)
    {
        FootSortOrder f = go.GetComponent<FootSortOrder>();
        if (f == null) f = go.AddComponent<FootSortOrder>();
        f.body = body; f.useBoundsBottom = true; f.isStatic = isStatic; f.useWorldOffset = false;
        f.Resolve();
        f.Apply();
        return f;
    }

    /// <summary>움직이지 않는 소품: root 아래 Default 정렬 레이어의 SpriteRenderer마다 그림 아래 끝(발)으로 Order를 한 번 정한다.
    /// FootSortOrder가 붙은 캐릭터(와 그 그림자)는 건너뛴다. 정한 렌더러 수를 돌려준다.</summary>
    public static int ApplyStatic(GameObject root)
    {
        if (root == null) return 0;
        int defaultLayer = SortingLayer.NameToID("Default");
        int count = 0;
        foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r.sortingLayerID != defaultLayer || r.sprite == null) continue;
            if (r.GetComponentInParent<FootSortOrder>(true) != null) continue;
            r.sortingOrder = OrderForFootY(r.bounds.min.y);
            count++;
        }
        return count;
    }
}
