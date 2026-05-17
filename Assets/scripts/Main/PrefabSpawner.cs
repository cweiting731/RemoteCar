using UnityEngine;
using Oculus.Interaction;
using Main.Room.MiniRoom;

public class PrefabSpawner : MonoBehaviour
{
    [Header("Prefab to Spawn")]
    public GameObject prefab;

    [Header("Spawn Point (指定生成位置)")]
    public Transform spawnPoint;

    [Header("MiniRoomContentBuilder Reference")]
    public MiniRoomContentBuilder miniRoomContentBuilder;

    [Header("Button Wrapper (When Selected)")]
    public InteractableUnityEventWrapper buttonWrapper;

    private GameObject currentInstance;
    private VehicleLikeMovement movementScript;

    // 👇 這個會給 When Selected 呼叫
    public void TogglePrefab()
    {
        if (currentInstance == null)
        {
            SpawnAndBind();
        }
        else
        {
            UnbindAndDestroy();
        }
    }

    void SpawnAndBind()
    {
        currentInstance = Instantiate(
            prefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        movementScript = currentInstance.GetComponent<VehicleLikeMovement>();

        if (miniRoomContentBuilder != null)
            miniRoomContentBuilder.RegisterCar(currentInstance.transform);

        if (movementScript != null && buttonWrapper != null)
        {
            buttonWrapper.WhenSelect.AddListener(movementScript.RecordInitialTransform);
        }

        Debug.Log("[PrefabSpawner] Spawned and bound RecordInitialTransform.");
    }

    void UnbindAndDestroy()
    {
        if (movementScript != null && buttonWrapper != null)
        {
            buttonWrapper.WhenSelect.RemoveListener(movementScript.RecordInitialTransform);
        }

        if (miniRoomContentBuilder != null)
            miniRoomContentBuilder.ClearCar();

        Destroy(currentInstance);

        currentInstance = null;
        movementScript = null;

        Debug.Log("[PrefabSpawner] Destroyed and listener removed.");
    }


    // 👇 如果你還想保留 Reset 功能
    public void ResetRotation()
    {
        if (currentInstance == null) return;

        Vector3 currentRotation = currentInstance.transform.eulerAngles;
        currentInstance.transform.eulerAngles = new Vector3(0, currentRotation.y, 0);
    }
}
