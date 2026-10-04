using UnityEditor;
using UnityEngine;

public class AnimalKeeperTextureImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/AnimalKeeperTitle/")) return;
        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
    }
}
