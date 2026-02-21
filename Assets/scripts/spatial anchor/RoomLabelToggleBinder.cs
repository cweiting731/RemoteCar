using UnityEngine;

public class RoomLabelToggleBinder : MonoBehaviour
{
    public RoomLabelMask roomLabelMask; // 這個腳本會控制 RoomLabelMask 的顯示與隱藏
    public RoomLabelFilterManager roomLabelFilterManager; // 這個腳本會提供當前的過濾狀態

    void Awake()
    {
        // 找到TMP 並將文字更改成roomLabelMask的名稱
        var uiText = GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (uiText != null)
        {
            // 首字母大寫
            // Everything 要另外處理
            string text = roomLabelMask.ToString();
            if (text == "-1")
            {
                uiText.text = "Everything";
            }
            else
            {
                uiText.text = text.Substring(0, 1).ToUpper() + text.Substring(1);
            }
        }
    }

    public void OnToggleChanged(bool isOn)
    {
        roomLabelFilterManager.UpdateMask(roomLabelMask, isOn);
    }
}