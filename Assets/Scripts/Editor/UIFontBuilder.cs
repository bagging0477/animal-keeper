using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Tools > Fonts > Rebuild Fonts. Assets/Fonts의 폰트 파일(.ttf/.otf)로 게임 전체가 쓰는 TMP 폰트 두 개를 만든다.
///
/// - UIFont SDF (Static): 영문/숫자/기본 기호 + KS X 1001 한글 2,350자 + 프로젝트(스크립트/씬/프리팹/ScriptableObject)에
///   쓰인 모든 글자를 4096x4096 아틀라스 한 장에 미리 구워 둔다. 실행 중에 바뀌지 않으므로 git에 커밋해도 안정적이다.
///   샘플링 크기는 64부터 2씩 줄여 가며 모든 글자가 한 장에 들어가는 가장 큰 값을 고른다(Font Asset Creator의 Optimum과 같은 목적).
/// - UIFont Fallback SDF (Dynamic): 목록에 없는 글자가 나와도 네모로 깨지지 않게 실행 중에 채우는 예비 폰트.
///   빌드 때(Clear Dynamic Data On Build)와 에디터 Play 종료 때(UIFontFallbackGuard) 비워서 커밋된 파일이 변하지 않게 한다.
///
/// 다시 만들 때는 기존 에셋 파일에 내용을 덮어써서 GUID와 하위 에셋(아틀라스/머티리얼)의 fileID를 유지한다 - 씬/프리팹의
/// 참조가 끊기지 않는다. 글씨 스타일(흰 Face + 검은 Outline)은 UIFont SDF의 머티리얼 하나에만 들어 있고 모든 글씨가 공유한다.
/// </summary>
public static class UIFontBuilder
{
    public const string FontFolder = "Assets/Fonts";
    public const string GeneratedFolder = FontFolder + "/Generated";
    public const string MainAssetPath = GeneratedFolder + "/UIFont SDF.asset";
    public const string FallbackAssetPath = GeneratedFolder + "/UIFont Fallback SDF.asset";
    private const string CharacterListPath = GeneratedFolder + "/UIFont Characters.txt";
    private const string HangulListPath = FontFolder + "/Charsets/KSX1001_Hangul2350.txt";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string ReportPath = "Logs/UIFontBuild.txt";
    private const string SdfShaderName = "TextMeshPro/Distance Field";

    private const int AtlasSize = 4096;
    private const int MaxSamplingPointSize = 64;
    private const int MinSamplingPointSize = 36;
    private const int SamplingPointStep = 2;
    private const int FallbackAtlasSize = 1024;

    // 공용 글씨 스타일. Outline은 글자 경계 안팎에 반씩 그려져 흰 획이 그만큼 가늘어진다. 셰이더는 FaceDilate의 절반만큼
    // 경계를 밀어내므로, FaceDilate를 OutlineWidth와 같게 두어야 안쪽 절반이 메워져 흰 획 굵기가 원래대로 유지되고
    // 테두리는 전부 글자 바깥에 그려진다.
    // Outline 두께는 픽셀이 아니라 SDF 범위(Padding)에 대한 비율이라, Padding이 작으면 값을 올려도 거의 보이지 않는다.
    // 더 굵게 하려면 OutlineWidth를 올린다(PaddingRatio 0.2에서 최대 약 0.45). 그 이상은 PaddingRatio도 같이 올린다.
    public const float OutlineWidth = 0.25f;
    private const float FaceDilate = OutlineWidth;

    // 영문/숫자(ASCII 0x20~0x7E)와 한글 자모(ㄱ~ㅣ)는 코드에서 범위로 넣는다.
    private const string BasicSymbols =
        "·…‥•※→←↑↓↔★☆●○◎◇◆□■△▲▽▼♡♥♤♠♧♣♪♬「」『』【】《》〈〉‘’“”–—―∼×÷±°℃₩￦";

