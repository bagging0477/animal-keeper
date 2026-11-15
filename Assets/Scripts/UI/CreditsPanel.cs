using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 타이틀의 CREDITS 창. 내용은 Resources/Credits/Credits.md 한 파일에서 읽는다 - 이 파일은 저장소 루트의 CREDITS.md를
/// 에디터가 그대로 복사한 것이다(CreditsSync: 에디터를 켤 때·빌드할 때·Tools > Credits > Sync CREDITS.md).
/// 크레딧을 고칠 때는 CREDITS.md만 고치면 된다.
/// - 마크다운은 간단히만 읽는다: "# 제목", "## 소제목", "- 목록"(들여쓴 "- "는 한 단계 안쪽), **굵게**, [글자](주소). HTML 주석은 숨긴다.
/// - 닫기 버튼(TitleMenu.OnCloseCredits)이나 ESC로 닫는다. 열 때마다 파일을 다시 읽고 맨 위에서 시작한다.
/// </summary>
public class CreditsPanel : MonoBehaviour
{
    public const string ResourcePath = "Credits/Credits";
    public const string MissingText = "크레딧 파일(Resources/Credits/Credits.md)을 찾을 수 없습니다.";

    [SerializeField] private TMP_Text body;
    [SerializeField] private ScrollRect scroll;

    private static readonly Regex Comment = new Regex(@"<!--[\s\S]*?-->");
    private static readonly Regex Bold = new Regex(@"\*\*(.+?)\*\*");
    private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\(([^)]+)\)");
    private static readonly Regex Url = new Regex(@"https?://[^\s<]+");

    private const string HeadingColor = "#5C4B48";
    private const string LinkColor = "#3E6E62";

    public bool IsOpen => gameObject.activeSelf;
    public TMP_Text Body => body;

    public void Open()
    {
        gameObject.SetActive(true);
        if (body != null) body.text = ToRichText(LoadMarkdown());
        Canvas.ForceUpdateCanvases();
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;
    }

    public void Close() => gameObject.SetActive(false);

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
    }

    public static string LoadMarkdown()
    {
        TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
        return asset != null ? asset.text : MissingText;
    }

    /// <summary>CREDITS.md(간단한 마크다운) → TextMeshPro 서식 글.</summary>
    public static string ToRichText(string markdown)
    {
        string md = Comment.Replace(markdown.Replace("\r\n", "\n"), "");
        var sb = new StringBuilder();
        bool lastBlank = true;
        foreach (string raw in md.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                if (!lastBlank) sb.Append('\n');
                lastBlank = true;
                continue;
            }
            lastBlank = false;
            int indent = line.Length - line.TrimStart().Length;
            string t = line.Trim();
            if (t.StartsWith("## ")) sb.Append($"<size=115%><b><color={HeadingColor}>{Inline(t.Substring(3))}</color></b></size>\n");
            else if (t.StartsWith("# ")) sb.Append($"<size=135%><b>{Inline(t.Substring(2))}</b></size>\n");
            else if (t.StartsWith("- ") && indent >= 2) sb.Append($"<indent=7%><size=88%>· {Inline(t.Substring(2))}</size></indent>\n");
            else if (t.StartsWith("- ")) sb.Append($"<indent=2.5%>• {Inline(t.Substring(2))}</indent>\n");
            else sb.Append(Inline(t)).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    // 한 줄 안의 꾸밈: 글자 "<"는 그대로 보이게 막고, **굵게**, [글자](주소) → 글자 (주소), 주소는 색을 다르게.
    private static string Inline(string s)
    {
        s = s.Replace("<", "<noparse><</noparse>");
        s = Link.Replace(s, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[2].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        s = Bold.Replace(s, "<b>$1</b>");
        s = Url.Replace(s, m => $"<color={LinkColor}>{m.Value}</color>");
        return s;
    }
}
