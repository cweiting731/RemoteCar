using System;
using System.Collections.Generic;
using ROS2;
using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

namespace Main.Room.SLAMRoom
{
	public class RosSLAMCameraPose : MonoBehaviour
	{
		public enum PoseFrameMode
		{
			Raw,
			RosBaseLinkToUnity,
			RosCameraOpticalToUnity
		}

		[Header("ROS2 Settings")]
		public string topicName = "/slam/camera_pose";

		[Header("Map Integration")]
		[Tooltip("Root of the global/mini map. Use the same root as RosPointCloudSubscriber.miniRoomRoot.")]
		public Transform globalMapRoot;
		[Tooltip("Optional marker Transform that will be moved to the latest camera pose.")]
		public Transform cameraPoseMarker;
		public Vector3 manualPositionOffset = Vector3.zero;

		[Header("Coordinate Conversion")]
		[Tooltip("Use RosBaseLinkToUnity when the ROS2 source frame is x forward, y left, z up.")]
		public PoseFrameMode frameMode = PoseFrameMode.RosBaseLinkToUnity;
		public bool applyRotation = true;

		[Header("Path Display")]
		public bool showPath = true;
		public LineRenderer pathLineRenderer;
		public Material pathMaterial;
		public Color pathColor = new Color(1f, 0.72f, 0.18f, 1f);
		public float pathWidth = 0.025f;
		public int maxPathPoints = 2000;
		public float minPathPointDistance = 0.03f;

		[Header("Info")]
		public ROS2InfoManager ros2InfoManager;

		[Header("Pose Loss")]
		[Tooltip("If no pose is received for this many seconds, clear the old path and restart on the next pose. Set to 0 or less to disable.")]
		public float poseLostTimeoutSeconds = 1.0f;

		[Header("Debug")]
		public bool enableDebugLog = true;
		public int logEveryNFrames = 30;

		[Header("Simulation")]
		public bool useSimulatedPose = false;
		public float simulatedRateHz = 20f;
		public Vector3 simulatedCenter = Vector3.zero;
		public Vector2 simulatedSizeMeters = new Vector2(2.2f, 1.4f);
		public float simulatedHeightMeters = 0.9f;
		public bool simulatedLoopPath = true;

		private readonly object messageLock = new object();
		private readonly List<Vector3> pathPoints = new List<Vector3>();
		private PoseStampedMsg pendingMessage;
		private bool hasPendingMessage;
		private ROSConnection ros;
		private int receivedPoseCount;
		private float simulationTimer;
		private float lastPoseReceivedTime = float.NegativeInfinity;
		private bool poseWasLost;
		private Material runtimePathMaterial;

		public Vector3 LatestMapPosition { get; private set; }
		public Quaternion LatestMapRotation { get; private set; } = Quaternion.identity;
		public bool HasPose { get; private set; }

		private void Awake()
		{
			SetupMarker();
			SetupLineRenderer();
			SetPathVisible(showPath);
		}

		private void OnValidate()
		{
			pathWidth = Mathf.Max(0.0001f, pathWidth);
			maxPathPoints = Mathf.Max(2, maxPathPoints);
			minPathPointDistance = Mathf.Max(0f, minPathPointDistance);
			simulatedRateHz = Mathf.Max(0.1f, simulatedRateHz);
			simulatedSizeMeters = new Vector2(Mathf.Max(0.01f, simulatedSizeMeters.x), Mathf.Max(0.01f, simulatedSizeMeters.y));

			if (pathLineRenderer != null)
			{
				ConfigureLineRenderer();
				SetPathVisible(showPath);
			}
		}

		private void Start()
		{
			try
			{
				if (useSimulatedPose)
				{
					if (enableDebugLog)
					{
						Debug.Log("[ROS2 CameraPose] Using simulated camera pose.");
					}

					return;
				}

				ros = ROSConnection.GetOrCreateInstance();
				ros.Subscribe<PoseStampedMsg>(topicName, ReceiveCameraPose);

				if (enableDebugLog)
				{
					Debug.Log($"[ROS2 CameraPose] Subscribed to topic: {topicName}");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ROS2 CameraPose] Subscribe failed: {ex.Message}");
			}
		}

		public void ReconnectROS2()
		{
			Ros2ReconnectHelper.Reconnect(this);
		}

		private void Update()
		{
			try
			{
				if (useSimulatedPose)
				{
					UpdateSimulatedPose();
					return;
				}

				CheckPoseLoss();

				PoseStampedMsg message = null;
				lock (messageLock)
				{
					if (hasPendingMessage)
					{
						message = pendingMessage;
						pendingMessage = null;
						hasPendingMessage = false;
					}
				}

				if (message != null)
				{
					ApplyPoseMessage(message);
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 CameraPose] Update failed: {ex.Message}");
				}
			}
		}

		private void SetupMarker()
		{
			if (cameraPoseMarker == null)
			{
				cameraPoseMarker = transform;
			}

			ParentToGlobalMap(cameraPoseMarker);
		}

