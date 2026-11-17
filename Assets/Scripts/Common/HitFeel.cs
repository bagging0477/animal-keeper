using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타격 순간의 손맛(화면 흔들림·파티클·피격음·플레이어 피격 깜빡임)을 한곳에서 낸다. 값은 GameBalanceConfig "손맛".
/// - 그림·소리만 바꾼다. 몬스터 이동·AI·체력·기절 타이머와 Time.timeScale은 건드리지 않는다.
/// - 무작위 값은 자기 System.Random만 쓴다 - UnityEngine.Random을 쓰면 몬스터 AI의 난수 순서가 바뀐다.
/// - 소리는 AudioManager로만 낸다. SoundEvents.Emit에 연결하지 않는다(소리반응형 몬스터가 반응하지 않게).
/// - 파티클은 빛을 받는 재질(hitParticleMaterial)로 그려서 시야 밖에서는 보이지 않는다.
/// - 히트스톱(맞은 대상의 그림만 잠깐 멈춤)은 SimpleFrameAnimator.HitStop / PlayerHitFeel이 한다.
/// </summary>
public static class HitFeel
{
    public enum Shake { Weak, Medium }

    private static readonly System.Random rng = new System.Random(20261117);

    private static GameBalanceConfig Config => GameManager.Instance != null ? GameManager.Instance.Config : null;

    // ===================== 순간별 =====================

    /// <summary>마취총 다트가 몬스터에 맞음: 흰 점 4개 + 피격음. 흔들림 없음.</summary>
    public static void DartHit(Vector2 point, MonsterHealth monster)
    {
        AudioManager.Instance?.PlayDartHit();
        SpriteRenderer body = BodyOf(monster);
        Burst(point, body, Particle.Dart);
    }

    /// <summary>포획망이 몬스터 하나 이상에 맞음(한 번 휘두를 때 한 번): 피격음 + 흔들림 약. 파티클은 맞은 몬스터마다 NetHitParticles.</summary>
    public static void NetHit()
    {
        AudioManager.Instance?.PlayNetHit();
        ShakeCamera(Shake.Weak);
    }

    public static void NetHitParticles(MonsterHealth monster)
    {
        SpriteRenderer body = BodyOf(monster);
        if (body != null) Burst(body.bounds.center, body, Particle.Net);
    }

    /// <summary>폭탄 폭발: 폭발음 + 연기·파편 + 시야 안이면 흔들림 중.</summary>
    public static void BombExplosion(Vector2 origin)
    {
        AudioManager.Instance?.PlayBombExplosion();
        Burst(origin, null, Particle.Bomb);
        if (PlayerVision.IsVisibleToPlayer(origin)) ShakeCamera(Shake.Medium);
    }

    /// <summary>몬스터가 쓰러짐: 둔탁한 소리 + 발밑 흙먼지 + 흔들림 약.</summary>
    public static void MonsterDown(MonsterHealth monster)
    {
        AudioManager.Instance?.PlayMonsterDown();
        SpriteRenderer body = BodyOf(monster);
        if (body != null) Burst(new Vector2(body.bounds.center.x, body.bounds.min.y + 0.1f), body, Particle.Death);
        ShakeCamera(Shake.Weak);
    }

    /// <summary>코뿔소가 돌진 중 벽에 부딪힘: 충돌음 + 부딪힌 쪽 먼지·돌 조각 + 시야 안이면 흔들림 중.</summary>
    public static void RhinoWall(SpriteRenderer body, Vector2 gamePosition, Vector2 dashDirection)
    {
        AudioManager.Instance?.PlayRhinoWall();
        if (body != null)
        {
            Bounds b = body.bounds;
            Vector2 front = (Vector2)b.center + new Vector2(dashDirection.x * b.extents.x, dashDirection.y * b.extents.y * 0.6f);
            // 부딪힌 벽·장애물 그림 위로 튀어야 하므로 폭탄처럼 모든 세계 그림 위(EffectOrder)에 그린다.
            Burst(front, body, Particle.RhinoWall, -dashDirection, FootSortOrder.EffectOrder);
        }
        if (PlayerVision.IsVisibleToPlayer(gamePosition)) ShakeCamera(Shake.Medium);
    }

