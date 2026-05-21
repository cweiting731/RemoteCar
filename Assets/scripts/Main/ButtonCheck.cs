using UnityEngine;

public class ButtonCheck : MonoBehaviour
{
    public void CheckButton()
    {
        Debug.Log("Button Pressed!");
    }

    public void ReconnectROS2()
    {
        ROS2.Ros2ReconnectHelper.Reconnect(this);
    }
}