		private void SetupLineRenderer()
		{
			if (pathLineRenderer == null)
			{
				GameObject pathObject = new GameObject("SLAM Camera Pose Path");
				pathObject.transform.SetParent(globalMapRoot != null ? globalMapRoot : transform.parent, false);
				pathLineRenderer = pathObject.AddComponent<LineRenderer>();
			}
			else
			{
				ParentToGlobalMap(pathLineRenderer.transform);
			}

			ConfigureLineRenderer();
		}

		private void ConfigureLineRenderer()
		{
			pathLineRenderer.useWorldSpace = false;
			pathLineRenderer.loop = false;
			pathLineRenderer.widthMultiplier = pathWidth;
			pathLineRenderer.positionCount = pathPoints.Count;
			pathLineRenderer.numCornerVertices = 2;
			pathLineRenderer.numCapVertices = 2;
			pathLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			pathLineRenderer.receiveShadows = false;

			Material material = pathMaterial;
			if (material == null)
			{
				if (!Application.isPlaying)
				{
					pathLineRenderer.startColor = pathColor;
					pathLineRenderer.endColor = pathColor;
					return;
				}

				if (runtimePathMaterial == null)
				{
					Shader shader = Shader.Find("Sprites/Default");
					if (shader == null)
					{
						shader = Shader.Find("Unlit/Color");
					}

					if (shader != null)
					{
						runtimePathMaterial = new Material(shader)
						{
							name = "Runtime SLAM Camera Path Material",
							hideFlags = HideFlags.DontSave
						};
					}
				}

				material = runtimePathMaterial;
			}

			if (material != null)
			{
				pathLineRenderer.material = material;
				if (material.HasProperty("_Color"))
				{
					material.color = pathColor;
				}
			}

			pathLineRenderer.startColor = pathColor;
			pathLineRenderer.endColor = pathColor;
		}

