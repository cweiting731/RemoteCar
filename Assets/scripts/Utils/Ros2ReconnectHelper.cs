using System.Collections;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

namespace ROS2
{
    public static class Ros2ReconnectHelper
    {
        private const float DefaultReconnectDelaySeconds = 0.25f;
        private static bool isReconnecting;

        public static bool IsReconnecting => isReconnecting;

        public static void AutoReconnectIfNeeded(
            MonoBehaviour runner,
            ROSConnection ros,
            ref float nextReconnectTime,
            float reconnectIntervalSeconds,
            string sourceLabel = null)
        {
            if (ros == null || !ros.HasConnectionError)
            {
                return;
            }

            ReconnectIfDue(runner, ref nextReconnectTime, reconnectIntervalSeconds, sourceLabel, "connection error");
        }

        public static void ReconnectIfDue(
            MonoBehaviour runner,
            ref float nextReconnectTime,
            float reconnectIntervalSeconds,
            string sourceLabel = null,
            string reason = null)
        {
            float currentTime = Time.unscaledTime;
            if (currentTime < nextReconnectTime)
            {
                return;
            }

            nextReconnectTime = currentTime + Mathf.Max(0.1f, reconnectIntervalSeconds);

            if (isReconnecting)
            {
                return;
            }

            if (!string.IsNullOrEmpty(sourceLabel))
            {
                string reasonText = string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})";
                Debug.Log($"[ROS2] Auto reconnect requested by {sourceLabel}{reasonText}.");
            }

            Reconnect(runner);
        }

        public static void Reconnect(
            MonoBehaviour runner,
            float delaySeconds = DefaultReconnectDelaySeconds,
            bool forceDisconnect = false)
        {
            if (runner == null)
            {
                ReconnectNow(forceDisconnect);
                return;
            }

            if (isReconnecting)
            {
                Debug.Log("[ROS2] Reconnect is already running.");
                return;
            }

            runner.StartCoroutine(ReconnectCoroutine(Mathf.Max(0f, delaySeconds), forceDisconnect));
        }

        public static void ReconnectNow(bool forceDisconnect = false)
        {
            ROSConnection ros = ROSConnection.GetOrCreateInstance();
            if (forceDisconnect)
            {
                ros.Disconnect();
            }

            ros.Connect();
            Debug.Log($"[ROS2] Connect requested to {ros.RosIPAddress}:{ros.RosPort}");
        }

        private static IEnumerator ReconnectCoroutine(float delaySeconds, bool forceDisconnect)
        {
            isReconnecting = true;

            try
            {
                ROSConnection ros = ROSConnection.GetOrCreateInstance();
                if (forceDisconnect)
                {
                    ros.Disconnect();
                }

                if (delaySeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(delaySeconds);
                }

                ros.Connect();
                Debug.Log($"[ROS2] Connect requested to {ros.RosIPAddress}:{ros.RosPort}");
            }
            finally
            {
                isReconnecting = false;
            }
        }
    }
}
