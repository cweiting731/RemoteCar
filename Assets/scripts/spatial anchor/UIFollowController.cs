using UnityEngine;
using UnityEngine.UI;

public class UIFollowController : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public Toggle anchorToggle;

    [Header("Follow Settings")]
    public float followDistance = 1.5f;
    public float followSpeed = 5f;
    public float rotateSpeed = 5f;
    public float distanceThreshold = 0.2f;
    public float verticalOffset = -0.2f; // 垂直偏移量，用來調整面板高低（負值表示往下）

    private bool isAnchored = false;

    void Start()
    {
        anchorToggle.onValueChanged.AddListener(OnToggleChanged);
    }

    void OnToggleChanged(bool value)
    {
        isAnchored = value;
        Debug.Log($"[UIFollowController] Anchor toggled: {isAnchored}");

        if (isAnchored)
        {
            // 建立 Spatial Anchor (加上 OVRSpatialAnchor 元件將 UI 釘住目前的真實空間位置)
            if (GetComponent<OVRSpatialAnchor>() == null)
            {
                gameObject.AddComponent<OVRSpatialAnchor>();
            }
        }
        else
        {
            // 刪除 Spatial Anchor (移除元件即可取消錨定，讓 UI 再次跟隨)
            OVRSpatialAnchor anchor = GetComponent<OVRSpatialAnchor>();
            if (anchor != null)
            {
                Destroy(anchor);
            }
        }
    }

    void Update()
    {
        if (isAnchored) return;

        // ===== Lazy Follow =====
        Vector3 targetPos = cameraTransform.position +
                            cameraTransform.forward * followDistance +
                            Vector3.up * verticalOffset;

        float distance = Vector3.Distance(transform.position, targetPos);

        if (distance > distanceThreshold)
        {
            // 平滑移動
            transform.position = Vector3.Lerp(
                transform.position,
                targetPos,
                Time.deltaTime * followSpeed
            );
        }

        // 面向使用者（全方位，包含上下仰角）
        Vector3 lookDir = transform.position - cameraTransform.position;

        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * rotateSpeed
            );
        }
    }
}