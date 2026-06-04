using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;

namespace StreamVideo
{
    public class RosStreamSubscriber : MonoBehaviour
    {
        [Header("ROS2 Settings")]
        public string topicName = "/camera/colored";
        public bool enableDebugLog = false;

        [Header("Info")]
        public RawImage rawImage;
        public ROS2.ROS2InfoManager ros2InfoManager;

        private ROSConnection ros;

        private Texture2D texture;
        private bool textureReady;
        private bool isSubscribed;
        private bool hasStarted;

        private byte[] bufferA;
        private byte[] bufferB;
        private byte[] writeBuffer;
        private byte[] readBuffer;
        private byte[] monoRgbBuffer;

        private int width;
        private int height;
        private string encoding;
        private double stampSeconds;
        private uint stampNanoseconds;

        private int hasNewFrame;

        private void Start()
        {
            hasStarted = true;
            ros = ROSConnection.GetOrCreateInstance();
            EnsureFrameBuffers(1);
            SubscribeNow();
        }

        private void OnEnable()
        {
            if (hasStarted)
            {
                SubscribeNow();
            }
        }

        private void OnDisable()
        {
            UnsubscribeNow();
        }

        private void OnDestroy()
        {
            UnsubscribeNow();
            ReleaseTexture();
        }

        private void OnImage(ImageMsg msg)
        {
            int size = msg.data.Length;
            ros2InfoManager?.RecordTopicBytes(topicName, msg.data.LongLength);

            EnsureFrameBuffers(size);
            System.Buffer.BlockCopy(msg.data, 0, writeBuffer, 0, size);

            width = (int)msg.width;
            height = (int)msg.height;
            encoding = msg.encoding;
            stampSeconds = msg.header?.stamp?.sec ?? 0;
            stampNanoseconds = msg.header?.stamp?.nanosec ?? 0;

            var tmp = writeBuffer;
            writeBuffer = readBuffer;
            readBuffer = tmp;

            Interlocked.Exchange(ref hasNewFrame, 1);
        }

        private void Update()
        {
            if (Interlocked.Exchange(ref hasNewFrame, 0) == 0)
                return;

            if (readBuffer == null || readBuffer.Length == 0)
                return;

            if (encoding == "bgr8")
            {
                EnsureTexture(width, height);
                ConvertBGRtoRGBFlip(readBuffer, width, height);
                texture.LoadRawTextureData(readBuffer);
            }
            else if (encoding == "mono8")
            {
                EnsureTexture(width, height);
                EnsureMonoRgbBuffer(width * height * 3);
                ConvertMonoToRGBFlip(readBuffer, monoRgbBuffer, width, height);
                texture.LoadRawTextureData(monoRgbBuffer);
            }
            else
            {
                return;
            }

            texture.Apply(false, false);
            ros2InfoManager?.RecordTopicDisplayLatencyFromHeader(topicName, stampSeconds, stampNanoseconds);
        }

        public void ResetROS2Subscriber()
        {
            if (ros == null)
            {
                ros = ROSConnection.GetOrCreateInstance();
            }

            Interlocked.Exchange(ref hasNewFrame, 0);
            UnsubscribeNow();
            SubscribeNow();
        }

        private void SubscribeNow()
        {
            if (ros == null || isSubscribed || string.IsNullOrEmpty(topicName))
            {
                return;
            }

            ros.Subscribe<ImageMsg>(topicName, OnImage);
            isSubscribed = true;

            if (enableDebugLog)
                Debug.Log($"Subscribed: {topicName}");
        }

        private void UnsubscribeNow()
        {
            if (ros == null || !isSubscribed || string.IsNullOrEmpty(topicName))
            {
                return;
            }

            ros.Unsubscribe(topicName);
            isSubscribed = false;
        }

        private void EnsureFrameBuffers(int size)
        {
            size = Mathf.Max(1, size);

            if (bufferA == null || bufferA.Length < size)
            {
                bufferA = new byte[size];
            }

            if (bufferB == null || bufferB.Length < size)
            {
                bufferB = new byte[size];
            }

            if (writeBuffer == null || writeBuffer.Length < size)
            {
                writeBuffer = bufferA;
            }

            if (readBuffer == null || readBuffer.Length < size)
            {
                readBuffer = bufferB;
            }
        }

        private void EnsureMonoRgbBuffer(int size)
        {
            if (monoRgbBuffer == null || monoRgbBuffer.Length < size)
            {
                monoRgbBuffer = new byte[size];
            }
        }

        private void EnsureTexture(int targetWidth, int targetHeight)
        {
            if (textureReady && texture != null && texture.width == targetWidth && texture.height == targetHeight)
            {
                return;
            }

            ReleaseTexture();
            texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false)
            {
                hideFlags = HideFlags.DontSave
            };

            if (rawImage != null)
            {
                rawImage.texture = texture;
            }

            textureReady = true;
        }

        private void ReleaseTexture()
        {
            if (rawImage != null && rawImage.texture == texture)
            {
                rawImage.texture = null;
            }

            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }

            textureReady = false;
        }

        private void ConvertBGRtoRGBFlip(byte[] data, int w, int h)
        {
            int stride = w * 3;

            for (int y = 0; y < h / 2; y++)
            {
                int top = y * stride;
                int bottom = (h - 1 - y) * stride;

                for (int x = 0; x < stride; x += 3)
                {
                    SwapPixel(data, top + x, bottom + x);
                }
            }

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < stride; x += 3)
                {
                    byte b = data[row + x];
                    byte r = data[row + x + 2];
                    data[row + x] = r;
                    data[row + x + 2] = b;
                }
            }
        }

        private void ConvertMonoToRGBFlip(byte[] monoData, byte[] rgbData, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * w;
                int dstRow = (h - 1 - y) * w * 3;
                for (int x = 0; x < w; x++)
                {
                    byte g = monoData[srcRow + x];
                    int dst = dstRow + x * 3;
                    rgbData[dst] = g;
                    rgbData[dst + 1] = g;
                    rgbData[dst + 2] = g;
                }
            }
        }

        private void SwapPixel(byte[] data, int a, int b)
        {
            byte t0 = data[a];
            byte t1 = data[a + 1];
            byte t2 = data[a + 2];

            data[a] = data[b];
            data[a + 1] = data[b + 1];
            data[a + 2] = data[b + 2];

            data[b] = t0;
            data[b + 1] = t1;
            data[b + 2] = t2;
        }
    }
}
