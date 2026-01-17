using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MiniRoomContentBuilder : MonoBehaviour
{
    [Header("Head / Camera")]
    public Transform head; // CenterEyeAnchor

    [Header("Room search")]
    public string roomNamePrefix = "Room -";

    [Header("Mini placement")]
    public float scaleFactor = 0.01f;
    public Vector3 offsetInFrontOfEyes = new Vector3(0f, 0.7f, 0.7f);
    public bool placeOnceInFront = true;
    public bool faceUserYawOnly = true;

    [Header("Material override")]
    public bool useOverrideMaterial = false;
    public Material overrideMaterial;

    private Transform _roomRoot;
    private Transform _contentRoot;
    private bool _placed = false;
    private readonly List<Renderer> _renderers = new();

    private IEnumerator Start()
    {
        if (head == null)
        {
            Debug.LogError("[MiniRoomContentBuilder] Head not assigned.");
            yield break;
        }

        yield return WaitForRoomRoot();
        BuildOrRebuild();
    }

    private IEnumerator WaitForRoomRoot()
    {
        while (_roomRoot == null)
        {
            _roomRoot = GameObject
                .FindObjectsOfType<Transform>(true)
                .FirstOrDefault(t => t.name.StartsWith(roomNamePrefix));

            yield return null;
        }
    }

    // ===== Public API（你之後可手動呼叫重建）=====
    public void BuildOrRebuild()
    {
        PrepareRootPlacement();
        RebuildContent();
        UpdateRootBoxCollider();
    }

    // ===== Root placement（只做一次）=====
    private void PrepareRootPlacement()
    {
        if (!placeOnceInFront || _placed) return;

        transform.position = GetPlacementWorldPos();
        transform.rotation = GetPlacementWorldRot();
        _placed = true;
    }

    private Vector3 GetPlacementWorldPos()
    {
        return head.position
             + head.right * offsetInFrontOfEyes.x
             + head.up * offsetInFrontOfEyes.y
             + head.forward * offsetInFrontOfEyes.z;
    }

    private Quaternion GetPlacementWorldRot()
    {
        if (!faceUserYawOnly)
            return head.rotation;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        return Quaternion.LookRotation(fwd, Vector3.up);
    }

    // ===== Content rebuild =====
    private void RebuildContent()
    {
        if (_contentRoot != null)
            Destroy(_contentRoot.gameObject);

        _contentRoot = new GameObject("MiniRoomContent").transform;
        _contentRoot.SetParent(transform, false);
        _contentRoot.localScale = Vector3.one * scaleFactor;

        _renderers.Clear();

        var meshFilters = _roomRoot.GetComponentsInChildren<MeshFilter>(true);
        var srcRenderers = _roomRoot.GetComponentsInChildren<Renderer>(true);

        if (meshFilters.Length == 0 || srcRenderers.Length == 0)
        {
            Debug.LogWarning("[MiniRoomContentBuilder] No meshes found.");
            return;
        }

        // 算房間中心
        Bounds roomBounds = srcRenderers[0].bounds;
        for (int i = 1; i < srcRenderers.Length; i++)
            roomBounds.Encapsulate(srcRenderers[i].bounds);

        Vector3 roomCenterLocal = _roomRoot.InverseTransformPoint(roomBounds.center);

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                continue;

            var srcMr = mf.GetComponent<MeshRenderer>();
            if (srcMr == null) continue;

            var go = new GameObject($"Mini_{mf.name}");
            go.transform.SetParent(_contentRoot, false);

            Vector3 localPos = _roomRoot.InverseTransformPoint(mf.transform.position);
            Quaternion localRot = Quaternion.Inverse(_roomRoot.rotation) * mf.transform.rotation;

            go.transform.localPosition = localPos - roomCenterLocal;
            go.transform.localRotation = localRot;
            go.transform.localScale = mf.transform.lossyScale;

            go.AddComponent<MeshFilter>().sharedMesh = Instantiate(mf.sharedMesh);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = srcMr.sharedMaterials;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            if (useOverrideMaterial && overrideMaterial != null)
                mr.sharedMaterial = overrideMaterial;

            _renderers.Add(mr);
        }
    }

    // ===== Collider update =====
    private void UpdateRootBoxCollider()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        var rs = _contentRoot.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return;

        Bounds worldBounds = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            worldBounds.Encapsulate(rs[i].bounds);

        col.center = transform.InverseTransformPoint(worldBounds.center);

        Vector3 size = worldBounds.size;
        size.x /= transform.lossyScale.x;
        size.y /= transform.lossyScale.y;
        size.z /= transform.lossyScale.z;
        col.size = size;
    }
}
