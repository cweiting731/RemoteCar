using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class CarControllerUDP : MonoBehaviour
{
    public string espIP = "192.168.0.123";  // 你的 ESP32 的 IP
    public int espPort = 4000;

    UdpClient udp;

    void Start()
    {
        udp = new UdpClient();
    }

    void Update()
    {
        // ===== 取得手把輸入 =====
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);

        float sx = stick.x;  // 左右
        float sy = stick.y;  // 前後

        // ===== 轉換成 ESP32 需要的數值 =====
        int hd = (int)((-sx + 1f) * 0.5f * 255f);
        int th = (int)((-sy + 1f) * 0.5f * 255f);

        Debug.Log($"[CarControllerUDP] stick=({sx:F2},{sy:F2}) => th={th}, hd={hd}");

        // ===== 組成字串 =====
        string msg = $"th={th},hd={hd}";

        // ===== 傳給 ESP32 小車 =====
        byte[] data = Encoding.ASCII.GetBytes(msg);
        udp.Send(data, data.Length, espIP, espPort);

        Debug.Log(msg);
    }
}
