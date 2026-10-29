using UnityEngine;
using UnityEngine.SceneManagement;

// Preload 씬(빌드 순서 0). 테이블을 한 번 읽어 검증한 뒤 Title 씬으로 넘긴다.
public sealed class PreloadController : MonoBehaviour
{
    private void Start()
    {
        TableManager.Initialize();
        SceneManager.LoadScene(SceneNames.TITLE);
    }
}
