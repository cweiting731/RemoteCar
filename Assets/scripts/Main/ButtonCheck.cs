using System.Collections;
using UnityEngine;
using Main.Room.SLAMRoom;
using StreamVideo;

public class ButtonCheck : MonoBehaviour
{
    public float resetSubscribeDelaySeconds = 0.25f;
    private bool isResettingROS2;

    public void CheckButton()
    {
        Debug.Log("Button Pressed!");
    }

    public void ReconnectROS2()
    {
        ROS2.Ros2ReconnectHelper.Reconnect(this, 0.25f, false);
        // ResetAllROS2();
    }

    public void ResetAllROS2()
    {
        if (isResettingROS2)
        {
            Debug.Log("[ROS2 Reset] Reset is already running.");
            return;
        }

        StartCoroutine(ResetAllROS2Coroutine());
    }

    private IEnumerator ResetAllROS2Coroutine()
    {
        isResettingROS2 = true;

        ROS2.Ros2ReconnectHelper.Reconnect(this, 0f);

        if (resetSubscribeDelaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(resetSubscribeDelaySeconds);
        }

        int resetCount = 0;

        foreach (RosStreamSubscriber subscriber in FindObjectsOfType<RosStreamSubscriber>(true))
        {
            subscriber.ResetROS2Subscriber();
            resetCount++;
        }

        foreach (RosPointCloudSubscriber subscriber in FindObjectsOfType<RosPointCloudSubscriber>(true))
        {
            subscriber.ResetROS2Subscriber();
            resetCount++;
        }

        foreach (RosSLAMCameraPose subscriber in FindObjectsOfType<RosSLAMCameraPose>(true))
        {
            subscriber.ResetROS2Subscriber();
            resetCount++;
        }

        Debug.Log($"[ROS2 Reset] Reset requested for {resetCount} ROS2 subscribers.");
        isResettingROS2 = false;
    }
}
