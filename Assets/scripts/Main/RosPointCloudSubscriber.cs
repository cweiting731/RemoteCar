using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using UnityEngine;
using System;

public class RosPointCloudSubscriber : MonoBehaviour
{
	// 支援依不同 ROS 座標系做最小轉換，方便直接對接常見點雲來源。
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
	public float pointScale = 1f;
	public int maxPoints = 200000;

	[Header("Coordinate Conversion")]
	public PointCloudFrameMode frameMode = PointCloudFrameMode.RosCameraOpticalToUnity;
	public Vector3 manualPositionOffset = Vector3.zero;

	[Header("Debug")]
	public bool enableDebugLog = true;
	public int logEveryNFrames = 30;

	[Header("Update Rate")]
	public float renderIntervalSeconds = 2f;

	private readonly object messageLock = new object();
	// ROS callback 在背景執行時先把最新點雲存起來，避免直接碰 Unity API。
	private PointCloud2Msg pendingMessage;
	private bool hasPendingMessage;

	private ParticleSystem.Particle[] particleBuffer;
	private ParticleSystem particleSystemInstance;
	private ROSConnection ros;
	private int receivedCloudCount;
	private int lastRenderedPointCount;
	private float lastRenderTime = -9999f;

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
		// 先準備粒子系統，後面收到點雲時就能直接更新。
		SetupParticleSystem();
	}

	private void Start()
	{
		ros = ROSConnection.GetOrCreateInstance();
		ros.Subscribe<PointCloud2Msg>(topicName, ReceivePointCloud);

		if (enableDebugLog)
		{
			Debug.Log($"[ROS2 PointCloud] Subscribed to topic: {topicName}");
		}
	}

	private void Update()
	{
		PointCloud2Msg message = null;
		float currentTime = Time.unscaledTime;

		if (currentTime - lastRenderTime < Mathf.Max(0.01f, renderIntervalSeconds))
		{
			return;
		}

		// 主執行緒每幀取出最新一筆點雲再進行渲染。
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

	private void SetupParticleSystem()
	{
		// 如果外部沒有指定粒子系統，就沿用元件上的或動態建立一個。
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
		// 以世界座標顯示點雲，避免跟著粒子系統本地座標一起變形。
		main.loop = false;
		main.playOnAwake = false;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
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

		if (particleMaterial != null)
		{
			renderer.material = particleMaterial;
		}

		particleSystemInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
	}

	private void ReceivePointCloud(PointCloud2Msg msg)
	{
		lock (messageLock)
		{
			pendingMessage = msg;
			hasPendingMessage = true;
		}
	}

	private void RenderPointCloud(PointCloud2Msg msg)
	{
		receivedCloudCount++;

		if (msg.data == null || msg.data.Length == 0 || msg.point_step == 0)
		{
			ClearCloud();
			return;
		}

		CacheFieldOffsets(msg);

		int pointStep = Mathf.Max(1, (int)msg.point_step);
		int rowStep = Mathf.Max(pointStep, (int)msg.row_step);
		int width = Mathf.Max(1, (int)msg.width);
		int height = Mathf.Max(1, (int)msg.height);
		// 依寬高與步長估算可讀取點數，並限制在 maxPoints 內。
		int pointCount = Mathf.Min((int)((long)width * height), maxPoints);
		int availablePoints = Mathf.Min(pointCount, msg.data.Length / pointStep);

		EnsureParticleCapacity(availablePoints);

		int renderedCount = 0;
		for (int i = 0; i < availablePoints; i++)
		{
			// PointCloud2 可能以 row_step 排列，這裡先把索引換成實際 byte offset。
			int row = i / width;
			int col = i % width;
			int baseIndex = row * rowStep + col * pointStep;

			if (baseIndex + Mathf.Max(zOffset, Mathf.Max(yOffset, xOffset)) >= msg.data.Length)
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
		// 每筆訊息都重新掃一次欄位，避免不同來源欄位順序不一致時沿用舊值。
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
			string name = field.name ?? string.Empty;

			if (name == "x")
			{
				xOffset = (int)field.offset;
			}
			else if (name == "y")
			{
				yOffset = (int)field.offset;
			}
			else if (name == "z")
			{
				zOffset = (int)field.offset;
			}
			else if (name == "rgb")
			{
				rgbOffset = (int)field.offset;
				rgbFieldType = field.datatype.ToString();
			}
			else if (name == "rgba")
			{
				rgbaOffset = (int)field.offset;
				rgbaFieldType = field.datatype.ToString();
			}
			else if (name == "intensity")
			{
				intensityOffset = (int)field.offset;
				intensityFieldType = field.datatype.ToString();
			}
		}
	}

	private Vector3 ReadPointPosition(PointCloud2Msg msg, int baseIndex)
	{
		// 依欄位 offset 讀出 x/y/z，再根據來源座標系轉到 Unity。
		float x = ReadSingle(msg.data, baseIndex + xOffset, msg.is_bigendian);
		float y = ReadSingle(msg.data, baseIndex + yOffset, msg.is_bigendian);
		float z = ReadSingle(msg.data, baseIndex + zOffset, msg.is_bigendian);

		switch (frameMode)
		{
			case PointCloudFrameMode.RosBaseLinkToUnity:
				return new Vector3(-y, z, x) * pointScale;
			case PointCloudFrameMode.RosCameraOpticalToUnity:
				return new Vector3(x, -y, z) * pointScale;
			default:
				return new Vector3(x, y, z) * pointScale;
		}
	}

	private Color ReadPointColor(PointCloud2Msg msg, int baseIndex)
	{
		// 優先用 rgb/rgba；沒有顏色時退回 intensity，再沒有就用預設色。
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
		// 常見的 rgb/rgba 在 PointCloud2 裡通常會被打包成 32-bit 值。
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
		// PointCloud2 可能是 little-endian 或 big-endian，這裡統一轉成 float。
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

	private void OnDisable()
	{
		ClearCloud();
	}
}
