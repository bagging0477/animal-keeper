using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Memes > Build Meme Sprites. 밈 효과 그림을 도트로 그려 PNG로 저장하고 GameBalanceConfig에 연결한다.
/// - DizzyStar.png: 코뿔소 기절 별 7x7. PPU 16(1도트 = 1/16칸), 피벗 가운데.
/// - ClerkBubble.png: 수의사 말풍선 몸통 12x12, 9-slice 테두리 4. ClerkBubbleTail.png: 꼬리 7x4. PPU 100(UI가 도트 배율을 정한다).
/// 모두 Sprite(Single), Point 필터, 압축 끔, 밉맵 끔. 외곽선 #2F2F2E, 낮은 채도 팔레트. 같은 경로에 덮어써서 GUID가 유지된다.
/// batchmode: -executeMethod MemeAssetBuilder.BuildBatch
/// </summary>
public static class MemeAssetBuilder
{
    public const string Folder = "Assets/Sprites/UI/Memes";
    public const string StarPath = Folder + "/DizzyStar.png";
    public const string BubblePath = Folder + "/ClerkBubble.png";
    public const string TailPath = Folder + "/ClerkBubbleTail.png";
    private const string ConfigPath = "Assets/Settings/GameBalanceConfig.asset";

    // 위에서 아래로. O 외곽선 #2F2F2E, Y 별 #CDB77A, H 별 밝은 곳 #E6D9A8, C 크림 #EDE3D3, S 크림 그늘 #BBAE9A, . 투명
    private static readonly string[] Star =
    {
        "...O...",
        "..OHO..",
        "OOOYOOO",
        "OHYYYYO",
        ".OYYYO.",
        ".OYOYO.",
        ".OO.OO.",
    };

    private static readonly string[] Bubble =
    {
        ".OOOOOOOOOO.",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OCCCCCCCCCCO",
        "OSSSSSSSSSSO",
        ".OOOOOOOOOO.",
    };

    // 맨 윗줄은 몸통 아래 외곽선과 겹쳐 그 자리를 그늘색으로 이어 준다.
    private static readonly string[] Tail =
    {
        "OSSSSSO",
        ".OCCCO.",
        "..OCO..",
        "...O...",
    };

    [MenuItem("Tools/Memes/Build Meme Sprites")]
    public static void Build() => TryBuild();

    public static void BuildBatch() => EditorApplication.Exit(TryBuild() ? 0 : 1);

    private static bool TryBuild()
    {
        Directory.CreateDirectory(Folder);
        Write(StarPath, Star);
        Write(BubblePath, Bubble);
        Write(TailPath, Tail);
        AssetDatabase.Refresh();
        Import(StarPath, 16f, Vector4.zero);
        Import(BubblePath, 100f, new Vector4(4, 4, 4, 4));
        Import(TailPath, 100f, Vector4.zero);

        var config = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(ConfigPath);
        if (config == null) { Debug.LogError("[MemeAssetBuilder] GameBalanceConfig 없음"); return false; }
        config.dizzyStarSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StarPath);
        config.clerkBubbleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BubblePath);
        config.clerkBubbleTailSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TailPath);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        bool ok = config.dizzyStarSprite != null && config.clerkBubbleSprite != null && config.clerkBubbleTailSprite != null;
        Debug.Log($"[MemeAssetBuilder] {(ok ? "완료" : "실패")}: 별 {config.dizzyStarSprite}, 말풍선 {config.clerkBubbleSprite}, 꼬리 {config.clerkBubbleTailSprite}");
        return ok;
    }

    private static void Write(string path, string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, ColorOf(rows[y][x]));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static Color ColorOf(char c)
    {
        switch (c)
        {
            case 'O': return Hex(0x2F2F2E);
            case 'Y': return Hex(0xCDB77A);
            case 'H': return Hex(0xE6D9A8);
            case 'C': return Hex(0xEDE3D3);
            case 'S': return Hex(0xBBAE9A);
            default: return new Color(0f, 0f, 0f, 0f);
        }
    }

    private static void Import(string path, float ppu, Vector4 border)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.spriteBorder = border;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
    }

    private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
}
