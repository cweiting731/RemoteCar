using System;
using UnityEngine;
using Main.Room.SLAMRoom;

namespace Main.Room
{
    public class RoomAligned : MonoBehaviour
    {
        [Header("Target Roots")]
        [Tooltip("Root object of the MiniRoom hierarchy that contains the player marker.")]
        public Transform miniRoomRoot;

        [Tooltip("Root object of the SLAMRoom that should be moved to align with the marker. Defaults to this object's parent.")]
        public Transform slamRoomRoot;

        [Header("Marker Search")]
        [Tooltip("Find marker by name keyword. Example: 'playerMarker' also matches 'playerMarker(Clone)'.")]
        public string playerMarkerName = "PlayerMarker";
        public bool includeInactiveObjects = true;

        [Header("Alignment Offset")]
        [Tooltip("World-space offset applied to the marker position before aligning the SLAM room root. Negative Y places the target below PlayerMarker.")]
        public Vector3 positionOffset = new Vector3(0f, -0.1f, 0f);

        [Tooltip("Additional yaw offset in degrees, applied around world up.")]
        public float yawOffsetDegrees = 0f;

        [Header("Auto Align")]
        public bool alignOnStart = true;

        [Header("Camera Pose Align")]
        [Tooltip("Pose source used to get the latest SLAM camera point.")]
        public RosSLAMCameraPose slamCameraPose;
        [Tooltip("Translate MiniRoom root together with SLAMRoom root when aligning camera pose point.")]
        public bool moveMiniRoomWithSlamRoom = false;

        [Header("Debug")]
        public bool enableDebugLog = true;

        private Transform cachedPlayerMarker;

        private void Awake()
        {
            ResolveRoots();
        }

        private void Start()
        {
            if (alignOnStart)
            {
                AlignLatestCameraPosePointToPlayerMarker();
            }
        }

        public bool AlignLatestCameraPosePointToPlayerMarker()
        {
            ResolveRoots();

            Transform marker = FindPlayerMarker();
            if (marker == null)
            {
                Debug.LogWarning($"[RoomAligned] PlayerMarker '{playerMarkerName}' was not found under MiniRoom.");
                return false;
            }

            if (!TryGetLatestCameraPoseWorld(out Vector3 cameraPoseWorldPosition, out Quaternion cameraPoseWorldRotation))
            {
                Debug.LogWarning("[RoomAligned] Latest camera pose is unavailable.");
                return false;
            }

            // Position alignment
            Vector3 targetPosition = marker.position + positionOffset;
            Vector3 delta = targetPosition - cameraPoseWorldPosition;

            if (slamRoomRoot != null)
            {
                slamRoomRoot.position += delta;
            }

            if (moveMiniRoomWithSlamRoom && miniRoomRoot != null)
            {
                miniRoomRoot.position += delta;
            }

            // Rotation alignment (horizontal/yaw only)
            Quaternion cameraHorizontalRotation = GetHorizontalRotationFromQuaternion(cameraPoseWorldRotation);
            Quaternion markerHorizontalRotation = GetHorizontalRotation(marker);
            Quaternion rotationDelta = Quaternion.Inverse(cameraHorizontalRotation) * markerHorizontalRotation;
            rotationDelta *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);

            if (slamRoomRoot != null)
            {
                slamRoomRoot.rotation = rotationDelta * slamRoomRoot.rotation;
            }

            if (moveMiniRoomWithSlamRoom && miniRoomRoot != null)
            {
                miniRoomRoot.rotation = rotationDelta * miniRoomRoot.rotation;
            }

            if (enableDebugLog)
            {
                string slamName = slamRoomRoot != null ? slamRoomRoot.name : "<null>";
                string miniName = miniRoomRoot != null ? miniRoomRoot.name : "<null>";
                Vector3 euler = slamRoomRoot != null ? slamRoomRoot.eulerAngles : Vector3.zero;
                Debug.Log($"[RoomAligned] Aligned camera pose point to marker. marker='{marker.name}', delta={delta}, yaw={euler.y:0.##}, slamRoom='{slamName}', miniRoom='{miniName}'");
            }

            return true;
        }

