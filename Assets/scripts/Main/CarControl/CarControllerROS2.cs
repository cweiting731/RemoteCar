using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using TMPro; // 需要安裝 ROS-TCP-Connector 的訊息包

namespace CarControl
{
    public class CarControllerROS2 : MonoBehaviour
    {
        [Header("ROS2 Settings")]
        public string topicName = "/command/car";
        public int publishRateHz = 15; // 發布頻率 (Hz)
        public float keepAliveSeconds = 0.25f; // 即使指令沒變，也週期性送出避免下游 timeout
        
        [Header("Info")]
        public TextMeshProUGUI controlInfo;

        [Header("Input Source")]
        public OVRInputGetter ovrInputGetter;  // ▲ 統一輸入來源
        public CarVisualizer carVisualizer; // ▲ 可選的視覺化元件，讓玩家看到輸入反饋
        
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

        // Enum
        private CarControlMode carControlMode = CarControlMode.SingleHand;

        void Start()
        {
            // 取得 ROS 連結實例
            ros = ROSConnection.GetOrCreateInstance();
            // 註冊發布者，指定 Topic 名稱與訊息類型
            ros.RegisterPublisher<StringMsg>(topicName);

            UpdateControlInfoUI();
        }

        void Update()
        {
            // 每幀更新控制值，再依照發送節流規則送出 ROS 指令
            UpdateInput();
            UpdateControlInfoUI();

            // 控制發布頻率：用累加減法避免掉幀後計時漂移
            float publishInterval = 1.0f / Mathf.Max(1, publishRateHz);
            publishTimer += Time.deltaTime;
            keepAliveTimer += Time.deltaTime;

            while (publishTimer >= publishInterval)
            {
                publishTimer -= publishInterval;

                bool changed = (th != lastPublishedTh) || (hd != lastPublishedHd) || true; // ▲ 強制每次都發送，確保下游持續收到指令避免 timeout
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
            // 從 OVRInputGetter 統一獲取輸入值
            if (ovrInputGetter == null) return;

            if (carControlMode == CarControlMode.SingleHand)
            {
                currentSx = ovrInputGetter.leftStickX;
                currentSy = ovrInputGetter.leftStickY;

                hd = (int)((currentSx + 1f) * 0.5f * 255f);
                th = (int)((-currentSy + 1f) * 0.5f * 255f);
            }
            else if (carControlMode == CarControlMode.DoubleHand)
            {
                currentSx = ovrInputGetter.rightStickX;
                currentSy = ovrInputGetter.leftStickY;

                hd = (int)((currentSx + 1f) * 0.5f * 255f);
                th = (int)((-currentSy + 1f) * 0.5f * 255f);
            }
            carVisualizer?.SetInput(currentSx, currentSy); // 更新視覺化反饋
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

        void UpdateControlInfoUI()
        {
            if (controlInfo == null)
            {
                return;
            }

            int forwardPercent = Mathf.RoundToInt(Mathf.Clamp01(currentSy) * 100f);
            int backwardPercent = Mathf.RoundToInt(Mathf.Clamp01(-currentSy) * 100f);
            int rightPercent = Mathf.RoundToInt(Mathf.Clamp01(currentSx) * 100f);
            int leftPercent = Mathf.RoundToInt(Mathf.Clamp01(-currentSx) * 100f);

            controlInfo.text =
                "Car Control Info\n" +
                $"  th: {th}\n" +
                $"  hd: {hd}\n" +
                $"  Forward: {forwardPercent}%\n" +
                $"  Right: {rightPercent}%\n" +
                $"  Left: {leftPercent}%\n" +
                $"  Backward: {backwardPercent}%";
        }

        public void SetCarControlMode(CarControlMode mode)
        {
            carControlMode = mode;
            // 根據模式調整控制邏輯（如果需要）
            // 目前在 UpdateInput 中統一處理，這裡可以擴展為不同模式的專屬邏輯
        }
    }
}
