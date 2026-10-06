using TMPro;
using UnityEngine;

/// <summary>
/// 게임의 모든 TextMeshPro 글씨가 쓰는 폰트/머티리얼의 단일 출처.
///
/// - 폰트는 TMP Settings의 Default Font Asset(= Tools > Fonts > Rebuild Fonts가 만드는 Assets/Fonts/Generated/UIFont SDF)이다.
///   씬/프리팹에 배치된 글씨는 에디터 도구가 이 폰트를 직접 연결해 두고, 코드로 만드는 글씨는 Apply()로 연결한다.
/// - 머티리얼은 폰트 에셋의 기본 머티리얼 하나만 공유한다(흰 Face + 검은 Outline). 글씨마다 머티리얼을 복제하지 않으므로
///   이 머티리얼 한 곳을 바꾸면 모든 글씨가 같이 바뀐다. 그래서 코드에서 outlineWidth/fontMaterial처럼 인스턴스
///   머티리얼을 만드는 속성은 쓰지 않는다 - 글씨별 차이는 color(정점 색)와 fontSize로만 낸다.
/// </summary>
public static class UIFont
{
    public static TMP_FontAsset Font => TMP_Settings.defaultFontAsset;

    /// <summary>글씨에 공용 폰트와 공용 머티리얼을 연결한다.</summary>
    public static void Apply(TMP_Text text)
    {
        TMP_FontAsset font = Font;
        if (text == null || font == null) return;
        text.font = font;
        text.fontSharedMaterial = font.material;
    }
}
