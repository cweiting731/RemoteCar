using System;
using System.Collections;
using System.IO;
using System.Net;
using UnityEngine;

public class MjpegStreamQuad : MonoBehaviour
{
    [Header("ESP32 IP")]
    public string esp32Ip = "10.240.164.12";   // <-- 換成你的 ESP32 IP

    private Texture2D texture;
    private Renderer rend;

    void Start()
    {
        // 拿 MeshRenderer
        rend = GetComponent<Renderer>();

        // 建立一個空的 Texture2D
        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        // 啟動 MJPEG Coroutine
        StartCoroutine(StreamMJPEG());
    }

    IEnumerator StreamMJPEG()
    {
        string url = $"http://{esp32Ip}:81/stream";
        Debug.Log("[MJPEG] Connecting to " + url);

        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Timeout = 5000;
        req.ReadWriteTimeout = 5000;

        WebResponse resp = null;
        Stream stream = null;

        try
        {
            resp = req.GetResponse();
            stream = resp.GetResponseStream();
            Debug.Log("[MJPEG] Connected.");
        }
        catch (Exception e)
        {
            Debug.LogError("[MJPEG] Connect failed: " + e);
            yield break;
        }

        byte[] buffer = new byte[1024];
        MemoryStream ms = new MemoryStream();

        while (true)
        {
            int bytesRead = 0;

            try { bytesRead = stream.Read(buffer, 0, buffer.Length); }
            catch { break; }

            if (bytesRead <= 0) continue;

            // 把 chunk 寫到 MemoryStream
            ms.Write(buffer, 0, bytesRead);

            byte[] data = ms.ToArray();

            // 找 JPEG SOI/EOI
            int start = FindMarker(data, new byte[] { 0xFF, 0xD8 });
            int end   = FindMarker(data, new byte[] { 0xFF, 0xD9 });

            if (start != -1 && end != -1 && end > start)
            {
                int len = end + 2 - start;

                byte[] jpg = new byte[len];
                Buffer.BlockCopy(data, start, jpg, 0, len);

                // 清掉已經用過的 bytes
                ms = new MemoryStream();
                if (end + 2 < data.Length)
                    ms.Write(data, end + 2, data.Length - (end + 2));

                // JPG → Texture2D
                texture.LoadImage(jpg);

                // 套用到 Quad 上
                rend.material.mainTexture = texture;
            }

            yield return null;
        }
    }

    // 尋找 byte pattern
    int FindMarker(byte[] buffer, byte[] pattern)
    {
        for (int i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
