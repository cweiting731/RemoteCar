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

        public static void Reconnect(MonoBehaviour runner, float delaySeconds = DefaultReconnectDelaySeconds)
        {
            if (runner == null)
            {
                ReconnectNow();
                return;
            }

            if (isReconnecting)
            {
                Debug.Log("[ROS2] Reconnect is already running.");
                return;
            }

            runner.StartCoroutine(ReconnectCoroutine(Mathf.Max(0f, delaySeconds)));
        }

        public static void ReconnectNow()
        {
            ROSConnection ros = ROSConnection.GetOrCreateInstance();
            ros.Disconnect();
            ros.Connect();
            Debug.Log($"[ROS2] Reconnected to {ros.RosIPAddress}:{ros.RosPort}");
        }

        private static IEnumerator ReconnectCoroutine(float delaySeconds)
        {
            isReconnecting = true;

            ROSConnection ros = ROSConnection.GetOrCreateInstance();
            ros.Disconnect();

            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            ros.Connect();
            Debug.Log($"[ROS2] Reconnected to {ros.RosIPAddress}:{ros.RosPort}");

            isReconnecting = false;
        }
    }
}
