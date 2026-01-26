using UnityEngine;

public class PrefabSpawner : MonoBehaviour
{
    [Header("Prefab to Spawn")]
    public GameObject prefab;

    [Header("Right Controller Anchor")]
    public Transform rightController;

    [Header("MiniRoomContentBuilder Reference")]
    public MiniRoomContentBuilder miniRoomContentBuilder;

    private GameObject currentInstance;
    private bool lastButtonState = false;
    private bool lastButton2State = false;

    void Update()
    {
        bool buttonPressed = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);

        // 只在「剛按下」時觸發
        if (buttonPressed && !lastButtonState)
        {
            TogglePrefab();
        }

        lastButtonState = buttonPressed;

        // 監聽第二個按鍵，重設旋轉
        bool button2Pressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        
        if (button2Pressed && !lastButton2State && currentInstance != null)
        {
            ResetRotation();
        }

        lastButton2State = button2Pressed;
    }

    void TogglePrefab()
    {
        if (currentInstance == null)
        {
            currentInstance = Instantiate(
                prefab,
                rightController.position,
                rightController.rotation
            );
            // 如果有 MiniRoomContentBuilder，設定 MarkerController
            if (miniRoomContentBuilder != null)
            {
                miniRoomContentBuilder.RegisterCar(currentInstance.transform);
            }
        }
        else
        {
            miniRoomContentBuilder.ClearCar();
            Destroy(currentInstance);
            currentInstance = null;
        }
    }

    void ResetRotation()
    {
        Vector3 currentRotation = currentInstance.transform.eulerAngles;
        currentInstance.transform.eulerAngles = new Vector3(0, currentRotation.y, 0);
    }
}
