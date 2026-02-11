using UnityEngine;

public class PrefabSpawner : MonoBehaviour
{
    [Header("Prefab to Spawn")]
    public GameObject prefab;

    [Header("Spawn Point (指定生成位置)")]
    public Transform spawnPoint;

    [Header("MiniRoomContentBuilder Reference")]
    public MiniRoomContentBuilder miniRoomContentBuilder;

    private GameObject currentInstance;

    // 👇 這個會給 When Selected 呼叫
    public void TogglePrefab()
    {
        if (currentInstance == null)
        {
            currentInstance = Instantiate(
                prefab,
                spawnPoint.position,
                spawnPoint.rotation
            );

            if (miniRoomContentBuilder != null)
            {
                miniRoomContentBuilder.RegisterCar(currentInstance.transform);
            }
            Debug.Log("[PrefabSpawner] Prefab spawned at: " + spawnPoint.position);
        }
        else
        {
            if (miniRoomContentBuilder != null)
                miniRoomContentBuilder.ClearCar();

            Destroy(currentInstance);
            currentInstance = null;
            Debug.Log("[PrefabSpawner] Prefab destroyed.");
        }
    }

    // 👇 如果你還想保留 Reset 功能
    public void ResetRotation()
    {
        if (currentInstance == null) return;

        Vector3 currentRotation = currentInstance.transform.eulerAngles;
        currentInstance.transform.eulerAngles = new Vector3(0, currentRotation.y, 0);
    }
}
