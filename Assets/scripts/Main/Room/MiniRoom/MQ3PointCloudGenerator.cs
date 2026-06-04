using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using System.Linq;

public class MiniRoomGenerator : MonoBehaviour
{
    [Header("MiniRoom 縮放")]
    public float miniScale = 0.01f;

    [Header("Room search")]
    public string roomNamePrefix = "Room -";

    [Header("半透明材質")]
    [Tooltip("可選。若不指定，會從來源材質複製並轉成半透明。")]
    public Material transparentMaterial;
    [Range(0f, 1f)]
    public float alpha = 0.35f;
    public Color tintColor = Color.cyan;

    private Transform miniRoot;
    private Material runtimeTransparentMaterial;

    private void Start()
    {
        if (MRUK.Instance != null)
        {
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }
    }

    private void OnSceneLoaded()
    {
        GenerateMiniRoom();
    }

    [ContextMenu("手動刷新 Mini GLOBAL_MESH")]
    public void GenerateMiniRoom()
    {
        if (MRUK.Instance == null)
        {
            Debug.LogError("MRUK.Instance 為 null，尚未初始化。");
            return;
        }

        List<Transform> roomRoots = FindRoomRoots();
        if (roomRoots.Count == 0)
        {
            Debug.LogError($"找不到任何房間根物件，請確認名稱前綴是否為 {roomNamePrefix}。");
            return;
        }

        RebuildMiniGlobalMeshes(roomRoots);
    }

    private void RebuildMiniGlobalMeshes(List<Transform> roomRoots)
    {
        if (miniRoot != null)
        {
            Destroy(miniRoot.gameObject);
            miniRoot = null;
        }

        List<(Transform roomRoot, MeshFilter meshFilter, MeshRenderer meshRenderer)> roomMeshes = new();
        foreach (Transform roomRoot in roomRoots)
        {
            GameObject globalMeshObj = FindGlobalMeshObject(roomRoot);
            if (globalMeshObj == null)
            {
                continue;
            }

            MeshFilter srcMf = globalMeshObj.GetComponent<MeshFilter>();
            MeshRenderer srcMr = globalMeshObj.GetComponent<MeshRenderer>();
            if (srcMf == null || srcMf.sharedMesh == null)
            {
                continue;
            }

            roomMeshes.Add((roomRoot, srcMf, srcMr));
        }

        if (roomMeshes.Count == 0)
        {
            Debug.LogError("找不到任何可用的 GLOBAL_MESH。\n");
            return;
        }

        Transform refRoom = roomRoots[0];
        List<Renderer> allRenderers = new();
        foreach (Transform roomRoot in roomRoots)
        {
            allRenderers.AddRange(roomRoot.GetComponentsInChildren<Renderer>(true));
        }

        if (allRenderers.Count == 0)
        {
            Debug.LogError("找不到任何可用的 Renderer，無法計算 MiniRoom 中心。\n");
            return;
        }

        Bounds combinedBounds = allRenderers[0].bounds;
        for (int i = 1; i < allRenderers.Count; i++)
        {
            combinedBounds.Encapsulate(allRenderers[i].bounds);
        }

        Vector3 combinedCenterWorld = combinedBounds.center;
        Vector3 combinedCenterLocal = refRoom.InverseTransformPoint(combinedCenterWorld);

        GameObject root = new GameObject("Mini_GLOBAL_MESHes");
        miniRoot = root.transform;
        miniRoot.SetParent(transform, false);
        miniRoot.localScale = Vector3.one * Mathf.Max(0.0001f, miniScale);

        int builtCount = 0;
        foreach (var roomMesh in roomMeshes)
        {
            if (CreateMiniRoomMesh(refRoom, combinedCenterLocal, roomMesh.roomRoot, roomMesh.meshFilter, roomMesh.meshRenderer))
            {
                builtCount++;
            }
        }

        Debug.Log($"[MiniRoom] 已生成 GLOBAL_MESH 迷你房間，共建立 {builtCount} 個房間。");
    }

    private bool CreateMiniRoomMesh(Transform refRoom, Vector3 combinedCenterLocal, Transform roomRoot, MeshFilter srcMf, MeshRenderer srcMr)
    {
        GameObject roomGo = new GameObject($"Mini_{roomRoot.name}_GLOBAL_MESH");
        roomGo.transform.SetParent(miniRoot, false);

        Vector3 localPos = refRoom.InverseTransformPoint(srcMf.transform.position);
        Quaternion localRot = Quaternion.Inverse(refRoom.rotation) * srcMf.transform.rotation;

        roomGo.transform.localPosition = localPos - combinedCenterLocal;
        roomGo.transform.localRotation = localRot;
        roomGo.transform.localScale = srcMf.transform.lossyScale;

        MeshFilter dstMf = roomGo.AddComponent<MeshFilter>();
        dstMf.sharedMesh = Instantiate(srcMf.sharedMesh);

        MeshRenderer dstMr = roomGo.AddComponent<MeshRenderer>();
        dstMr.sharedMaterial = ResolveTransparentMaterial(srcMr);
        dstMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dstMr.receiveShadows = false;

        MeshCollider dstMc = roomGo.AddComponent<MeshCollider>();
        dstMc.sharedMesh = dstMf.sharedMesh;

        return true;
    }

    private List<Transform> FindRoomRoots()
    {
        return GameObject
            .FindObjectsOfType<Transform>(true)
            .Where(t => t.name.StartsWith(roomNamePrefix) && t.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
            .OrderBy(t => t.name)
            .ToList();
    }

    private GameObject FindGlobalMeshObject(Transform roomRoot)
    {
        if (roomRoot == null)
        {
            return null;
        }

        MeshFilter[] roomMeshFilters = roomRoot.GetComponentsInChildren<MeshFilter>(true);
        foreach (var filter in roomMeshFilters)
        {
            if (filter == null)
            {
                continue;
            }

            if (filter.gameObject.name.Contains("GLOBAL_MESH") || filter.gameObject.name.Contains("GlobalMesh"))
            {
                return filter.gameObject;
            }
        }

        MeshFilter[] allMeshFilters = FindObjectsOfType<MeshFilter>();
        foreach (var filter in allMeshFilters)
        {
            if (filter != null && filter.transform.IsChildOf(roomRoot) &&
                (filter.gameObject.name.Contains("GLOBAL_MESH") || filter.gameObject.name.Contains("GlobalMesh")))
            {
                return filter.gameObject;
            }
        }

        return null;
    }

    private Material ResolveTransparentMaterial(MeshRenderer srcMr)
    {
        if (runtimeTransparentMaterial != null)
        {
            return runtimeTransparentMaterial;
        }

        if (transparentMaterial != null)
        {
            runtimeTransparentMaterial = new Material(transparentMaterial);
        }
        else if (srcMr != null && srcMr.sharedMaterial != null)
        {
            runtimeTransparentMaterial = new Material(srcMr.sharedMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            runtimeTransparentMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
        }

        runtimeTransparentMaterial.name = "MiniRoom_GlobalMesh_Transparent";
        ApplyTransparency(runtimeTransparentMaterial);
        return runtimeTransparentMaterial;
    }

    private void ApplyTransparency(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        Color tint = tintColor;
        tint.a = Mathf.Clamp01(alpha);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", tint);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", tint);
        }
    }

    private void OnDestroy()
    {
        if (runtimeTransparentMaterial != null)
        {
            Destroy(runtimeTransparentMaterial);
            runtimeTransparentMaterial = null;
        }
    }
}