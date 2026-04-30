using UnityEngine;
using UnityEngine.SceneManagement;

public class RoomMaterialChanger : MonoBehaviour
{
    [Header("材質設定")]
    public Material onMaterial;  // Toggle 開啟時的材質
    public Material offMaterial; // Toggle 關閉時的材質

    public void ToggleRoomMaterials(bool isOn)
    {
        // 1. 取得目前啟用的場景中所有最頂層（Root）物件
        GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
        Material targetMaterial = isOn ? onMaterial : offMaterial;

        foreach (GameObject root in rootObjects)
        {
            // 2. 檢查頂層物件名稱是否以 "Room-" 開頭
            if (root.name.StartsWith("Room-"))
            {
                // 3. 取得該物件及其所有子物件（包含自己）的 MeshRenderer
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

                foreach (MeshRenderer renderer in renderers)
                {
                    renderer.material = targetMaterial;
                }
                
                Debug.Log($"已替換根物件及其子代材質: {root.name}");
            }
        }
    }
}