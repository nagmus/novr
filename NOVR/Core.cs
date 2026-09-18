using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NOVR.VrCamera;
using NOVR.VrTogglers;
using NOVR.VrUi;
using NOVR.VrUi.Native;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace NOVR;

public class Core : MonoBehaviour
{
    private static bool _isApplicationQuitting;
    
    private float _originalFixedDeltaTime;

    private NOVRHeadsetData? _headsetData;
    private NOUIManager? _vrUi;
    private PropertyInfo? _refreshRateProperty;
    private VrTogglerManager? _vrTogglerManager;
    
    private Aircraft _aircraft;
    private Aircraft _oldAircraft;

    public static string CurrentAircraftId { get; private set; }

    public static void Create()
    {
        new GameObject("NOVR").AddComponent<Core>();
    }

    private void Awake()
    {
        Application.quitting -= HandleApplicationQuitting;
        Application.quitting += HandleApplicationQuitting;
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<VrCameraManager>();
        gameObject.AddComponent<APIBus>();
if (NOVRPlugin.LogSource != null)
            NOVRPlugin.LogSource.LogMessage($"[Core Awake] New Core instance created. name={name}");
        EnsureNativeMenuEnvironmentAssetCache();
    }

    private static void HandleApplicationQuitting()
    {
        _isApplicationQuitting = true;
    }

    private void OnApplicationQuit()
    {
        _isApplicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (NOVRPlugin.LogSource != null)
            NOVRPlugin.LogSource.LogMessage("[Core OnDestroy] NOVR has been destroyed. This shouldn't have happened. Recreating...");
        if (_isApplicationQuitting) return;

        Debug.Log("NOVR has been destroyed. This shouldn't have happened. Recreating...");

        Create();
    }

    private void Start()
    {
        EnsureNativeMenuEnvironmentAssetCache();
        
        var xrDeviceType = Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.XRModule") ??
                           Type.GetType("UnityEngine.XR.XRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine.VRModule") ??
                           Type.GetType("UnityEngine.VR.VRDevice, UnityEngine");

        _refreshRateProperty = xrDeviceType?.GetProperty("refreshRate");
        
        _headsetData = NOVRBehaviour.Create<NOVRHeadsetData>(transform);
        _vrUi = NOVRBehaviour.Create<NOUIManager>(transform);
        NOVRBehaviour.Create<CameraMoveController>(transform);

        if (ModConfiguration.Instance.LogXrStartupDiagnostics.Value &&
            gameObject.GetComponent<XrStartupDiagnosticsBehaviour>() == null)
        {
            gameObject.AddComponent<XrStartupDiagnosticsBehaviour>();
        }
        
        _vrTogglerManager = new VrTogglerManager();
        
    }



    private void Update()
    {
        EnsureNativeMenuEnvironmentAssetCache();
        UpdatePhysicsRate();
        ReloadConfigIfChangedOnDisk();
    }

    private System.DateTime _configLastWriteTime;
    private float _nextConfigCheckTime;

    // Picks up edits made to the config file while the game is running (e.g. by tools/Setup-NOVRCameraBindings).
    private void ReloadConfigIfChangedOnDisk()
    {
        if (Time.unscaledTime < _nextConfigCheckTime) return;
        _nextConfigCheckTime = Time.unscaledTime + 0.5f;

        var config = ModConfiguration.Instance.Config;
        try
        {
            var lastWriteTime = System.IO.File.GetLastWriteTimeUtc(config.ConfigFilePath);
            if (_configLastWriteTime == default)
            {
                _configLastWriteTime = lastWriteTime;
                return;
            }

            if (lastWriteTime == _configLastWriteTime) return;
            config.Reload();
            _configLastWriteTime = System.IO.File.GetLastWriteTimeUtc(config.ConfigFilePath);
            Debug.Log("[NOVR] Config file changed on disk; reloaded.");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NOVR] Failed to reload config: {exception.Message}");
        }
    }

    private void EnsureNativeMenuEnvironmentAssetCache()
    {
        if (NativeMenuEnvironmentAssetCache.Instance != null ||
            gameObject.GetComponent<NativeMenuEnvironmentAssetCache>() != null)
        {
            return;
        }

        gameObject.AddComponent<NativeMenuEnvironmentAssetCache>();
    }

    private void UpdatePhysicsRate()
    {
        if (_originalFixedDeltaTime == 0)
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
        }

        if (_refreshRateProperty == null) return;

        var headsetRefreshRate = (float)_refreshRateProperty.GetValue(null, null);
        if (headsetRefreshRate <= 0) return;


        Time.fixedDeltaTime = _originalFixedDeltaTime;
        
    }
    private void FixedUpdate()
    {
        _oldAircraft = _aircraft;
        GameManager.GetLocalAircraft(out _aircraft);
        if (_aircraft != _oldAircraft)
        {
            CurrentAircraftId = ResolveAircraftId(_aircraft);
            if (ModConfiguration.Instance.TryGetSavedOffset(CurrentAircraftId, out var f, out var r, out var u))
            {
                ModConfiguration.Instance.CockpitHeadForwardOffset.Value = f;
                ModConfiguration.Instance.CockpitHeadRightOffset.Value = r;
                ModConfiguration.Instance.CockpitHeadUpOffset.Value = u;
            }
            NOVRHeadsetData.CalibrateTranslation();
        }
        CameraStateManager.enableMouseLook = false;
    }

    private static string ResolveAircraftId(Aircraft aircraft)
    {
        if (aircraft == null || aircraft.definition == null) return null;
        return Regex.Replace(aircraft.definition.name, "[^a-zA-Z0-9_]", "_");
    }

}