    /// <summary>플레이어가 맞음: 흔들림 중 + 몸 빨강 깜빡임·그림 멈춤(PlayerHitFeel). 소리는 GameManager가 원래대로 낸다.</summary>
    public static void PlayerHit()
    {
        ShakeCamera(Shake.Medium);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || player.GetComponent<SpriteRenderer>() == null) return;
        PlayerHitFeel feel = player.GetComponent<PlayerHitFeel>();
        if (feel == null) feel = player.AddComponent<PlayerHitFeel>();
        GameBalanceConfig c = Config;
        feel.Play(c != null ? c.playerHurtFlashColor : new Color(1f, 0.25f, 0.25f, 1f),
                  c != null ? c.playerHurtFlashDuration : 0.15f,
                  c != null ? c.hitStopDuration : 0.05f);
    }

    /// <summary>맞은 대상의 그림을 멈출 시간(초).</summary>
    public static float HitStopDuration => Config != null ? Config.hitStopDuration : 0.05f;

    // ===================== 화면 흔들림 =====================

    public static void ShakeCamera(Shake level)
    {
        GameBalanceConfig c = Config;
        if (c == null || !c.screenShakeEnabled) return;
        Camera cam = Camera.main;
        CameraFollow follow = cam != null ? cam.GetComponent<CameraFollow>() : null;
        if (follow == null || !follow.isActiveAndEnabled) return;
        if (level == Shake.Medium) follow.AddShake(c.shakeMediumAmplitude, c.shakeMediumDuration, c.shakeMediumFrequency);
        else follow.AddShake(c.shakeWeakAmplitude, c.shakeWeakDuration, c.shakeWeakFrequency);
    }

    /// <summary>흔들림 방향용 [0, 1) 난수(UnityEngine.Random을 쓰지 않는다).</summary>
    internal static float Next01() => (float)rng.NextDouble();

    // ===================== 파티클 =====================

    internal enum Particle { Dart, Net, Bomb, Death, RhinoWall }

    // 낮은 채도 팔레트(Logs/ColorUnify/notes.md 공통 팔레트).
    private static readonly Color Cream = Hex(0xEDE3D3), CreamShadow = Hex(0xBBAE9A), Stone = Hex(0x77736B),
        FadedWood = Hex(0x8A7462), Wood = Hex(0x5C4B48), LightGrass = Hex(0x8E956A), Grass = Hex(0x6B7650);

    private struct Spec
    {
        public int Count; public Color[] Colors; public float SpeedMin, SpeedMax, Life, Gravity, BigChance, Spread;
    }

    private static Spec SpecOf(Particle kind)
    {
        switch (kind)
        {
            case Particle.Dart: return new Spec { Count = 4, Colors = new[] { Cream, CreamShadow }, SpeedMin = 1.2f, SpeedMax = 2.2f, Life = 0.2f, Gravity = 0f, BigChance = 0f, Spread = 360f };
            case Particle.Net: return new Spec { Count = 4, Colors = new[] { LightGrass, Grass, CreamShadow }, SpeedMin = 1.2f, SpeedMax = 2.2f, Life = 0.25f, Gravity = -4f, BigChance = 0f, Spread = 360f };
            case Particle.Bomb: return new Spec { Count = 12, Colors = new[] { CreamShadow, Stone, FadedWood }, SpeedMin = 2f, SpeedMax = 4f, Life = 0.5f, Gravity = 1.5f, BigChance = 0.4f, Spread = 360f };
            case Particle.Death: return new Spec { Count = 6, Colors = new[] { FadedWood, Stone }, SpeedMin = 0.8f, SpeedMax = 1.6f, Life = 0.4f, Gravity = 0.6f, BigChance = 0.3f, Spread = 360f };
            default: return new Spec { Count = 8, Colors = new[] { Stone, Wood, CreamShadow }, SpeedMin = 1.5f, SpeedMax = 3f, Life = 0.35f, Gravity = -5f, BigChance = 0.3f, Spread = 120f };
        }
    }

    /// <summary>point에 작은 도트 파티클을 뿌린다. anchor가 있으면 그 그림 바로 위(같은 정렬 레이어, Order + 1)에, 없으면 폭탄처럼 모든 세계 그림 위에 그린다. orderOverride가 있으면 그 Order를 쓴다.</summary>
    private static void Burst(Vector2 point, SpriteRenderer anchor, Particle kind, Vector2? direction = null, int? orderOverride = null)
    {
        GameBalanceConfig c = Config;
        if (c == null || c.hitParticleMaterial == null) return;
        Spec s = SpecOf(kind);
        var go = new GameObject("HitParticles_" + kind);
        go.transform.position = new Vector3(point.x, point.y, 0f);
        var burst = go.AddComponent<HitParticleBurst>();
        int layer = anchor != null ? anchor.sortingLayerID : 0;
        int order = orderOverride ?? (anchor != null ? anchor.sortingOrder + 1 : FootSortOrder.EffectOrder);
        float baseAngle = direction.HasValue ? Mathf.Atan2(direction.Value.y, direction.Value.x) * Mathf.Rad2Deg : 0f;
        for (int i = 0; i < s.Count; i++)
        {
            float angle = (s.Spread >= 360f ? Next01() * 360f : baseAngle + (Next01() - 0.5f) * s.Spread) * Mathf.Deg2Rad;
            float speed = Mathf.Lerp(s.SpeedMin, s.SpeedMax, Next01());
            Color col = s.Colors[(int)(Next01() * s.Colors.Length) % s.Colors.Length];
            int size = Next01() < s.BigChance ? 2 : 1;
            burst.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed, col, size, c.hitParticleMaterial, layer, order);
        }
        burst.Begin(s.Life, s.Gravity);
    }

    private static SpriteRenderer BodyOf(MonsterHealth monster)
    {
        if (monster == null) return null;
        SimpleFrameAnimator anim = monster.GetComponentInChildren<SimpleFrameAnimator>();
        return anim != null ? anim.GetComponent<SpriteRenderer>() : monster.GetComponentInChildren<SpriteRenderer>();
    }

    private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    private static Sprite pixel;

    /// <summary>1도트(1/16칸) 흰 정사각형. Point 필터.</summary>
    internal static Sprite Pixel
    {
        get
        {
            if (pixel != null) return pixel;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "HitFeelPixel" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            pixel = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 16f);
            pixel.name = "HitFeelPixel";
            return pixel;
        }
    }
}

