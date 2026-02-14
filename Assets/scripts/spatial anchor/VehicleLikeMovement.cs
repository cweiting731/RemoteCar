using UnityEngine;

public class VehicleLikeMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 90.0f;

    [Header("Info Label (Optional)")]
    private InfoLabel infoLabel;

    private bool isInitialTransformRecorded = false;
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private void Start()
    {
        infoLabel = GameObject.FindObjectOfType<InfoLabel>();
    }

    void Update()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);

        float moveInput = stick.y;
        float turnInput = stick.x;

        // 前進 / 後退
        Vector3 move = transform.forward * moveInput * moveSpeed * Time.deltaTime;
        transform.position += move;

        // 左右轉（車子邏輯）
        float turn = turnInput * turnSpeed * Time.deltaTime;
        transform.Rotate(0f, turn, 0f);

        // Debug.Log("World Pos: " + transform.position);
        // Debug.Log("Local Pos: " + transform.localPosition);

        // 5秒一次自動顯示相對 Transform（測試用）
        if (isInitialTransformRecorded && Time.frameCount % (5 * 60) == 0) // 每5秒（假設60 FPS）
        {
            // 取得相對 Transform
            var (relativePos, relativeRot) = GetRelativeTransform();
            // 做 relativePos, relativeRot 的驗證
            // Debug.Log($"[VehicleLikeMovement] Relative Transform: Position={relativePos}, Rotation={relativeRot}");
            infoLabel?.UpdateInfo($"Relative Pos: {relativePos}\nRelative Rot: {relativeRot.eulerAngles}");
            // ApplyRelativeTransform(relativePos, relativeRot);

            // 這裡可以將 relativePos 和 relativeRot 發送給其他裝置
            // 或者用於其他邏輯
        }
    }

    // 1️⃣ 記錄初始 Transform
    // 1️⃣ 記錄初始 Transform
    public void RecordInitialTransform()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        isInitialTransformRecorded = true;
        // Debug.Log($"[VehicleLikeMovement] Recorded Initial Transform: Position={initialPosition}, Rotation={initialRotation}");
        infoLabel?.UpdateInfo($"Recorded Initial Transform:\nPosition: {initialPosition}\nRotation: {initialRotation.eulerAngles}");
    }

    // 2️⃣ 取得相對 Transform（可逆資料）
    public (Vector3 relativePosition, Quaternion relativeRotation) GetRelativeTransform()
    {
        Vector3 relativePos =
            Quaternion.Inverse(initialRotation) *
            (transform.position - initialPosition);

        Quaternion relativeRot =
            Quaternion.Inverse(initialRotation) *
            transform.rotation;

        // 實際距離
        float distance = Vector3.Distance(transform.position, initialPosition);
        // Debug.Log($"[VehicleLikeMovement] Distance from Initial Position: {distance}");

        return (relativePos, relativeRot);
    }

    // 3️⃣ 使用相對資料回推世界 Transform
    public void ApplyRelativeTransform(Vector3 relativePosition, Quaternion relativeRotation)
    {
        Vector3 worldPos =
            initialPosition +
            (initialRotation * relativePosition);

        Quaternion worldRot =
            initialRotation *
            relativeRotation;

        if (worldPos == transform.position && worldRot == transform.rotation)
        {
            Debug.Log("[VehicleLikeMovement] World Transform matches current Transform.");
        }
        else
        {
            Debug.Log($"[VehicleLikeMovement] Applying World Transform: Position={worldPos}, Rotation={worldRot}");
        }

        transform.position = worldPos;
        transform.rotation = worldRot;
    }
}
