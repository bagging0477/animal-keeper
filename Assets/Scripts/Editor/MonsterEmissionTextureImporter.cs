using UnityEditor;
using UnityEngine;

/// <summary>몬스터 눈 발광 마스크(Resources/MonsterEmission)의 가져오기 설정: 원본과 같은 픽셀 그대로(Point, 압축 없음, 밉맵 없음),
/// 눈 위치를 구하려고 읽기 가능(isReadable)으로 둔다. 알파만 쓴다.</summary>
public class MonsterEmissionTextureImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/MonsterEmission/")) return;
        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = true;
        importer.sRGBTexture = false;
    }
}
