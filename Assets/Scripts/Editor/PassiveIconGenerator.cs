using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>패시브용 임시 아이콘을 코드로 그려 Assets/Sprites/Items에 PNG로 저장한다. 정식 아이콘이 생기면
/// PassiveDefinition.icon 필드만 바꾸면 된다. 이미 파일이 있으면 덮어쓰지 않는다(지운 뒤 다시 실행하면 새로 그린다).
/// 메뉴: Tools > Items > Generate Placeholder Passive Icons</summary>
public static class PassiveIconGenerator
{
    public const string Folder = "Assets/Sprites/Items";
    public const string RunningShoesPath = Folder + "/RunningShoes_placeholder.png";
    public const string InventorySlotPath = Folder + "/InventorySlot_placeholder.png";
    public const string FlashlightPath = Folder + "/Flashlight_placeholder.png";

    private const int Grid = 32;   // 32x32 칸에 그리고
    private const int Scale = 2;   // 2배로 늘려 64x64 PNG로 저장

    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
    private static readonly Color32 Outline = new Color32(40, 28, 24, 255);
    private static readonly Color32 Sole = new Color32(70, 64, 72, 255);
    private static readonly Color32 Midsole = new Color32(240, 236, 226, 255);
    private static readonly Color32 Upper = new Color32(60, 140, 220, 255);
    private static readonly Color32 UpperShade = new Color32(40, 100, 175, 255);
    private static readonly Color32 Stripe = new Color32(255, 200, 60, 255);
    private static readonly Color32 Lace = new Color32(250, 250, 250, 255);
    private static readonly Color32 Bag = new Color32(176, 118, 64, 255);
    private static readonly Color32 BagShade = new Color32(132, 84, 44, 255);
    private static readonly Color32 Strap = new Color32(96, 60, 32, 255);
    private static readonly Color32 Buckle = new Color32(230, 200, 90, 255);
    private static readonly Color32 PlusBadge = new Color32(70, 170, 80, 255);
    private static readonly Color32 PlusMark = new Color32(250, 250, 250, 255);
    private static readonly Color32 Body = new Color32(60, 66, 80, 255);
    private static readonly Color32 BodyShine = new Color32(110, 120, 140, 255);
    private static readonly Color32 Lens = new Color32(255, 250, 200, 255);
    private static readonly Color32 Beam = new Color32(255, 230, 120, 255);
    private static readonly Color32 Switch = new Color32(220, 70, 60, 255);

    private delegate void Painter(System.Action<int, int, Color32> set);

    [MenuItem("Tools/Items/Generate Placeholder Passive Icons")]
    public static void GenerateAll()
    {
        Debug.Log($"[PassiveIconGenerator] {RunningShoesPath}: {(EnsureRunningShoesIcon() != null ? "준비됨" : "실패")}");
        Debug.Log($"[PassiveIconGenerator] {InventorySlotPath}: {(EnsureInventorySlotIcon() != null ? "준비됨" : "실패")}");
        Debug.Log($"[PassiveIconGenerator] {FlashlightPath}: {(EnsureFlashlightIcon() != null ? "준비됨" : "실패")}");
    }

    /// <summary>러닝화 임시 아이콘 스프라이트. 없으면 그려서 저장한다.</summary>
    public static Sprite EnsureRunningShoesIcon() => EnsureIcon(RunningShoesPath, PaintRunningShoes);

    /// <summary>인벤토리 칸 +1 임시 아이콘 스프라이트. 없으면 그려서 저장한다.</summary>
    public static Sprite EnsureInventorySlotIcon() => EnsureIcon(InventorySlotPath, PaintInventorySlot);

    /// <summary>플래시라이트 임시 아이콘 스프라이트. 없으면 그려서 저장한다.</summary>
    public static Sprite EnsureFlashlightIcon() => EnsureIcon(FlashlightPath, PaintFlashlight);

    private static Sprite EnsureIcon(string path, Painter paint)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(path, Draw(paint).EncodeToPNG());
            AssetDatabase.ImportAsset(path);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // 오른쪽을 보는 운동화 옆모습: 밑창 / 흰 중창 / 파란 갑피 + 노란 줄 / 흰 끈.
    private static void PaintRunningShoes(System.Action<int, int, Color32> Set)
    {
        // 밑창 (앞코가 살짝 들린다)
        for (int x = 4; x <= 27; x++) Set(x, 6, Sole);
        for (int x = 3; x <= 28; x++) Set(x, 7, Sole);
        Set(28, 8, Sole); Set(29, 8, Sole);

        // 중창
        for (int x = 3; x <= 27; x++) { Set(x, 8, Midsole); Set(x, 9, Midsole); }
        Set(28, 9, Midsole);

        // 갑피: 뒤꿈치(x 3~12)는 높고, 앞코 쪽으로 갈수록 낮아진다
        for (int x = 3; x <= 28; x++)
        {
            int top = x <= 12 ? 21 : Mathf.Max(12, 21 - Mathf.RoundToInt((x - 12) * 0.6f));
            if (x == 3) top = 19;
            for (int y = 10; y <= top; y++) Set(x, y, y <= 11 ? UpperShade : Upper);
        }

        // 뒤꿈치 탭과 발목 구멍
        for (int y = 18; y <= 22; y++) Set(4, y, UpperShade);
        for (int x = 6; x <= 10; x++) Set(x, 21, Outline);

        // 옆면 노란 줄 (뒤에서 앞으로 내려가는 사선)
        for (int x = 7; x <= 20; x++)
        {
            int y = 17 - (x - 7) / 3;
            Set(x, y, Stripe); Set(x, y - 1, Stripe);
        }

        // 신발 끈
        for (int i = 0; i < 4; i++)
        {
            int x = 13 + i * 2;
            int y = 19 - i;
            Set(x, y, Lace); Set(x + 1, y, Lace);
        }
    }

