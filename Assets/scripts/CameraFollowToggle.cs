using UnityEngine;
using UnityEngine.XR;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction; // 🔹 for HandGrabInteractable
using Oculus.Interaction.HandGrab;


public class CameraFollowToggle : MonoBehaviour
{
    [Header("Follow Settings")]
    public Transform cube;
    public Transform centerEyeAnchor;
    public bool followRotation = true;

    [Header("Scaniverse Settings")]
    public Transform scaniverse;
    public float scaleFactor = 100f;
    public float scaleDuration = 0.8f; // 放大時間
    public float cooldown = 1f;

    [Header("Component References")]
    public CubeMovement cubeMovement;             // ✅ 指定 CubeMovement 腳本

    private bool isFollowing = false;
    private bool isScaledUp = false;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 originalScale;
    private float lastToggleTime = -999f;

    private InputDevice rightController;
    private Coroutine scalingCoroutine;
    private Rigidbody cubeRb;

    void Start()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        if (scaniverse != null)
            originalScale = scaniverse.localScale;

        if (cube != null)
            cubeRb = cube.GetComponent<Rigidbody>();

        TryInitializeController();
    }

    void TryInitializeController()
    {
        var rightDevices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.RightHand, rightDevices);
        if (rightDevices.Count > 0)
            rightController = rightDevices[0];
    }

    void Update()
    {
        if (!rightController.isValid)
            TryInitializeController();

        // 檢查 A 鍵
        if (rightController.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed) && aPressed)
        {
            if (Time.time - lastToggleTime >= cooldown)
            {
                ToggleFollowAndScale();
                lastToggleTime = Time.time;
            }
        }

        // 跟隨 Cube
        if (isFollowing && cube != null)
        {
            transform.position = cube.position;
            if (followRotation)
                transform.rotation = cube.rotation;
        }
    }

    void ToggleFollowAndScale()
    {
        isFollowing = !isFollowing;

        if (isFollowing)
        {
            originalPosition = transform.position;
            originalRotation = transform.rotation;
            Debug.Log("[Camera] Now following Cube");

            if (scaniverse != null && !isScaledUp)
            {
                if (scalingCoroutine != null) StopCoroutine(scalingCoroutine);
                scalingCoroutine = StartCoroutine(SmoothScale(scaniverse, originalScale * scaleFactor, scaleDuration));
                isScaledUp = true;

                // ✅ 停用抓取（避免手碰飛）
                SetGrabComponentsEnabled(false);

                // ✅ 通知 CubeMovement 進入放大狀態
                // if (cubeMovement != null) cubeMovement.isScaniverseScaledUp = true;
            }
        }
        else
        {
            transform.position = originalPosition;
            transform.rotation = originalRotation;
            Debug.Log("[Camera] Returned to original position");

            if (scaniverse != null && isScaledUp)
            {
                if (scalingCoroutine != null) StopCoroutine(scalingCoroutine);
                scalingCoroutine = StartCoroutine(SmoothScale(scaniverse, originalScale, scaleDuration));
                isScaledUp = false;

                // ✅ 恢復抓取
                SetGrabComponentsEnabled(true);

                // ✅ 通知 CubeMovement 回復正常狀態
                // if (cubeMovement != null) cubeMovement.isScaniverseScaledUp = false;
            }
        }
    }

    IEnumerator SmoothScale(Transform target, Vector3 targetScale, float duration)
    {
        Vector3 startScale = target.localScale;
        float time = 0f;

        // 暫時停用 Cube 的物理
        if (cubeRb != null)
            cubeRb.isKinematic = true;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, time / duration);
            target.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }

        target.localScale = targetScale;

        // 放大完成後重新啟用物理
        if (cubeRb != null)
            cubeRb.isKinematic = false;
    }

    // 🔹 關閉所有 Grab 類元件
    void SetGrabComponentsEnabled(bool enabled)
    {
        if (scaniverse == null) return;

        var grabComponents = scaniverse.GetComponentsInChildren<MonoBehaviour>();
        foreach (var comp in grabComponents)
        {
            if (comp == null) continue;
            string name = comp.GetType().Name;
            if (name.Contains("GrabInteractable") || name.Contains("Grabbable"))
            {
                comp.enabled = enabled;
                Debug.Log($"[Scaniverse] {(enabled ? "啟用" : "停用")} {name}");
            }
        }
    }
}