    private static readonly string[] ScannedExtensions = { ".cs", ".unity", ".prefab", ".asset" };
    private static readonly Regex YamlEscape = new Regex(@"\\(x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})");
    private const long MaxScannedFileSize = 4 * 1024 * 1024;

    [MenuItem("Tools/Fonts/Rebuild Fonts")]
    public static void RebuildFromMenu()
    {
        if (Rebuild(out string summary))
        {
            EditorUtility.DisplayDialog("Rebuild Fonts", summary, "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Rebuild Fonts", "폰트를 만들지 못했다. Console을 확인하자.\n\n" + summary, "OK");
        }
    }

    /// <summary>배치 모드용(-executeMethod UIFontBuilder.BatchRebuildAndApply). 폰트를 만들고 씬/프리팹 글씨에 연결한다.</summary>
    public static void BatchRebuildAndApply()
    {
        int exitCode = 0;
        try
        {
            if (!Rebuild(out _)) exitCode = 1;
            else if (!UIFontApplier.ApplyAll()) exitCode = 1;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            exitCode = 1;
        }
        EditorApplication.Exit(exitCode);
    }

    public static bool Rebuild(out string summary)
    {
        var report = new StringBuilder();
        summary = string.Empty;

        Font sourceFont = FindSourceFont(out string fontPath);
        if (sourceFont == null)
        {
            summary = $"{FontFolder}에서 .ttf/.otf 폰트를 찾지 못했다.";
            Debug.LogError("UIFontBuilder: " + summary);
            return false;
        }
        report.AppendLine($"source font: {fontPath}");

        string wanted = CollectCharacters(out int scannedFiles);
        report.AppendLine($"scanned files: {scannedFiles}");
        report.AppendLine($"requested characters: {wanted.Length}");

        // 폰트에 아예 없는 글자는 아틀라스 크기와 무관하므로 먼저 걸러낸다(작은 크기로 한 번 시험 삼아 넣어본다).
        string notInFont = FindCharactersMissingFromFont(sourceFont, wanted);
        string characters = notInFont.Length == 0 ? wanted : new string(wanted.Where(c => notInFont.IndexOf(c) < 0).ToArray());
        report.AppendLine($"not in font ({notInFont.Length}): {notInFont}");

        TMP_FontAsset built = null;
        int samplingPointSize = 0;
        for (int size = MaxSamplingPointSize; size >= MinSamplingPointSize; size -= SamplingPointStep)
        {
            TMP_FontAsset candidate = CreateFontAsset(sourceFont, size, AtlasSize, AtlasPopulationMode.Dynamic);
            candidate.TryAddCharacters(characters, out string didNotFit);
            if (string.IsNullOrEmpty(didNotFit))
            {
                built = candidate;
                samplingPointSize = size;
                break;
            }
            report.AppendLine($"sampling {size}: {didNotFit.Length} characters did not fit");
            DestroyFontAsset(candidate);
        }

        if (built == null)
        {
            summary = $"샘플링 크기 {MinSamplingPointSize}까지 줄여도 {characters.Length}자가 {AtlasSize} 아틀라스 한 장에 들어가지 않는다.";
            Debug.LogError("UIFontBuilder: " + summary);
            File.WriteAllText(ReportPath, report.ToString());
            return false;
        }

        built.atlasPopulationMode = AtlasPopulationMode.Static;
        ApplyStyle(built.material, built.atlasTextures[0]);

        if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder(FontFolder, "Generated");

        TMP_FontAsset main = Persist(built, MainAssetPath, "UIFont SDF");

        TMP_FontAsset fallbackTemp = CreateFontAsset(sourceFont, samplingPointSize, FallbackAtlasSize, AtlasPopulationMode.Dynamic);
        fallbackTemp.ClearFontAssetData(true);
        ApplyStyle(fallbackTemp.material, fallbackTemp.atlasTextures[0]);
        TMP_FontAsset fallback = Persist(fallbackTemp, FallbackAssetPath, "UIFont Fallback SDF");
        SetSerializedBool(fallback, "m_ClearDynamicDataOnBuild", true);

        main.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        EditorUtility.SetDirty(main);
        EditorUtility.SetDirty(fallback);

        SetTmpSettingsFonts(main, fallback);
        WriteCharacterList(characters);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(CharacterListPath);

        long usedArea = main.glyphTable.Sum(g => (long)(g.glyphRect.width + main.atlasPadding * 2) * (g.glyphRect.height + main.atlasPadding * 2));
        float usage = (float)usedArea / ((long)AtlasSize * AtlasSize);
        report.AppendLine($"sampling point size: {samplingPointSize}");
        report.AppendLine($"padding: {main.atlasPadding}");
        report.AppendLine($"render mode: {main.atlasRenderMode}");
        report.AppendLine($"atlas: {main.atlasWidth}x{main.atlasHeight}, textures: {main.atlasTextures.Length}");
        report.AppendLine($"characters in static atlas: {main.characterTable.Count}");
        report.AppendLine($"glyphs in static atlas: {main.glyphTable.Count}");
        report.AppendLine($"atlas usage (approx): {usage:P1}");
        report.AppendLine($"fallback: {FallbackAssetPath} (dynamic, clear on build)");
        report.AppendLine($"outline width: {OutlineWidth}, face dilate: {FaceDilate}");
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());

