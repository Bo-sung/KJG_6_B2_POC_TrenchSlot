using UnityEditor;
using UnityEngine;

// 검증 진입점 종료. CLI 배치 모드에서만 에디터를 끝내고, 열린 에디터에서 돌렸을 때는 결과만 남긴다.
public static class PocEditorExit
{
    public static void Exit(int _code)
    {
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(_code);
            return;
        }
        Debug.Log($"[PocEditorExit] 대화형 에디터라 종료하지 않습니다(code={_code}).");
    }
}
