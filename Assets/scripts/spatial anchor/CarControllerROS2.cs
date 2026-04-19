using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std; // 需要安裝 ROS-TCP-Connector 的訊息包

public class CarControllerROS2 : MonoBehaviour
{
    [Header("ROS2 Settings")]
    public string topicName = "/command/car";
    public int publishRateHz = 15; // 發布頻率 (Hz)
    public float keepAliveSeconds = 0.25f; // 即使指令沒變，也週期性送出避免下游 timeout
    
    // ROSConnection 負責處理與 ROS-TCP-Endpoint 的通訊
    private ROSConnection ros;

    private int th = 127;
    private int hd = 127;
    private int lastPublishedTh = int.MinValue;
    private int lastPublishedHd = int.MinValue;
    private float currentSx = 0f;
    private float currentSy = 0f;

    // ▲ 新增：用於穩定控制發布頻率的時間計時器
    private float publishTimer = 0f;
    private float keepAliveTimer = 0f;

    void Start()
    {
        // 取得 ROS 連結實例
        ros = ROSConnection.GetOrCreateInstance();
        // 註冊發布者，指定 Topic 名稱與訊息類型
        ros.RegisterPublisher<StringMsg>(topicName);
    }

    void Update()
    {
        // 每幀更新控制值，再依照發送節流規則送出 ROS 指令
        UpdateInput();

        // 控制發布頻率：用累加減法避免掉幀後計時漂移
        float publishInterval = 1.0f / Mathf.Max(1, publishRateHz);
        publishTimer += Time.deltaTime;
        keepAliveTimer += Time.deltaTime;

        while (publishTimer >= publishInterval)
        {
            publishTimer -= publishInterval;

            bool changed = (th != lastPublishedTh) || (hd != lastPublishedHd);
            bool keepAliveDue = keepAliveTimer >= Mathf.Max(0.05f, keepAliveSeconds);

            if (changed || keepAliveDue)
            {
                PublishCarCommand();
                keepAliveTimer = 0f;
            }
        }
    }

    void UpdateInput()
    {
        // 只保留左手搖桿輸入，避免鍵盤測試邏輯干擾正式控制流程
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        currentSx = stick.x;  // 左右
        currentSy = stick.y;  // 前後

        // 將 -1~1 的搖桿值轉成 0~255，方便下游控制器解析
        hd = (int)((-currentSx + 1f) * 0.5f * 255f);
        th = (int)((-currentSy + 1f) * 0.5f * 255f);
    }

    void PublishCarCommand()
    {
        // 為了避免高負載環境下(每秒15次)持續印 Log 加劇卡頓與 TCP 阻塞，先將其註解
        /*
        if (useKeyboardTestInput)
        {
             Debug.Log($"[ROS2 Test Input] Keyboard: th={th}, hd={hd} (W/S/A/D)");
        }
        else
        {
            Debug.Log($"[ROS2 Input] Thumbstick: sx={currentSx:F2}, sy={currentSy:F2} => th={th}, hd={hd}");
        }
        */

        // ===== 封裝成 ROS2 String 訊息 =====
        string cmdString = $"th={th},hd={hd}";
        StringMsg msg = new StringMsg(cmdString);

        // ===== 發送至 ROS2 =====
        ros.Publish(topicName, msg);
        lastPublishedTh = th;
        lastPublishedHd = hd;

        // 調試顯示 (拿掉避免狂洗Console導致卡頓)
        // Debug.Log($"[ROS2 Publish] {topicName}: {cmdString}");
    }
}