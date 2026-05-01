using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.VisualScripting;
using CarControl;

namespace ControlSetting
{
    public class ControlSettingToggleGroup : MonoBehaviour
    {
        [Header("Toggles")]
        public Toggle tglSingleHand;
        public Toggle tglDoubleHand;
        public Toggle tglMiniMovePos;
        public Toggle tglMiniMoveRot;
        public Toggle tglSLAMMovePos;
        public Toggle tglSLAMMoveRot;
        public Toggle tglLock;

        [Header("Connected Controller")]
        public CarControllerROS2 carController;

        void Start()
        {
            // 使用 onValueChanged 監聽 Toggle 狀態改變
            // 使用 lambda 運算式時，isOn 是 Toggle 傳入的新狀態
            tglSingleHand.onValueChanged.AddListener((isOn) => { if(isOn) OnSingleHand(); });
            tglDoubleHand.onValueChanged.AddListener((isOn) => { if(isOn) OnDoubleHand(); });
            tglMiniMovePos.onValueChanged.AddListener((isOn) => { if(isOn) OnMiniMovePos(); });
            tglMiniMoveRot.onValueChanged.AddListener((isOn) => { if(isOn) OnMiniMoveRot(); });
            tglSLAMMovePos.onValueChanged.AddListener((isOn) => { if(isOn) OnSLAMMovePos(); });
            tglSLAMMoveRot.onValueChanged.AddListener((isOn) => { if(isOn) OnSLAMMoveRot(); });
            tglLock.onValueChanged.AddListener((isOn) => { if(isOn) OnLock(); });

            // init value
            tglSingleHand.isOn = true;
            tglLock.isOn = true;
        }

        // --- 核心邏輯處理 ---
        // 當 Toggle 被打開 (true) 時觸發的互斥邏輯

        void OnSingleHand()
        {
            tglDoubleHand.isOn = false; // 單手開啟會關閉雙手
            tglMiniMovePos.isOn = false;
            tglSLAMMovePos.isOn = false;
            if (carController != null)
            {
                carController.SetCarControlMode(CarControlMode.SingleHand);
            }
        }

        void OnDoubleHand()
        {
            // 雙手控制會關閉所有其他功能
            tglSingleHand.isOn = false;
            tglMiniMovePos.isOn = false;
            tglMiniMoveRot.isOn = false;
            tglSLAMMovePos.isOn = false;
            tglSLAMMoveRot.isOn = false;
            tglLock.isOn = false;
            if (carController != null)
            {
                carController.SetCarControlMode(CarControlMode.DoubleHand);
            }
        }

        void OnMiniMovePos()
        {
            tglSingleHand.isOn = false;
            tglDoubleHand.isOn = false;
            tglMiniMoveRot.isOn = false;
            tglSLAMMovePos.isOn = false;
            tglSLAMMoveRot.isOn = false;
            tglLock.isOn = false;
        }

        void OnMiniMoveRot()
        {
            tglSingleHand.isOn = true; // Rotation 開啟則單手必開啟
            tglDoubleHand.isOn = false;
            tglMiniMovePos.isOn = false;
            tglSLAMMovePos.isOn = false;
            CheckLockCondition();
        }

        void OnSLAMMovePos()
        {
            tglSingleHand.isOn = false;
            tglDoubleHand.isOn = false;
            tglMiniMovePos.isOn = false;
            tglMiniMoveRot.isOn = false;
            tglSLAMMoveRot.isOn = false;
            tglLock.isOn = false;
        }

        void OnSLAMMoveRot()
        {
            tglSingleHand.isOn = true; // Rotation 開啟則單手必開啟
            tglDoubleHand.isOn = false;
            tglMiniMovePos.isOn = false;
            tglSLAMMovePos.isOn = false;
            CheckLockCondition();
        }

        void OnLock()
        {
            tglSingleHand.isOn = true;
            tglDoubleHand.isOn = false;
            tglMiniMovePos.isOn = false;
            tglMiniMoveRot.isOn = true;
            tglSLAMMovePos.isOn = false;
            tglSLAMMoveRot.isOn = true;
        }

        // --- 輔助邏輯 ---

        void CheckLockCondition()
        {
            // 若兩個 Rotation 都 On，則開啟 LOCK
            if (tglMiniMoveRot.isOn && tglSLAMMoveRot.isOn)
            {
                tglLock.isOn = true;
            }
        }
    }
}