/// <summary>HitFeel 파티클 한 번 분량. 도트(1/16칸) 정사각형 몇 개를 날리고 흐리게 한 뒤 사라진다. 위치는 1/16칸 격자에 맞춰 그린다.
/// FootSortOrder를 붙이지 않는다(장애물 가림 판정 대상이 되지 않게).</summary>
public class HitParticleBurst : MonoBehaviour
{
    private const float Grid = 1f / 16f;

    private class Dot { public SpriteRenderer Renderer; public Vector2 Offset; public Vector2 Velocity; public Color Color; }

    private readonly List<Dot> dots = new List<Dot>();
    private float life, age, gravity;

    internal void Add(Vector2 velocity, Color color, int size, Material material, int sortingLayerId, int sortingOrder)
    {
        var child = new GameObject("Dot");
        child.transform.SetParent(transform, false);
        child.transform.localScale = new Vector3(size, size, 1f);
        var r = child.AddComponent<SpriteRenderer>();
        r.sprite = HitFeel.Pixel;
        r.sharedMaterial = material;
        r.sortingLayerID = sortingLayerId;
        r.sortingOrder = sortingOrder;
        r.color = color;
        dots.Add(new Dot { Renderer = r, Velocity = velocity, Color = color });
    }

    internal void Begin(float seconds, float gravityPerSecond)
    {
        life = Mathf.Max(0.05f, seconds);
        gravity = gravityPerSecond;
        Apply();
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life) { Destroy(gameObject); return; }
        foreach (Dot d in dots)
        {
            d.Velocity.y += gravity * Time.deltaTime;
            d.Velocity *= Mathf.Max(0f, 1f - 3f * Time.deltaTime); // 빨리 퍼졌다가 멈춘다
            d.Offset += d.Velocity * Time.deltaTime;
        }
        Apply();
    }

    private void Apply()
    {
        float t = Mathf.Clamp01(age / life);
        float alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
        foreach (Dot d in dots)
        {
            d.Renderer.transform.localPosition = new Vector3(Mathf.Round(d.Offset.x / Grid) * Grid, Mathf.Round(d.Offset.y / Grid) * Grid, 0f);
            Color c = d.Color; c.a = alpha;
            d.Renderer.color = c;
        }
    }
}

/// <summary>플레이어가 맞았을 때 몸 그림을 빨갛게 깜빡이고(몬스터 피격 깜빡임과 같은 색·시간) 그림을 잠깐 멈춘다. HitFeel.PlayerHit이 처음 맞을 때 붙인다.</summary>
[DefaultExecutionOrder(200)]
public class PlayerHitFeel : MonoBehaviour
{
    private SpriteRenderer body;
    private float flashRemaining, holdRemaining;
    private Color flashColor, baseColor = Color.white;
    private Sprite heldSprite;
    private bool flashing;

    /// <summary>지금 깜빡이는 중인지(테스트용).</summary>
    public bool IsFlashing => flashing;

    public void Play(Color color, float flashSeconds, float holdSeconds)
    {
        if (body == null) body = GetComponent<SpriteRenderer>();
        if (body == null) return;
        if (!flashing) baseColor = body.color;
        flashColor = color;
        flashRemaining = flashSeconds;
        flashing = flashSeconds > 0f;
        if (flashing) body.color = new Color(flashColor.r, flashColor.g, flashColor.b, baseColor.a);
        holdRemaining = holdSeconds;
        heldSprite = null;
    }

    private void LateUpdate()
    {
        if (body == null) return;
        if (holdRemaining > 0f)
        {
            if (heldSprite == null) heldSprite = body.sprite; else body.sprite = heldSprite;
            holdRemaining -= Time.deltaTime;
        }
        if (!flashing) return;
        flashRemaining -= Time.deltaTime;
        if (flashRemaining > 0f) body.color = new Color(flashColor.r, flashColor.g, flashColor.b, body.color.a);
        else { body.color = new Color(baseColor.r, baseColor.g, baseColor.b, body.color.a); flashing = false; }
    }
}
