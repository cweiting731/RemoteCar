using System.Data.Common;
using UnityEngine;

// deprecated
[RequireComponent(typeof(LineRenderer))]
public class MiniRoomRayCollisionPredictor : MonoBehaviour
{
    [Header("車體碰撞設定")]
    public float detectDistance = 0.1f;           // 偵測距離
    public float carWidthOffset = 0.02f;          // 左右射線偏移模擬車寬
    public float carHeightOffset = 0.01f;         // 上下射線偏移模擬車高
    public int raysPerDirection = 3;              // 每方向射線數量

    [Header("環境設定")]
    public LayerMask environmentLayer;            // 只偵測 MiniRoomEnvironment

    private LineRenderer line;
    private Vector3[] directions;

    private bool lineActive = false;
    private Vector3 lineStart, lineEnd;

    void Awake()
    {
        // 初始化 LineRenderer
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.enabled = false;
        line.startWidth = 0.005f;
        line.endWidth = 0.005f;
        line.material = new Material(Shader.Find("Unlit/Color"));
        line.startColor = Color.red;
        line.endColor = Color.red;
        line.useWorldSpace = true;

        // 四方向
        directions = new Vector3[] {
            Vector3.forward,
            -Vector3.forward,
            Vector3.right,
            -Vector3.right
        };
    }

    void Update()
    {
        PredictCollision();
        // 更新警示線
        if (lineActive)
        {
            line.enabled = true;
            line.SetPosition(0, lineStart);
            line.SetPosition(1, lineEnd);
        }
        else
        {
            line.enabled = false;
        }
    }

    void PredictCollision()
    {
        lineActive = false;

        foreach (var dir in directions)
        {
            for (int i = -raysPerDirection/2; i <= raysPerDirection/2; i++)
            {
                for (int j = -raysPerDirection/2; j <= raysPerDirection/2; j++)
                {
                    // 計算射線起點，左右/上下偏移
                    Vector3 offset = transform.right * i * carWidthOffset + transform.up * j * carHeightOffset;
                    Vector3 origin = transform.position + offset;
                    Vector3 direction = transform.TransformDirection(dir);

                    float dd;
                    if (dir == Vector3.forward || dir == -Vector3.forward)
                        dd = detectDistance * 4f; // 前後方向加長距離
                    else
                        dd = detectDistance;

                    if (Physics.Raycast(origin, direction, out RaycastHit hit, dd, environmentLayer))
                    {
                        lineActive = true;
                        lineStart = transform.position;
                        lineEnd = hit.point;
                        // return; // 偵測到一個就畫線
                    }
                }
            }
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.yellow;

        foreach (var dir in directions)
        {
            Vector3 direction = transform.TransformDirection(dir);
            Gizmos.DrawLine(transform.position, transform.position + direction * detectDistance);
        }
    }

    // 工具方法：遞迴設定物件 Layer
    public static void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}