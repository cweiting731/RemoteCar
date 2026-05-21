using System.Collections.Generic;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using Main.UI;

namespace ROS2
{
    public class ROS2InfoManager : MonoBehaviour
    {
        [System.Serializable]
        public class ROS2Info
        {
            public string topicName;
            public bool isTransport; // true = Transmit(to ROS2), false = Receive(from ROS2)
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
        public UIRealtimeGraph realtimeGraph;
        public ROS2Info[] ros2Info;
        public float infoUpdateIntervalSeconds = 1f;

        private readonly object throughputLock = new object();
        private readonly Dictionary<string, long> throughputBytesByTopic = new Dictionary<string, long>();
        private ROSConnection ros;
        private float infoTimer = 0f;

        private void Start()
        {
            ros = ROSConnection.GetOrCreateInstance();
            UpdateInfo();
        }

        public void Update()
        {
            infoTimer += Time.deltaTime;
            float updateInterval = Mathf.Max(0.1f, infoUpdateIntervalSeconds);
            if (infoTimer < updateInterval)
            {
                return;
            }

            UpdateTopicMbpsFromRecordedBytes(infoTimer);
            UpdateInfo();
            UpdateGraph();
            infoTimer = 0f;
        }

        public void UpdateInfo()
        {
            string info = "";
            string status = ros != null && ros.HasConnectionError ? "<color=red>Disconnected</color>" : "<color=green>Connected</color>";
            info += $"ROS2 Connection: {status}\n";

            if (ros2Info != null)
            {
                foreach (var ros2 in ros2Info)
                {
                    if (ros2 == null)
                    {
                        continue;
                    }
                    // Debug.Log($"[ROS2InfoManager] Topic: {ros2.topicName}, {(ros2.isTransport ? "Transmit" : "Receive")}: {ros2.GetMbps()} Mbps");
                    info += $"{ros2.topicName}\n  {(ros2.isTransport ? "Transmit" : "Receive")}: {ros2.GetMbps():F3} Mbps\n";
                }
            }

            if (infoText != null)
            {
                infoText.text = info;
            }
            else
            {
                Debug.LogWarning("[ROS2InfoManager] Info TextMeshProUGUI is not assigned.");
            }
        }

        public void SetTopicMbps(string topicName, float mbps)
        {
            if (ros2Info == null)
            {
                return;
            }

            foreach (var ros2 in ros2Info)
            {
                if (ros2 != null && ros2.topicName == topicName)
                {
                    ros2.SetMbps(mbps);
                    break;
                }
            }
        }

        public void RecordTopicBytes(string topicName, long byteCount)
        {
            if (string.IsNullOrEmpty(topicName) || byteCount <= 0)
            {
                return;
            }

            lock (throughputLock)
            {
                if (!throughputBytesByTopic.ContainsKey(topicName))
                {
                    throughputBytesByTopic.Add(topicName, 0);
                }

                throughputBytesByTopic[topicName] += byteCount;
            }
        }

        private void UpdateTopicMbpsFromRecordedBytes(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f)
            {
                return;
            }

            lock (throughputLock)
            {
                List<string> topicNames = new List<string>(throughputBytesByTopic.Keys);
                foreach (string topicName in topicNames)
                {
                    float mbps = throughputBytesByTopic[topicName] * 8f / elapsedSeconds / 1_000_000f;
                    SetTopicMbps(topicName, mbps);
                    throughputBytesByTopic[topicName] = 0;
                }
            }
        }

        private void UpdateGraph()
        {
            if (realtimeGraph == null || ros2Info == null)
            {
                return;
            }

            float totalInputMbps = 0f;
            float totalOutputMbps = 0f;
            foreach (var ros2 in ros2Info)
            {
                if (ros2 == null)
                {
                    continue;
                }

                if (ros2.isTransport)
                {
                    totalInputMbps += ros2.GetMbps();
                }
                else
                {
                    totalOutputMbps += ros2.GetMbps();
                }
            }

            realtimeGraph.AddSample("Transmit", totalInputMbps);
            realtimeGraph.AddSample("Receive", totalOutputMbps);
        }

        public void ReconnectROS2()
        {
            Ros2ReconnectHelper.Reconnect(this);
        }
    }
}
