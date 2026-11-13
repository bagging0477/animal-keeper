using System;
using UnityEditor;
using UnityEngine;

/// <summary>batch로 다른 기능 테스트(-executeMethod XxxTest.RunBatch 등)를 돌릴 때 1일차 튜토리얼의 멈춤 화면을 끈다.
/// 멈춤 화면은 Time.timeScale을 0으로 두어 그 테스트의 시간·이동·소환을 멈추기 때문이다. 튜토리얼 테스트와 에디터에서 직접 하는 Play는 그대로 둔다.</summary>
[InitializeOnLoad]
public static class TutorialIntroTestSwitch
{
    static TutorialIntroTestSwitch()
    {
        if (!Application.isBatchMode) return;
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-executeMethod");
        if (i < 0 || i + 1 >= args.Length) return;
        TutorialGuide.IntrosSuppressed = !args[i + 1].StartsWith("TutorialGuideTest.", StringComparison.Ordinal);
    }
}
