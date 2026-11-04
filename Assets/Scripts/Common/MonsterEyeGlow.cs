using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 몬스터 눈(발광 부위)을 빛나게 한다 - 빛 효과 규칙(Logs/Lighting/lighting_proposal.md)의 시안 A.
/// - 몸 그림(body)은 "Animal Keeper/Sprite-Lit-Emission" 재질을 쓴다. 이 컴포넌트가 프레임마다 지금 그림의 발광 마스크
///   (Resources/MonsterEmission/{maskFolder}/{원본 텍스처 이름}_emission, 원본과 같은 크기, 눈 픽셀만 알파 1)를 넣는다.
///   발광은 몸과 같은 그리기 안에서 더해지므로 정렬·가림(나무 잎 등)이 몸과 같다.
/// - 켜는 조건: 지금 프레임에 눈 픽셀이 있고, 살아 있고, **눈 위치**가 플레이어 시야 안(PlayerVision.IsVisibleToPlayer)일 때.
///   발 위치가 아니라 눈 위치로 보므로 몸이 빛 경계에 걸친 몬스터의 눈만 어둠 속에 떠 보이지 않는다. 시야 밖이면 눈도 꺼진다.
/// - 눈 픽셀이 없는 프레임(쓰러짐·피격·돌진 등)은 빛나지 않는다.
/// - 행동·판정에는 쓰지 않는다(그리기만 바꾼다). 마스크 텍스처는 읽기 가능(isReadable)이어야 눈 위치를 구한다(MonsterEmissionTextureImporter).
/// </summary>
[DefaultExecutionOrder(1000)] // 그림 프레임(애니메이터)과 시야 라이트 방향(PlayerVision.LateUpdate)이 정해진 뒤
[DisallowMultipleComponent]
public class MonsterEyeGlow : MonoBehaviour
{
    public const string MaskRoot = "MonsterEmission";

    [Tooltip("발광을 더할 몸 그림(재질은 Animal Keeper/Sprite-Lit-Emission).")]
    [SerializeField] private SpriteRenderer body;
    [Tooltip("Resources/MonsterEmission 아래 이 몬스터의 마스크 폴더 이름.")]
    [SerializeField] private string maskFolder;
    [Tooltip("눈 색(HDR 아님, 밝기는 glowIntensity로 곱한다).")]
    [SerializeField] private Color glowColor = Color.white;
    [Tooltip("눈 밝기 배율. 블룸 기준 밝기(2.6)를 넘어야 살짝 번진다 - 빛 효과 규칙의 L4(3~5).")]
    [SerializeField] private float glowIntensity = 5f;

    private static readonly int EmissionTexId = Shader.PropertyToID("_EmissionTex");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

    private struct EyeInfo
    {
        public bool HasEye;
        public Vector2 LocalCenter; // 스프라이트 로컬 좌표(뒤집기 전, 월드 단위)
    }

    private static readonly Dictionary<(string folder, Texture2D source), Texture2D> MaskCache = new Dictionary<(string, Texture2D), Texture2D>();
    private static readonly Dictionary<Texture2D, Color32[]> PixelCache = new Dictionary<Texture2D, Color32[]>();
    private static readonly Dictionary<(Texture2D mask, Sprite sprite), EyeInfo> EyeCache = new Dictionary<(Texture2D, Sprite), EyeInfo>();

    private MonsterHealth health;
    private MaterialPropertyBlock block;
    private Texture2D appliedMask;
    private float appliedIntensity = -1f;

    /// <summary>이번 프레임에 눈이 빛나고 있는지.</summary>
    public bool IsGlowing { get; private set; }
    /// <summary>지금 그림 프레임에 눈 픽셀이 있는지.</summary>
    public bool HasEyeThisFrame { get; private set; }
    /// <summary>지금 눈 가운데의 월드 위치(x, y). 눈 픽셀이 없는 프레임이면 마지막 값.</summary>
    public Vector2 EyeWorldPosition { get; private set; }
    public SpriteRenderer Body => body;
    public Color GlowColor => glowColor;
    public float GlowIntensity => glowIntensity;

    private void Awake()
    {
        health = GetComponent<MonsterHealth>();
        block = new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        IsGlowing = false;
        Apply(null, 0f);
    }

    private void LateUpdate()
    {
        if (body == null) return;
        Sprite sprite = body.sprite;
        Texture2D mask = sprite != null ? MaskFor(maskFolder, sprite.texture) : null;
        EyeInfo eye = mask != null ? EyeOf(mask, sprite) : default;
        HasEyeThisFrame = eye.HasEye;
        if (eye.HasEye)
        {
            Vector2 local = eye.LocalCenter;
            if (body.flipX) local.x = -local.x;
            if (body.flipY) local.y = -local.y;
            EyeWorldPosition = body.transform.TransformPoint(local);
        }
        bool alive = health == null || !health.IsDead;
        IsGlowing = eye.HasEye && alive && body.enabled && body.gameObject.activeInHierarchy && PlayerVision.IsVisibleToPlayer(EyeWorldPosition);
        Apply(mask, IsGlowing ? glowIntensity : 0f);
    }

    // 값이 바뀐 프레임에만 PropertyBlock을 다시 넣는다.
    private void Apply(Texture2D mask, float intensity)
    {
        if (body == null || block == null) return;
        if (mask == appliedMask && Mathf.Approximately(intensity, appliedIntensity)) return;
        body.GetPropertyBlock(block);
        block.SetTexture(EmissionTexId, mask != null ? mask : Texture2D.blackTexture);
        block.SetColor(EmissionColorId, glowColor);
        block.SetFloat(EmissionIntensityId, intensity);
        body.SetPropertyBlock(block);
        appliedMask = mask;
        appliedIntensity = intensity;
    }

    private static Texture2D MaskFor(string folder, Texture2D source)
    {
        if (source == null || string.IsNullOrEmpty(folder)) return null;
        if (!MaskCache.TryGetValue((folder, source), out Texture2D mask))
        {
            mask = Resources.Load<Texture2D>($"{MaskRoot}/{folder}/{source.name}_emission");
            MaskCache[(folder, source)] = mask;
        }
        return mask;
    }

    private static EyeInfo EyeOf(Texture2D mask, Sprite sprite)
    {
        if (EyeCache.TryGetValue((mask, sprite), out EyeInfo info)) return info;
        if (!PixelCache.TryGetValue(mask, out Color32[] pixels))
        {
            pixels = mask.isReadable ? mask.GetPixels32() : null;
            if (pixels == null) Debug.LogWarning($"[MonsterEyeGlow] 발광 마스크 {mask.name}를 읽을 수 없다(isReadable 꺼짐) - 눈 위치를 구하지 못해 빛나지 않는다.");
            PixelCache[mask] = pixels;
        }
        info = default;
        if (pixels != null)
        {
            Rect r = sprite.rect;
            int x0 = Mathf.RoundToInt(r.x), y0 = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            Vector2 sum = Vector2.zero;
            int count = 0;
            for (int y = y0; y < y0 + h && y < mask.height; y++)
                for (int x = x0; x < x0 + w && x < mask.width; x++)
                {
                    if (pixels[y * mask.width + x].a < 128) continue;
                    sum += new Vector2(x + 0.5f, y + 0.5f);
                    count++;
                }
            if (count > 0)
            {
                Vector2 centerPx = sum / count - new Vector2(x0, y0) - sprite.pivot;
                info = new EyeInfo { HasEye = true, LocalCenter = centerPx / sprite.pixelsPerUnit };
            }
        }
        EyeCache[(mask, sprite)] = info;
        return info;
    }
}
