using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CubeMovement : MonoBehaviour
{
    public float moveForce = 50f;
    public float maxSpeed = 5f;
    public float jumpForce = 5f;
    public float airControl = 0.4f;
    public Transform cameraTransform;

    private Rigidbody rb;
    private bool isGrounded;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        // StartCoroutine(DelayEnablePhysics());
    }

    void FixedUpdate()
    {
        CheckGrounded();
        HandleMovement();
    }

    void HandleMovement()
    {
        // 🟢 左手搖桿（x=左右, y=前後）
        Vector2 axis = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector3 inputDir = new Vector3(axis.x, 0, axis.y);
        if (inputDir.sqrMagnitude < 0.001f) return;

        // 以相機方向為基準移動
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0; camRight.y = 0;
        Vector3 moveDir = (camForward * inputDir.z + camRight * inputDir.x).normalized;

        Vector3 horizontalVel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        if (horizontalVel.magnitude < maxSpeed)
            rb.AddForce(moveDir * moveForce * (isGrounded ? 1f : airControl), ForceMode.Force);

        // ⬆️ 跳躍（A 鍵 / Button.One）
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch) && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
    }

    void CheckGrounded()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down, 0.6f);
    }
}
