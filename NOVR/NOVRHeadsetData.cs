using System;
using System.Collections.Generic;
using NOVR.UnityTypesHelper;
using UnityEngine;
using UnityEngine.XR;

namespace NOVR;

[DefaultExecutionOrder(-100)]
public class NOVRHeadsetData : NOVRBehaviour
{
    public static Vector3 TranslationAnchor { get; private set; }
    public static Vector3 Translation { get; private set; }
    public static Vector3 TranslationCalibrationOffset { get; private set; }
    public static Vector3 TranslationError => Quaternion.Inverse(RotationCalibrationOffset) * (Translation - TranslationAnchor - CockpitOffset) - TranslationCalibrationOffset;

    // Configured cockpit head offset, in the game camera's local space (x = right, y = up, z = forward).
    // Applied live and kept out of the calibration, so it always points along the cockpit's axes.
    public static Vector3 CockpitOffset => new(
        ModConfiguration.Instance.CockpitHeadRightOffset.Value,
        0f,
        ModConfiguration.Instance.CockpitHeadForwardOffset.Value);

    public static Quaternion Rotation { get; private set; }
    public static Quaternion RotationCalibrationOffset { get; private set; } = Quaternion.identity;
    public static Quaternion RotationError => Quaternion.Inverse(RotationCalibrationOffset) * Rotation;

    // Raised after the view has been recentered, so smoothed or world-anchored UI can snap to the new pose.
    public static event Action? Recentered;

    // Tracking jumps bigger than this in a single frame can't be real head motion, so they are treated as an
    // external recenter (e.g. SteamVR's "Reset seated position").
    private const float ExternalRecenterAngleThreshold = 45f;
    private const float ExternalRecenterDistanceThreshold = 0.5f;

    private static bool _hasPreviousHeadPose;
    private static Quaternion _previousHeadRotation;
    private static Vector3 _previousHeadPosition;
    private static int _externalRecenterFrame = -1;
    private static readonly List<XRInputSubsystem> InputSubsystems = new();
    private static readonly HashSet<XRInputSubsystem> SubscribedInputSubsystems = new();
    private float _nextSubsystemScanTime;

    // Makes the direction the player is currently facing the new forward, and the current head position the new zero.
    public static void Recenter()
    {
        CalibrateTranslation();

        // Yaw from the horizontal forward direction, which stays correct when looking steeply up or down.
        var headRotation = GetHeadRotation();
        var forward = headRotation * Vector3.forward;
        var yaw = new Vector2(forward.x, forward.z).sqrMagnitude > 0.0001f
            ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg
            : headRotation.eulerAngles.y;
        RotationCalibrationOffset = Quaternion.Euler(0f, -yaw, 0f);

        UpdateTransform();
        Debug.Log("[NOVR] VR view recentered.");
        Recentered?.Invoke();
    }

    public static void SetAnchor(Vector3 anchor)
    {
        TranslationAnchor = anchor;
    }

    public static void CalibrateTranslation(CalibrationAxes calibrationAxes = CalibrationAxes.All, bool overrideExistingInNonCalibratedAxes = false)
    {
        Vector3 currentError = -GetHeadPosition();
        bool ov = overrideExistingInNonCalibratedAxes;
        TranslationCalibrationOffset = new Vector3(
            (calibrationAxes & CalibrationAxes.X) != 0 ? currentError.x : ov ? TranslationCalibrationOffset.x : 0,
            (calibrationAxes & CalibrationAxes.Y) != 0 ? currentError.y : ov ? TranslationCalibrationOffset.y : 0,
            (calibrationAxes & CalibrationAxes.Z) != 0 ? currentError.z : ov ? TranslationCalibrationOffset.z : 0
        );
    }

    public static void CalibrateRotation(CalibrationAxes calibrationAxes = CalibrationAxes.Yaw, bool overrideExistingInNonCalibratedAxes = false)
    {
        var currentRotation = GetHeadRotation();
        var currentEuler = currentRotation.eulerAngles;
        Vector3 currentError = new(
            -Mathf.DeltaAngle(0f, currentEuler.x),
            -Mathf.DeltaAngle(0f, currentEuler.y),
            -Mathf.DeltaAngle(0f, currentEuler.z)
        );

        bool ov = overrideExistingInNonCalibratedAxes;
        RotationCalibrationOffset = Quaternion.Euler(
            (calibrationAxes & CalibrationAxes.X) != 0 ? currentError.x : ov ? RotationCalibrationOffset.eulerAngles.x : 0,
            (calibrationAxes & CalibrationAxes.Y) != 0 ? currentError.y : ov ? RotationCalibrationOffset.eulerAngles.y : 0,
            (calibrationAxes & CalibrationAxes.Z) != 0 ? currentError.z : ov ? RotationCalibrationOffset.eulerAngles.z : 0
        );
    }

