using UnityEngine;

public class MarkerController : MonoBehaviour
{
    [Header("Tracking Target")]
    public Transform target;            // Head / Hand

    [Header("Room")]
    public Transform roomRoot;           // Room - xxxx
    public Vector3 roomCenterLocal;

    [Header("Mini Room")]
    public float scaleFactor = 0.01f;

    [Header("Material")]
    public Material markerMaterial;

    private bool _initialized;
    private Vector3 _originalLocalScale;

    private void Awake()
    {
        _originalLocalScale = transform.localScale;
    }

    /// <summary>
    /// 由 Builder 呼叫，完成初始化
    /// </summary>
    public void Initialize(
        Transform trackingTarget,
        Transform roomRootTransform,
        Vector3 roomCenter,
        float miniScale)
    {
        target = trackingTarget;
        roomRoot = roomRootTransform;
        roomCenterLocal = roomCenter;
        scaleFactor = miniScale;

        Renderer renderer = GetComponent<Renderer>();
        if (renderer != null && markerMaterial != null)
        {
            renderer.material = markerMaterial;
        }

        // Resize marker according to mini room scale
        transform.localScale = _originalLocalScale * scaleFactor;

        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized || target == null || roomRoot == null)
            return;

        UpdatePosition();
        UpdateRotation();
    }

    private void UpdatePosition()
    {
        Vector3 targetLocalInRoom =
            roomRoot.InverseTransformPoint(target.position);

        Vector3 miniLocalPos =
            (targetLocalInRoom - roomCenterLocal) * scaleFactor;

        transform.localPosition = miniLocalPos;
    }

    private void UpdateRotation()
    {
        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) return;

        transform.localRotation =
            Quaternion.LookRotation(forward, Vector3.up);
    }
}
