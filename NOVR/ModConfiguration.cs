using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace NOVR;

public class ModConfiguration
{
    public static ModConfiguration Instance;
    

    public readonly ConfigFile Config;
    public readonly ConfigEntry<float> TargetDesignatorOvershoot;
    public readonly ConfigEntry<bool> EnableNativeMenuUi;
    public readonly ConfigEntry<float> NativeMenuScale;
    public readonly ConfigEntry<float> NativeMenuDistance;
    public readonly ConfigEntry<float> NativeMenuHeightOffset;
    public readonly ConfigEntry<float> ZoomSpeed;
    public readonly ConfigEntry<float> MaximumZoom;
    public readonly ConfigEntry<bool> InstantZoomOut;
    public readonly ConfigEntry<string> CursorInputMode;
    public readonly ConfigEntry<bool> EnableNativeMenuEnvironment;
    public readonly ConfigEntry<bool> EnableExperimentalSteamVrControllerProfiles;
    public readonly ConfigEntry<bool> LogXrStartupDiagnostics;
    public readonly ConfigEntry<bool> VerboseDiagnostics;
    public readonly ConfigEntry<float> CockpitHeadForwardOffset;
    public readonly ConfigEntry<float> CockpitHeadRightOffset;
    public readonly ConfigEntry<float> CockpitHeadUpOffset;
    public readonly ConfigEntry<KeyCode> RecenterShortcut;
    public readonly ConfigEntry<float> RecenterShortcutDelay;
    public readonly ConfigEntry<bool> ShowRecenterInPauseMenu;
    public readonly ConfigEntry<bool> SavePositionTrigger;
    public readonly ConfigEntry<float> MapClickMaxRadius;
    public readonly ConfigEntry<float> HudMinimapOpacity;

    private readonly Dictionary<string, (ConfigEntry<float> Forward, ConfigEntry<float> Right, ConfigEntry<float> Up)> _perPlaneEntries = new();

    public readonly ConfigEntry<float> CameraMoveSpeed;
    public readonly ConfigEntry<float> CameraMoveMaxOffset;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyForward;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyBack;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyLeft;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyRight;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyUp;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyDown;
    public readonly ConfigEntry<KeyCode> CameraMoveKeyReset;
    public readonly ConfigEntry<string> CameraMoveAxisLeftRight;
    public readonly ConfigEntry<string> CameraMoveAxisForwardBack;
    public readonly ConfigEntry<string> CameraMoveAxisUpDown;
    public readonly ConfigEntry<bool> CameraMoveInvertLeftRight;
    public readonly ConfigEntry<bool> CameraMoveInvertForwardBack;
    public readonly ConfigEntry<bool> CameraMoveInvertUpDown;
    public readonly ConfigEntry<float> CameraMoveDeadzone;
    public readonly ConfigEntry<string> CameraMoveButtonForward;
    public readonly ConfigEntry<string> CameraMoveButtonBack;
    public readonly ConfigEntry<string> CameraMoveButtonLeft;
    public readonly ConfigEntry<string> CameraMoveButtonRight;
    public readonly ConfigEntry<string> CameraMoveButtonUp;
    public readonly ConfigEntry<string> CameraMoveButtonDown;
    public readonly ConfigEntry<string> CameraMoveButtonVerticalModifier;
    public readonly ConfigEntry<string> CameraMoveButtonReset;
    public readonly ConfigEntry<string> RecenterButton;
    public readonly ConfigEntry<bool> LogControllerInput;