    protected override void Awake()
    {
        base.Awake();
        DisableCameraAutoTracking();
    }

    private void DisableCameraAutoTracking()
    {
        var camera = GetComponent<Camera>();
        if (!camera) return;

        var cameraTrackingDisablingMethod = UuvrXrDevice.XrDeviceType?.GetMethod("DisableAutoXRCameraTracking");

        if (cameraTrackingDisablingMethod != null)
        {
            cameraTrackingDisablingMethod.Invoke(null, new object[] { camera, true });
        }
        else
        {
            // TODO: use alternative method for disabling tracking.
            Debug.LogWarning("Failed to find DisableAutoXRCameraTracking method. Using SetStereoViewMatrix, which also prevents Unity from auto-tracking cameras, but can cause other issues.");
            // TODO: this crashes some games? Example Monster Girl Island. Although that game already comes with VR stuff, dunno if could affect.
            // camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, camera.worldToCameraMatrix);
            // camera.SetStereoViewMatrix(Camera.StereoscopicEye.Right, camera.worldToCameraMatrix);
        }
    }

    protected override void OnBeforeRender()
    {
        base.OnBeforeRender();
        UpdateTransform();
    }

    private void Update()
    {
        SubscribeToTrackingOriginChanges();
        DetectExternalRecenter();
        UpdateTransform();
    }

    private void LateUpdate()
    {
        UpdateTransform();
    }

    private static void UpdateTransform()
    {
        // Head movement is rotated along with the yaw calibration, so leaning forward still moves you forward
        // after recentering while turned.
        Translation = TranslationAnchor + CockpitOffset + RotationCalibrationOffset * (GetHeadPosition() + TranslationCalibrationOffset);
        Rotation = RotationCalibrationOffset * GetHeadRotation();
    }

    private void SubscribeToTrackingOriginChanges()
    {
        if (Time.unscaledTime < _nextSubsystemScanTime) return;
        _nextSubsystemScanTime = Time.unscaledTime + 1f;

        SubsystemManager.GetSubsystems(InputSubsystems);
        foreach (var subsystem in InputSubsystems)
        {
            if (SubscribedInputSubsystems.Add(subsystem))
            {
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
            }
        }
    }

    private static void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
    {
        // Wait a frame so the new tracking origin is reflected in the head pose before recentering on it.
        _externalRecenterFrame = Time.frameCount + 1;
    }

    // When the runtime recenters (e.g. SteamVR "Reset seated position"), NOVR's own calibration would be applied
    // on top of the new origin and leave the view off by the old correction, so recenter NOVR along with it.
    private static void DetectExternalRecenter()
    {
        var headRotation = GetHeadRotation();
        var headPosition = GetHeadPosition();

        if (_hasPreviousHeadPose &&
            (Quaternion.Angle(headRotation, _previousHeadRotation) > ExternalRecenterAngleThreshold ||
             Vector3.Distance(headPosition, _previousHeadPosition) > ExternalRecenterDistanceThreshold))
        {
            _externalRecenterFrame = Time.frameCount;
        }

        _hasPreviousHeadPose = headRotation != default;
        _previousHeadRotation = headRotation;
        _previousHeadPosition = headPosition;

        if (_externalRecenterFrame >= 0 && Time.frameCount >= _externalRecenterFrame)
        {
            _externalRecenterFrame = -1;
            Debug.Log("[NOVR] Tracking origin changed (e.g. SteamVR recenter); recentering NOVR view.");
            Recenter();
        }
    }

    private static Vector3 GetHeadPosition()
    {
        if (TryGetHeadDevice(out var device) &&
            device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
        {
            return position;
        }

#pragma warning disable CS0618
        return InputTracking.GetLocalPosition(XRNode.Head);
#pragma warning restore CS0618
    }

    private static Quaternion GetHeadRotation()
    {
        if (TryGetHeadDevice(out var device) &&
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
        {
            return rotation;
        }

#pragma warning disable CS0618
        return InputTracking.GetLocalRotation(XRNode.Head);
#pragma warning restore CS0618
    }

    private static bool TryGetHeadDevice(out InputDevice device)
    {
        device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (device.isValid)
        {
            return true;
        }

        device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        return device.isValid;
    }
}
