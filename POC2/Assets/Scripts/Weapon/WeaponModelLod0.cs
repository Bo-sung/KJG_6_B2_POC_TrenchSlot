using UnityEngine;

// AssetFactory game FBX는 LOD0·LOD1·LOD2 메시와 UCX 충돌 껍질을 한 파일에 담는다.
// 1인칭과 바닥 외형은 LOD0만 쓴다. 검증되지 않은 하위 LOD와 UCX 메시는 끄고, 자동 생성된 LODGroup도 끈다.
public sealed class WeaponModelLod0 : MonoBehaviour
{
    private void Awake()
    {
        foreach (var group in GetComponentsInChildren<LODGroup>(true))
        {
            group.enabled = false;
        }
        foreach (var child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform)
            {
                continue;
            }
            if (child.name.StartsWith("UCX_") || child.name.EndsWith("_LOD1") || child.name.EndsWith("_LOD2"))
            {
                child.gameObject.SetActive(false);
            }
        }
    }
}
