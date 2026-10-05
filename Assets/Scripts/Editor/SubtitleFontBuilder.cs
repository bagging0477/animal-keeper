using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Assets/Fonts에 넣은 폰트(.ttf/.otf)로 자막용 TMP SDF 폰트 에셋(Assets/Resources/SubtitleFont.asset)을 만든다.
/// 한글은 글자 수가 많아 미리 구워둘 수 없으므로 Dynamic(필요한 글자를 실행 중에 아틀라스에 추가)으로 두고,
/// 2048 아틀라스 + SDFAA 렌더 모드로 확대/축소와 여러 해상도에서도 가장자리가 선명하게 한다.
/// 폰트를 바꾸면 이 메뉴를 다시 실행하면 된다(같은 경로의 에셋을 새로 만든다).
/// </summary>
public static class SubtitleFontBuilder
{
    private const string FontFolder = "Assets/Fonts";
    private const string OutputPath = "Assets/Resources/SubtitleFont.asset";
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9; // 샘플링 크기의 10% - 테두리(Outline) 0.2를 넣어도 잘리지 않는 여유
    private const int AtlasSize = 2048;

    [MenuItem("Tools/Subtitles/Build Subtitle Font Asset")]
    public static void Build()
    {
        string fontPath = Directory.Exists(FontFolder)
            ? Directory.GetFiles(FontFolder).Select(p => p.Replace('\\', '/'))
                .FirstOrDefault(p => p.EndsWith(".ttf", System.StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith(".otf", System.StringComparison.OrdinalIgnoreCase))
            : null;
        Font font = fontPath != null ? AssetDatabase.LoadAssetAtPath<Font>(fontPath) : null;
        if (font == null)
        {
            Debug.LogError($"SubtitleFontBuilder: {FontFolder}에서 .ttf/.otf 폰트를 찾지 못했다.");
            return;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding,
            GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
        if (fontAsset == null)
        {
            Debug.LogError($"SubtitleFontBuilder: '{fontPath}'로 폰트 에셋을 만들지 못했다.");
            return;
        }
        fontAsset.name = "SubtitleFont";

        // CreateFontAsset은 모바일 SDF 셰이더(테두리가 키워드로만 켜진다)를 붙인다. 테두리가 항상 그려지는 데스크톱 SDF
        // 셰이더로 바꾸고 자막 기본 스타일(흰 글자 + 검은 테두리)을 머티리얼에 미리 넣어둔다 - 이 머티리얼이 빌드에
        // 포함되므로 셰이더도 같이 포함된다.
        Material material = fontAsset.material;
        material.shader = Shader.Find("TextMeshPro/Distance Field");
        material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
        material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, SubtitleManager.OutlineWidth);
        material.SetFloat(ShaderUtilities.ID_FaceDilate, SubtitleManager.FaceDilate);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutputPath) != null) AssetDatabase.DeleteAsset(OutputPath);

        AssetDatabase.CreateAsset(fontAsset, OutputPath);
        // 아틀라스 텍스처와 머티리얼은 폰트 에셋 안에 하위 에셋으로 같이 저장해야 씬을 다시 열어도 유지된다.
        fontAsset.atlasTextures[0].name = "SubtitleFont Atlas";
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
        fontAsset.material.name = "SubtitleFont Material";
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(OutputPath);
        Debug.Log($"SubtitleFontBuilder: '{fontPath}' → {OutputPath} (Dynamic, {AtlasSize}x{AtlasSize}, SDFAA)");
    }
}
