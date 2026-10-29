using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Selective, repeatable CLI authoring. Never calls the shared scene / prefab builders.
public static class PocPlayerFeedbackAuthoring
{
    private const string HUD_PATH = "Assets/Prefabs/UI/HUD.prefab";

    public static void AuthorHudOnly()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HUD_PATH);
        try
        {
            HudView view = root.GetComponentInChildren<HudView>(true);
            Transform existing = root.transform.Find("DamageDirection");
            RectTransform pivot;
            if (existing != null)
            {
                pivot = (RectTransform)existing;
            }
            else
            {
                GameObject indicator = new GameObject("DamageDirection", typeof(RectTransform), typeof(CanvasGroup));
                pivot = indicator.GetComponent<RectTransform>();
                pivot.SetParent(root.transform, false);
                pivot.anchorMin = pivot.anchorMax = new Vector2(0.5f, 0.5f);
                pivot.sizeDelta = Vector2.zero;
                // Inward-pointing chevron, rotated about screen center by the presenter.
                AddBar(pivot, "Left", new Vector2(-10f, 152f), -45f);
                AddBar(pivot, "Right", new Vector2(10f, 152f), 45f);
            }
            CanvasGroup group = pivot.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("damageDirectionGroup").objectReferenceValue = group;
            serialized.FindProperty("damageDirectionPivot").objectReferenceValue = pivot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HUD_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
    }

    private static void AddBar(RectTransform _parent, string _name, Vector2 _position, float _angle)
    {
        GameObject bar = new GameObject(_name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = bar.GetComponent<RectTransform>();
        rect.SetParent(_parent, false);
        rect.anchoredPosition = _position;
        rect.sizeDelta = new Vector2(32f, 7f);
        rect.localRotation = Quaternion.Euler(0f, 0f, _angle);
        Image image = bar.GetComponent<Image>();
        image.color = new Color(1f, 0.15f, 0.08f, 1f);
        image.raycastTarget = false;
    }

    // Final integrator runs this against its saved Game after all parallel changes merge.
    public static void WireSavedGame()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        PlayerMovement movement = Object.FindFirstObjectByType<PlayerMovement>();
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        InputAction crouch = actions.FindAction("Player/Crouch", true);
        InputActionReference reference = null;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(actions)))
        {
            if (asset is InputActionReference candidate && candidate.action == crouch)
            {
                reference = candidate;
                break;
            }
        }
        if (reference == null)
        {
            const string REFERENCE_PATH = "Assets/Prefabs/Gameplay/CrouchAction.asset";
            reference = AssetDatabase.LoadAssetAtPath<InputActionReference>(REFERENCE_PATH);
            if (reference == null)
            {
                reference = InputActionReference.Create(crouch);
                AssetDatabase.CreateAsset(reference, REFERENCE_PATH);
            }
        }
        SerializedObject player = new SerializedObject(movement);
        player.FindProperty("crouchAction").objectReferenceValue = reference;
        player.ApplyModifiedPropertiesWithoutUndo();
        HudPresenter presenter = Object.FindFirstObjectByType<HudPresenter>();
        SerializedObject hud = new SerializedObject(presenter);
        hud.FindProperty("damageView").objectReferenceValue = Object.FindFirstObjectByType<Camera>().transform;
        hud.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(movement.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("[PocPlayerFeedbackAuthoring] Wired existing Player/Crouch and directional feedback camera in saved Game.");
    }
}