    // 정면 배낭 + 오른쪽 아래 초록 "+" 배지.
    private static void PaintInventorySlot(System.Action<int, int, Color32> Set)
    {
        // 손잡이 고리
        for (int x = 12; x <= 19; x++) Set(x, 27, Strap);
        Set(11, 26, Strap); Set(11, 25, Strap); Set(20, 26, Strap); Set(20, 25, Strap);

        // 몸통 (모서리를 깎은 사각형)
        for (int y = 4; y <= 24; y++)
        {
            int inset = y == 4 || y == 24 ? 1 : 0;
            for (int x = 6 + inset; x <= 25 - inset; x++) Set(x, y, x >= 22 ? BagShade : Bag);
        }

        // 덮개와 앞주머니
        for (int x = 6; x <= 25; x++) { Set(x, 17, BagShade); Set(x, 18, BagShade); }
        for (int y = 7; y <= 13; y++)
            for (int x = 10; x <= 21; x++) Set(x, y, y == 13 || x == 10 || x == 21 ? Strap : BagShade);

        // 끈과 버클
        for (int y = 14; y <= 22; y++) { Set(12, y, Strap); Set(19, y, Strap); }
        Set(12, 16, Buckle); Set(19, 16, Buckle);

        // "+" 배지
        for (int y = 1; y <= 10; y++)
            for (int x = 20; x <= 29; x++)
            {
                int dx = x - 24, dy = y - 5;
                if (dx * dx + dy * dy <= 22) Set(x, y, PlusBadge);
            }
        for (int i = -3; i <= 3; i++) { Set(24 + i, 5, PlusMark); Set(24, 5 + i, PlusMark); }
        for (int i = -3; i <= 3; i++) { Set(25 + i, 5, PlusMark); Set(25, 5 + i, PlusMark); }
    }

    // 오른쪽을 비추는 손전등: 손잡이 / 넓어지는 머리 / 렌즈 + 노란 빛줄기.
    private static void PaintFlashlight(System.Action<int, int, Color32> Set)
    {
        // 손잡이와 스위치
        for (int x = 2; x <= 13; x++)
            for (int y = 13; y <= 18; y++) Set(x, y, y == 17 ? BodyShine : Body);
        Set(9, 19, Switch); Set(10, 19, Switch);

        // 머리: 오른쪽으로 갈수록 넓어진다
        for (int x = 14; x <= 18; x++)
        {
            int half = 3 + (x - 14);
            for (int y = 16 - half; y <= 15 + half; y++) Set(x, y, y == 14 + half ? BodyShine : Body);
        }

        // 렌즈
        for (int y = 9; y <= 22; y++) Set(19, y, Lens);

        // 빛줄기 세 갈래
        for (int i = 0; i < 9; i++)
        {
            Set(21 + i, 15, Beam); Set(21 + i, 16, Beam);
            Set(21 + i, 19 + i / 2, Beam);
            Set(21 + i, 12 - i / 2, Beam);
        }
    }

    // 그린 뒤 바깥 테두리(빈 칸 중 채워진 칸에 닿은 곳)를 두르고 Scale배로 늘린다.
    private static Texture2D Draw(Painter paint)
    {
        Color32[] px = new Color32[Grid * Grid];
        for (int i = 0; i < px.Length; i++) px[i] = Clear;
        paint((x, y, c) =>
        {
            if (x >= 0 && x < Grid && y >= 0 && y < Grid) px[y * Grid + x] = c;
        });

        Color32[] filled = (Color32[])px.Clone();
        for (int y = 0; y < Grid; y++)
        {
            for (int x = 0; x < Grid; x++)
            {
                if (filled[y * Grid + x].a != 0) continue;
                bool touches = false;
                for (int dy = -1; dy <= 1 && !touches; dy++)
                    for (int dx = -1; dx <= 1 && !touches; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < Grid && ny >= 0 && ny < Grid && filled[ny * Grid + nx].a != 0) touches = true;
                    }
                if (touches) px[y * Grid + x] = Outline;
            }
        }

        int size = Grid * Scale;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] scaled = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                scaled[y * size + x] = px[(y / Scale) * Grid + x / Scale];
        tex.SetPixels32(scaled);
        tex.Apply();
        return tex;
    }
}
