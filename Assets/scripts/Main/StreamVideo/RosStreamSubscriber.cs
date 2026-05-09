using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine.UI;
using UnityEngine;
using TMPro;
using ROS2;

namespace StreamVideo
{
    public class RosStreamSubscriber : MonoBehaviour
    {
        [Header("ROS2 Settings")]
        public string topicName = "/camera/colored"; // 或者 "/camera" (灰階)

        [Header("Debug")]
        public bool enableDebugLog = true;
        public int logEveryNFrames = 30;

        [Header("Info")]
        public bool isTest = false;
        public ROS2InfoManager ros2Info;

        private Texture2D texture; // 補上遺漏的宣告
        private RawImage rawImage;
        private bool isTextureInitialized = false;
        private int receivedImageCount = 0;
        private byte[] processedImageData; // ▲ 新增: 用來重複使用的資料轉換陣列，避免嚴重 GC 卡頓
        private byte[] latestRawData;
        private string latestEncoding;
        private int latestWidth;
        private int latestHeight;
        private readonly object latestFrameLock = new object();

        // FPS 計算及連線資訊相關
        private float fpsTimer = 0f;
        private int fpsFrameCount = 0;
        private float currentFps = 0f;
        private float currentMbps = 0f;
        private readonly object statsLock = new object();
        private ROSConnection ros;

        // 只保留最新一張影像，避免高速影像進來時在主執行緒排隊塞爆
        private bool isNewImageAvailable = false;
        private bool isProcessing = false; // 避免重入，確保同一時間只處理一張 frame

        void Start()
        {
            rawImage = GetComponent<RawImage>();

            if (rawImage == null)
            {
                Debug.LogError("[ROS2 Image] RawImage not found on this GameObject.");
            }

            // 取得 ROS 連結並訂閱 Topic
            ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<ImageMsg>(topicName, ReceiveImage);

            if (enableDebugLog)
            {
                Debug.Log($"[ROS2 Image] Subscribed to topic: {topicName}");
            }

        }

        void Update()
        {
            fpsTimer += Time.deltaTime;
            // 每秒更新一次 FPS
            if (fpsTimer >= 0.5f)
            {
                lock (statsLock)
                {
                    currentFps = fpsFrameCount / fpsTimer;
                    fpsFrameCount = 0;
                }

                // 如果是測試模式，模擬 Mbps 數值；否則根據實際收到的位元組數計算 Mbps
                if (isTest)
                {
                    currentMbps = Random.Range(5f, 20f); // 模擬 5-20 Mbps 的範圍
                }
                if (isTest && ros2Info != null)
                {
                    ros2Info.SetTopicMbps(topicName, currentMbps);
                    ros2Info.UpdateInfo();
                }
                else if (ros2Info == null)
                {
                    Debug.LogWarning("[ROS2StreamSubscriber] ROS2InfoManager reference is not assigned.");
                }

                fpsTimer = 0f;
            }

            // 每幀最多只啟動一次處理；如果上一張還沒做完，就先等下一輪更新
            if (isNewImageAvailable && !isProcessing)
            {
                ProcessLatestImageAsync();
                isNewImageAvailable = false;
            }
        }


        void ReceiveImage(ImageMsg msg)
        {
            receivedImageCount++;

            // 在回呼當下先把資料複製出來，避免後續背景處理時碰到訊息物件生命週期問題
            int dataLength = msg.data != null ? msg.data.Length : 0;
            lock (statsLock)
            {
                fpsFrameCount++;
            }

            if (!isTest)
            {
                ros2Info?.RecordTopicBytes(topicName, dataLength);
            }

            lock (latestFrameLock)
            {
                if (latestRawData == null || latestRawData.Length != dataLength)
                {
                    latestRawData = new byte[dataLength];
                }

                if (dataLength > 0)
                {
                    System.Buffer.BlockCopy(msg.data, 0, latestRawData, 0, dataLength);
                }

                latestEncoding = msg.encoding;
                latestWidth = (int)msg.width;
                latestHeight = (int)msg.height;
            }

            isNewImageAvailable = true;
        }

