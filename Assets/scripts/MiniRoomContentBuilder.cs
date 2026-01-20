using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Label White List")]
    public RoomLabelMask labelWhiteList = RoomLabelMask.NONE;

    [Header("Label Setting")]
    public GameObject labelPrefab;
    public Vector3 labelOffset = new Vector3(0f, 2f, 0f);

    private Transform _roomRoot;
    private Transform _contentRoot;
    private Transform _labelRoot;

    private bool _placed = false;
    private readonly List<Renderer> _renderers = new();
    private readonly List<GameObject> _spawnedLabels = new();

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

    // ================= Public API =================
    public void BuildOrRebuild()
    {
        PrepareRootPlacement();
        RebuildContent();
        UpdateRootBoxCollider();
    }

    // ================= Root placement =================
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

    // ================= Content rebuild =================
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

            var parent = new GameObject($"Mini_{mf.name}");
            parent.transform.SetParent(_contentRoot, false);

            var go = new GameObject("mesh");
            go.transform.SetParent(parent.transform, false);

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

            // ===== Label =====
            if (labelPrefab != null)
            {
                if (TryGetMaskFromName(mf.name, out RoomLabelMask mask))
                {
                    if (labelWhiteList == RoomLabelMask.NONE || (labelWhiteList & mask) != 0)
                    {
                        CreateLabelForRenderer(mr, mask.ToString(), parent.transform);
                    }
                }
            }
        }
    }

    // ================= Label rebuild =================
    private bool TryGetMaskFromName(string objectName, out RoomLabelMask mask)
    {
        string n = objectName.ToUpperInvariant();

        if (n.Contains("FLOOR"))        { mask = RoomLabelMask.FLOOR; return true; }
        if (n.Contains("CEILING"))      { mask = RoomLabelMask.CEILING; return true; }
        if (n.Contains("WALL_ART"))     { mask = RoomLabelMask.WALL_ART; return true; }
        if (n.Contains("WALL"))         { mask = RoomLabelMask.WALL_FACE; return true; }
        if (n.Contains("TABLE"))        { mask = RoomLabelMask.TABLE; return true; }
        if (n.Contains("COUCH") || n.Contains("SOFA"))
                                        { mask = RoomLabelMask.COUCH; return true; }
        if (n.Contains("BED"))          { mask = RoomLabelMask.BED; return true; }
        if (n.Contains("SCREEN") || n.Contains("TV"))
                                        { mask = RoomLabelMask.SCREEN; return true; }
        if (n.Contains("LAMP") || n.Contains("LIGHT"))
                                        { mask = RoomLabelMask.LAMP; return true; }
        if (n.Contains("PLANT"))        { mask = RoomLabelMask.PLANT; return true; }
        if (n.Contains("STORAGE") || n.Contains("CABINET") || n.Contains("SHELF"))
                                        { mask = RoomLabelMask.STORAGE; return true; }
        if (n.Contains("DOOR"))         { mask = RoomLabelMask.DOOR_FRAME; return true; }
        if (n.Contains("WINDOW"))       { mask = RoomLabelMask.WINDOW_FRAME; return true; }

        mask = RoomLabelMask.OTHER;
        return true;
    }


    private void CreateLabelForRenderer(Renderer r, string text, Transform parent = null)
    {
        Bounds b = r.bounds;

        Vector3 worldPos =
            b.center +
            Vector3.up * (b.extents.y + labelOffset.y) * scaleFactor;

        var go = Instantiate(labelPrefab, _labelRoot);
        go.name = $"Label_{text}";
        go.transform.position = worldPos;

        var uiText = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (uiText != null)
            uiText.text = text;

        if (parent != null)
            go.transform.SetParent(parent, true);

        go.transform.localScale *= scaleFactor * 2;

        _spawnedLabels.Add(go);
    }

    private void ClearLabels()
    {
        foreach (var l in _spawnedLabels)
            if (l) Destroy(l);

        _spawnedLabels.Clear();
    }

    private bool TryMapLabel(string semantic, out RoomLabelMask mask)
    {
        switch (semantic)
        {
            case "TABLE": mask = RoomLabelMask.TABLE; return true;
            case "COUCH": mask = RoomLabelMask.COUCH; return true;
            case "BED": mask = RoomLabelMask.BED; return true;
            case "SCREEN": mask = RoomLabelMask.SCREEN; return true;
            case "LAMP": mask = RoomLabelMask.LAMP; return true;
            case "PLANT": mask = RoomLabelMask.PLANT; return true;
            case "STORAGE": mask = RoomLabelMask.STORAGE; return true;
            default:
                mask = RoomLabelMask.OTHER;
                return false;
        }
    }

    // ================= Collider =================
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
