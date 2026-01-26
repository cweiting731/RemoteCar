using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class MiniRoomContentBuilder : MonoBehaviour
{
    [Header("Head / Camera")]
    public Transform head;

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

    [Header("Label")]
    public GameObject labelPrefab;
    public Vector3 labelOffset = new Vector3(0f, 2f, 0f);

    [Header("Player Marker")]
    [SerializeField] private GameObject playerMarkerPrefab;

    private readonly List<Transform> _roomRoots = new();
    private Transform _contentRoot;
    private bool _placed;

    private IEnumerator Start()
    {
        if (head == null)
        {
            Debug.LogError("Head not assigned");
            yield break;
        }

        yield return WaitForRoomRoots();
        BuildOrRebuild();
    }

    private IEnumerator WaitForRoomRoots()
    {
        while (_roomRoots.Count == 0)
        {
            var rooms = GameObject
                .FindObjectsOfType<Transform>(true)
                .Where(t => t.name.StartsWith(roomNamePrefix))
                .ToList();

            foreach (var r in rooms)
            {
                if (r.GetComponentsInChildren<MeshRenderer>(true).Length > 0)
                    _roomRoots.Add(r);
            }

            yield return null;
        }
    }

    public void BuildOrRebuild()
    {
        PrepareRootPlacement();
        RebuildContent();
        UpdateRootBoxCollider();
    }

    private void PrepareRootPlacement()
    {
        if (!placeOnceInFront || _placed) return;

        transform.position =
            head.position
            + head.right * offsetInFrontOfEyes.x
            + head.up * offsetInFrontOfEyes.y
            + head.forward * offsetInFrontOfEyes.z;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        _placed = true;
    }

    private void RebuildContent()
    {
        if (_contentRoot != null)
            Destroy(_contentRoot.gameObject);

        _contentRoot = new GameObject("MiniRoomContent").transform;
        _contentRoot.SetParent(transform, false);
        _contentRoot.localScale = Vector3.one * scaleFactor;

        // ===== Collect all renderers =====
        List<Renderer> allRenderers = new();
        foreach (var r in _roomRoots)
            allRenderers.AddRange(r.GetComponentsInChildren<Renderer>(true));

        if (allRenderers.Count == 0)
        {
            Debug.LogWarning("No room renderers found");
            return;
        }

        // ===== Combined bounds =====
        Bounds combinedBounds = allRenderers[0].bounds;
        for (int i = 1; i < allRenderers.Count; i++)
            combinedBounds.Encapsulate(allRenderers[i].bounds);

        Vector3 combinedCenterWorld = combinedBounds.center;

        // 使用第一個 room 當 reference（因為都在 0,0,0）
        Transform refRoom = _roomRoots[0];
        Vector3 combinedCenterLocal =
            refRoom.InverseTransformPoint(combinedCenterWorld);

        // ===== Clone meshes =====
        foreach (var room in _roomRoots)
        {
            var meshFilters = room.GetComponentsInChildren<MeshFilter>(true);

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0)
                    continue;

                var srcMr = mf.GetComponent<MeshRenderer>();
                if (srcMr == null) continue;

                GameObject go = new GameObject($"Mini_{mf.name}");
                go.transform.SetParent(_contentRoot, false);

                Vector3 localPos =
                    refRoom.InverseTransformPoint(mf.transform.position);

                Quaternion localRot =
                    Quaternion.Inverse(refRoom.rotation) * mf.transform.rotation;

                go.transform.localPosition = localPos - combinedCenterLocal;
                go.transform.localRotation = localRot;
                go.transform.localScale = mf.transform.lossyScale;

                go.AddComponent<MeshFilter>().sharedMesh =
                    Instantiate(mf.sharedMesh);

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = srcMr.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                if (useOverrideMaterial && overrideMaterial != null)
                    mr.sharedMaterial = overrideMaterial;
            }
        }

        SpawnPlayerMarker(combinedCenterLocal);
    }

    private void UpdateRootBoxCollider()
    {
        var col = GetComponent<BoxCollider>();
        if (!col) return;

        var rs = _contentRoot.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            b.Encapsulate(rs[i].bounds);

        col.center = transform.InverseTransformPoint(b.center);
        col.size = b.size / transform.lossyScale.x;
    }

    private void SpawnPlayerMarker(Vector3 roomCenterLocal)
    {
        if (!playerMarkerPrefab) return;

        GameObject marker = Instantiate(playerMarkerPrefab, transform, false);
        marker.transform.localScale = Vector3.one / scaleFactor;

        var ctrl = marker.GetComponent<PlayerMarkerController>();
        if (!ctrl) return;

        ctrl.Initialize(
            head,
            _roomRoots[0],
            roomCenterLocal,
            scaleFactor
        );
    }
}
