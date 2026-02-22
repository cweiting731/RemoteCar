using UnityEngine;

public class VisualSetting : MonoBehaviour
{
    // 指定要控制的 Panel 上的 CanvasGroup
    public CanvasGroup panelGroup;

    // 切換顯示/隱藏 + 互動
    private bool isVisible = true;

    public void TogglePanel()
    {
        isVisible = !isVisible;

        if (isVisible)
            ShowPanel();
        else
            HidePanel();
    }

    // 顯示並可互動
    public void ShowPanel()
    {
        panelGroup.alpha = 1f;              // 完全顯示
        panelGroup.interactable = true;     // 可互動
        panelGroup.blocksRaycasts = true;   // 接收事件

        var colliders = panelGroup.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
        {
            col.enabled = isVisible;
        }
    }

    // 隱形且不可互動
    public void HidePanel()
    {
        panelGroup.alpha = 0f;              // 完全透明
        panelGroup.interactable = false;    // 不可互動
        panelGroup.blocksRaycasts = false;  // 不接收事件

        var colliders = panelGroup.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
        {
            col.enabled = isVisible;
        }
    }
}