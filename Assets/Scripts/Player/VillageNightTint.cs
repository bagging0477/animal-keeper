using UnityEngine;

/// <summary>
/// 마을 어둠 색: 카메라 바로 앞에 화면을 덮는 사각형을 두고 지금 시간 구간의 색(GameBalanceConfig.villagePhaseDarknessColors)을
/// 화면 전체에 똑같이 더한다(셰이더 "Animal Keeper/Screen Add Color", 가산). 해질녘 보라 → 밤 남색 → … → 5구간 붉은색.
/// 시야 밖(빛을 못 받아 검은) 곳과 바닥이 같은 색이 되므로 몬스터·나무 실루엣이 생기지 않는다 - 시야 판정과 화면이 계속 일치한다.
/// VillageScene의 Main Camera에 붙인다. 정렬은 Default 레이어 맨 위라 월드 그림 위, 화면 UI(Overlay Canvas) 아래다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class VillageNightTint : MonoBehaviour
{
    private const int TintSortingOrder = 32767;

    [SerializeField] private GameBalanceConfig config;
    [Tooltip("Animal Keeper/Screen Add Color 셰이더 재질.")]
    [SerializeField] private Material additiveMaterial;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private Camera cam;
    private SpriteRenderer tint;
    private MaterialPropertyBlock block;
    private Color applied = new Color(-1f, -1f, -1f, -1f);

    /// <summary>지금 화면에 더하고 있는 색.</summary>
    public Color CurrentColor { get; private set; }
    public SpriteRenderer TintRenderer => tint;

    private GameBalanceConfig Config => GameManager.Instance != null && GameManager.Instance.Config != null ? GameManager.Instance.Config : config;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        var go = new GameObject("NightTint");
        go.transform.SetParent(transform, false);
        tint = go.AddComponent<SpriteRenderer>();
        tint.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width); // 1칸 크기
        tint.sprite.name = "NightTintSprite";
        tint.sharedMaterial = additiveMaterial;
        tint.sortingLayerID = SortingLayer.NameToID("Default");
        tint.sortingOrder = TintSortingOrder;
        block = new MaterialPropertyBlock();
    }

    private void OnDestroy()
    {
        if (tint != null && tint.sprite != null) Destroy(tint.sprite);
    }

    private void LateUpdate()
    {
        if (tint == null) return;
        // 화면을 넉넉히 덮도록 카메라 크기에 맞춘다(창 크기·비율이 바뀌어도 따라간다).
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        tint.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.5f);
        tint.transform.localScale = new Vector3(w * 1.1f, h * 1.1f, 1f);

        GameBalanceConfig c = Config;
        int phase = GameManager.Instance != null ? GameManager.Instance.VillagePhase : 1;
        Color color = c != null ? c.GetVillagePhaseDarknessColor(phase) : Color.black;
        CurrentColor = color;
        tint.enabled = additiveMaterial != null && (color.r > 0f || color.g > 0f || color.b > 0f);
        if (color == applied) return;
        tint.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        tint.SetPropertyBlock(block);
        applied = color;
    }
}
