using UnityEngine;

/// <summary>인벤토리 아이콘 기본값을 고르는 공용 도우미. 동물/시체의 기본 아이콘은 "그 스프라이트 시트의
/// 첫 번째 프레임"(이름 끝 번호가 가장 작은 조각, 보통 *_0)으로 고정한다. 실행 중에는 텍스처에 딸린 조각
/// 스프라이트 목록을 꺼낼 방법이 없어서, 에디터에서(OnValidate) 미리 찾아 숨은 직렬화 필드에 저장해 두고
/// 빌드에서는 그 값을 그대로 쓴다.</summary>
public static class InventoryIconUtility
{
#if UNITY_EDITOR
    /// <summary>sheet 텍스처를 Sprite Editor로 잘라 둔 조각 중 첫 번째 프레임을 돌려준다. 잘라 둔 조각이
    /// 없으면 null.</summary>
    public static Sprite FindSheetFirstFrame(Texture2D sheet)
    {
        if (sheet == null) return null;

        string path = UnityEditor.AssetDatabase.GetAssetPath(sheet);
        if (string.IsNullOrEmpty(path)) return null;

        Sprite first = null;
        int firstIndex = int.MaxValue;
        foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
        {
            if (asset is not Sprite sprite) continue;

            int index = TrailingNumber(sprite.name);
            if (first == null || index < firstIndex)
            {
                first = sprite;
                firstIndex = index;
            }
        }
        return first;
    }

    // "koala_sheet_12" -> 12. 번호가 없으면 맨 뒤로 보낸다.
    private static int TrailingNumber(string name)
    {
        int underscore = name.LastIndexOf('_');
        return underscore >= 0 && int.TryParse(name.Substring(underscore + 1), out int n) ? n : int.MaxValue;
    }
#endif
}
