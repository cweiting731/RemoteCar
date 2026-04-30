using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class RosPublisher : MonoBehaviour
{
    private ROSConnection ros;
    public string topicName = "/unity_msg";

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        // 註冊發送者
        ros.RegisterPublisher<StringMsg>(topicName);
    }

    void Update()
    {
        // 每5秒發送一次消息
        if (Time.frameCount % (5 * 60) == 0) // 假設遊戲運行在60 FPS
        {
            StringMsg msg = new StringMsg("Hello from Unity!");
            Debug.Log("[ROS2] 發送消息到 " + topicName + ": " + msg.data);
            ros.Publish(topicName, msg);
        }
    }
}