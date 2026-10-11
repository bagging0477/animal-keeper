using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 점검용 그림: 맵 전체 캡처(같은 영역·같은 배율), 위반 위치 표시, 모음 이미지(contact sheet), 플레이어 카메라 캡처.
/// 글자는 폰트 없이 3x5 픽셀 글꼴로 직접 그린다(숫자, #, x, 영문 대문자 일부).
/// </summary>
public static class GridMapAuditImages
{
    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['0'] = new[] { "111", "101", "101", "101", "111" }, ['1'] = new[] { "010", "110", "010", "010", "111" },
        ['2'] = new[] { "111", "001", "111", "100", "111" }, ['3'] = new[] { "111", "001", "111", "001", "111" },
        ['4'] = new[] { "101", "101", "111", "001", "001" }, ['5'] = new[] { "111", "100", "111", "001", "111" },
        ['6'] = new[] { "111", "100", "111", "101", "111" }, ['7'] = new[] { "111", "001", "010", "010", "010" },
        ['8'] = new[] { "111", "101", "111", "101", "111" }, ['9'] = new[] { "111", "101", "111", "001", "111" },
        ['#'] = new[] { "101", "111", "101", "111", "101" }, ['x'] = new[] { "000", "101", "010", "101", "000" },
        [' '] = new[] { "000", "000", "000", "000", "000" }, ['!'] = new[] { "010", "010", "010", "000", "010" },
        ['-'] = new[] { "000", "000", "111", "000", "000" }, ['/'] = new[] { "001", "001", "010", "100", "100" },
        ['A'] = new[] { "010", "101", "111", "101", "101" }, ['B'] = new[] { "110", "101", "110", "101", "110" },
        ['C'] = new[] { "111", "100", "100", "100", "111" }, ['D'] = new[] { "110", "101", "101", "101", "110" },
        ['E'] = new[] { "111", "100", "110", "100", "111" }, ['F'] = new[] { "111", "100", "110", "100", "100" },
        ['G'] = new[] { "111", "100", "101", "101", "111" }, ['I'] = new[] { "111", "010", "010", "010", "111" },
        ['J'] = new[] { "001", "001", "001", "101", "111" }, ['K'] = new[] { "101", "110", "100", "110", "101" },
        ['L'] = new[] { "100", "100", "100", "100", "111" }, ['M'] = new[] { "101", "111", "111", "101", "101" },
        ['N'] = new[] { "110", "101", "101", "101", "101" }, ['O'] = new[] { "111", "101", "101", "101", "111" },
        ['P'] = new[] { "111", "101", "111", "100", "100" }, ['R'] = new[] { "110", "101", "110", "101", "101" },
        ['S'] = new[] { "111", "100", "111", "001", "111" }, ['T'] = new[] { "111", "010", "010", "010", "010" },
        ['U'] = new[] { "101", "101", "101", "101", "111" }, ['V'] = new[] { "101", "101", "101", "101", "010" },
        ['W'] = new[] { "101", "101", "111", "111", "101" }, ['Y'] = new[] { "101", "101", "010", "010", "010" },
        ['H'] = new[] { "101", "101", "111", "101", "101" },
    };

    public static void DrawText(Texture2D tex, int x, int yTop, string text, int scale, Color fg, Color bg)
    {
        text = text.ToUpperInvariant().Replace('X', 'x');
        int w = text.Length * 4 * scale + scale, h = 7 * scale;
        FillRect(tex, x, yTop - h, w, h, bg);
        for (int i = 0; i < text.Length; i++)
        {
            if (!Glyphs.TryGetValue(text[i], out string[] g)) g = Glyphs[' '];
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 3; col++)
                    if (g[row][col] == '1')
                        FillRect(tex, x + scale + (i * 4 + col) * scale, yTop - scale - (row + 1) * scale, scale, scale, fg);
        }
    }

    public static void FillRect(Texture2D tex, int x, int y, int w, int h, Color c)
    {
        int x0 = Mathf.Clamp(x, 0, tex.width), x1 = Mathf.Clamp(x + w, 0, tex.width), y0 = Mathf.Clamp(y, 0, tex.height), y1 = Mathf.Clamp(y + h, 0, tex.height);
        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++) tex.SetPixel(xx, yy, c);
    }

    public static void DrawBox(Texture2D tex, int x, int y, int w, int h, int thickness, Color c)
    {
        FillRect(tex, x, y, w, thickness, c);
        FillRect(tex, x, y + h - thickness, w, thickness, c);
        FillRect(tex, x, y, thickness, h, c);
        FillRect(tex, x + w - thickness, y, thickness, h, c);
    }

    private static Texture2D Load(string file)
    {
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(file));
        return tex;
    }

    private static void Save(Texture2D tex, string file)
    {
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
    }

    /// <summary>
    /// 월드 영역 area를 칸당 ppu 픽셀로 찍고(Global 라이트 1 - 시야 연출 없이), 표시(markers: 월드 좌표, 색)와 왼쪽 위 글자를 그린다.
    /// </summary>
    public static void CaptureMap(Rect area, int ppu, string file, string label, IEnumerable<(Vector2 pos, Color color)> markers, bool flagged)
    {
        Light2D[] globals = Object.FindObjectsByType<Light2D>().Where(l => l.lightType == Light2D.LightType.Global).ToArray();
        float[] intensities = globals.Select(l => l.intensity).ToArray();
        foreach (Light2D l in globals) l.intensity = 1f;
        try { VillageRoomBuilder.CaptureArea(area, ppu, file); }
        finally { for (int i = 0; i < globals.Length; i++) globals[i].intensity = intensities[i]; }

        Texture2D tex = Load(file);
        foreach ((Vector2 pos, Color color) in markers ?? Enumerable.Empty<(Vector2, Color)>())
        {
            int px = Mathf.RoundToInt((pos.x - area.x) * ppu), py = Mathf.RoundToInt((pos.y - area.y) * ppu);
            int s = Mathf.Max(6, ppu * 2);
            DrawBox(tex, px - s / 2, py - s / 2, s, s, Mathf.Max(1, ppu / 4), color);
            DrawBox(tex, px - s / 2 - 2, py - s / 2 - 2, s + 4, s + 4, 1, Color.black);
        }
        if (flagged) DrawBox(tex, 0, 0, tex.width, tex.height, Mathf.Max(3, tex.width / 150), Color.red);
        if (!string.IsNullOrEmpty(label)) DrawText(tex, 4, tex.height - 4, label, Mathf.Max(3, tex.width / 160), Color.white, new Color(0f, 0f, 0f, 0.85f));
        Save(tex, file);
        Object.DestroyImmediate(tex);
    }

    /// <summary>같은 크기 캡처들을 columns열로 이어 붙인다. 크기가 다르면 첫 장 크기에 맞춰 왼쪽 아래 기준으로 붙인다.</summary>
    public static void ContactSheet(IList<string> files, int columns, string output, string title)
    {
        if (files.Count == 0) return;
        List<Texture2D> tiles = files.Select(Load).ToList();
        int tw = tiles.Max(t => t.width), th = tiles.Max(t => t.height), pad = 6;
        int rows = Mathf.CeilToInt(files.Count / (float)columns);
        int titleH = 40;
        Texture2D sheet = new Texture2D(columns * (tw + pad) + pad, rows * (th + pad) + pad + titleH, TextureFormat.RGBA32, false);
        FillRect(sheet, 0, 0, sheet.width, sheet.height, new Color(0.15f, 0.15f, 0.17f, 1f));
        for (int i = 0; i < tiles.Count; i++)
        {
            int cx = i % columns, cy = i / columns;
            int ox = pad + cx * (tw + pad), oy = sheet.height - titleH - (cy + 1) * (th + pad);
            sheet.SetPixels(ox, oy, tiles[i].width, tiles[i].height, tiles[i].GetPixels());
        }
        DrawText(sheet, pad, sheet.height - 6, title, 5, Color.white, new Color(0.15f, 0.15f, 0.17f, 1f));
        Save(sheet, output);
        foreach (Texture2D t in tiles) Object.DestroyImmediate(t);
        Object.DestroyImmediate(sheet);
    }

    /// <summary>게임 카메라(Camera.main)를 그대로 1280 x 720으로 찍는다 - 시야 라이트·마스크가 실제 화면과 같다.</summary>
    public static void CaptureGameCamera(string file, string label)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        int width = 1280, height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevTarget = cam.targetTexture;
        cam.targetTexture = rt;
        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
        else cam.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        cam.targetTexture = prevTarget;
        if (!string.IsNullOrEmpty(label)) DrawText(tex, 4, tex.height - 4, label, 4, Color.white, new Color(0f, 0f, 0f, 0.85f));
        Save(tex, file);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
    }
}
