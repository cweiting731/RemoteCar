using TMPro;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
namespace ROS2
{
    public class ROS2InfoManager : MonoBehaviour
    {
        [System.Serializable]
        public class ROS2Info
        {
            public string topicName;
            public bool isTransport; // true = 輸入頻寬(to Ros2), false = 輸出頻寬(from Ros2)
            private float Mbps;

            public float GetMbps()
            {
                return Mbps;
            }
            public void SetMbps(float mbps)
            {
                Mbps = mbps;
            }
        }
        public TextMeshProUGUI infoText;
        public UIRealtimeGraph realtimeGraph; // ▲ 可選的即時圖表元件，用於視覺化頻寬變化
        public ROS2Info[] ros2Info;

        private ROSConnection ros;
        private void Start()
        {
            ros = ROSConnection.GetOrCreateInstance();       
            UpdateInfo(); 
        }

        public void Update()
        {
            // 每秒更新一次 ROS2 連線狀態與頻寬資訊，並更新即時圖表
            if (Time.frameCount % Mathf.RoundToInt(1f / Time.deltaTime) == 0) // 每秒更新一次
            {
                UpdateInfo();
                if (realtimeGraph != null)
                {
                    // 分成輸入和輸出兩條線，加總同一類型的 Mbps 以簡化圖表
                    float totalInputMbps = 0f;
                    float totalOutputMbps = 0f;
                    foreach (var ros2 in ros2Info)
                    {
                        if (ros2.isTransport)
                            totalInputMbps += ros2.GetMbps();
                        else
                            totalOutputMbps += ros2.GetMbps();
                    }
                    realtimeGraph.AddSample("Transmit", totalInputMbps); // 輸入線
                    realtimeGraph.AddSample("Receive", totalOutputMbps); // 輸出線
                }
            }
        }

        public void UpdateInfo()
        {
            string info = "";
            string status = ros.HasConnectionError ? "<color=red>Disconnected</color>" : "<color=green>Connected</color>";
            info += $"ROS2 Connection: {status}\n";
            foreach (var ros2 in ros2Info)
            {
                info += $"{ros2.topicName}\n  {(ros2.isTransport ? "Transmit" : "Receive")}: {ros2.GetMbps():F2} Mbps\n";
            }
            if (infoText != null)
                infoText.text = info;
            else Debug.LogWarning("[ROS2InfoManager] Info TextMeshProUGUI is not assigned.");
        }

        public void SetTopicMbps(string topicName, float mbps)
        {
            foreach (var ros2 in ros2Info)
            {
                if (ros2.topicName == topicName)
                {
                    ros2.SetMbps(mbps);
                    break;
                }
            }
        }
    }
}