using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std; // 需要對應的訊息格式

namespace ROS2
{
    public class RosSubscriber : MonoBehaviour
    {
        void Start()
        {
            // 註冊訂閱者，接收 String 訊息
            ROSConnection.GetOrCreateInstance().Subscribe<StringMsg>("/msg", ReceiveMsg);
        }

        void ReceiveMsg(StringMsg msg)
        {
            Debug.Log("[ROS2] 收到來自 ROS 的消息: " + msg.data);
        }
    }
}