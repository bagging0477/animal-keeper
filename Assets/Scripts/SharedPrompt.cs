using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VillageScene의 여러 상호작용 지점(동물, 트럭 등)이 화면에 하나뿐인 안내 문구 UI를
/// 공유해서 쓸 때, 서로의 SetActive(false) 호출이 같은 프레임 안에서 충돌하지 않도록
/// 조정한다. 각 스크립트는 매 프레임 BeginFrameIfNeeded로 시작하고, 상호작용 범위 안에
/// 들어왔을 때만 Show를 호출한다.
/// </summary>
public static class SharedPrompt
{
    private static int touchedFrame = -1;

    public static void BeginFrameIfNeeded(Text promptText)
    {
        if (promptText == null) return;
        if (touchedFrame == Time.frameCount) return;

        touchedFrame = Time.frameCount;
        promptText.gameObject.SetActive(false);
    }

    public static void Show(Text promptText, string message)
    {
        if (promptText == null) return;

        BeginFrameIfNeeded(promptText);
        promptText.gameObject.SetActive(true);
        promptText.text = message;
    }
}
