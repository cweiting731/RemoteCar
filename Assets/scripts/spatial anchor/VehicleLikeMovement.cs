using UnityEngine;

public class VehicleLikeMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 90.0f;

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
    }
}
