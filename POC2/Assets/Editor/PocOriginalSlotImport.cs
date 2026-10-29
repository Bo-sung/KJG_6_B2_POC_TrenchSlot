using UnityEditor;
using UnityEngine;

// 원본 슬롯 외형(SlotMachineOriginal)을 SlotMachine.prefab 루트 아래에 붙이고 릴·레버·마키·지급 파티클을 연결한다.
// 루트 GameObject·Transform·BoxCollider·컴포넌트의 fileID는 그대로 두어 씬 인스턴스 오버라이드가 깨지지 않게 한다.
// 옛 근사(프리미티브) 자식은 지우고, 기존 Model 자식도 매번 지우고 다시 붙이므로 여러 번 실행해도 결과가 같다.
// 실행: Unity -batchmode -projectPath <프로젝트> -executeMethod PocOriginalSlotImport.ApplySlotPrefab
// 마키 발광 머티리얼은 원본에 없어 비워 둔다(SlotMachineReels가 null이면 발광을 건너뛴다).
public static class PocOriginalSlotImport
{
    private const string SLOT_PREFAB_PATH = "Assets/Prefabs/Gameplay/SlotMachine.prefab";
    private const string ORIGINAL_PREFAB_PATH = "Assets/Art/SlotMachineOriginal/SlotMachineOriginal.prefab";
    private const string MODEL_NAME = "Model";
    private const string INTACT_NAME = "IntactVisual";
    private const int REEL_COUNT = 3;

    public static void ApplySlotPrefab()
    {
        GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(ORIGINAL_PREFAB_PATH);
        if (original == null)
        {
            Debug.LogError("PocOriginalSlotImport: 원본 프리팹이 없습니다 - " + ORIGINAL_PREFAB_PATH);
            return;
        }

        Transform originalModel = original.transform.Find(MODEL_NAME);
        if (originalModel == null)
        {
            Debug.LogError("PocOriginalSlotImport: 원본 프리팹에 " + MODEL_NAME + " 자식이 없습니다.");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(SLOT_PREFAB_PATH);
        try
        {
            if (!Bind(contents, originalModel.gameObject))
            {
                Debug.LogError("PocOriginalSlotImport: 바인딩 실패, 저장하지 않습니다.");
                return;
            }
            PrefabUtility.SaveAsPrefabAsset(contents, SLOT_PREFAB_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.ImportAsset(SLOT_PREFAB_PATH);
        Debug.Log("PocOriginalSlotImport: " + SLOT_PREFAB_PATH + " 갱신 완료");
    }

    private static bool Bind(GameObject _root, GameObject _originalModel)
    {
        var reelsComponent = _root.GetComponent<SlotMachineReels>();
        if (reelsComponent == null)
        {
            return false;
        }

        // 기존 자식(옛 근사 오브젝트·이전 Model)을 모두 비운다. 루트 자체와 컴포넌트는 건드리지 않는다.
        for (int i = _root.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(_root.transform.GetChild(i).gameObject);
        }

        GameObject model = Object.Instantiate(_originalModel, _root.transform);
        model.name = MODEL_NAME;

        Transform intact = model.transform.Find(INTACT_NAME);
        if (intact == null)
        {
            return false;
        }

        var reelTransforms = new Transform[REEL_COUNT];
        for (int i = 0; i < REEL_COUNT; i++)
        {
            // 릴 축(ReelAxis_N)을 돌려야 원본 실린더의 축 방향(Z 90도)이 유지된다.
            reelTransforms[i] = intact.Find("ReelAxis_" + (i + 1));
            if (reelTransforms[i] == null)
            {
                return false;
            }
        }

        Transform lever = intact.Find("Lever");
        Transform marquee = intact.Find("Marquee");
        Transform payout = intact.Find("BanknotePayout");
        if (lever == null || marquee == null || payout == null)
        {
            return false;
        }

        var marqueeRenderer = marquee.GetComponent<Renderer>();
        var payoutParticles = payout.GetComponent<ParticleSystem>();
        if (marqueeRenderer == null || payoutParticles == null)
        {
            return false;
        }

        var so = new SerializedObject(reelsComponent);
        SerializedProperty reelsProperty = so.FindProperty("reels");
        reelsProperty.arraySize = REEL_COUNT;
        for (int i = 0; i < REEL_COUNT; i++)
        {
            reelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = reelTransforms[i];
        }
        so.FindProperty("lever").objectReferenceValue = lever;
        so.FindProperty("marquee").objectReferenceValue = marqueeRenderer;
        so.FindProperty("marqueeGlow").objectReferenceValue = null;
        so.FindProperty("payoutParticles").objectReferenceValue = payoutParticles;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
