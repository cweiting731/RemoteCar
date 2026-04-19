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

    [Header("Follow Mode")]
    public bool horizontalToEyeOnly = true; // 只在水平面跟隨，避免頭部上下擺動造成面板忽高忽低
    public bool yawOnlyRotation = true; // 只做左右朝向，保持平面垂直不傾斜

    private bool isAnchored = false;

    void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (anchorToggle != null)
        {
            anchorToggle.onValueChanged.AddListener(OnToggleChanged);
        }
        else
        {
            Debug.LogWarning("[UIFollowController] anchorToggle is not assigned.");
        }
    }

    void OnDestroy()
    {
        if (anchorToggle != null)
        {
            anchorToggle.onValueChanged.RemoveListener(OnToggleChanged);
        }
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
        if (cameraTransform == null) return;
        if (isAnchored) return;

        // ===== Lazy Follow =====
        Vector3 followForward = cameraTransform.forward;
        if (horizontalToEyeOnly)
        {
            followForward.y = 0f;
            if (followForward.sqrMagnitude < 0.0001f)
            {
                followForward = transform.forward;
                followForward.y = 0f;
            }
            followForward.Normalize();
        }

        Vector3 targetPos = cameraTransform.position + followForward * followDistance + Vector3.up * verticalOffset;

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

        // 面向使用者：可切換為只做水平旋轉，讓平面保持直立
        Vector3 lookDir = transform.position - cameraTransform.position;
        if (yawOnlyRotation)
        {
            lookDir.y = 0f;
        }

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