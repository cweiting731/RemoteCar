using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;

[RequireComponent(typeof(ParticleSystem))]
public class MiniRoomGenerator : MonoBehaviour
{
    private ParticleSystem pcParticleSystem;

    [Header("點雲縮放與外觀設定")]
    public float displayScale = 0.01f;     // 預設縮放 0.01 倍
    public Color pointColor = Color.cyan;   // 點雲顏色
    public float particleSize = 0.005f;    // 點的大小

    void Awake()
    {
        pcParticleSystem = GetComponent<ParticleSystem>();
        SetupParticleSystem(); // 自動設定 Particle System 參數
    }

    void Start()
    {
        // 監聽 MRUK 的房間載入完成事件
        if (MRUK.Instance != null)
        {
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }
    }

    // 當你在 Unity 編輯器中「第一次掛載此腳本」或「點擊 Reset」時，會自動執行此處的設定
    void Reset()
    {
        SetupParticleSystem();
    }

    /// <summary>
    /// 自動初始化與設定 Particle System 的各項屬性，防止粒子飛走或手動設定錯誤
    /// </summary>
    void SetupParticleSystem()
    {
        if (pcParticleSystem == null) pcParticleSystem = GetComponent<ParticleSystem>();
        
        // 1. 基本模組設定 (Main Module)
        var main = pcParticleSystem.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = 1000f; // 讓點保持存活不消失
        main.startSpeed = 0f;       // 點必須固定在原地
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // 設為 Local，移動父物件時迷你房間才會跟著動
        main.maxParticles = 100000; // 支持高達 10 萬個點

        // 2. 關閉發射模組 (Emission) 與形狀模組 (Shape)
        var emission = pcParticleSystem.emission;
        emission.rateOverTime = 0f;
        
        var shape = pcParticleSystem.shape;
        shape.enabled = false;

        // 3. 渲染模組設定 (Renderer)
        var psRenderer = GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            // 如果你有特定的粒子材質，可以在這裡手動賦值，預設會使用 Unity 的 Default-Particle
        }
    }

    void OnSceneLoaded()
    {
        GenerateMiniRoom();
    }

    [ContextMenu("手動刷新迷你房間")]
    public void GenerateMiniRoom()
    {
        MRUKRoom currentRoom = MRUK.Instance.GetCurrentRoom();
        if (currentRoom == null)
        {
            Debug.LogError("找不到當前房間！");
            return;
        }

        // 修正：在 MRUK 中正確尋找 Global Mesh GameObject 的方法
        GameObject globalMeshObj = FindGlobalMeshObject(currentRoom);

        if (globalMeshObj == null)
        {
            Debug.LogError("找不到 Global Mesh！請確認 [BuildingBlock] Effect Mesh 的 Cut Holes 是否設為 None。");
            return;
        }

        MeshFilter mf = globalMeshObj.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogError("Global Mesh 上沒有找到有效的 MeshFilter。");
            return;
        }

        Vector3[] vertices = mf.sharedMesh.vertices;
        int pointCount = vertices.Length;

        // 準備粒子陣列
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            // 取得原始房間網格的頂點座標
            Vector3 rawPos = vertices[i];

            // 將其乘以 0.01 倍，縮放到 Particle System 的 Local 空間
            particles[i].position = rawPos * displayScale;

            // 設定粒子的基本外觀
            particles[i].startColor = pointColor;
            particles[i].startSize = particleSize;
            particles[i].remainingLifetime = 1000f; // 保持存活
        }

        // 清除舊粒子並塞入新點雲
        pcParticleSystem.Clear();
        pcParticleSystem.SetParticles(particles, pointCount);

        Debug.Log($"[MiniRoom] 成功用 Particle System 生成迷你房間！總點數: {pointCount}");
    }

    /// <summary>
    /// 修正後的尋找 Global Mesh 方法：遍歷房間內的所有錨點
    /// </summary>
    private GameObject FindGlobalMeshObject(MRUKRoom room)
    {
        // 方法 A：尋找帶有 GLOBAL_MESH 標籤的特殊錨點
        foreach (var anchor in room.Anchors)
        {
            if (anchor.HasLabel(OVRSceneManager.Classification.GlobalMesh) || 
                anchor.name.Contains("GLOBAL_MESH") || 
                anchor.name.Contains("GlobalMesh"))
            {
                // 通常網格會掛載在該錨點本身或其子物件上
                if (anchor.GetComponent<MeshFilter>() != null) return anchor.gameObject;
                
                MeshFilter childMf = anchor.GetComponentInChildren<MeshFilter>();
                if (childMf != null) return childMf.gameObject;
            }
        }

        // 方法 B：如果從錨點找不到，直接從 Effect Mesh 生成出來的環境物件中用名字搜尋
        MeshFilter[] allMeshFilters = FindObjectsOfType<MeshFilter>();
        foreach (var filter in allMeshFilters)
        {
            if (filter.gameObject.name.Contains("GLOBAL_MESH") || filter.gameObject.name.Contains("GlobalMesh"))
            {
                return filter.gameObject;
            }
        }

        return null;
    }
}