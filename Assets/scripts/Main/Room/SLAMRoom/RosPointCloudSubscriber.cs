using System;
using System.Collections;
using System.Collections.Generic;
using ROS2;
using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.Serialization;

namespace Main.Room.SLAMRoom
{
	public class RosPointCloudSubscriber : MonoBehaviour
	{
		public enum PointCloudFrameMode
		{
			Raw,
			RosBaseLinkToUnity,
			RosCameraOpticalToUnity
		}

		[Header("ROS2 Settings")]
		public string topicName = "/slam/point_cloud";
		public bool enableAutoReconnect = true;
		public float autoReconnectIntervalSeconds = 2f;
		public float noDataReconnectTimeoutSeconds = 5f;

		[Header("Rendering")]
		public ParticleSystem targetParticleSystem;
		public Material particleMaterial;
		public Color defaultPointColor = Color.cyan;
		public float pointSize = 0.02f;
		public int maxPoints = 200000;

		[Header("Mini Room Transform")]
		public Transform miniRoomRoot;
		[FormerlySerializedAs("pointScale")]
		[Tooltip("Target world scale for the room. 1 means the point cloud is rendered at real-world meter size.")]
		public float roomScale = 1f;
		[Tooltip("When enabled, parent Transform scale is compensated so roomScale=1 is a real-world 1:1 room.")]
		public bool roomScaleIsWorldScale = true;

		[Header("ICP Alignment")]
		[Tooltip("Root containing MiniRoomContentBuilder generated GLOBAL_MESH objects. If empty, this searches by icpTargetRootName.")]
		public Transform icpTargetGlobalMeshRoot;
		public string icpTargetRootName = "MiniRoomGlobalMeshes";
		[Tooltip("Transform moved by ICP. Defaults to miniRoomRoot, then the ParticleSystem transform.")]
		public Transform icpTransformRoot;
		public int icpIterations = 12;
		public int icpSourceSampleCount = 1200;
		public int icpTargetSampleCount = 2500;
		[Tooltip("0 disables rejection. Use a larger value when the initial offset is big.")]
		public float icpMaxCorrespondenceDistance = 0f;
		public bool icpAllowScale = true;
		public bool icpClampScale = true;
		public Vector2 icpScaleClamp = new Vector2(0.25f, 4f);
		[Tooltip("How many source points are matched before yielding a frame during button-triggered ICP.")]
		public int icpSourcePointsPerFrame = 80;

		[Header("Coordinate Conversion")]
		[Tooltip("Use RosBaseLinkToUnity when the ROS2 source frame is x forward, y left, z up.")]
		public PointCloudFrameMode frameMode = PointCloudFrameMode.RosBaseLinkToUnity;
		public Vector3 manualPositionOffset = Vector3.zero;

		[Header("Debug")]
		public bool enableDebugLog = true;
		public int logEveryNFrames = 30;

		[Header("Update Rate")]
		public float renderIntervalSeconds = 2f;

		[Header("Info")]
		public ROS2InfoManager ros2InfoManager; // ▲ 用於更新 ROS2 連線與頻寬資訊的管理器

		[Header("Simulation")]
		public bool useSimulatedPointCloud = false;
		[Tooltip("When enabled, simulated room points are generated in a ROS-style frame and converted by frameMode, matching real point cloud data.")]
		public bool simulatedUsesCoordinateConversion = true;
		public int simulatedPointCount = 12000;
		[FormerlySerializedAs("simulatedRoomSize")]
		[Tooltip("Full-size simulated room dimensions in meters when roomScale is 1.")]
		public Vector3 simulatedRoomSizeMeters = new Vector3(4f, 2.7f, 5f);
		public float simulatedNoiseAmount = 0.01f;
		public bool animateSimulatedNoise = true;

		private readonly object messageLock = new object();
		private PointCloud2Msg pendingMessage;
		private double pendingReceiveUnixSeconds = -1.0;
		private bool hasPendingMessage;

		private ParticleSystem.Particle[] particleBuffer;
		private ParticleSystem particleSystemInstance;
		private Material runtimeParticleMaterial;
		private ROSConnection ros;
		private float nextReconnectTime = 0f;
		private float lastDataSeenTime = 0f;
		private int lastSeenCloudCount = 0;
		private int receivedCloudCount;
		private int lastRenderedPointCount;
		private float lastRenderTime = -9999f;
		private float lastScaleDebugLogTime = -9999f;

		private int xOffset = -1;
		private int yOffset = -1;
		private int zOffset = -1;
		private int rgbOffset = -1;
		private int rgbaOffset = -1;
		private int intensityOffset = -1;
		private string rgbFieldType = string.Empty;
		private string rgbaFieldType = string.Empty;
		private string intensityFieldType = string.Empty;
		private ParticleSystem.Particle[] icpParticleReadBuffer;
		private readonly List<Vector3> icpSourceWorldPoints = new List<Vector3>();
		private readonly List<Vector3> icpTargetWorldPoints = new List<Vector3>();
		private readonly List<Vector3> icpMatchedSourcePoints = new List<Vector3>();
		private readonly List<Vector3> icpMatchedTargetPoints = new List<Vector3>();
		private Coroutine icpAlignmentCoroutine;
		public bool IsIcpRunning { get; private set; }

		private void Awake()
		{
			SetupParticleSystem();
			ApplyRoomScale();
		}

