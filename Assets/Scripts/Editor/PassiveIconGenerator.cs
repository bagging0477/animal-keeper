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

    [MenuItem("Tools/Items/Generate Placeholder Passive Icons")]
    public static void GenerateAll()
    {
        Sprite sprite = EnsureRunningShoesIcon();
        Debug.Log($"[PassiveIconGenerator] {RunningShoesPath}: {(sprite != null ? "준비됨" : "실패")}");
    }

    /// <summary>러닝화 임시 아이콘 스프라이트. 없으면 그려서 저장한다.</summary>
    public static Sprite EnsureRunningShoesIcon()
    {
        if (!File.Exists(RunningShoesPath))
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(RunningShoesPath, DrawRunningShoes().EncodeToPNG());
            AssetDatabase.ImportAsset(RunningShoesPath);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(RunningShoesPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(RunningShoesPath);
    }

    // 오른쪽을 보는 운동화 옆모습: 밑창 / 흰 중창 / 파란 갑피 + 노란 줄 / 흰 끈, 마지막에 바깥 테두리.
    private static Texture2D DrawRunningShoes()
    {
        Color32[] px = new Color32[Grid * Grid];
        for (int i = 0; i < px.Length; i++) px[i] = Clear;

        void Set(int x, int y, Color32 c)
        {
            if (x >= 0 && x < Grid && y >= 0 && y < Grid) px[y * Grid + x] = c;
        }

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

        // 바깥 테두리: 빈 칸 중 채워진 칸에 닿은 곳
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
