using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OVRInputGetter : MonoBehaviour
{
    public MiniRoomContentBuilder miniRoomContentBuilder;

    [Header("Rotate Setting")]
    public float rotateSpeed = 90f;

    private float currentSx;

    private void Update()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        currentSx = stick.x;  // 左右
        // currentSy = stick.y;  // 前後

        if (miniRoomContentBuilder != null)
        {
            float deltaYaw = currentSx * rotateSpeed * Time.deltaTime;
            miniRoomContentBuilder.RotateByInput(deltaYaw);
        }
    }
}