using UnityEngine;

namespace Main.Room.MiniRoom.Label
{
    public class LabelBillboard : MonoBehaviour
    {
        void LateUpdate()
        {
            if (Camera.main == null) return;
            transform.forward = Camera.main.transform.forward;
        }
    }
}