		private void OnValidate()
		{
			pointSize = Mathf.Max(0.0001f, pointSize);
			roomScale = Mathf.Max(0.0001f, roomScale);
			renderIntervalSeconds = Mathf.Max(0.01f, renderIntervalSeconds);
			maxPoints = Mathf.Max(1, maxPoints);
			icpIterations = Mathf.Max(1, icpIterations);
			icpSourceSampleCount = Mathf.Max(8, icpSourceSampleCount);
			icpTargetSampleCount = Mathf.Max(8, icpTargetSampleCount);
			icpSourcePointsPerFrame = Mathf.Max(1, icpSourcePointsPerFrame);
			icpMaxCorrespondenceDistance = Mathf.Max(0f, icpMaxCorrespondenceDistance);
			icpScaleClamp = new Vector2(
				Mathf.Max(0.0001f, icpScaleClamp.x),
				Mathf.Max(0.0001f, Mathf.Max(icpScaleClamp.x, icpScaleClamp.y))
			);
			simulatedPointCount = Mathf.Max(1, simulatedPointCount);
			simulatedRoomSizeMeters = new Vector3(
				Mathf.Max(0.1f, simulatedRoomSizeMeters.x),
				Mathf.Max(0.1f, simulatedRoomSizeMeters.y),
				Mathf.Max(0.1f, simulatedRoomSizeMeters.z)
			);
			simulatedNoiseAmount = Mathf.Max(0f, simulatedNoiseAmount);
			ApplyRoomScale();
		}

		private void Start()
		{
			try
			{
				if (useSimulatedPointCloud)
				{
					if (enableDebugLog)
					{
						Debug.Log("[ROS2 PointCloud] Using simulated point cloud.");
					}

					return;
				}

				ros = ROSConnection.GetOrCreateInstance();
				ros.Subscribe<PointCloud2Msg>(topicName, ReceivePointCloud);
				lastDataSeenTime = Time.unscaledTime;

				if (enableDebugLog)
				{
					Debug.Log($"[ROS2 PointCloud] Subscribed to topic: {topicName}");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ROS2 PointCloud] Subscribe failed: {ex.Message}");
			}
		}

		public void ReconnectROS2()
		{
			Ros2ReconnectHelper.Reconnect(this, 0f);
			ResetROS2Subscriber();
		}

		public void ResetROS2Subscriber()
		{
			if (useSimulatedPointCloud)
			{
				return;
			}

			EnsureRosConnection();
			lock (messageLock)
			{
				pendingMessage = null;
				pendingReceiveUnixSeconds = -1.0;
				hasPendingMessage = false;
			}

			ClearCloud();
			ResubscribeNow("manual reset");
		}

		private void CheckAutoReconnect()
		{
			if (!enableAutoReconnect || useSimulatedPointCloud || ros == null)
			{
				return;
			}

			if (receivedCloudCount != lastSeenCloudCount)
			{
				lastSeenCloudCount = receivedCloudCount;
				lastDataSeenTime = Time.unscaledTime;
			}

			if (ros.HasConnectionError)
			{
				ReconnectAndResubscribeIfDue("connection error");
				return;
			}

			if (noDataReconnectTimeoutSeconds <= 0f)
			{
				return;
			}

			if (Time.unscaledTime - lastDataSeenTime < noDataReconnectTimeoutSeconds)
			{
				return;
			}

			ResubscribeIfDue($"no data for {noDataReconnectTimeoutSeconds:0.##}s");
		}

		private void ReconnectAndResubscribeIfDue(string reason)
		{
			float currentTime = Time.unscaledTime;
			if (currentTime < nextReconnectTime)
			{
				return;
			}

			nextReconnectTime = currentTime + Mathf.Max(0.1f, autoReconnectIntervalSeconds);
			Ros2ReconnectHelper.Reconnect(this);
			ResubscribeNow(reason);
			lastDataSeenTime = currentTime;
		}

		private void ResubscribeIfDue(string reason)
		{
			float currentTime = Time.unscaledTime;
			if (currentTime < nextReconnectTime)
			{
				return;
			}

			nextReconnectTime = currentTime + Mathf.Max(0.1f, autoReconnectIntervalSeconds);
			ResubscribeNow(reason);
			lastDataSeenTime = currentTime;
		}

		private void ResubscribeNow(string reason)
		{
			if (ros == null)
			{
				return;
			}

			if (enableDebugLog)
			{
				Debug.Log($"[ROS2 PointCloud] Re-subscribing to {topicName} ({reason}).");
			}

			ros.Unsubscribe(topicName);
			ros.Subscribe<PointCloud2Msg>(topicName, ReceivePointCloud);
			lastDataSeenTime = Time.unscaledTime;
			lastSeenCloudCount = receivedCloudCount;
		}

		private void EnsureRosConnection()
		{
			if (ros == null)
			{
				ros = ROSConnection.GetOrCreateInstance();
			}
		}

		private void Update()
		{
			try
			{
				if (enableAutoReconnect)				
				{
					CheckAutoReconnect();
				}
				ApplyRoomScale();

				float currentTime = Time.unscaledTime;
				if (currentTime - lastRenderTime < renderIntervalSeconds)
				{
					return;
				}

				if (useSimulatedPointCloud)
				{
					lastRenderTime = currentTime;
					RenderSimulatedPointCloud(currentTime);
					return;
				}

				PointCloud2Msg message = null;
				double receiveUnixSeconds = -1.0;
				lock (messageLock)
				{
					if (hasPendingMessage)
					{
						message = pendingMessage;
						receiveUnixSeconds = pendingReceiveUnixSeconds;
						pendingMessage = null;
						pendingReceiveUnixSeconds = -1.0;
						hasPendingMessage = false;
					}
				}

				if (message != null)
				{
					lastRenderTime = currentTime;
					RenderPointCloud(message, receiveUnixSeconds);
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 PointCloud] Update failed: {ex.Message}");
				}
			}
			finally
			{
				ApplyRoomScale();
			}
		}

		private void SetupParticleSystem()
		{
			particleSystemInstance = targetParticleSystem;

			if (particleSystemInstance == null)
			{
				particleSystemInstance = GetComponent<ParticleSystem>();
			}

			if (particleSystemInstance == null)
			{
				particleSystemInstance = gameObject.AddComponent<ParticleSystem>();
			}

			var main = particleSystemInstance.main;
			main.loop = false;
			main.playOnAwake = false;
			main.simulationSpace = ParticleSystemSimulationSpace.Local;
			main.maxParticles = Mathf.Max(1, maxPoints);
			main.startSpeed = 0f;
			main.startLifetime = 999999f;
			main.startSize = pointSize;
			main.scalingMode = ParticleSystemScalingMode.Hierarchy;

			var emission = particleSystemInstance.emission;
			emission.rateOverTime = 0f;

			var shape = particleSystemInstance.shape;
			shape.enabled = false;

			var renderer = particleSystemInstance.GetComponent<ParticleSystemRenderer>();
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.alignment = ParticleSystemRenderSpace.World;
			renderer.sortMode = ParticleSystemSortMode.None;

			renderer.material = GetParticleMaterial();

			particleSystemInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}

