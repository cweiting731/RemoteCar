using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SharedObjectManager : MonoBehaviour
{
    public static SharedObjectManager Instance { get; private set; }

    [Header("Scene Names")]
    public string mrSceneName = "MRScene";
    public string xrSceneName = "XRScene";

    [Header("同步設定")]
    [Range(0.001f, 1f)]
    public float scaleRatio = 0.01f; // MR : XR 比例

    [Header("目前狀態")]
    public bool inMRScene = true;

    // 儲存狀態
    private Vector3 savedLocalPos = Vector3.zero;
    private Quaternion savedLocalRot = Quaternion.identity;
    private bool hasSavedPosition = false; // ✅ 新增：用來判斷是否第一次載入

    void Awake()
    {
        // --- 單例 + 常駐 ---
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Update()
    {
        Vector2 axis = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        Debug.Log($"🕹 右手搖桿輸入: {axis}");  
        // --- 按 M 鍵切換模式 ---
        if (Input.GetKeyDown(KeyCode.M))
        {
            SwitchScene();
        }
    }

    /// <summary>
    /// 切換 MR / XR 模式
    /// </summary>
    public void SwitchScene()
    {
        GameObject cube = GameObject.Find("Cube");
        if (cube == null)
        {
            Debug.LogWarning("⚠️ 無法找到 Cube 物件，請確認命名一致。");
            return;
        }

        // 儲存 Cube 的相對位置（相對於 Scaniverse）
        if (cube.transform.parent != null)
        {
            savedLocalPos = cube.transform.localPosition;
            savedLocalRot = cube.transform.localRotation;
            hasSavedPosition = true; // ✅ 標記為已儲存
            Debug.Log($"💾 已儲存 Cube 位置 localPos={savedLocalPos}");
        }

        // 切換場景
        string targetScene = inMRScene ? xrSceneName : mrSceneName;
        Debug.Log($"🔁 切換至場景：{targetScene}");
        SceneManager.LoadScene(targetScene);
    }

    /// <summary>
    /// 場景載入完成後自動更新 Cube 的位置
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(InitCubeAfterLoad());
    }

    private IEnumerator InitCubeAfterLoad()
    {
        yield return null; // 等一幀，確保物理系統初始化完

        GameObject cube = GameObject.Find("Cube");
        if (cube == null)
        {
            Debug.LogWarning("⚠️ 新場景中找不到 Cube，無法同步位置。");
            yield break;
        }

        // ✅ 若是第一次載入，直接略過初始化（避免掉下去）
        if (!hasSavedPosition)
        {
            Debug.Log("🟢 第一次載入，不重設 Cube 位置。");
            yield break;
        }

        // 🧭 根據當前切換方向調整比例
        Vector3 newLocalPos = savedLocalPos;
        Quaternion newLocalRot = savedLocalRot;

        if (inMRScene)
        {
            // MR → XR：放大
            // newLocalPos /= scaleRatio;
            inMRScene = false;
        }
        else
        {
            // XR → MR：縮小
            // newLocalPos *= scaleRatio;
            inMRScene = true;
        }

        // 🧩 設定位置與旋轉
        cube.transform.localPosition = newLocalPos;
        cube.transform.localRotation = newLocalRot;

        // 🪄 微微上升避免穿模
        cube.transform.position += Vector3.up * 0.05f;

        Debug.Log($"✅ 已更新 Cube 位置: {cube.transform.position}");
    }
}
