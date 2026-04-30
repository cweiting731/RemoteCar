using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MiniRoomFromEffectMesh : MonoBehaviour
{
    [Header("Head / camera")]
    public Transform head; // CenterEyeAnchor

    [Header("Room root search")]
    public string roomNamePrefix = "Room -";

    [Header("Mini settings")]
    public float scaleFactor = 0.01f;
    public Vector3 offsetInFrontOfEyes = new Vector3(0f, -0.10f, 0.70f);

    [Tooltip("生成時把迷你模型放到眼前一次，之後不再跟著 head 移動")]
    public bool placeOnceInFront = true;

    [Tooltip("放置時是否讓迷你模型朝向使用者（只用 yaw，不跟著低頭仰頭）")]
    public bool faceUserYawOnly = true;

    [Header("Material override")]
    public bool useOverrideMaterial = true;
    public Material overrideMaterial; // 你想套用的材質（可 runtime 改）

    [Header("Grab / Collider")]
    public bool addOneBoxColliderOnRoot = true;
    public bool addRigidbodyOnRoot = true;

    [Tooltip("如果你使用 Oculus Interaction，建議勾選，並依下方步驟加上 define")]
    public bool addOculusGrabComponents = false;

    private Transform _roomRoot;
    private Transform _miniRoot;
    private Transform _miniContent;
    private readonly List<Renderer> _miniRenderers = new();

    IEnumerator Start()
    {
        if (head == null)
        {
            Debug.LogError("[MiniRoomFromEffectMesh] head not assigned (CenterEyeAnchor).");
            yield break;
        }

        yield return StartCoroutine(WaitForRoomRoot());
        if (_roomRoot == null)
        {
            Debug.LogError("[MiniRoomFromEffectMesh] Room root not found.");
            yield break;
        }

        BuildMiniRoom(_roomRoot);
    }

    private IEnumerator WaitForRoomRoot()
    {
        while (true)
        {
            var t = GameObject.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(x => x.name.StartsWith(roomNamePrefix));

            if (t != null)
            {
                _roomRoot = t;
                yield break;
            }

            yield return null;
        }
    }

    private void BuildMiniRoom(Transform roomRoot)
    {
        if (_miniRoot != null) Destroy(_miniRoot.gameObject);

        // 迷你房間根（先不 parent 到 head；我們要「只定位一次」）
        _miniRoot = new GameObject("MiniRoomRoot").transform;
        _miniRoot.position = GetPlacementWorldPos();
        _miniRoot.rotation = GetPlacementWorldRot();

        // 內容根：縮放寫在這裡
        _miniContent = new GameObject("MiniRoomContent").transform;
        _miniContent.SetParent(_miniRoot, false);
        _miniContent.localScale = Vector3.one * scaleFactor;

        // 收集 roomRoot 底下所有 mesh
        var meshFilters = roomRoot.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters == null || meshFilters.Length == 0)
        {
            Debug.LogWarning("[MiniRoomFromEffectMesh] No MeshFilter found under room root.");
            return;
        }

        // 用 Renderer bounds 算房間中心 pivot
        var renderers = roomRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[MiniRoomFromEffectMesh] No Renderer found under room root.");
            return;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        Vector3 roomCenterLocal = roomRoot.InverseTransformPoint(b.center);

        _miniRenderers.Clear();

        foreach (var mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                continue;

            var srcMr = mf.GetComponent<MeshRenderer>();
            if (srcMr == null) continue;

            var child = new GameObject($"Mini_{mf.gameObject.name}");
            child.transform.SetParent(_miniContent, false);

            // 相對 roomRoot 的 pose
            Vector3 localPos = roomRoot.InverseTransformPoint(mf.transform.position);
            Quaternion localRot = Quaternion.Inverse(roomRoot.rotation) * mf.transform.rotation;
            Vector3 localScale = mf.transform.lossyScale;

            child.transform.localPosition = (localPos - roomCenterLocal);
            child.transform.localRotation = localRot;
            child.transform.localScale = localScale;

            var meshCopy = Instantiate(mf.sharedMesh);
            child.AddComponent<MeshFilter>().sharedMesh = meshCopy;

            var dstMr = child.AddComponent<MeshRenderer>();
            dstMr.sharedMaterials = srcMr.sharedMaterials;
            dstMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dstMr.receiveShadows = false;

            _miniRenderers.Add(dstMr);
        }

        // 套材質（可選）
        ApplyMaterialOverrideIfNeeded();

        // 加 collider / rigidbody（用於抓取）
        if (addOneBoxColliderOnRoot)
            EnsureRootBoxColliderFitsContent();
        if (addRigidbodyOnRoot)
            EnsureRootRigidbody();

        // Oculus Interaction 抓取（可選）
        if (addOculusGrabComponents)
            TryAddOculusGrabComponents();

        Debug.Log("[MiniRoomFromEffectMesh] Mini room built.");
    }

    private Vector3 GetPlacementWorldPos()
    {
        if (!placeOnceInFront) return head.position;

        // 用 head 的 forward/right/up 把 offset 轉成世界座標位移
        return head.position
               + head.right * offsetInFrontOfEyes.x
               + head.up * offsetInFrontOfEyes.y
               + head.forward * offsetInFrontOfEyes.z;
    }

    private Quaternion GetPlacementWorldRot()
    {
        if (!placeOnceInFront) return head.rotation;

        if (!faceUserYawOnly) return head.rotation;

        // 只取 yaw，讓迷你模型面向你但不會跟著你低頭仰頭
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd, Vector3.up);
    }

    private void ApplyMaterialOverrideIfNeeded()
    {
        if (!useOverrideMaterial || overrideMaterial == null) return;

        foreach (var r in _miniRenderers)
        {
            if (r == null) continue;
            r.sharedMaterial = overrideMaterial; // 全部統一
        }
    }

    /// <summary>Runtime 改材質用：你可以從 UI/按鍵呼叫</summary>
    public void SetOverrideMaterial(Material mat, bool enableOverride = true)
    {
        overrideMaterial = mat;
        useOverrideMaterial = enableOverride;
        ApplyMaterialOverrideIfNeeded();
    }

    private void EnsureRootBoxColliderFitsContent()
    {
        // 用 _miniContent 下所有 Renderer bounds 算一個總 bounds
        var rs = _miniContent.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        // 將 bounds 轉成 root local
        Vector3 centerLocal = _miniRoot.InverseTransformPoint(b.center);

        // size 用 world size 轉 local（近似；這裡 root 沒縮放，基本 OK）
        Vector3 sizeWorld = b.size;
        Vector3 sizeLocal = sizeWorld; // root.localScale=1，近似成立

        var col = _miniRoot.GetComponent<BoxCollider>();
        if (col == null) col = _miniRoot.gameObject.AddComponent<BoxCollider>();

        col.center = centerLocal;
        col.size = sizeLocal;
    }

    private void EnsureRootRigidbody()
    {
        var rb = _miniRoot.GetComponent<Rigidbody>();
        if (rb == null) rb = _miniRoot.gameObject.AddComponent<Rigidbody>();

        // 你要抓取移動，通常設 kinematic，交給 Interactor 控制
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    /// <summary>
    /// Oculus Interaction 的抓取元件很多版本命名不同。
    /// 我用「Reflection」避免你沒裝相關套件時編譯直接炸掉。
    /// </summary>
    private void TryAddOculusGrabComponents()
    {
        // 需要 Oculus.Interaction 套件（你有 [BuildingBlock] OVRInteractionComprehensive 通常就有）
        // 基本上需要：
        // - Grabbable
        // - GrabInteractable
        // - OneGrabFreeTransformer（或 TwoGrabFreeTransformer）
        //
        // 下面用反射加，失敗就提示你改用 Inspector 手動加（見下方說明）

        bool Add(string typeFullName)
        {
            var t = System.Type.GetType(typeFullName);
            if (t == null) return false;
            if (_miniRoot.GetComponent(t) != null) return true;
            _miniRoot.gameObject.AddComponent(t);
            return true;
        }

        // 常見命名（不同版本可能略有差）
        bool ok1 = Add("Oculus.Interaction.Grabbable, Oculus.Interaction");
        // bool ok2 = Add("Oculus.Interaction.GrabInteractable, Oculus.Interaction");
        // bool ok3 = Add("Oculus.Interaction.OneGrabFreeTransformer, Oculus.Interaction");

        if (!(ok1))
        {
            Debug.LogWarning(
                "[MiniRoomFromEffectMesh] Failed to add Oculus grab components by reflection. " +
                "Please add Grabbable + GrabInteractable (+ OneGrabFreeTransformer) manually in Inspector.");
        }
    }
}
