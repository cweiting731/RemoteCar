using UnityEngine;

namespace Main.Room
{
    public class RoomTransformController : MonoBehaviour
    {
        [Header("輸入來源")]
        public OVRInputGetter inputGetter;

        [Header("狀態開關")]
        public bool isMoveActive = false;
        public bool isRotateActive = false;

        [Header("移動設定")]
        public float moveSpeed = 1.5f;
        public float moveAcceleration = 8.0f; // 數值越高，反應越靈敏

        [Header("旋轉設定")]
        public float rotateSpeed = 60.0f;
        public float rotateAcceleration = 10.0f;

        private Vector3 currentMoveVel = Vector3.zero;
        private float currentRotateVel = 0f;

        void Update()
        {
            if (inputGetter == null) return;

            // 處理移動邏輯
            if (isMoveActive)
            {
                // 左手控制 XZ 平面 (leftStickX, leftStickY)
                // 右手控制 Y 軸上下 (rightStickY)
                Vector3 targetInput = new Vector3(inputGetter.leftStickX, inputGetter.rightStickY, inputGetter.leftStickY);
                UpdateMovement(targetInput);
            }
            else
            {
                // 關閉時平滑減速至零
                currentMoveVel = Vector3.Lerp(currentMoveVel, Vector3.zero, moveAcceleration * Time.deltaTime);
            }

            // 處理旋轉邏輯
            if (isRotateActive)
            {
                // 右手控制水平旋轉 (rightStickX)
                UpdateRotation(inputGetter.rightStickX);
            }
            else
            {
                currentRotateVel = Mathf.Lerp(currentRotateVel, 0f, rotateAcceleration * Time.deltaTime);
            }

            // 實際應用位移與旋轉
            ApplyTransform();
        }

        private void UpdateMovement(Vector3 inputVec)
        {
            Vector3 targetVel = inputVec * moveSpeed;
            // 透過 Lerp 達到速度的緩啟動與緩停效果
            currentMoveVel = Vector3.Lerp(currentMoveVel, targetVel, moveAcceleration * Time.deltaTime);
        }

        private void UpdateRotation(float horizontalInput)
        {
            float targetRotVel = horizontalInput * rotateSpeed;
            currentRotateVel = Mathf.Lerp(currentRotateVel, targetRotVel, rotateAcceleration * Time.deltaTime);
        }

        private void ApplyTransform()
        {
            // 位移使用 Space.World 避免受到房間自身旋轉後的方向干擾
            transform.Translate(currentMoveVel * Time.deltaTime, Space.World);
            
            // 旋轉沿著世界座標的 Y 軸
            transform.Rotate(Vector3.up, currentRotateVel * Time.deltaTime, Space.World);
        }

        // 方便 UI 按鈕呼叫的 Function
        public void SetMoveStatus(bool status) => isMoveActive = status;
        public void SetRotateStatus(bool status) => isRotateActive = status;
    }
}