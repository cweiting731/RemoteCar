using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ToggleIconController : MonoBehaviour
{
    public Image targetIcon;   // 拖入你的 Icon 物件
    public Sprite onSprite;    // 拖入「可見」的圖
    public Sprite offSprite;   // 拖入「隱藏」的圖

    public void UpdateIcon(bool isOn)
    {
        if (targetIcon != null)
        {
            targetIcon.sprite = isOn ? onSprite : offSprite;
        }
    }
    // Start is called before the first frame update
    // void Start()
    // {
        
    // }

    // // Update is called once per frame
    // void Update()
    // {
        
    // }
}
