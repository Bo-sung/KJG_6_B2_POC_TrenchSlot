using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ESC 일시정지 메뉴 프리팹을 만들고 Game 씬의 Presenters에 연결한다.
// 메뉴 POC/Build Pause Menu. 이미 있으면 덮어쓰거나 중복으로 넣지 않는다.
public static class PocPauseMenuAuthoring
{
    private const string SCENE_GAME = "Assets/Scenes/Game.unity";
    private const string PREFAB_PATH = PocAssetBuilder.UI_PREFAB_DIR + "/PauseMenu.prefab";
    private const string PRESENTERS_NAME = "Presenters";

    [MenuItem("POC/Build Pause Menu")]
    public static void Build()
    {
        PocAssetBuilder.EnsureFolders();
        PocAssetBuilder.LoadFonts();
        BuildPrefab();
        WireGameScene();
        Debug.Log("[PocPauseMenuAuthoring] Pause menu built and wired");
    }

    private static void BuildPrefab()
    {
        var canvas = PocAssetBuilder.CanvasRoot("PauseMenu", 25);
        var view = canvas.gameObject.AddComponent<PauseMenuView>();
        var root = canvas.transform;

        var panel = PocAssetBuilder.Panel(root, "Panel", new Color(0f, 0f, 0f, 0.7f));
        PocAssetBuilder.Label(panel.transform, "Title", "일시정지", 80, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(700f, 120f));
        var resume = PocAssetBuilder.MakeButton(panel.transform, "ResumeButton", "계속하기", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(400f, 100f));
        var title = PocAssetBuilder.MakeButton(panel.transform, "TitleButton", "타이틀로", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(400f, 100f));

        var so = new SerializedObject(view);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("resumeButton").objectReferenceValue = resume;
        so.FindProperty("titleButton").objectReferenceValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();

        PocAssetBuilder.SavePrefab(canvas.gameObject, PREFAB_PATH);
    }

    private static void WireGameScene()
    {
        Scene scene = EditorSceneManager.OpenScene(SCENE_GAME, OpenSceneMode.Single);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            throw new InvalidOperationException($"{PREFAB_PATH} 프리팹이 없습니다.");
        }
        PauseMenuView existing = UnityEngine.Object.FindFirstObjectByType<PauseMenuView>(FindObjectsInactive.Include);
        PauseMenuView view = existing != null ? existing : ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<PauseMenuView>();

        var presenters = GameObject.Find(PRESENTERS_NAME);
        if (presenters == null)
        {
            throw new InvalidOperationException($"Game 씬에 {PRESENTERS_NAME} 오브젝트가 없습니다.");
        }
        var presenter = presenters.GetComponent<PauseMenuPresenter>();
        if (presenter == null)
        {
            presenter = presenters.AddComponent<PauseMenuPresenter>();
        }
        var so = new SerializedObject(presenter);
        so.FindProperty("view").objectReferenceValue = view;
        so.FindProperty("gameManager").objectReferenceValue = UnityEngine.Object.FindFirstObjectByType<GameManager>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
