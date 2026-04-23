using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OVRInputGetter : MonoBehaviour
{
    public MiniRoomContentBuilder miniRoomContentBuilder;

    [Header("Rotate Setting")]
    public float rotateSpeed = 90f;

    // ===== 左控制器輸入 =====
    [HideInInspector] public float leftStickX = 0f;  // 左手搖桿左右（目前未使用）
    [HideInInspector] public float leftStickY = 0f;  // 左手搖桿前後（控制小車前進後退）

    // ===== 右控制器輸入 =====
    [HideInInspector] public float rightStickX = 0f; // 右手搖桿左右（控制小車轉向，當握持鍵未按下時）
    [HideInInspector] public float rightStickY = 0f; // 右手搖桿前後（控制小車前進後退）

    [HideInInspector] public bool isRightGripPressed = false; // 右手握持鍵狀態

    private void Update()
    {
        // ===== 左手搖桿輸入（前後軸 - 控制小車前進後退） =====
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        leftStickX = leftStick.x;
        leftStickY = leftStick.y;

        // ===== 右手搖桿輸入（左右軸） =====
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        rightStickX = rightStick.x;
        rightStickY = rightStick.y;
        
        isRightGripPressed = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        Debug.Log($"[OVRInputGetter] Left Stick: ({leftStickX:F2}, {leftStickY:F2}), Right Stick: ({rightStickX:F2}, {rightStickY:F2}), Right Grip: {isRightGripPressed}");
    }
}