        summary = $"샘플링 {samplingPointSize}, Padding {main.atlasPadding}, {AtlasSize}x{AtlasSize} 한 장에 {main.characterTable.Count}자" +
                  (notInFont.Length > 0 ? $"\n폰트에 없는 글자 {notInFont.Length}개는 제외: {notInFont}" : string.Empty);
        Debug.Log("UIFontBuilder: " + summary.Replace('\n', ' ') + $" (자세한 내용: {ReportPath})");
        return true;
    }

    private static Font FindSourceFont(out string path)
    {
        path = Directory.Exists(FontFolder)
            ? Directory.GetFiles(FontFolder).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            : null;
        return path != null ? AssetDatabase.LoadAssetAtPath<Font>(path) : null;
    }

    // Outline이 그려질 SDF 범위(샘플링 크기 대비). 작으면 OutlineWidth를 올려도 테두리가 거의 보이지 않는다.
    private const float PaddingRatio = 0.2f;

    private static int PaddingFor(int samplingPointSize) => Mathf.Clamp(Mathf.RoundToInt(samplingPointSize * PaddingRatio), 8, 16);

    private static TMP_FontAsset CreateFontAsset(Font font, int samplingPointSize, int atlasSize, AtlasPopulationMode mode)
    {
        return TMP_FontAsset.CreateFontAsset(font, samplingPointSize, PaddingFor(samplingPointSize), GlyphRenderMode.SDFAA,
            atlasSize, atlasSize, mode, false);
    }

    private static void DestroyFontAsset(TMP_FontAsset asset)
    {
        if (asset == null) return;
        if (asset.atlasTextures != null)
        {
            foreach (Texture2D texture in asset.atlasTextures)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        if (asset.material != null) UnityEngine.Object.DestroyImmediate(asset.material);
        UnityEngine.Object.DestroyImmediate(asset);
    }

    private static string FindCharactersMissingFromFont(Font font, string characters)
    {
        TMP_FontAsset probe = TMP_FontAsset.CreateFontAsset(font, 16, 2, GlyphRenderMode.SDFAA, AtlasSize, AtlasSize,
            AtlasPopulationMode.Dynamic, true);
        probe.TryAddCharacters(characters, out string missing);
        DestroyFontAsset(probe);
        return missing ?? string.Empty;
    }

    // ---------- 글자 목록 ----------

    private static string CollectCharacters(out int scannedFiles)
    {
        var set = new SortedSet<int>();
        for (int c = 0x20; c <= 0x7E; c++) set.Add(c);
        for (int c = 0x3131; c <= 0x3163; c++) set.Add(c);
        foreach (char c in BasicSymbols) set.Add(c);

        if (File.Exists(HangulListPath))
        {
            foreach (char c in File.ReadAllText(HangulListPath, Encoding.UTF8)) set.Add(c);
        }
        else
        {
            Debug.LogWarning($"UIFontBuilder: {HangulListPath}이 없어 KS X 1001 한글 2,350자를 넣지 못했다.");
        }

        scannedFiles = 0;
        foreach (string file in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (Array.IndexOf(ScannedExtensions, ext) < 0) continue;
            if (path.StartsWith("Assets/TextMesh Pro/", StringComparison.Ordinal)) continue;
            if (path.StartsWith(GeneratedFolder + "/", StringComparison.Ordinal)) continue;
            if (new FileInfo(path).Length > MaxScannedFileSize) continue;

            string text = File.ReadAllText(path, Encoding.UTF8);
            scannedFiles++;
            foreach (char c in text)
            {
                if (c >= 0x80) set.Add(c);
            }
            if (ext != ".cs")
            {
                // Unity YAML은 ASCII 밖의 글자를 "가"처럼 이스케이프해서 저장한다.
                foreach (Match m in YamlEscape.Matches(text))
                {
                    int code = Convert.ToInt32(m.Value.Substring(2), 16);
                    if (code <= 0xFFFF) set.Add(code);
                }
            }
        }

        var sb = new StringBuilder(set.Count);
        foreach (int code in set)
        {
            if (IsRenderable(code)) sb.Append((char)code);
        }
        return sb.ToString();
    }

    private static bool IsRenderable(int code)
    {
        if (code < 0x20 || (code >= 0x7F && code <= 0x9F)) return false;
        if (code >= 0xD800 && code <= 0xF8FF) return false; // 서로게이트, 사용자 정의 영역
        if (code == 0xFEFF || code >= 0xFFF0) return false;  // BOM, 특수 용도
        return true;
    }

    private static void WriteCharacterList(string characters)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < characters.Length; i += 64)
        {
            sb.Append(characters, i, Math.Min(64, characters.Length - i)).Append('\n');
        }
        File.WriteAllText(CharacterListPath, sb.ToString(), new UTF8Encoding(false));
    }

    // ---------- 저장 ----------

    private static void ApplyStyle(Material material, Texture2D atlas)
    {
        Shader sdf = Shader.Find(SdfShaderName);
        if (sdf != null) material.shader = sdf;
        material.SetTexture("_MainTex", atlas);
        material.SetColor("_FaceColor", Color.white);
        material.SetColor("_OutlineColor", Color.black);
        material.SetFloat("_OutlineWidth", OutlineWidth);
        material.SetFloat("_OutlineSoftness", 0f);
        material.SetFloat("_FaceDilate", FaceDilate);
        ShaderUtilities.UpdateShaderRatios(material);
    }

    /// <summary>임시로 만든 폰트 에셋을 path에 저장한다. 이미 있으면 그 파일의 폰트/아틀라스/머티리얼 오브젝트에 내용만
    /// 덮어써서 GUID와 fileID를 그대로 둔다.</summary>
    private static TMP_FontAsset Persist(TMP_FontAsset temp, string path, string baseName)
    {
        Texture2D tempAtlas = temp.atlasTextures[0];
        Material tempMaterial = temp.material;

        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing == null)
        {
            temp.name = baseName;
            tempAtlas.name = baseName + " Atlas";
            tempMaterial.name = baseName + " Material";
            AssetDatabase.CreateAsset(temp, path);
            AssetDatabase.AddObjectToAsset(tempAtlas, temp);
            AssetDatabase.AddObjectToAsset(tempMaterial, temp);
            return temp;
        }

        Texture2D atlas = existing.atlasTextures != null && existing.atlasTextures.Length > 0 ? existing.atlasTextures[0] : null;
        if (atlas == null)
        {
            atlas = new Texture2D(1, 1, tempAtlas.format, false) { name = baseName + " Atlas" };
            AssetDatabase.AddObjectToAsset(atlas, existing);
        }
        Material material = existing.material;
        if (material == null)
        {
            material = new Material(tempMaterial) { name = baseName + " Material" };
            AssetDatabase.AddObjectToAsset(material, existing);
        }

        EditorUtility.CopySerialized(temp, existing);
        existing.name = baseName;

        atlas.Reinitialize(tempAtlas.width, tempAtlas.height, tempAtlas.format, false);
        atlas.LoadRawTextureData(tempAtlas.GetRawTextureData());
        atlas.Apply(false, false);

        material.shader = tempMaterial.shader;
        material.CopyPropertiesFromMaterial(tempMaterial);
        material.SetTexture("_MainTex", atlas);

        var so = new SerializedObject(existing);
        SerializedProperty textures = so.FindProperty("m_AtlasTextures");
        textures.arraySize = 1;
        textures.GetArrayElementAtIndex(0).objectReferenceValue = atlas;
        so.FindProperty("m_Material").objectReferenceValue = material;
        so.ApplyModifiedPropertiesWithoutUndo();
        existing.ReadFontAssetDefinition();

        EditorUtility.SetDirty(atlas);
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(existing);
        DestroyFontAsset(temp);
        return existing;
    }

    private static void SetSerializedBool(UnityEngine.Object target, string property, bool value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(property);
        if (p == null)
        {
            Debug.LogWarning($"UIFontBuilder: {target.name}에서 {property} 속성을 찾지 못했다.");
            return;
        }
        p.boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>TMP Settings의 기본 폰트를 font로, 글로벌 Fallback 목록을 fallback 하나로 맞춘다. 글로벌 Fallback은
    /// 폰트 에셋 자체의 Fallback 목록 다음에 검사되므로, 다른 폰트를 쓰는 글씨가 남아 있어도 한글이 네모로 깨지지 않는다.
    /// 누락 글리프 경고(m_warningsDisabled = 0)도 켜 둔다 - Fallback에도 없는 글자는 Console에 경고가 남는다.</summary>
    private static void SetTmpSettingsFonts(TMP_FontAsset font, TMP_FontAsset fallback)
    {
        UnityEngine.Object settings = AssetDatabase.LoadMainAssetAtPath(TmpSettingsPath);
        if (settings == null)
        {
            Debug.LogWarning($"UIFontBuilder: {TmpSettingsPath}을 찾지 못해 기본 폰트를 바꾸지 못했다.");
            return;
        }
        var so = new SerializedObject(settings);
        so.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
        SerializedProperty fallbacks = so.FindProperty("m_fallbackFontAssets");
        fallbacks.arraySize = 1;
        fallbacks.GetArrayElementAtIndex(0).objectReferenceValue = fallback;
        so.FindProperty("m_warningsDisabled").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
    }
}

/// <summary>에디터 Play 중에 Fallback 폰트가 채운 글자를 Play가 끝날 때 비워서, 커밋된 Fallback 에셋 파일이 바뀌지 않게 한다.
/// 어떤 글자가 Fallback으로 그려졌는지 알려 주므로 Rebuild Fonts를 다시 실행해 고정 폰트에 넣으면 된다.</summary>
[InitializeOnLoad]
internal static class UIFontFallbackGuard
{
    static UIFontFallbackGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;

        TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.FallbackAssetPath);
        if (fallback == null || fallback.characterTable == null || fallback.characterTable.Count == 0) return;

        string used = new string(fallback.characterTable.Select(c => (char)c.unicode).ToArray());
        Debug.LogWarning($"UIFont: 고정 폰트에 없는 글자 {used.Length}개를 Fallback이 실행 중에 그렸다: {used} - " +
                         "Tools > Fonts > Rebuild Fonts를 실행하면 고정 폰트에 포함된다.");
        fallback.ClearFontAssetData(true);
        EditorUtility.SetDirty(fallback);
        AssetDatabase.SaveAssetIfDirty(fallback);
    }
}
