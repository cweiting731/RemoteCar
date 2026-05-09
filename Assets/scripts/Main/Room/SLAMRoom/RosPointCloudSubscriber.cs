using System;
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

		[Header("Coordinate Conversion")]
		public PointCloudFrameMode frameMode = PointCloudFrameMode.RosCameraOpticalToUnity;
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
		private bool hasPendingMessage;

		private ParticleSystem.Particle[] particleBuffer;
		private ParticleSystem particleSystemInstance;
		private Material runtimeParticleMaterial;
		private ROSConnection ros;
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

		private void Update()
		{
			try
			{
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
					lastRenderTime = currentTime;
					RenderPointCloud(message);
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

		private void RenderPointCloud(PointCloud2Msg msg)
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
