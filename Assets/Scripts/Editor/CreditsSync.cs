using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 저장소 루트의 CREDITS.md(크레딧 원본 한 파일)를 게임이 읽는 Assets/Resources/Credits/Credits.md로 복사한다.
/// 빌드에는 Assets 밖 파일이 들어가지 않기 때문이다. 에디터를 켤 때, 빌드 직전, Tools > Credits > Sync CREDITS.md에서 돈다.
/// 내용이 같으면 아무것도 하지 않는다.
/// </summary>
[InitializeOnLoad]
public class CreditsSync : IPreprocessBuildWithReport
{
    public const string SourcePath = "CREDITS.md";
    public const string ResourceFile = "Assets/Resources/Credits/Credits.md";

    static CreditsSync() => EditorApplication.delayCall += () => Sync(false);

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Sync(true);

    [MenuItem("Tools/Credits/Sync CREDITS.md")]
    private static void SyncFromMenu() => Sync(true);

    /// <summary>복사했으면 참. CREDITS.md가 없으면 거짓(경고).</summary>
    public static bool Sync(bool log)
    {
        if (!File.Exists(SourcePath))
        {
            Debug.LogWarning($"[CreditsSync] {Path.GetFullPath(SourcePath)}가 없다 - 크레딧 화면은 지금 있는 {ResourceFile}를 그대로 쓴다.");
            return false;
        }
        string src = File.ReadAllText(SourcePath);
        if (File.Exists(ResourceFile) && File.ReadAllText(ResourceFile) == src)
        {
            if (log) Debug.Log("[CreditsSync] 이미 같다: " + ResourceFile);
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ResourceFile));
        File.WriteAllText(ResourceFile, src);
        AssetDatabase.ImportAsset(ResourceFile);
        Debug.Log($"[CreditsSync] {SourcePath} → {ResourceFile} 복사");
        return true;
    }
}
