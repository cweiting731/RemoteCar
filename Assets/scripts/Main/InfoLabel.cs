using UnityEngine;

public class InfoLabel : MonoBehaviour
{
    // 顯示元件
    public GameObject labelObject; // 這個物件會被啟用/禁用來顯示或隱藏資訊
    // 文字元件
    public TMPro.TextMeshProUGUI textMesh;
    private string labelText;
    private Coroutine hideCoroutine;

    void Start()
    {
        UpdateInfo("Welcome !");
    }
    public void UpdateInfo(string info)
    {
        labelText = info;

        // 更新 TextMeshPro
        if (textMesh != null)
        {
            textMesh.text = labelText;
        }

        // 確保物件顯示
        labelObject.SetActive(true);

        // 如果之前有計時，先停止
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }

        // 啟動新的 3 秒倒數
        hideCoroutine = StartCoroutine(AutoHide());
    }

    private System.Collections.IEnumerator AutoHide()
    {
        yield return new WaitForSeconds(3f);
        labelObject.SetActive(false);
        hideCoroutine = null; // 清掉引用
    }
}