        async void ProcessLatestImageAsync()
        {
            isProcessing = true;
            try
            {
                byte[] rawDataSnapshot;
                string encodingSnapshot;
                int w;
                int h;

                // 只在鎖內取快照，實際轉換與貼圖更新放到鎖外，減少阻塞時間
                lock (latestFrameLock)
                {
                    if (latestRawData == null || latestRawData.Length == 0)
                    {
                        return;
                    }

                    rawDataSnapshot = new byte[latestRawData.Length];
                    System.Buffer.BlockCopy(latestRawData, 0, rawDataSnapshot, 0, latestRawData.Length);
                    encodingSnapshot = latestEncoding;
                    w = latestWidth;
                    h = latestHeight;
                }

                // 第一次收到畫面，或解析度變了，就重新建立 Texture
                if (!isTextureInitialized || texture == null || texture.width != w || texture.height != h)
                {
                    // bgr8 與 mono8 最後都轉成 RGB24，讓 Unity 端的貼圖格式固定一致
                    texture = new Texture2D(w, h, TextureFormat.RGB24, false);
                    if (rawImage != null)
                    {
                        rawImage.texture = texture;
                    }
                    isTextureInitialized = true;

                    if (enableDebugLog)
                    {
                        Debug.Log($"[ROS2 Image] Texture initialized: {w}x{h}, encoding={encodingSnapshot}");
                    }
                }

                // 檢查來源資料長度，避免格式不符時把錯誤資料硬塞進貼圖
                int expectedBytes = encodingSnapshot == "mono8" ? w * h : w * h * 3;
                if (rawDataSnapshot.Length < expectedBytes)
                {
                    if (enableDebugLog)
                    {
                        Debug.LogWarning($"[ROS2 Image] Invalid frame bytes. got={rawDataSnapshot.Length}, expected={expectedBytes}, encoding={encodingSnapshot}");
                    }
                    return;
                }

                // 避免每幀產生新的 byte[] 導致 GC 卡頓，確保陣列大小正確並重複使用
                int outputLength = w * h * 3;
                if (processedImageData == null || processedImageData.Length != outputLength)
                {
                    processedImageData = new byte[outputLength];
                }

                // 把像素轉換放到背景執行緒，降低主執行緒卡頓
                await System.Threading.Tasks.Task.Run(() =>
                {
                    if (encodingSnapshot == "bgr8")
                    {
                        ConvertBGRtoRGBAndFlip(w, h, rawDataSnapshot, processedImageData);
                    }
                    else if (encodingSnapshot == "mono8")
                    {
                        ConvertMono8ToRGBAndFlip(w, h, rawDataSnapshot, processedImageData);
                    }
                });

                if (encodingSnapshot == "bgr8" || encodingSnapshot == "mono8")
                {
                    // 回到主執行緒後把轉換結果寫進 Texture
                    texture.LoadRawTextureData(processedImageData);
                    texture.Apply();
                }
                else
                {
                    if (enableDebugLog)
                    {
                        Debug.LogWarning($"[ROS2 Image] Unsupported encoding: {encodingSnapshot}");
                    }
                }

                if (enableDebugLog && receivedImageCount % Mathf.Max(1, logEveryNFrames) == 0)
                {
                    Debug.Log($"[ROS2 Image] Received={receivedImageCount}, size={w}x{h}, encoding={encodingSnapshot}, bytes={rawDataSnapshot.Length}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ROS2 Image] Process frame failed: {ex.Message}");
            }
            finally
            {
                isProcessing = false;
            }
        }

        // 將 BGR 轉成 RGB，同時上下翻轉，對齊 Unity 貼圖座標
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
                    rgbData[dstIdx] = bgrData[srcIdx + 2]; // R
                    rgbData[dstIdx + 1] = bgrData[srcIdx + 1]; // G
                    rgbData[dstIdx + 2] = bgrData[srcIdx];     // B
                }
            }
        }

        // mono8 轉成 RGB 三通道，方便直接寫入同一種 TextureFormat
        void ConvertMono8ToRGBAndFlip(int width, int height, byte[] monoData, byte[] rgbData)
        {
            int rowBytes = width;
            for (int y = 0; y < height; y++)
            {
                int srcRowStart = y * rowBytes;
                int dstRowStart = (height - 1 - y) * width * 3;
                for (int x = 0; x < width; x++)
                {
                    byte gray = monoData[srcRowStart + x];
                    int dstIdx = dstRowStart + x * 3;
                    rgbData[dstIdx] = gray;
                    rgbData[dstIdx + 1] = gray;
                    rgbData[dstIdx + 2] = gray;
                }
            }
        }
    }
}