		private void ReceiveCameraPose(PoseStampedMsg msg)
		{
			try
			{
				ros2InfoManager?.RecordTopicBytes(topicName, EstimatePoseStampedBytes(msg));

				lock (messageLock)
				{
					pendingMessage = msg;
					hasPendingMessage = true;
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 CameraPose] Receive failed: {ex.Message}");
				}
			}
			finally
			{
				receivedPoseCount++;
			}
		}

		private void ApplyPoseMessage(PoseStampedMsg msg)
		{
			if (msg?.pose?.position == null || msg.pose.orientation == null)
			{
				return;
			}

			Vector3 rosPosition = new Vector3(
				(float)msg.pose.position.x,
				(float)msg.pose.position.y,
				(float)msg.pose.position.z
			);
			Quaternion rosRotation = new Quaternion(
				(float)msg.pose.orientation.x,
				(float)msg.pose.orientation.y,
				(float)msg.pose.orientation.z,
				(float)msg.pose.orientation.w
			);

			ApplyMapPose(ConvertRosPositionToUnity(rosPosition), ConvertRosRotationToUnity(rosRotation), msg.header?.frame_id);
		}

		public void ApplyMapPose(Vector3 mapPosition, Quaternion mapRotation, string frameId = "")
		{
			if (poseWasLost)
			{
				poseWasLost = false;
			}

			LatestMapPosition = mapPosition + manualPositionOffset;
			LatestMapRotation = mapRotation;
			HasPose = true;
			lastPoseReceivedTime = Time.unscaledTime;

			if (cameraPoseMarker != null)
			{
				ParentToGlobalMap(cameraPoseMarker);
				cameraPoseMarker.localPosition = LatestMapPosition;
				if (applyRotation)
				{
					cameraPoseMarker.localRotation = LatestMapRotation;
				}
			}

			AddPathPoint(LatestMapPosition);

			if (enableDebugLog && receivedPoseCount % Mathf.Max(1, logEveryNFrames) == 0)
			{
				Debug.Log($"[ROS2 CameraPose] Received={receivedPoseCount}, frame={frameId}, mapPosition={LatestMapPosition}");
			}
		}

		public void SetPathVisible(bool visible)
		{
			showPath = visible;
			if (pathLineRenderer != null)
			{
				pathLineRenderer.enabled = visible;
			}
		}

		public void ClearPath()
		{
			pathPoints.Clear();
			if (pathLineRenderer != null)
			{
				pathLineRenderer.positionCount = 0;
			}
		}

		public void SetSimulatedPoseEnabled(bool enabled)
		{
			useSimulatedPose = enabled;
		}

		[ContextMenu("Simulate One Pose Message")]
		public void SimulateOnePoseMessage()
		{
			float angle = receivedPoseCount * 0.18f;
			PoseStampedMsg msg = new PoseStampedMsg();
			msg.header.frame_id = "simulated";
			msg.pose.position = new PointMsg(
				Mathf.Cos(angle) * simulatedSizeMeters.x * 0.5f,
				-simulatedHeightMeters,
				Mathf.Sin(angle) * simulatedSizeMeters.y * 0.5f
			);
			msg.pose.orientation = new RosMessageTypes.Geometry.QuaternionMsg(0.0, 0.0, 0.0, 1.0);

			ReceiveCameraPose(msg);
			ApplyPoseMessage(msg);
		}

		private void AddPathPoint(Vector3 mapPosition)
		{
			if (pathPoints.Count > 0 && Vector3.Distance(pathPoints[pathPoints.Count - 1], mapPosition) < minPathPointDistance)
			{
				return;
			}

			pathPoints.Add(mapPosition);
			while (pathPoints.Count > maxPathPoints)
			{
				pathPoints.RemoveAt(0);
			}

			if (pathLineRenderer == null)
			{
				return;
			}

			pathLineRenderer.positionCount = pathPoints.Count;
			pathLineRenderer.SetPositions(pathPoints.ToArray());
		}

		private void UpdateSimulatedPose()
		{
			simulationTimer += Time.deltaTime;
			float interval = 1f / simulatedRateHz;
			if (simulationTimer < interval)
			{
				return;
			}

			simulationTimer -= interval;

			float time = Time.unscaledTime;
			float angle = simulatedLoopPath ? time * 0.55f : time * 0.35f;
			Vector3 position = simulatedCenter + new Vector3(
				Mathf.Cos(angle) * simulatedSizeMeters.x * 0.5f,
				simulatedHeightMeters,
				Mathf.Sin(angle) * simulatedSizeMeters.y * 0.5f
			);
			Vector3 tangent = new Vector3(
				-Mathf.Sin(angle) * simulatedSizeMeters.x * 0.5f,
				0f,
				Mathf.Cos(angle) * simulatedSizeMeters.y * 0.5f
			);
			Quaternion rotation = tangent.sqrMagnitude > 0.0001f
				? Quaternion.LookRotation(tangent.normalized, Vector3.up)
				: Quaternion.identity;

			receivedPoseCount++;
			ros2InfoManager?.RecordTopicBytes(topicName, 80);
			ApplyMapPose(position, rotation, "simulated");
		}

		private void CheckPoseLoss()
		{
			if (!HasPose || poseLostTimeoutSeconds <= 0f || poseWasLost)
			{
				return;
			}

			if (Time.unscaledTime - lastPoseReceivedTime < poseLostTimeoutSeconds)
			{
				return;
			}

			poseWasLost = true;
			HasPose = false;
			ClearPath();

			if (enableDebugLog)
			{
				Debug.Log($"[ROS2 CameraPose] Pose lost. Cleared path after {poseLostTimeoutSeconds:0.##}s without updates.");
			}
		}

		public Vector3 ConvertRosPositionToUnity(Vector3 rosPosition)
		{
			switch (frameMode)
			{
				case PoseFrameMode.RosBaseLinkToUnity:
					return new Vector3(-rosPosition.y, rosPosition.z, rosPosition.x);
				case PoseFrameMode.RosCameraOpticalToUnity:
					return new Vector3(rosPosition.x, -rosPosition.y, rosPosition.z);
				default:
					return rosPosition;
			}
		}

		public Quaternion ConvertRosRotationToUnity(Quaternion rosRotation)
		{
			if (frameMode == PoseFrameMode.Raw)
			{
				return rosRotation;
			}

			Matrix4x4 rosMatrix = Matrix4x4.Rotate(rosRotation);
			Vector3 unityRight = ConvertRosPositionToUnity(ToVector3(rosMatrix.GetColumn(0)));
			Vector3 unityUp = ConvertRosPositionToUnity(ToVector3(rosMatrix.GetColumn(1)));
			Vector3 unityForward = ConvertRosPositionToUnity(ToVector3(rosMatrix.GetColumn(2)));

			if (unityForward.sqrMagnitude < 0.0001f || unityUp.sqrMagnitude < 0.0001f)
			{
				return Quaternion.identity;
			}

			unityForward.Normalize();
			unityUp = Vector3.ProjectOnPlane(unityUp, unityForward);
			if (unityUp.sqrMagnitude < 0.0001f)
			{
				unityUp = Vector3.ProjectOnPlane(unityRight, unityForward);
			}

			return Quaternion.LookRotation(unityForward, unityUp.normalized);
		}

		private Vector3 ToVector3(Vector4 value)
		{
			return new Vector3(value.x, value.y, value.z);
		}

		private void ParentToGlobalMap(Transform target)
		{
			if (target == null || globalMapRoot == null || target.parent == globalMapRoot)
			{
				return;
			}

			target.SetParent(globalMapRoot, false);
		}

		private long EstimatePoseStampedBytes(PoseStampedMsg msg)
		{
			int frameIdBytes = string.IsNullOrEmpty(msg?.header?.frame_id) ? 0 : System.Text.Encoding.UTF8.GetByteCount(msg.header.frame_id);
			return 8 + 4 + frameIdBytes + 7 * sizeof(double);
		}

		private void OnDestroy()
		{
			if (runtimePathMaterial != null)
			{
				Destroy(runtimePathMaterial);
				runtimePathMaterial = null;
			}
		}
	}
}
