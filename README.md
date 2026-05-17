# RemoteCar

RemoteCar 是一個以 Unity 打造的 Mixed Reality 專案，結合 Meta XR、OpenXR 與 ROS-TCP-Connector，用來展示空間互動、迷你房間、車輛控制與 ROS 資料串流等功能。

[實體遠端車輛控制專案](https://github.com/Nues0913/ros2_ws)

## 專案重點

- 使用 Meta XR / OpenXR 建立 MR / XR 執行環境。
- 透過 ROS-TCP-Connector 與外部 ROS 節點交換資料。
- 支援車輛控制介面，包含單手與雙手控制模式。
- 可將房間或場景內容縮放成可放在眼前的迷你模型。
- 提供 UI、標記、物件抓取與相機跟隨等互動元件。
- 內含影像串流顯示與 SLAM point cloud 與 camera pose 訂閱。

## 開發環境

- Unity 2022.3.60f1
- Meta XR SDK All 78.0.0
- OpenXR
- Unity Robotics ROS-TCP-Connector

## 目錄概覽

- `Assets/Scenes/`：主要場景，使用 `Main.unity`。
- `Assets/scripts/`：主要腳本。
- `Assets/scripts/Main/CarControl/`：ROS2 車輛控制與搖桿視覺化。
- `Assets/scripts/Main/Room/`：迷你房間、標籤與場景內容建構相關功能。
- `Assets/scripts/Main/UI/`：UI 跟隨、格線、即時圖表等介面元件。
- `Assets/scripts/Main/ROS2/`：ROS2 訊息統計、發布與訂閱輔助工具。
- `Assets/scripts/Main/StreamVideo/`：ROS2 影像串流相關腳本。

## 主要功能

### 1. 車輛控制

`CarControllerROS2` 會把手把輸入轉成 `th` / `hd` 指令並發布到 `/command/car`。`CarVisualizer` 則負責顯示虛擬搖桿狀態。

### 2. ROS2 資料串流

專案包含 ROS2 訊息統計與發布/訂閱相關腳本，並記錄不同 `topic` 的 `Mbps`，繪製折線圖紀錄。

- `RosStreamSubscriber` 會訂閱 `/camera/colored`，將 ROS 影像轉換後顯示到 UI 的 `RawImage`，並同步更新影像 FPS 與頻寬資訊。
- `RosPointCloudSubscriber` 會訂閱 `/slam/point_cloud`，把 `PointCloud2` 轉成粒子系統點雲，用來顯示 SLAM 建出的空間幾何。
- `RosSLAMCameraPose` 會訂閱 `/slam/camera_pose`，更新相機在地圖中的位置標記，並用線條顯示移動軌跡。

### 3. 迷你房間

`MiniRoomContentBuilder` 會利用 Meta 的 EffectMesh，在場景中搜尋系統自動產生的房間網格，接著把這些網格彙整並縮放成 `MiniRoom`。其中的 `playerMarker` 會依照 `CenterEyeAnchor` 對應到場景內 EffectMesh 的位置與方向進行轉換，讓使用者能在迷你房間中快速確認自身定位。

### 4. MR 互動 UI

專案內含跟隨相機的 UI、開關式控制群組、標記顯示、預測碰撞與物件生成等元件，適合做空間互動與控制面板整合。


## 基本使用方式

1. 使用 Unity 2022.3.60f1 開啟專案。
2. 確認 Package Manager 已安裝 Meta XR、OpenXR 與 ROS-TCP-Connector。
3. 開啟對應場景 `Main.unity`。
4. 在 Inspector 裡把必要引用接好，例如相機、手把輸入、UI、ROS2 管理器、房間根物件與控制開關，在 `Robotic -> ROS Settings` 設定好 `ROS 相關資訊`。
5. 根據 https://github.com/Nues0913/ros2_ws 設定遠端小車服務，連上你的 ROS 端橋接服務後執行場景，測試車輛控制、影像串流與迷你房間功能。

## 常見控制模式

- 單手控制：預設的車輛控制模式。
- 雙手控制：可在控制開關中切換。
- 迷你房間移動 / 旋轉：由 Control Setting 設定與管理。
- 鎖定模式：使用左手控制車輛，右手控制 MiniRoom & SLAMRoom 旋轉。
- UI 鎖定：UI 面板預設跟隨，點擊 UI 面板上面的 `Anchor` 會生成 `Spatial Anchor` 固定 UI 面板，再次點擊解除
- Align：初始化定位服務，會移動 `SLAMRoom`，將其中的 `CameraPose` 與 `MiniRoom` 的 `playerMarker` 定位在一起，**需確保在初始化時點擊使用，並讓使用者抱著小車(鏡頭朝前)**。

## 注意事項

> **注意：在 Meta Quest 3 上執行前請先完成房間掃描並儲存**
>
> 需要確保你的 Meta Quest 3 已經有儲存當前房間的掃描結果，否則系統產生的 EffectMesh 可能是即時或隨機生成的暫時模型，會導致 MiniRoom 內容與實際空間不一致或定位錯誤。建議在裝置上使用系統或 Meta App 的環境掃描功能完成並儲存該空間的掃描資料，然後再啟動此應用以確保 EffectMesh 與場景匹配。

- 迷你房間相關腳本會依場景中的 Room root 命名與層級結構工作，使用前請確認命名規則一致。
- 若要在 Meta Quest 或其他 XR 裝置上執行，請先確認 XR / OpenXR 設定與裝置權限都已完成。


