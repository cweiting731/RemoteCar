using System.Collections.Generic;
using TMPro;
using RosMessageTypes.Std;
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
            private float displayLatencyMs = -1f;
            private float displayFps = -1f;

            public float GetMbps()
            {
                return Mbps;
            }

            public void SetMbps(float mbps)
            {
                Mbps = mbps;
            }

            public float GetDisplayLatencyMs()
            {
                return displayLatencyMs;
            }

            public void SetDisplayLatencyMs(float latencyMs)
            {
                displayLatencyMs = latencyMs;
            }

            public float GetDisplayFps()
            {
                return displayFps;
            }

            public void SetDisplayFps(float fps)
            {
                displayFps = fps;
            }
        }

        public TextMeshProUGUI infoText;
        public UIRealtimeGraph realtimeGraph;
        public ROS2Info[] ros2Info;
        public float infoUpdateIntervalSeconds = 1f;

        private readonly object throughputLock = new object();
        private readonly Dictionary<string, long> throughputBytesByTopic = new Dictionary<string, long>();
        private readonly Dictionary<string, int> displayedFramesByTopic = new Dictionary<string, int>();
        private readonly Dictionary<string, string> runtimeInfoByKey = new Dictionary<string, string>();
        private ROSConnection ros;
        private float infoTimer = 0f;
        private const double MinimumPlausibleUnixSeconds = 946684800.0; // 2000-01-01 UTC
        private const double MaximumPlausibleDisplayLatencySeconds = 60.0;

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

            UpdateTopicStatsFromRecordedData(infoTimer);
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
                    if (!ros2.isTransport)
                    {
                        float latencyMs = ros2.GetDisplayLatencyMs();
                        info += latencyMs >= 0f ? $"  Display Latency: {latencyMs:F1} ms\n" : "  Display Latency: -- ms\n";

                        float displayFps = ros2.GetDisplayFps();
                        if (displayFps >= 0f)
                        {
                            info += $"  Display FPS: {displayFps:F1}\n";
                        }
                    }
                }
            }

            if (runtimeInfoByKey.Count > 0)
            {
                foreach (var item in runtimeInfoByKey)
                {
                    if (!string.IsNullOrEmpty(item.Value))
                    {
                        info += $"{item.Value}\n";
                    }
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

        public void SetRuntimeInfo(string key, string info)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (string.IsNullOrEmpty(info))
            {
                runtimeInfoByKey.Remove(key);
                return;
            }

            runtimeInfoByKey[key] = info;
        }

        public void ClearRuntimeInfo(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            runtimeInfoByKey.Remove(key);
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

        public void RecordTopicDisplayLatency(string topicName, HeaderMsg header)
        {
            RecordTopicDisplayLatency(topicName, header, -1.0);
        }

        public void RecordTopicDisplayLatencyFromHeader(string topicName, HeaderMsg header)
        {
            if (header?.stamp == null)
            {
                SetTopicDisplayLatency(topicName, -1f);
                return;
            }

            RecordTopicDisplayLatencyFromHeader(topicName, header.stamp.sec, header.stamp.nanosec);
        }

        public void RecordTopicDisplayLatencyFromHeader(string topicName, double stampSeconds, double stampNanoseconds)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                return;
            }

            double sentSeconds = stampSeconds + stampNanoseconds * 1e-9;
            if (sentSeconds <= 0.0)
            {
                SetTopicDisplayLatency(topicName, -1f);
                return;
            }

            double nowSeconds = GetCurrentUnixSeconds();
            if (TryGetHeaderLatencyMs(sentSeconds, nowSeconds, out float latencyMs))
            {
                SetTopicDisplayLatency(topicName, latencyMs);
                return;
            }

            SetTopicDisplayLatency(topicName, -1f);
        }

        public void RecordTopicDisplayLatency(string topicName, HeaderMsg header, double fallbackReceiveUnixSeconds)
        {
            if (header?.stamp == null)
            {
                RecordReceiveToDisplayLatency(topicName, fallbackReceiveUnixSeconds);
                return;
            }

            RecordTopicDisplayLatency(topicName, header.stamp.sec, header.stamp.nanosec, fallbackReceiveUnixSeconds);
        }

        public void RecordTopicDisplayLatency(string topicName, double stampSeconds, double stampNanoseconds)
        {
            RecordTopicDisplayLatency(topicName, stampSeconds, stampNanoseconds, -1.0);
        }

        public void RecordTopicDisplayLatency(string topicName, double stampSeconds, double stampNanoseconds, double fallbackReceiveUnixSeconds)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                return;
            }

            double nowUnixSeconds = GetCurrentUnixSeconds();
            double sentUnixSeconds = stampSeconds + stampNanoseconds * 1e-9;
            if (!TryGetHeaderLatencyMs(sentUnixSeconds, nowUnixSeconds, out float latencyMs))
            {
                RecordReceiveToDisplayLatency(topicName, fallbackReceiveUnixSeconds, nowUnixSeconds);
                return;
            }

            SetTopicDisplayLatency(topicName, latencyMs);
        }

        public static double GetCurrentUnixSeconds()
        {
            return (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)).TotalSeconds;
        }

        private static bool IsPlausibleUnixStamp(double stampUnixSeconds, double nowUnixSeconds)
        {
            return stampUnixSeconds >= MinimumPlausibleUnixSeconds && stampUnixSeconds <= nowUnixSeconds + 60.0;
        }

        private static bool TryGetHeaderLatencyMs(double stampUnixSeconds, double nowUnixSeconds, out float latencyMs)
        {
            latencyMs = -1f;
            if (!IsPlausibleUnixStamp(stampUnixSeconds, nowUnixSeconds))
            {
                return false;
            }

            double latencySeconds = nowUnixSeconds - stampUnixSeconds;
            if (latencySeconds < 0.0 || latencySeconds > MaximumPlausibleDisplayLatencySeconds)
            {
                return false;
            }

            latencyMs = (float)(latencySeconds * 1000.0);
            return true;
        }

        private void RecordReceiveToDisplayLatency(string topicName, double receiveUnixSeconds)
        {
            RecordReceiveToDisplayLatency(topicName, receiveUnixSeconds, GetCurrentUnixSeconds());
        }

        private void RecordReceiveToDisplayLatency(string topicName, double receiveUnixSeconds, double nowUnixSeconds)
        {
            if (receiveUnixSeconds <= 0.0)
            {
                SetTopicDisplayLatency(topicName, -1f);
                return;
            }

            float latencyMs = (float)((nowUnixSeconds - receiveUnixSeconds) * 1000.0);
            SetTopicDisplayLatency(topicName, Mathf.Max(0f, latencyMs));
        }

        public void SetTopicDisplayLatency(string topicName, float latencyMs)
        {
            if (ros2Info == null)
            {
                return;
            }

            foreach (var ros2 in ros2Info)
            {
                if (ros2 != null && ros2.topicName == topicName)
                {
                    ros2.SetDisplayLatencyMs(latencyMs);
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

        public void RecordTopicDisplayedFrame(string topicName)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                return;
            }

            lock (throughputLock)
            {
                if (!displayedFramesByTopic.ContainsKey(topicName))
                {
                    displayedFramesByTopic.Add(topicName, 0);
                }

                displayedFramesByTopic[topicName]++;
            }
        }

        private void UpdateTopicStatsFromRecordedData(float elapsedSeconds)
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

                topicNames = new List<string>(displayedFramesByTopic.Keys);
                foreach (string topicName in topicNames)
                {
                    float fps = displayedFramesByTopic[topicName] / elapsedSeconds;
                    SetTopicDisplayFps(topicName, fps);
                    displayedFramesByTopic[topicName] = 0;
                }
            }
        }

        private void SetTopicDisplayFps(string topicName, float fps)
        {
            if (ros2Info == null)
            {
                return;
            }

            foreach (var ros2 in ros2Info)
            {
                if (ros2 != null && ros2.topicName == topicName)
                {
                    ros2.SetDisplayFps(fps);
                    break;
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
