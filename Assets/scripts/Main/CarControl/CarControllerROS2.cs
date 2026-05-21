using System.Text;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using RosMessageTypes.Std;
using ROS2;

namespace CarControl
{
    public class CarControllerROS2 : MonoBehaviour
    {
        [Header("ROS2 Settings")]
        public string topicName = "/command/car";
        public int publishRateHz = 15;
        public float keepAliveSeconds = 0.25f;

        [Header("Info")]
        public ROS2InfoManager ros2InfoManager;

        [Header("Input Source")]
        public OVRInputGetter ovrInputGetter;
        public CarVisualizer carVisualizer;

        private ROSConnection ros;

        private int th = 127;
        private int hd = 127;
        private int lastPublishedTh = int.MinValue;
        private int lastPublishedHd = int.MinValue;
        private float currentSx = 0f;
        private float currentSy = 0f;

        private float publishTimer = 0f;
        private float keepAliveTimer = 0f;

        private bool singleHandMode = true;
        private bool doubleHandMode = false;

        private void Start()
        {
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<StringMsg>(topicName);
        }

        private void Update()
        {
            UpdateInput();

            float publishInterval = 1.0f / Mathf.Max(1, publishRateHz);
            publishTimer += Time.deltaTime;
            keepAliveTimer += Time.deltaTime;

            while (publishTimer >= publishInterval)
            {
                publishTimer -= publishInterval;

                bool changed = th != lastPublishedTh || hd != lastPublishedHd || true;
                bool keepAliveDue = keepAliveTimer >= Mathf.Max(0.05f, keepAliveSeconds);

                if (changed || keepAliveDue)
                {
                    PublishCarCommand();
                    keepAliveTimer = 0f;
                }
            }
        }

        private void UpdateInput()
        {
            if (ovrInputGetter == null)
            {
                return;
            }

            if (singleHandMode)
            {
                currentSx = ovrInputGetter.leftStickX;
                currentSy = ovrInputGetter.leftStickY;

                hd = (int)((-currentSx + 1f) * 0.5f * 255f);
                th = (int)((-currentSy + 1f) * 0.5f * 255f);
                carVisualizer?.SetInput(currentSx, currentSy);
            }
            else if (doubleHandMode)
            {
                currentSx = ovrInputGetter.rightStickX;
                currentSy = ovrInputGetter.leftStickY;

                hd = (int)((-currentSx + 1f) * 0.5f * 255f);
                th = (int)((-currentSy + 1f) * 0.5f * 255f);
                carVisualizer?.SetInput(currentSx, currentSy);
            }
        }

        private void PublishCarCommand()
        {
            string cmdString = $"th={th},hd={hd}";
            StringMsg msg = new StringMsg(cmdString);

            ros.Publish(topicName, msg);
            lastPublishedTh = th;
            lastPublishedHd = hd;

            long messageSizeBytes = Encoding.UTF8.GetByteCount(cmdString);
            ros2InfoManager?.RecordTopicBytes(topicName, messageSizeBytes);
        }

        public void SetSingleHandMode(bool enabled)
        {
            singleHandMode = enabled;
            if (enabled)
            {
                doubleHandMode = false;
            }
        }

        public void SetDoubleHandMode(bool enabled)
        {
            doubleHandMode = enabled;
            if (enabled)
            {
                singleHandMode = false;
            }
        }

        public void ReconnectROS2()
        {
            Ros2ReconnectHelper.Reconnect(this);
        }
    }
}