    public ModConfiguration(ConfigFile config)
    {
        Instance = this;

        Config = config;
        TargetDesignatorOvershoot = config.Bind(
            "General",
            "Target Designator Overshoot",
            1.2f,
            "How much the target designator should multiply rotation to make for easier high off boresight target designation. Set to 1.0 to disable");

        EnableNativeMenuUi = config.Bind(
            "Experimental",
            "Enable Native Menu UI",
            true,
            "Use NOVR's native VR menu UI for non-flight menus. Disable to fall back to the existing patched game UI.");

        NativeMenuScale = config.Bind(
            "Experimental",
            "Native Menu Scale",
            1.25f,
            "Size multiplier for NOVR's native VR menu UI. Values from 0.75 to 2.0 are supported.");

        NativeMenuDistance = config.Bind(
            "Experimental",
            "Native Menu Distance",
            3.0f,
            "Distance in meters from the headset when NOVR's native VR menu UI is opened or recentered. Values from 1.5 to 6.0 are supported.");

        NativeMenuHeightOffset = config.Bind(
            "Experimental",
            "Native Menu Height Offset",
            0.0f,
            "Vertical offset in meters applied when NOVR's native VR menu UI is opened or recentered. Values from -0.25 to 1.0 are supported.");

        ZoomSpeed = config.Bind(
            "VR Zoom",
            "Zoom Speed",
            2.0f,
            new ConfigDescription(
                "How quickly headset magnification changes while Zoom View is held, in magnification units per second.",
                new AcceptableValueRange<float>(0.1f, 20.0f)));

        MaximumZoom = config.Bind(
            "VR Zoom",
            "Maximum Zoom",
            4.0f,
            new ConfigDescription(
                "Maximum binocular-style headset magnification.",
                new AcceptableValueRange<float>(1.0f, 10.0f)));

        InstantZoomOut = config.Bind(
            "VR Zoom",
            "Instant Zoom Out",
            false,
            "When enabled, any Zoom View out input immediately returns the headset view to 1x magnification.");
        CursorInputMode = config.Bind(
            "Experimental",
            "Cursor Input Mode",
            "Auto",
            "Selects input source for the VR UI cursor. 'Auto' = use controller if tracked, else mouse. 'Mouse' = always use mouse. 'Controller' = always use controller ray.");

        EnableNativeMenuEnvironment = config.Bind(
            "Experimental",
            "Enable Native Menu Environment",
            false,
            "Show an experimental 3D native menu environment using real game preview assets.");

        EnableExperimentalSteamVrControllerProfiles = config.Bind(
            "Experimental",
            "Enable Experimental SteamVR Controller Profiles",
            false,
            "Register a minimal set of OpenXR controller interaction profiles before VR startup. Enables Valve Index, HTC Vive, and Khronos Simple Controller profiles only; hand tracking is not enabled.");

        LogXrStartupDiagnostics = config.Bind(
            "Diagnostics",
            "Log XR Startup Diagnostics",
            false,
            "Log read-only XR loader, OpenXR runtime, subsystem, and input device state during VR startup.");

        VerboseDiagnostics = config.Bind(
            "Diagnostics",
            "Verbose Diagnostics",
            false,
            "When enabled, NOVR emits per-frame and per-second diagnostic logs " +
            "(controller laser dumps, controller input pose dumps, cursor mode dumps, " +
            "asset-cache waiting messages). Disabled by default for performance — " +
            "enable only when troubleshooting.");

        CockpitHeadForwardOffset = config.Bind(
            "Experimental",
            "Cockpit Head Forward Offset",
            0.08f,
            "Offset in meters applied to the cockpit head forward vector. Helps keep the ejection seat bars out of your face.");

        CockpitHeadRightOffset = config.Bind(
            "Experimental",
            "Cockpit Head Right Offset",
            0.0f,
            "Offset in meters applied to the cockpit head right vector. Moves you left (negative) or right (positive) to correct off-center seating.");

        CockpitHeadUpOffset = config.Bind(
            "Experimental",
            "Cockpit Head Up Offset",
            0.0f,
            "Offset in meters applied to the cockpit head up vector. Moves you down (negative) or up (positive).");

        RecenterShortcut = config.Bind(
            "Input",
            "Recenter Shortcut",
            KeyCode.F9,
            "Keyboard shortcut to recenter the VR view. For HOTAS users, map a joystick button to this key via external software.");

        RecenterShortcutDelay = config.Bind(
            "Input",
            "Recenter Shortcut Delay",
            3.0f,
            "Seconds between pressing the Recenter Shortcut and the recenter happening, giving you time to look forward. Set to 0 to recenter instantly.");

        ShowRecenterInPauseMenu = config.Bind(
            "Input",
            "Show Recenter In Pause Menu",
            true,
            "Add a 'RECENTER VIEW' button to the in-game pause menu while seated in a cockpit. Clicking it recenters the VR view immediately.");

        SavePositionTrigger = config.Bind(
            "Experimental",
            "Save Position For Current Aircraft",
            false,
            "Check this box to save the current Cockpit Head Forward/Right/Up Offset values for the aircraft you're currently in. Automatically unchecks itself.");

        const string cameraMoveSection = "Camera Move";
        const string controlPathHelp = " Unity Input System control path, e.g. \"/<device>/hat/up\"; enable Log Controller Input to see the paths of your controls. Empty disables.";

        CameraMoveSpeed = config.Bind(cameraMoveSection, "Move Speed", 0.2f,
            "How fast the Camera Move bindings move your head in the cockpit, in meters per second. Moving adjusts the Cockpit Head Offsets and saves them for the current aircraft.");
        CameraMoveMaxOffset = config.Bind(cameraMoveSection, "Max Offset", 0.5f,
            "Maximum cockpit head offset, in meters, along each axis that the Camera Move bindings can reach.");
        CameraMoveKeyForward = config.Bind(cameraMoveSection, "Key Forward", KeyCode.None, "Keyboard key that moves your head forward in the cockpit.");
        CameraMoveKeyBack = config.Bind(cameraMoveSection, "Key Back", KeyCode.None, "Keyboard key that moves your head back.");
        CameraMoveKeyLeft = config.Bind(cameraMoveSection, "Key Left", KeyCode.None, "Keyboard key that moves your head left.");
        CameraMoveKeyRight = config.Bind(cameraMoveSection, "Key Right", KeyCode.None, "Keyboard key that moves your head right.");
        CameraMoveKeyUp = config.Bind(cameraMoveSection, "Key Up", KeyCode.None, "Keyboard key that moves your head up.");
        CameraMoveKeyDown = config.Bind(cameraMoveSection, "Key Down", KeyCode.None, "Keyboard key that moves your head down.");
        CameraMoveKeyReset = config.Bind(cameraMoveSection, "Key Reset", KeyCode.None,
            "Keyboard key that resets the cockpit head offsets to their defaults.");
        CameraMoveAxisLeftRight = config.Bind(cameraMoveSection, "Axis Left Right", "", "Joystick axis that moves your head left/right." + controlPathHelp);
        CameraMoveAxisForwardBack = config.Bind(cameraMoveSection, "Axis Forward Back", "",
            "Joystick axis that moves your head forward/back, or up/down while the vertical modifier is held." + controlPathHelp);
        CameraMoveAxisUpDown = config.Bind(cameraMoveSection, "Axis Up Down", "", "Joystick axis that moves your head up/down." + controlPathHelp);
        CameraMoveInvertLeftRight = config.Bind(cameraMoveSection, "Invert Axis Left Right", false, "Invert the left/right axis.");
        CameraMoveInvertForwardBack = config.Bind(cameraMoveSection, "Invert Axis Forward Back", false, "Invert the forward/back axis.");
        CameraMoveInvertUpDown = config.Bind(cameraMoveSection, "Invert Axis Up Down", false, "Invert the up/down axis.");
        CameraMoveDeadzone = config.Bind(cameraMoveSection, "Axis Deadzone", 0.2f, "Axis deflection below this value is ignored.");
        CameraMoveButtonForward = config.Bind(cameraMoveSection, "Button Forward", "", "Joystick button or hat direction that moves your head forward." + controlPathHelp);
        CameraMoveButtonBack = config.Bind(cameraMoveSection, "Button Back", "", "Joystick button that moves your head back." + controlPathHelp);
        CameraMoveButtonLeft = config.Bind(cameraMoveSection, "Button Left", "", "Joystick button that moves your head left." + controlPathHelp);
        CameraMoveButtonRight = config.Bind(cameraMoveSection, "Button Right", "", "Joystick button that moves your head right." + controlPathHelp);
        CameraMoveButtonUp = config.Bind(cameraMoveSection, "Button Up", "", "Joystick button that moves your head up." + controlPathHelp);
        CameraMoveButtonDown = config.Bind(cameraMoveSection, "Button Down", "", "Joystick button that moves your head down." + controlPathHelp);
        CameraMoveButtonVerticalModifier = config.Bind(cameraMoveSection, "Button Vertical Modifier", "",
            "While this joystick button is held, forward/back input moves your head up/down instead." + controlPathHelp);
        CameraMoveButtonReset = config.Bind(cameraMoveSection, "Button Reset", "",
            "Joystick button that resets the cockpit head offsets to their defaults." + controlPathHelp);

        RecenterButton = config.Bind(
            "Input",
            "Recenter Button",
            "",
            "Joystick button that recenters the VR view, like the Recenter Shortcut." + controlPathHelp);

        LogControllerInput = config.Bind(
            "Input",
            "Log Controller Input",
            false,
            "Log pressed keys and moved joystick controls (with their control paths) to BepInEx/LogOutput.log, to find the values for the joystick bindings.");

        MapClickMaxRadius = config.Bind(
            "Map",
            "Click Max Radius",
            0.0375f,
            "Maximum normalized distance from the VR cursor to a map icon for the icon to be selectable on click. Distance is measured as a fraction of the map image's smaller dimension, so it stays consistent regardless of zoom level, HUD scale, or HMD resolution. Values from 0.0 to 0.5 are supported.");

        HudMinimapOpacity = config.Bind(
            "HUD",
            "Minimap Opacity",
            1.0f,
            "Opacity of the in-cockpit minimap (the small map in the HUD, not the full clickable map). " +
            "1.0 is fully opaque, 0.0 hides it completely. Applies only when the minimap is shown; the full map view is unaffected.");

        SavePositionTrigger.SettingChanged += (_, _) =>
        {
            if (!SavePositionTrigger.Value) return;
            SavePositionTrigger.Value = false;

            if (string.IsNullOrEmpty(Core.CurrentAircraftId))
            {
                Debug.LogWarning("[NOVR] Cannot save cockpit offset: no aircraft is currently active.");
                return;
            }

            SaveCurrentOffsetFor(Core.CurrentAircraftId, CockpitHeadForwardOffset.Value, CockpitHeadRightOffset.Value, CockpitHeadUpOffset.Value);
        };

        PreloadPerPlaneEntries();
    }