		private Material GetParticleMaterial()
		{
			if (particleMaterial != null && particleMaterial.shader != null && particleMaterial.shader.isSupported)
			{
				return particleMaterial;
			}

			if (runtimeParticleMaterial != null)
			{
				return runtimeParticleMaterial;
			}

			Shader shader = Shader.Find("MRTest/PointCloudVertexColor");
			if (shader == null || !shader.isSupported)
			{
				shader = Shader.Find("Mobile/Particles/Alpha Blended");
			}

			if (shader == null || !shader.isSupported)
			{
				shader = Shader.Find("Particles/Standard Unlit");
			}

			if (shader == null || !shader.isSupported)
			{
				shader = Shader.Find("Unlit/Color");
			}

			if (shader == null)
			{
				Debug.LogError("[ROS2 PointCloud] No supported particle shader found.");
				return particleMaterial;
			}

			runtimeParticleMaterial = new Material(shader)
			{
				name = "Runtime PointCloud Particle Material",
				hideFlags = HideFlags.DontSave
			};

			if (runtimeParticleMaterial.HasProperty("_Color"))
			{
				runtimeParticleMaterial.SetColor("_Color", Color.white);
			}

			return runtimeParticleMaterial;
		}

		private void ReceivePointCloud(PointCloud2Msg msg)
		{
			try
			{
				if (msg?.data != null)
				{
					ros2InfoManager?.RecordTopicBytes(topicName, msg.data.LongLength);
				}

				lock (messageLock)
				{
					pendingMessage = msg;
					pendingReceiveUnixSeconds = ROS2InfoManager.GetCurrentUnixSeconds();
					hasPendingMessage = true;
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 PointCloud] Receive failed: {ex.Message}");
				}
			}
			finally
			{
				receivedCloudCount++;
			}
		}

		private void RenderPointCloud(PointCloud2Msg msg, double receiveUnixSeconds)
		{
			try
			{
				if (msg == null || msg.data == null || msg.data.Length == 0 || msg.point_step == 0)
				{
					ClearCloud();
					return;
				}

				CacheFieldOffsets(msg);
				if (xOffset < 0 || yOffset < 0 || zOffset < 0)
				{
					if (enableDebugLog)
					{
						Debug.LogWarning("[ROS2 PointCloud] Missing x/y/z fields.");
					}

					ClearCloud();
					return;
				}

				int pointStep = Mathf.Max(1, (int)msg.point_step);
				int rowStep = Mathf.Max(pointStep, (int)msg.row_step);
				int width = Mathf.Max(1, (int)msg.width);
				int height = Mathf.Max(1, (int)msg.height);
				int pointCount = Mathf.Min((int)((long)width * height), maxPoints);
				int availablePoints = Mathf.Min(pointCount, msg.data.Length / pointStep);

				EnsureParticleCapacity(availablePoints);

				int renderedCount = 0;
				int largestPositionOffset = Mathf.Max(xOffset, Mathf.Max(yOffset, zOffset));
				for (int i = 0; i < availablePoints; i++)
				{
					int row = i / width;
					int col = i % width;
					int baseIndex = row * rowStep + col * pointStep;

					if (baseIndex + largestPositionOffset + 3 >= msg.data.Length)
					{
						break;
					}

					Vector3 position = ReadPointPosition(msg, baseIndex);
					if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
						float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
					{
						continue;
					}

					particleBuffer[renderedCount] = new ParticleSystem.Particle
					{
						position = position + manualPositionOffset,
						startColor = ReadPointColor(msg, baseIndex),
						startSize = pointSize,
						remainingLifetime = 999999f,
						startLifetime = 999999f,
						velocity = Vector3.zero,
						rotation = 0f,
						angularVelocity = 0f
					};

					renderedCount++;
					if (renderedCount >= particleBuffer.Length)
					{
						break;
					}
				}

				if (renderedCount == 0)
				{
					ClearCloud();
					return;
				}

				particleSystemInstance.SetParticles(particleBuffer, renderedCount);
				particleSystemInstance.Play();
				lastRenderedPointCount = renderedCount;
				ros2InfoManager?.RecordTopicDisplayLatency(topicName, msg.header, receiveUnixSeconds);

				if (enableDebugLog && receivedCloudCount % Mathf.Max(1, logEveryNFrames) == 0)
				{
					Debug.Log($"[ROS2 PointCloud] Received={receivedCloudCount}, rendered={renderedCount}, frame={msg.header.frame_id}, step={pointStep}, rowStep={rowStep}");
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 PointCloud] Render failed: {ex.Message}");
				}
			}
			finally
			{
				ApplyRoomScale();
			}
		}

		private void RenderSimulatedPointCloud(float time)
		{
			try
			{
				int pointCount = Mathf.Min(simulatedPointCount, maxPoints);
				EnsureParticleCapacity(pointCount);

				Vector3 halfRoom = simulatedRoomSizeMeters * 0.5f;
				int renderedCount = 0;

				for (int i = 0; i < pointCount; i++)
				{
					Vector3 position = GetSimulatedRoomPoint(i, pointCount, halfRoom, time);
					if (simulatedUsesCoordinateConversion)
					{
						position = ConvertRosPositionToUnity(ConvertUnityPositionToRos(position));
					}

					Color color = GetSimulatedRoomColor(i, pointCount, position, halfRoom);

					particleBuffer[renderedCount] = new ParticleSystem.Particle
					{
						position = position + manualPositionOffset,
						startColor = color,
						startSize = pointSize,
						remainingLifetime = 999999f,
						startLifetime = 999999f,
						velocity = Vector3.zero,
						rotation = 0f,
						angularVelocity = 0f
					};

					renderedCount++;
				}

				particleSystemInstance.SetParticles(particleBuffer, renderedCount);
				particleSystemInstance.Play();
				lastRenderedPointCount = renderedCount;

				if (enableDebugLog && receivedCloudCount % Mathf.Max(1, logEveryNFrames) == 0)
				{
					Debug.Log($"[ROS2 PointCloud] Simulated rendered={renderedCount}, fullSizeMeters={simulatedRoomSizeMeters}, roomScale={roomScale}");
				}
			}
			catch (Exception ex)
			{
				if (enableDebugLog)
				{
					Debug.LogError($"[ROS2 PointCloud] Simulate failed: {ex.Message}");
				}
			}
			finally
			{
				receivedCloudCount++;
				ApplyRoomScale();
			}
		}

		private Vector3 GetSimulatedRoomPoint(int index, int totalCount, Vector3 halfRoom, float time)
		{
			float u = Halton(index + 1, 2);
			float v = Halton(index + 1, 3);
			int band = Mathf.FloorToInt((index / Mathf.Max(1f, totalCount - 1f)) * 10f);
			Vector3 position;

			if (band <= 1)
			{
				position = new Vector3(Mathf.Lerp(-halfRoom.x, halfRoom.x, u), -halfRoom.y, Mathf.Lerp(-halfRoom.z, halfRoom.z, v));
			}
			else if (band == 2)
			{
				position = new Vector3(Mathf.Lerp(-halfRoom.x, halfRoom.x, u), halfRoom.y, Mathf.Lerp(-halfRoom.z, halfRoom.z, v));
			}
			else if (band <= 4)
			{
				float z = band == 3 ? -halfRoom.z : halfRoom.z;
				position = new Vector3(Mathf.Lerp(-halfRoom.x, halfRoom.x, u), Mathf.Lerp(-halfRoom.y, halfRoom.y, v), z);
			}
			else if (band <= 6)
			{
				float x = band == 5 ? -halfRoom.x : halfRoom.x;
				position = new Vector3(x, Mathf.Lerp(-halfRoom.y, halfRoom.y, v), Mathf.Lerp(-halfRoom.z, halfRoom.z, u));
			}
			else
			{
				position = GetSimulatedObstaclePoint(index, u, v, halfRoom);
			}

			if (simulatedNoiseAmount > 0f)
			{
				float noiseTime = animateSimulatedNoise ? time : 0f;
				position += new Vector3(
					Mathf.PerlinNoise(index * 0.017f, noiseTime) - 0.5f,
					Mathf.PerlinNoise(index * 0.031f, noiseTime + 7.1f) - 0.5f,
					Mathf.PerlinNoise(index * 0.047f, noiseTime + 13.7f) - 0.5f
				) * simulatedNoiseAmount;
			}

			return position;
		}

		private Vector3 GetSimulatedObstaclePoint(int index, float u, float v, Vector3 halfRoom)
		{
			Vector3 halfBox = new Vector3(
				Mathf.Min(0.45f, halfRoom.x * 0.22f),
				Mathf.Min(0.45f, halfRoom.y * 0.35f),
				Mathf.Min(0.35f, halfRoom.z * 0.18f)
			);
			Vector3 center = new Vector3(halfRoom.x * 0.25f, -halfRoom.y + halfBox.y, halfRoom.z * 0.15f);
			int face = index % 5;

			switch (face)
			{
				case 0:
					return center + new Vector3(Mathf.Lerp(-halfBox.x, halfBox.x, u), halfBox.y, Mathf.Lerp(-halfBox.z, halfBox.z, v));
				case 1:
					return center + new Vector3(-halfBox.x, Mathf.Lerp(-halfBox.y, halfBox.y, v), Mathf.Lerp(-halfBox.z, halfBox.z, u));
				case 2:
					return center + new Vector3(halfBox.x, Mathf.Lerp(-halfBox.y, halfBox.y, v), Mathf.Lerp(-halfBox.z, halfBox.z, u));
				case 3:
					return center + new Vector3(Mathf.Lerp(-halfBox.x, halfBox.x, u), Mathf.Lerp(-halfBox.y, halfBox.y, v), -halfBox.z);
				default:
					return center + new Vector3(Mathf.Lerp(-halfBox.x, halfBox.x, u), Mathf.Lerp(-halfBox.y, halfBox.y, v), halfBox.z);
			}
		}

		private Color GetSimulatedRoomColor(int index, int totalCount, Vector3 position, Vector3 halfRoom)
		{
			int band = Mathf.FloorToInt((index / Mathf.Max(1f, totalCount - 1f)) * 10f);

			if (band <= 1)
			{
				return new Color(0.2f, 0.85f, 0.95f, 1f);
			}

			if (band == 2)
			{
				return new Color(0.45f, 0.65f, 1f, 1f);
			}

			if (band <= 6)
			{
				float height = Mathf.InverseLerp(-halfRoom.y, halfRoom.y, position.y);
				return Color.Lerp(new Color(0.15f, 0.45f, 1f, 1f), new Color(0.95f, 0.95f, 1f, 1f), height);
			}

			return new Color(1f, 0.72f, 0.25f, 1f);
		}

		private float Halton(int index, int radix)
		{
			float result = 0f;
			float fraction = 1f / radix;

			while (index > 0)
			{
				result += fraction * (index % radix);
				index /= radix;
				fraction /= radix;
			}

			return result;
		}

		public void AlignPointCloudToGlobalMeshByICPFromButton()
		{
			if (!Application.isPlaying)
			{
				AlignPointCloudToGlobalMeshByICP();
				return;
			}

			if (IsIcpRunning)
			{
				if (enableDebugLog)
				{
					Debug.Log("[ROS2 PointCloud ICP] ICP is already running.");
				}

				return;
			}

			icpAlignmentCoroutine = StartCoroutine(AlignPointCloudToGlobalMeshByICPCoroutine());
		}

		[ContextMenu("Align PointCloud To GlobalMesh By ICP")]
		public bool AlignPointCloudToGlobalMeshByICP()
		{
			try
			{
				if (particleSystemInstance == null)
				{
					SetupParticleSystem();
				}

				Transform targetRoot = ResolveIcpTargetRoot();
				if (targetRoot == null)
				{
					Debug.LogWarning($"[ROS2 PointCloud ICP] Target root '{icpTargetRootName}' was not found.");
					return false;
				}

				Transform alignRoot = ResolveIcpTransformRoot();
				if (alignRoot == null)
				{
					Debug.LogWarning("[ROS2 PointCloud ICP] No transform root available to move.");
					return false;
				}

				if (!CollectPointCloudWorldSamples(icpSourceWorldPoints, icpSourceSampleCount))
				{
					Debug.LogWarning("[ROS2 PointCloud ICP] No point cloud samples available. Wait for a cloud frame first.");
					return false;
				}

				if (!CollectTargetMeshWorldSamples(targetRoot, icpTargetWorldPoints, icpTargetSampleCount))
				{
					Debug.LogWarning($"[ROS2 PointCloud ICP] No mesh samples found under '{targetRoot.name}'.");
					return false;
				}

				Quaternion cumulativeRotation = Quaternion.identity;
				float cumulativeScale = 1f;
				Vector3 cumulativeTranslation = Vector3.zero;
				float finalRmse = 0f;
				int finalMatchCount = 0;

				for (int i = 0; i < Mathf.Max(1, icpIterations); i++)
				{
					BuildNearestNeighborPairs(
						icpSourceWorldPoints,
						icpTargetWorldPoints,
						icpMatchedSourcePoints,
						icpMatchedTargetPoints,
						icpMaxCorrespondenceDistance
					);

					if (icpMatchedSourcePoints.Count < 6)
					{
						Debug.LogWarning($"[ROS2 PointCloud ICP] Too few matches ({icpMatchedSourcePoints.Count}). Try increasing icpMaxCorrespondenceDistance.");
						return false;
					}

					if (!TryComputeYawSimilarity(
						icpMatchedSourcePoints,
						icpMatchedTargetPoints,
						out Quaternion stepRotation,
						out float stepScale,
						out Vector3 stepTranslation,
						out finalRmse))
					{
						Debug.LogWarning("[ROS2 PointCloud ICP] Failed to compute transform.");
						return false;
					}

					finalMatchCount = icpMatchedSourcePoints.Count;
					for (int p = 0; p < icpSourceWorldPoints.Count; p++)
					{
						icpSourceWorldPoints[p] = stepRotation * (icpSourceWorldPoints[p] * stepScale) + stepTranslation;
					}

					cumulativeTranslation = stepRotation * (cumulativeTranslation * stepScale) + stepTranslation;
					cumulativeRotation = stepRotation * cumulativeRotation;
					cumulativeScale *= stepScale;
				}

				ApplyIcpTransform(alignRoot, cumulativeRotation, cumulativeScale, cumulativeTranslation);

				if (enableDebugLog)
				{
					Debug.Log($"[ROS2 PointCloud ICP] Aligned '{alignRoot.name}' to '{targetRoot.name}'. matches={finalMatchCount}, rmse={finalRmse:0.0000}, scaleDelta={cumulativeScale:0.####}, yawDelta={cumulativeRotation.eulerAngles.y:0.##}");
				}

				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[ROS2 PointCloud ICP] Align failed: {ex.Message}");
				return false;
			}
		}

		private IEnumerator AlignPointCloudToGlobalMeshByICPCoroutine()
		{
			IsIcpRunning = true;

			try
			{
				if (particleSystemInstance == null)
				{
					SetupParticleSystem();
				}

				Transform targetRoot = ResolveIcpTargetRoot();
				if (targetRoot == null)
				{
					Debug.LogWarning($"[ROS2 PointCloud ICP] Target root '{icpTargetRootName}' was not found.");
					yield break;
				}

				Transform alignRoot = ResolveIcpTransformRoot();
				if (alignRoot == null)
				{
					Debug.LogWarning("[ROS2 PointCloud ICP] No transform root available to move.");
					yield break;
				}

				if (!CollectPointCloudWorldSamples(icpSourceWorldPoints, icpSourceSampleCount))
				{
					Debug.LogWarning("[ROS2 PointCloud ICP] No point cloud samples available. Wait for a cloud frame first.");
					yield break;
				}

				if (!CollectTargetMeshWorldSamples(targetRoot, icpTargetWorldPoints, icpTargetSampleCount))
				{
					Debug.LogWarning($"[ROS2 PointCloud ICP] No mesh samples found under '{targetRoot.name}'.");
					yield break;
				}

				Quaternion cumulativeRotation = Quaternion.identity;
				float cumulativeScale = 1f;
				Vector3 cumulativeTranslation = Vector3.zero;
				float finalRmse = 0f;
				int finalMatchCount = 0;

				for (int i = 0; i < Mathf.Max(1, icpIterations); i++)
				{
					yield return BuildNearestNeighborPairsCoroutine(
						icpSourceWorldPoints,
						icpTargetWorldPoints,
						icpMatchedSourcePoints,
						icpMatchedTargetPoints,
						icpMaxCorrespondenceDistance
					);

					if (icpMatchedSourcePoints.Count < 6)
					{
						Debug.LogWarning($"[ROS2 PointCloud ICP] Too few matches ({icpMatchedSourcePoints.Count}). Try increasing icpMaxCorrespondenceDistance.");
						yield break;
					}

					if (!TryComputeYawSimilarity(
						icpMatchedSourcePoints,
						icpMatchedTargetPoints,
						out Quaternion stepRotation,
						out float stepScale,
						out Vector3 stepTranslation,
						out finalRmse))
					{
						Debug.LogWarning("[ROS2 PointCloud ICP] Failed to compute transform.");
						yield break;
					}

					finalMatchCount = icpMatchedSourcePoints.Count;
					for (int p = 0; p < icpSourceWorldPoints.Count; p++)
					{
						icpSourceWorldPoints[p] = stepRotation * (icpSourceWorldPoints[p] * stepScale) + stepTranslation;
					}

					cumulativeTranslation = stepRotation * (cumulativeTranslation * stepScale) + stepTranslation;
					cumulativeRotation = stepRotation * cumulativeRotation;
					cumulativeScale *= stepScale;
					yield return null;
				}

				ApplyIcpTransform(alignRoot, cumulativeRotation, cumulativeScale, cumulativeTranslation);

				if (enableDebugLog)
				{
					Debug.Log($"[ROS2 PointCloud ICP] Aligned '{alignRoot.name}' to '{targetRoot.name}'. matches={finalMatchCount}, rmse={finalRmse:0.0000}, scaleDelta={cumulativeScale:0.####}, yawDelta={cumulativeRotation.eulerAngles.y:0.##}");
				}
			}
			finally
			{
				IsIcpRunning = false;
				icpAlignmentCoroutine = null;
			}
		}

		private Transform ResolveIcpTargetRoot()
		{
			if (icpTargetGlobalMeshRoot != null)
			{
				return icpTargetGlobalMeshRoot;
			}

			if (string.IsNullOrEmpty(icpTargetRootName))
			{
				return null;
			}

			Transform[] transforms = FindObjectsOfType<Transform>(true);
			foreach (Transform candidate in transforms)
			{
				if (candidate.name.Equals(icpTargetRootName, StringComparison.OrdinalIgnoreCase))
				{
					icpTargetGlobalMeshRoot = candidate;
					return candidate;
				}
			}

			return null;
		}

		private Transform ResolveIcpTransformRoot()
		{
			if (icpTransformRoot != null)
			{
				return icpTransformRoot;
			}

			if (miniRoomRoot != null)
			{
				return miniRoomRoot;
			}

			return particleSystemInstance != null ? particleSystemInstance.transform : transform;
		}

		private bool CollectPointCloudWorldSamples(List<Vector3> samples, int maxSampleCount)
		{
			samples.Clear();

			if (particleSystemInstance == null)
			{
				return false;
			}

			int particleCount = particleSystemInstance.particleCount;
			if (particleCount <= 0)
			{
				particleCount = lastRenderedPointCount;
			}

			if (particleCount <= 0)
			{
				return false;
			}

			if (icpParticleReadBuffer == null || icpParticleReadBuffer.Length < particleCount)
			{
				icpParticleReadBuffer = new ParticleSystem.Particle[Mathf.Max(particleCount, 1024)];
			}

			int readCount = particleSystemInstance.GetParticles(icpParticleReadBuffer);
			if (readCount <= 0)
			{
				readCount = Mathf.Min(lastRenderedPointCount, particleBuffer != null ? particleBuffer.Length : 0);
				for (int i = 0; i < readCount; i++)
				{
					icpParticleReadBuffer[i] = particleBuffer[i];
				}
			}

			if (readCount <= 0)
			{
				return false;
			}

			int stride = Mathf.Max(1, readCount / Mathf.Max(1, maxSampleCount));
			for (int i = 0; i < readCount && samples.Count < maxSampleCount; i += stride)
			{
				Vector3 worldPoint = particleSystemInstance.transform.TransformPoint(icpParticleReadBuffer[i].position);
				if (IsFinite(worldPoint))
				{
					samples.Add(worldPoint);
				}
			}

			return samples.Count >= 6;
		}

		private bool CollectTargetMeshWorldSamples(Transform targetRoot, List<Vector3> samples, int maxSampleCount)
		{
			samples.Clear();

			if (targetRoot == null)
			{
				return false;
			}

			MeshFilter[] meshFilters = targetRoot.GetComponentsInChildren<MeshFilter>(true);
			int totalVertexCount = 0;
			foreach (MeshFilter mf in meshFilters)
			{
				if (mf != null && mf.sharedMesh != null)
				{
					totalVertexCount += mf.sharedMesh.vertexCount;
				}
			}

			if (totalVertexCount <= 0)
			{
				return false;
			}

			int stride = Mathf.Max(1, totalVertexCount / Mathf.Max(1, maxSampleCount));
			int globalVertexIndex = 0;

			foreach (MeshFilter mf in meshFilters)
			{
				if (mf == null || mf.sharedMesh == null)
				{
					continue;
				}

				Vector3[] vertices = mf.sharedMesh.vertices;
				for (int i = 0; i < vertices.Length && samples.Count < maxSampleCount; i++)
				{
					if (globalVertexIndex % stride == 0)
					{
						Vector3 worldPoint = mf.transform.TransformPoint(vertices[i]);
						if (IsFinite(worldPoint))
						{
							samples.Add(worldPoint);
						}
					}

					globalVertexIndex++;
				}
			}

			return samples.Count >= 6;
		}

		private void BuildNearestNeighborPairs(
			List<Vector3> sourcePoints,
			List<Vector3> targetPoints,
			List<Vector3> matchedSource,
			List<Vector3> matchedTarget,
			float maxDistance)
		{
			matchedSource.Clear();
			matchedTarget.Clear();

			float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
			for (int i = 0; i < sourcePoints.Count; i++)
			{
				Vector3 source = sourcePoints[i];
				float bestDistanceSqr = float.PositiveInfinity;
				Vector3 bestTarget = Vector3.zero;

				for (int t = 0; t < targetPoints.Count; t++)
				{
					float distanceSqr = (targetPoints[t] - source).sqrMagnitude;
					if (distanceSqr < bestDistanceSqr)
					{
						bestDistanceSqr = distanceSqr;
						bestTarget = targetPoints[t];
					}
				}

				if (bestDistanceSqr <= maxDistanceSqr)
				{
					matchedSource.Add(source);
					matchedTarget.Add(bestTarget);
				}
			}
		}

		private IEnumerator BuildNearestNeighborPairsCoroutine(
			List<Vector3> sourcePoints,
			List<Vector3> targetPoints,
			List<Vector3> matchedSource,
			List<Vector3> matchedTarget,
			float maxDistance)
		{
			matchedSource.Clear();
			matchedTarget.Clear();

			float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
			int pointsSinceYield = 0;

			for (int i = 0; i < sourcePoints.Count; i++)
			{
				Vector3 source = sourcePoints[i];
				float bestDistanceSqr = float.PositiveInfinity;
				Vector3 bestTarget = Vector3.zero;

				for (int t = 0; t < targetPoints.Count; t++)
				{
					float distanceSqr = (targetPoints[t] - source).sqrMagnitude;
					if (distanceSqr < bestDistanceSqr)
					{
						bestDistanceSqr = distanceSqr;
						bestTarget = targetPoints[t];
					}
				}

				if (bestDistanceSqr <= maxDistanceSqr)
				{
					matchedSource.Add(source);
					matchedTarget.Add(bestTarget);
				}

				pointsSinceYield++;
				if (pointsSinceYield >= icpSourcePointsPerFrame)
				{
					pointsSinceYield = 0;
					yield return null;
				}
			}
		}

		private bool TryComputeYawSimilarity(
			List<Vector3> sourcePoints,
			List<Vector3> targetPoints,
			out Quaternion rotation,
			out float scale,
			out Vector3 translation,
			out float rmse)
		{
			rotation = Quaternion.identity;
			scale = 1f;
			translation = Vector3.zero;
			rmse = 0f;

			int count = Mathf.Min(sourcePoints.Count, targetPoints.Count);
			if (count < 2)
			{
				return false;
			}

			Vector3 sourceCentroid = Vector3.zero;
			Vector3 targetCentroid = Vector3.zero;
			for (int i = 0; i < count; i++)
			{
				sourceCentroid += sourcePoints[i];
				targetCentroid += targetPoints[i];
			}

			sourceCentroid /= count;
			targetCentroid /= count;

			float a = 0f;
			float b = 0f;
			float sourceSq = 0f;
			for (int i = 0; i < count; i++)
			{
				Vector3 source = sourcePoints[i] - sourceCentroid;
				Vector3 target = targetPoints[i] - targetCentroid;

				a += source.x * target.x + source.z * target.z;
				b += source.z * target.x - source.x * target.z;
				sourceSq += source.x * source.x + source.z * source.z;
			}

			if (sourceSq < 0.000001f)
			{
				return false;
			}

			float yawRadians = Mathf.Atan2(b, a);
			rotation = Quaternion.Euler(0f, yawRadians * Mathf.Rad2Deg, 0f);

			if (icpAllowScale)
			{
				scale = Mathf.Sqrt(a * a + b * b) / sourceSq;
				if (icpClampScale)
				{
					scale = Mathf.Clamp(scale, icpScaleClamp.x, icpScaleClamp.y);
				}
			}

			translation = targetCentroid - (rotation * (sourceCentroid * scale));

			float errorSum = 0f;
			for (int i = 0; i < count; i++)
			{
				Vector3 aligned = rotation * (sourcePoints[i] * scale) + translation;
				errorSum += (targetPoints[i] - aligned).sqrMagnitude;
			}

			rmse = Mathf.Sqrt(errorSum / count);
			return IsFinite(translation) && !float.IsNaN(scale) && !float.IsInfinity(scale);
		}

		private void ApplyIcpTransform(Transform alignRoot, Quaternion rotation, float scaleDelta, Vector3 translation)
		{
			Vector3 oldPosition = alignRoot.position;
			alignRoot.position = rotation * (oldPosition * scaleDelta) + translation;
			alignRoot.rotation = rotation * alignRoot.rotation;

			if (icpAllowScale)
			{
				alignRoot.localScale *= scaleDelta;

				Transform scaleTarget = miniRoomRoot != null
					? miniRoomRoot
					: (particleSystemInstance != null ? particleSystemInstance.transform : transform);

				if (alignRoot == scaleTarget)
				{
					roomScale = roomScaleIsWorldScale && alignRoot.parent != null
						? Mathf.Max(0.0001f, AverageAbsScale(alignRoot.lossyScale))
						: Mathf.Max(0.0001f, AverageAbsScale(alignRoot.localScale));
					ApplyRoomScale();
				}
			}
		}

		private float AverageAbsScale(Vector3 scale)
		{
			return (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
		}

		private bool IsFinite(Vector3 value)
		{
			return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
				!float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
		}

		private void EnsureParticleCapacity(int requiredCount)
		{
			requiredCount = Mathf.Clamp(requiredCount, 1, Mathf.Max(1, maxPoints));

			if (particleBuffer == null || particleBuffer.Length < requiredCount)
			{
				particleBuffer = new ParticleSystem.Particle[Mathf.Max(requiredCount, 1024)];
			}

			var main = particleSystemInstance.main;
			if (main.maxParticles < particleBuffer.Length)
			{
				main.maxParticles = particleBuffer.Length;
			}
		}

		private void ClearCloud()
		{
			lastRenderedPointCount = 0;

			if (particleSystemInstance != null)
			{
				particleSystemInstance.Clear();
			}
		}

		private void CacheFieldOffsets(PointCloud2Msg msg)
		{
			xOffset = yOffset = zOffset = -1;
			rgbOffset = rgbaOffset = intensityOffset = -1;
			rgbFieldType = rgbaFieldType = intensityFieldType = string.Empty;

			if (msg.fields == null)
			{
				return;
			}

			for (int i = 0; i < msg.fields.Length; i++)
			{
				var field = msg.fields[i];
				string fieldName = field.name ?? string.Empty;

				if (fieldName == "x")
				{
					xOffset = (int)field.offset;
				}
				else if (fieldName == "y")
				{
					yOffset = (int)field.offset;
				}
				else if (fieldName == "z")
				{
					zOffset = (int)field.offset;
				}
				else if (fieldName == "rgb")
				{
					rgbOffset = (int)field.offset;
					rgbFieldType = field.datatype.ToString();
				}
				else if (fieldName == "rgba")
				{
					rgbaOffset = (int)field.offset;
					rgbaFieldType = field.datatype.ToString();
				}
				else if (fieldName == "intensity")
				{
					intensityOffset = (int)field.offset;
					intensityFieldType = field.datatype.ToString();
				}
			}
		}

		private Vector3 ReadPointPosition(PointCloud2Msg msg, int baseIndex)
		{
			float x = ReadSingle(msg.data, baseIndex + xOffset, msg.is_bigendian);
			float y = ReadSingle(msg.data, baseIndex + yOffset, msg.is_bigendian);
			float z = ReadSingle(msg.data, baseIndex + zOffset, msg.is_bigendian);

			return ConvertRosPositionToUnity(new Vector3(x, y, z));
		}

		public Vector3 ConvertRosPositionToUnity(Vector3 rosPosition)
		{
			switch (frameMode)
			{
				case PointCloudFrameMode.RosBaseLinkToUnity:
					return new Vector3(-rosPosition.y, rosPosition.z, rosPosition.x);
				case PointCloudFrameMode.RosCameraOpticalToUnity:
					return new Vector3(rosPosition.x, -rosPosition.y, rosPosition.z);
				default:
					return rosPosition;
			}
		}

		public Vector3 ConvertUnityPositionToRos(Vector3 unityPosition)
		{
			switch (frameMode)
			{
				case PointCloudFrameMode.RosBaseLinkToUnity:
					return new Vector3(unityPosition.z, -unityPosition.x, unityPosition.y);
				case PointCloudFrameMode.RosCameraOpticalToUnity:
					return new Vector3(unityPosition.x, -unityPosition.y, unityPosition.z);
				default:
					return unityPosition;
			}
		}

		private Color ReadPointColor(PointCloud2Msg msg, int baseIndex)
		{
			if (rgbOffset >= 0)
			{
				return DecodePackedColor(msg.data, baseIndex + rgbOffset, msg.is_bigendian, rgbFieldType);
			}

			if (rgbaOffset >= 0)
			{
				return DecodePackedColor(msg.data, baseIndex + rgbaOffset, msg.is_bigendian, rgbaFieldType);
			}

			if (intensityOffset >= 0)
			{
				float intensity = ReadSingle(msg.data, baseIndex + intensityOffset, msg.is_bigendian);
				intensity = Mathf.Clamp01(intensity);
				return new Color(intensity, intensity, intensity, 1f);
			}

			return defaultPointColor;
		}

		private Color DecodePackedColor(byte[] data, int index, bool bigEndian, string fieldType)
		{
			if (index < 0 || index + 3 >= data.Length)
			{
				return defaultPointColor;
			}

			uint raw = ReadUInt32(data, index, bigEndian);

			byte r = (byte)((raw >> 16) & 0xFF);
			byte g = (byte)((raw >> 8) & 0xFF);
			byte b = (byte)(raw & 0xFF);
			byte a = (byte)((raw >> 24) & 0xFF);

			if (a == 0)
			{
				a = 255;
			}

			if (!string.IsNullOrEmpty(fieldType) && fieldType.Contains("FLOAT"))
			{
				return new Color(r / 255f, g / 255f, b / 255f, 1f);
			}

			return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
		}

		private float ReadSingle(byte[] data, int index, bool bigEndian)
		{
			if (index < 0 || index + 3 >= data.Length)
			{
				return float.NaN;
			}

			if (!bigEndian)
			{
				return BitConverter.ToSingle(data, index);
			}

			byte[] reversed = new byte[4];
			reversed[0] = data[index + 3];
			reversed[1] = data[index + 2];
			reversed[2] = data[index + 1];
			reversed[3] = data[index];
			return BitConverter.ToSingle(reversed, 0);
		}

		private uint ReadUInt32(byte[] data, int index, bool bigEndian)
		{
			if (index < 0 || index + 3 >= data.Length)
			{
				return 0;
			}

			if (!bigEndian)
			{
				return BitConverter.ToUInt32(data, index);
			}

			return ((uint)data[index] << 24) |
				((uint)data[index + 1] << 16) |
				((uint)data[index + 2] << 8) |
				data[index + 3];
		}

		private void ApplyRoomScale()
		{
			Transform scaleTarget = miniRoomRoot;

			if (scaleTarget == null)
			{
				scaleTarget = particleSystemInstance != null ? particleSystemInstance.transform : transform;
			}

			float targetScale = Mathf.Max(0.0001f, roomScale);
			if (!roomScaleIsWorldScale || scaleTarget.parent == null)
			{
				scaleTarget.localScale = Vector3.one * targetScale;
				LogRoomScale(scaleTarget);
				return;
			}

			Vector3 parentScale = scaleTarget.parent.lossyScale;
			scaleTarget.localScale = new Vector3(
				SafeDivideScale(targetScale, parentScale.x),
				SafeDivideScale(targetScale, parentScale.y),
				SafeDivideScale(targetScale, parentScale.z)
			);
			LogRoomScale(scaleTarget);
		}

		private float SafeDivideScale(float targetScale, float parentAxisScale)
		{
			if (Mathf.Abs(parentAxisScale) < 0.0001f)
			{
				return targetScale;
			}

			return targetScale / parentAxisScale;
		}

		private void LogRoomScale(Transform scaleTarget)
		{
			if (!enableDebugLog || !Application.isPlaying || Time.unscaledTime - lastScaleDebugLogTime < 3f)
			{
				return;
			}

			lastScaleDebugLogTime = Time.unscaledTime;
			Debug.Log($"[ROS2 PointCloud] roomScale={roomScale}, localScale={scaleTarget.localScale}, worldScale={scaleTarget.lossyScale}, scaleTarget={scaleTarget.name}");
		}

		private void OnDisable()
		{
			ClearCloud();
		}

		private void OnDestroy()
		{
			if (runtimeParticleMaterial != null)
			{
				Destroy(runtimeParticleMaterial);
				runtimeParticleMaterial = null;
			}
		}
	}	
}
