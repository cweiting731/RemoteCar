using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.VisualScripting;
using CarControl;
using Main.Room;

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
        public RoomTransformController rootRoomController;
        public RoomTransformController miniRoomController;
        public RoomTransformController slamRoomController;

        void Start()
        {
            // 使用 onValueChanged 監聽 Toggle 狀態改變
            // 使用 lambda 運算式時，isOn 是 Toggle 傳入的新狀態
            tglSingleHand.onValueChanged.AddListener((isOn) => { OnSingleHand(isOn); });
            tglDoubleHand.onValueChanged.AddListener((isOn) => { OnDoubleHand(isOn); });
            tglMiniMovePos.onValueChanged.AddListener((isOn) => { OnMiniMovePos(isOn); });
            tglMiniMoveRot.onValueChanged.AddListener((isOn) => { OnMiniMoveRot(isOn); });
            tglSLAMMovePos.onValueChanged.AddListener((isOn) => { OnSLAMMovePos(isOn); });
            tglSLAMMoveRot.onValueChanged.AddListener((isOn) => { OnSLAMMoveRot(isOn); });
            tglLock.onValueChanged.AddListener((isOn) => { OnLock(isOn); });

            // init value
            tglSingleHand.isOn = true;
            tglLock.isOn = true;
        }

        // --- 核心邏輯處理 ---
        // 當 Toggle 被打開 (true) 時觸發的互斥邏輯

        void OnSingleHand(bool isOn)
        {
            if (isOn)
            {
                carController.SetSingleHandMode(true);

                tglDoubleHand.isOn = false; // 單手開啟會關閉雙手
                carController.SetDoubleHandMode(false);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);
            }
            else {
                carController.SetSingleHandMode(false);
            }
        }

        void OnDoubleHand(bool isOn)
        {
            if (isOn)
            {
                // 雙手控制會關閉所有其他功能
                tglSingleHand.isOn = false;
                carController.SetSingleHandMode(false);

                carController.SetDoubleHandMode(true);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                tglMiniMoveRot.isOn = false;
                miniRoomController.SetRotateStatus(false);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);

                tglSLAMMoveRot.isOn = false;
                slamRoomController.SetRotateStatus(false);

                tglLock.isOn = false;
                rootRoomController.SetRotateStatus(false);
            }
            else {
                carController.SetDoubleHandMode(false);
            }
        }

        void OnMiniMovePos(bool isOn)
        {
            if (isOn)
            {
                tglSingleHand.isOn = false;
                carController.SetSingleHandMode(false);

                tglDoubleHand.isOn = false;
                carController.SetDoubleHandMode(false);

                miniRoomController.SetMoveStatus(true);

                tglMiniMoveRot.isOn = false;
                miniRoomController.SetRotateStatus(false);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);
                
                tglSLAMMoveRot.isOn = false;
                slamRoomController.SetRotateStatus(false);

                tglLock.isOn = false;
                rootRoomController.SetRotateStatus(false);
            }
            else
            {
                miniRoomController.SetMoveStatus(false);
            }
        }

        void OnMiniMoveRot(bool isOn)
        {
            if (isOn)
            {
                tglSingleHand.isOn = true; // Rotation 開啟則單手必開啟
                carController.SetSingleHandMode(true);

                tglDoubleHand.isOn = false;
                carController.SetDoubleHandMode(false);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                miniRoomController.SetRotateStatus(true);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);

                tglSLAMMoveRot.isOn = false;
                slamRoomController.SetRotateStatus(false);

                // CheckLockCondition();
            }
            else
            {
                miniRoomController.SetRotateStatus(false);
                // CheckLockCondition();
            }
        }

        void OnSLAMMovePos(bool isOn)
        {
            if (isOn)
            {
                tglSingleHand.isOn = false;
                carController.SetSingleHandMode(false);

                tglDoubleHand.isOn = false;
                carController.SetDoubleHandMode(false);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                tglMiniMoveRot.isOn = false;
                miniRoomController.SetRotateStatus(false);

                slamRoomController.SetMoveStatus(true);

                tglSLAMMoveRot.isOn = false;
                slamRoomController.SetRotateStatus(false);

                tglLock.isOn = false;
                rootRoomController.SetRotateStatus(false);
            }
            else 
            {
                slamRoomController.SetMoveStatus(false);
            }
        }

        void OnSLAMMoveRot(bool isOn)
        {
            if (isOn)
            {
                tglSingleHand.isOn = true; // Rotation 開啟則單手必開啟
                carController.SetSingleHandMode(true);

                tglDoubleHand.isOn = false;
                carController.SetDoubleHandMode(false);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                tglMiniMoveRot.isOn = false;
                miniRoomController.SetRotateStatus(false);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);

                slamRoomController.SetRotateStatus(true);
                // CheckLockCondition();
            }
            else 
            {
                slamRoomController.SetRotateStatus(false);
                // CheckLockCondition();
            }
        }

        void OnLock(bool isOn)
        {
            if (isOn) 
            {
                tglSingleHand.isOn = true;
                carController.SetSingleHandMode(true);

                tglDoubleHand.isOn = false;
                carController.SetDoubleHandMode(false);

                tglMiniMovePos.isOn = false;
                miniRoomController.SetMoveStatus(false);

                tglMiniMoveRot.isOn = false;
                miniRoomController.SetRotateStatus(false);

                tglSLAMMovePos.isOn = false;
                slamRoomController.SetMoveStatus(false);

                tglSLAMMoveRot.isOn = false;
                slamRoomController.SetRotateStatus(false);

                rootRoomController.SetRotateStatus(true);
            }
            else
            {
                rootRoomController.SetRotateStatus(false);
            }
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