    private void PreloadPerPlaneEntries()
    {
        if (!File.Exists(Config.ConfigFilePath)) return;

        var inPerPlaneSection = false;
        foreach (var rawLine in File.ReadAllLines(Config.ConfigFilePath))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inPerPlaneSection = line.Equals("[PerPlaneOffsets]");
                continue;
            }

            if (!inPerPlaneSection) continue;

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0) continue;

            var key = line.Substring(0, equalsIndex).Trim();
            if (key.EndsWith("_Forward"))
            {
                GetOrCreatePlaneEntries(key.Substring(0, key.Length - "_Forward".Length));
            }
            else if (key.EndsWith("_Right"))
            {
                GetOrCreatePlaneEntries(key.Substring(0, key.Length - "_Right".Length));
            }
            else if (key.EndsWith("_Up"))
            {
                GetOrCreatePlaneEntries(key.Substring(0, key.Length - "_Up".Length));
            }
        }
    }

    private (ConfigEntry<float> Forward, ConfigEntry<float> Right, ConfigEntry<float> Up) GetOrCreatePlaneEntries(string aircraftId)
    {
        if (_perPlaneEntries.TryGetValue(aircraftId, out var entries)) return entries;

        var forward = Config.Bind(
            "PerPlaneOffsets",
            $"{aircraftId}_Forward",
            float.NaN,
            "Saved Cockpit Head Forward Offset for this aircraft type. NaN means no value has been saved yet.");

        var right = Config.Bind(
            "PerPlaneOffsets",
            $"{aircraftId}_Right",
            float.NaN,
            "Saved Cockpit Head Right Offset for this aircraft type. NaN means no value has been saved yet.");

        var up = Config.Bind(
            "PerPlaneOffsets",
            $"{aircraftId}_Up",
            float.NaN,
            "Saved Cockpit Head Up Offset for this aircraft type. NaN means no value has been saved yet (treated as 0).");

        entries = (forward, right, up);
        _perPlaneEntries[aircraftId] = entries;
        return entries;
    }

    public bool TryGetSavedOffset(string aircraftId, out float forward, out float right, out float up)
    {
        forward = 0f;
        right = 0f;
        up = 0f;
        if (string.IsNullOrEmpty(aircraftId)) return false;

        var entries = GetOrCreatePlaneEntries(aircraftId);
        if (float.IsNaN(entries.Forward.Value) || float.IsNaN(entries.Right.Value)) return false;

        forward = entries.Forward.Value;
        right = entries.Right.Value;
        up = float.IsNaN(entries.Up.Value) ? 0f : entries.Up.Value;
        return true;
    }

    public void SaveCurrentOffsetFor(string aircraftId, float forward, float right, float up)
    {
        if (string.IsNullOrEmpty(aircraftId)) return;

        var entries = GetOrCreatePlaneEntries(aircraftId);
        entries.Forward.Value = forward;
        entries.Right.Value = right;
        entries.Up.Value = up;
    }
}
