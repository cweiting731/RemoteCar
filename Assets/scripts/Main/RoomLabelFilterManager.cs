using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class RoomLabelFilterManager : MonoBehaviour
{
    public RoomLabelMask currentMask = RoomLabelMask.EVERYTHING;

    public List<RoomLabelToggleBinder> binders;

    private bool isUpdatingUI = false;

    public void UpdateMask(RoomLabelMask type, bool isOn)
    {
        if (isUpdatingUI) return;

        // ===== 特殊處理 =====

        if (type == RoomLabelMask.NONE)
        {
            if (isOn)
                SetAll(false);
            return;
        }

        if (type == RoomLabelMask.EVERYTHING)
        {
            if (isOn)
                SetAll(true);
            return;
        }

        // ===== 一般 Flag =====

        if (isOn)
            currentMask |= type;
        else
            currentMask &= ~type;

        SyncSpecialToggles();
        ApplyFilter();
    }

    void SetAll(bool state)
    {
        isUpdatingUI = true;

        currentMask = state ? RoomLabelMask.EVERYTHING : RoomLabelMask.NONE;

        foreach (var binder in binders)
        {
            var toggle = binder.GetComponent<Toggle>();

            if (binder.roomLabelMask == RoomLabelMask.NONE)
                toggle.isOn = !state;

            else if (binder.roomLabelMask == RoomLabelMask.EVERYTHING)
                toggle.isOn = state;

            else
                toggle.isOn = state;
        }

        isUpdatingUI = false;

        ApplyFilter();
    }

    void SyncSpecialToggles()
    {
        isUpdatingUI = true;

        bool allOn = true;
        bool allOff = true;

        foreach (var binder in binders)
        {
            if (binder.roomLabelMask == RoomLabelMask.NONE ||
                binder.roomLabelMask == RoomLabelMask.EVERYTHING)
                continue;

            var toggle = binder.GetComponent<Toggle>();

            if (toggle.isOn)
                allOff = false;
            else
                allOn = false;
        }

        foreach (var binder in binders)
        {
            var toggle = binder.GetComponent<Toggle>();

            if (binder.roomLabelMask == RoomLabelMask.EVERYTHING)
                toggle.isOn = allOn;

            if (binder.roomLabelMask == RoomLabelMask.NONE)
                toggle.isOn = allOff;
        }

        isUpdatingUI = false;
    }

    void ApplyFilter()
    {
        foreach (var label in FindObjectsOfType<RoomLabel>())
        {
            bool show = (currentMask & label.labelType) != 0;
            // 找到名為Visual的子物件，並根據show來決定是否顯示
            var visual = label.transform.Find("Visual");
            if (visual != null)
            {
                visual.gameObject.SetActive(show);
            }
        }
    }
}