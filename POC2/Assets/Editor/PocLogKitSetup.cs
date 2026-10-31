using LogKit;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 빌드 씬마다 LogKit GameLogger 오브젝트를 두고 업로드 키와 서버 주소를 Inspector 값으로 넣는다.
// 키는 환경 변수 POC_LOGKIT_KEY로만 받는다. 코드나 저장소 파일에는 키를 넣지 않는다.
// 실행: -batchmode -executeMethod PocLogKitSetup.Run (환경 변수 POC_LOGKIT_KEY 필요)
public static class PocLogKitSetup
{
    private const string KEY_VARIABLE = "POC_LOGKIT_KEY";
    private const string SERVER_URL = "http://172.21.10.67:5000/api/logs";
    private const string OBJECT_NAME = "LogKit";

    [MenuItem("POC/Setup LogKit Loggers")]
    public static void Run()
    {
        string key = Environment.GetEnvironmentVariable(KEY_VARIABLE);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{KEY_VARIABLE} 환경 변수가 비어 있습니다.");
        }

        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (!buildScene.enabled)
            {
                continue;
            }
            Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
            GameLogger logger = UnityEngine.Object.FindFirstObjectByType<GameLogger>(FindObjectsInactive.Include);
            if (logger == null)
            {
                logger = new GameObject(OBJECT_NAME).AddComponent<GameLogger>();
            }

            var settings = new SerializedObject(logger);
            settings.FindProperty("_developmentServerUrl").stringValue = SERVER_URL;
            settings.FindProperty("_releaseServerUrl").stringValue = SERVER_URL;
            settings.FindProperty("_apiKey").stringValue = key;
            settings.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PocLogKitSetup] {buildScene.path}: GameLogger 설정 저장");
        }
    }
}
