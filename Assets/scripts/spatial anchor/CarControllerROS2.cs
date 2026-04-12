using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std; // 需要安裝 ROS-TCP-Connector 的訊息包

public class CarControllerROS2 : MonoBehaviour
{
    [Header("ROS2 Settings")]
    public string topicName = "/command/car";
    public int publishRateHz = 15; // 發布頻率 (Hz)

    [Header("Test Input")]
    public bool useKeyboardTestInput = false;
    
    // ROSConnection 負責處理與 ROS-TCP-Endpoint 的通訊
    private ROSConnection ros;

    private int th = 127;
    private int hd = 127;
    private float currentSx = 0f;
    private float currentSy = 0f;

    void Start()
    {
        // 取得 ROS 連結實例
        ros = ROSConnection.GetOrCreateInstance();
        // 註冊發布者，指定 Topic 名稱與訊息類型
        ros.RegisterPublisher<StringMsg>(topicName);
    }

    void Update()
    {
        // 測試有沒有收到任何輸入 (鍵盤、滑鼠)
        if (useKeyboardTestInput)
        {
            if (Input.anyKeyDown)
            {
                Debug.Log("[ROS2 Test Input] Any key pressed");
            }
        }
        // 測試有沒有收到鍵盤輸入
        if (useKeyboardTestInput)
        {
            if (Input.GetKeyDown(KeyCode.W)) Debug.Log("[ROS2 Test Input] W pressed - forward");
            if (Input.GetKeyDown(KeyCode.S)) Debug.Log("[ROS2 Test Input] S pressed - backward");
            if (Input.GetKeyDown(KeyCode.A)) Debug.Log("[ROS2 Test Input] A pressed - left");
            if (Input.GetKeyDown(KeyCode.D)) Debug.Log("[ROS2 Test Input] D pressed - right");
        }
        UpdateInput();

        // 控制發布頻率
        if (Time.frameCount % (60 / publishRateHz) == 0) // 假設遊戲運行在60 FPS，這樣可以達到指定的發布頻率
        {
            PublishCarCommand();
        }
    }

    void UpdateInput()
    {
        // ===== 取得輸入 =====
        if (useKeyboardTestInput)
        {
            // 鍵盤上排數字鍵 1/2/3/4 測試輸入
            // 基準值 127，按下方向鍵時偏移 +-50
            int center = 127;
            int delta = 50;
            th = center;
            hd = center;

            // 1: forward, 2: backward, 3: left, 4: right
            if (Input.GetKey(KeyCode.W)) th = center - delta;
            else if (Input.GetKey(KeyCode.S)) th = center + delta;

            if (Input.GetKey(KeyCode.A)) hd = center + delta;
            else if (Input.GetKey(KeyCode.D)) hd = center - delta;
            //th = 200; // 固定前進
            //hd = 200; // 固定右轉
        }
        else
        {
            // 原本手把輸入
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            currentSx = stick.x;  // 左右
            currentSy = stick.y;  // 前後

            // 原本 0-255 的轉換
            hd = (int)((-currentSx + 1f) * 0.5f * 255f);
            th = (int)((-currentSy + 1f) * 0.5f * 255f);
        }
    }

    void PublishCarCommand()
    {
        if (useKeyboardTestInput)
        {
             Debug.Log($"[ROS2 Test Input] Keyboard: th={th}, hd={hd} (W/S/A/D)");
        }
        else
        {
            Debug.Log($"[ROS2 Input] Thumbstick: sx={currentSx:F2}, sy={currentSy:F2} => th={th}, hd={hd}");
        }

        // ===== 封裝成 ROS2 String 訊息 =====
        string cmdString = $"th={th},hd={hd}";
        StringMsg msg = new StringMsg(cmdString);

        // ===== 發送至 ROS2 =====
        ros.Publish(topicName, msg);

        // 調試顯示
        Debug.Log($"[ROS2 Publish] {topicName}: {cmdString}");
    }
}