        public bool AlignToPlayerMarker()
        {
            ResolveRoots();

            Transform marker = FindPlayerMarker();
            if (marker == null)
            {
                Debug.LogWarning($"[RoomAligned] PlayerMarker '{playerMarkerName}' was not found under MiniRoom.");
                return false;
            }

            Vector3 targetPosition = marker.position + positionOffset;
            Quaternion targetRotation = GetHorizontalRotation(marker);
            targetRotation *= Quaternion.Euler(0f, yawOffsetDegrees, 0f);

            Transform roomToMove = slamRoomRoot != null ? slamRoomRoot : transform;
            roomToMove.SetPositionAndRotation(targetPosition, targetRotation);

            if (enableDebugLog)
            {
                Vector3 euler = roomToMove.eulerAngles;
                Debug.Log($"[RoomAligned] Align success. room='{roomToMove.name}', marker='{marker.name}', pos={roomToMove.position}, yaw={euler.y:0.##}");
            }

            return true;
        }

        // Unity UI Button OnClick only supports public void methods with no parameters.
        public void AlignToPlayerMarkerFromButton()
        {
            if (enableDebugLog)
            {
                Debug.Log("[RoomAligned] Align button clicked: align latest camera pose point to player marker.");
            }

            AlignLatestCameraPosePointToPlayerMarker();
        }

        public bool RefreshPlayerMarker()
        {
            cachedPlayerMarker = null;
            bool found = FindPlayerMarker() != null;
            if (enableDebugLog)
            {
                Debug.Log($"[RoomAligned] RefreshPlayerMarker result={found}");
            }

            return found;
        }

        public Transform FindPlayerMarker()
        {
            if (cachedPlayerMarker != null)
            {
                return cachedPlayerMarker;
            }

            if (miniRoomRoot == null)
            {
                return null;
            }

            foreach (Transform child in miniRoomRoot.GetComponentsInChildren<Transform>(includeInactiveObjects))
            {
                if (!string.IsNullOrEmpty(playerMarkerName)
                    && child.name.IndexOf(playerMarkerName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cachedPlayerMarker = child;
                    if (enableDebugLog)
                    {
                        Debug.Log($"[RoomAligned] Found marker '{cachedPlayerMarker.name}' at {cachedPlayerMarker.position}");
                    }

                    return cachedPlayerMarker;
                }
            }

            if (enableDebugLog)
            {
                Debug.LogWarning($"[RoomAligned] Could not find marker '{playerMarkerName}' under '{miniRoomRoot.name}'.");
            }

            return null;
        }

        private void ResolveRoots()
        {
            if (slamRoomRoot == null)
            {
                slamRoomRoot = transform.parent != null ? transform.parent : transform;

                if (enableDebugLog)
                {
                    Debug.Log($"[RoomAligned] slamRoomRoot auto-assigned to '{slamRoomRoot.name}'.");
                }
            }

            if (miniRoomRoot == null && enableDebugLog)
            {
                Debug.LogWarning("[RoomAligned] miniRoomRoot is not assigned.");
            }

            if (slamCameraPose == null)
            {
                slamCameraPose = FindObjectOfType<RosSLAMCameraPose>(true);
                if (slamCameraPose != null && enableDebugLog)
                {
                    Debug.Log($"[RoomAligned] slamCameraPose auto-assigned to '{slamCameraPose.name}'.");
                }
            }
        }

        private Quaternion GetHorizontalRotation(Transform source)
        {
            Vector3 forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(source.right, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = slamRoomRoot != null ? slamRoomRoot.forward : Vector3.forward;
                forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private bool TryGetLatestCameraPoseWorld(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (slamCameraPose == null || !slamCameraPose.HasPose)
            {
                return false;
            }

            worldPosition = slamCameraPose.LatestMapPosition;
            worldRotation = slamCameraPose.LatestMapRotation;

            if (slamCameraPose.globalMapRoot != null)
            {
                worldPosition = slamCameraPose.globalMapRoot.TransformPoint(worldPosition);
                worldRotation = slamCameraPose.globalMapRoot.rotation * worldRotation;
            }

            return true;
        }

        private Quaternion GetHorizontalRotationFromQuaternion(Quaternion sourceRotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(sourceRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(sourceRotation * Vector3.right, Vector3.up);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}