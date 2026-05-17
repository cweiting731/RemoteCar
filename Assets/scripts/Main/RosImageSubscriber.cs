using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor; // 需要包含 sensor_msgs

// deprecated
public class RosImageSubscriber : MonoBehaviour
{
    [Header("ROS2 Settings")]
    public string topicName = "/camera/colored"; // 或者 "/camera" (灰階)

    [Header("Debug")]
    public bool enableDebugLog = true;
    public int logEveryNFrames = 30;

    private Texture2D texture;
    private Renderer rend;
    private bool isTextureInitialized = false;
    private int receivedImageCount = 0;
    private byte[] processedImageData; // ▲ 新增: 用來重複使用的資料轉換陣列，避免嚴重 GC 卡頓

    void Start()
    {
        rend = GetComponent<Renderer>();

        if (rend == null)
        {
            Debug.LogError("[ROS2 Image] Renderer not found on this GameObject.");
        }
        
        // 取得 ROS 連結並訂閱 Topic
        ROSConnection.GetOrCreateInstance().Subscribe<ImageMsg>(topicName, ReceiveImage);

        if (enableDebugLog)
        {
            Debug.Log($"[ROS2 Image] Subscribed to topic: {topicName}");
        }
    }

    void ReceiveImage(ImageMsg msg)
    {
        receivedImageCount++;

        // 1. 根據第一次收到的訊息初始化 Texture
        if (!isTextureInitialized)
        {
            // ROS2 的 bgr8 對應 Unity 的 RGB24 (需要手動轉 byte 順序)
            // 如果訂閱的是 /camera (mono8)，格式請改為 Alpha8 或 R8
            texture = new Texture2D((int)msg.width, (int)msg.height, TextureFormat.RGB24, false);
            if (rend != null)
            {
                rend.material.mainTexture = texture;
            }
            isTextureInitialized = true;

            if (enableDebugLog)
            {
                Debug.Log($"[ROS2 Image] Texture initialized: {msg.width}x{msg.height}, encoding={msg.encoding}");
            }
        }

        // 2. 處理影像數據
        byte[] rawData = msg.data;

        // ▲ 避免每幀產生新的 byte[] 導致 GC 卡頓，確保陣列大小正確並重複使用
        if (processedImageData == null || processedImageData.Length != rawData.Length)
        {
            processedImageData = new byte[rawData.Length];
        }

        if (msg.encoding == "bgr8")
        {
            // ROS 的 bgr8 是 [B, G, R, B, G, R...]
            // Unity 的 RGB24 是 [R, G, B, R, G, B...]
            // 我們需要轉換順序並上下顛倒
            ConvertBGRtoRGBAndFlip((int)msg.width, (int)msg.height, rawData, processedImageData);
            texture.LoadRawTextureData(processedImageData);
        }
        else if (msg.encoding == "mono8")
        {
            // 灰階需要上下顛倒 (如果是單通道 Texture)
            FlipMono8Vertically((int)msg.width, (int)msg.height, rawData, processedImageData);
            texture.LoadRawTextureData(processedImageData);
        }
        else
        {
            Debug.LogWarning($"[ROS2 Image] Unsupported encoding: {msg.encoding}");
            return;
        }

        // 3. 套用到 Texture 並上傳 GPU
        texture.Apply();

        if (enableDebugLog && receivedImageCount % Mathf.Max(1, logEveryNFrames) == 0)
        {
            Debug.Log($"[ROS2 Image] Received={receivedImageCount}, size={msg.width}x{msg.height}, encoding={msg.encoding}, bytes={rawData.Length}");
        }
    }

    // 將 BGR 轉換為 RGB 並上下顛倒的輔助函式
    void ConvertBGRtoRGBAndFlip(int width, int height, byte[] bgrData, byte[] rgbData)
    {
        int rowBytes = width * 3;
        for (int y = 0; y < height; y++)
        {
            int srcRowStart = y * rowBytes;
            // Unity 的紋理起點在左下角，所以將資料上下顛倒
            int dstRowStart = (height - 1 - y) * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int srcIdx = srcRowStart + x * 3;
                int dstIdx = dstRowStart + x * 3;
                rgbData[dstIdx]     = bgrData[srcIdx + 2]; // R
                rgbData[dstIdx + 1] = bgrData[srcIdx + 1]; // G
                rgbData[dstIdx + 2] = bgrData[srcIdx];     // B
            }
        }
    }

    // 將單通道灰階影像上下顛倒
    void FlipMono8Vertically(int width, int height, byte[] monoData, byte[] flippedData)
    {
        int rowBytes = width;
        for (int y = 0; y < height; y++)
        {
            int srcRowStart = y * rowBytes;
            int dstRowStart = (height - 1 - y) * rowBytes;
            System.Array.Copy(monoData, srcRowStart, flippedData, dstRowStart, rowBytes);
        }
    }
}