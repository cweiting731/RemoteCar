using UnityEngine;
using Meta.XR.MRUtilityKit;

public class SceneMeshDisplay : MonoBehaviour
{
    private GameObject miniScene;

    void Start()
    {
        // 嘗試抓取當前房間 (需先在 Quest 系統完成掃描)
        var currentRoom = MRUK.Instance.GetCurrentRoom();
        if (currentRoom == null)
        {
            Debug.LogError("⚠️ No scanned room found. Please run Room Setup on your Quest 3 first.");
            return;
        }

        // 生成縮小版房間模型
        miniScene = Instantiate(currentRoom.gameObject);
        miniScene.transform.localScale = Vector3.one * 0.01f;

        // 放在使用者面前 1 公尺
        Transform cam = Camera.main.transform;
        miniScene.transform.position = cam.position + cam.forward * 1.0f;
        miniScene.transform.rotation = Quaternion.identity;

        // 設定半透明材質
        foreach (var renderer in miniScene.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.6f, 0.8f, 1f, 0.4f);
        }

        // 可互動（Grab）
        // var rb = miniScene.AddComponent<Rigidbody>();
        // rb.isKinematic = true;
        // miniScene.AddComponent<XRGrabInteractable>();
    }
}
