using UnityEngine;
using UnityEngine.UI;
using System;

namespace CarControl
{
    public class CarVisualizer : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private RectTransform joystickRoot;
        [SerializeField] private RectTransform handle;

        [Header("Settings")]
        [SerializeField] private float radius = 100f;
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private bool smooth = true;
        [SerializeField] private float smoothSpeed = 10f;
        [SerializeField] private float rotationOffsetDegrees = 0f;

        // 輸出值 (-1 ~ 1)
        public Vector2 InputVector { get; private set; }

        // 提供外部監聽（例如控制角色）
        public Action<Vector2> OnValueChanged;

        private Vector2 targetPosition;

        void Update()
        {
            // 平滑移動 handle（視覺用）
            if (smooth)
            {
                handle.anchoredPosition = Vector2.Lerp(
                    handle.anchoredPosition,
                    targetPosition,
                    Time.deltaTime * smoothSpeed
                );
            }
            else
            {
                handle.anchoredPosition = targetPosition;
            }

            UpdateHandleRotation();
        }

        /// <summary>
        /// 外部呼叫（例如你的 x, y 輸入）
        /// x, y 範圍建議為 -1 ~ 1
        /// </summary>
        public void SetInput(float x, float y)
        {
            Vector2 input = new Vector2(x, y);

            // Dead Zone
            if (input.magnitude < deadZone)
            {
                InputVector = Vector2.zero;
                targetPosition = Vector2.zero;
                OnValueChanged?.Invoke(InputVector);
                return;
            }

            // 限制在圓形內
            if (input.magnitude > 1f)
                input = input.normalized;

            InputVector = input;

            // UI 位置
            targetPosition = input * radius;

            OnValueChanged?.Invoke(InputVector);
        }

        /// <summary>
        /// 重置（例如放開搖桿）
        /// </summary>
        public void ResetJoystick()
        {
            InputVector = Vector2.zero;
            targetPosition = Vector2.zero;
            OnValueChanged?.Invoke(InputVector);
        }

        private void UpdateHandleRotation()
        {
            if (handle == null)
            {
                return;
            }

            float angle = InputVector == Vector2.zero ? 90f : Mathf.Atan2(InputVector.y, InputVector.x) * Mathf.Rad2Deg;
            handle.localRotation = Quaternion.Euler(0f, 0f, angle + rotationOffsetDegrees);
        }

        /// <summary>
        /// 取得角度（度）
        /// </summary>
        public float GetAngle()
        {
            if (InputVector == Vector2.zero) return 0f;
            return Mathf.Atan2(InputVector.y, InputVector.x) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 取得強度（0~1）
        /// </summary>
        public float GetMagnitude()
        {
            return InputVector.magnitude;
        }
    }
}