using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using BepInEx.Logging;
using NOVR.VrUi.Native;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace NOVR.VrCamera;

// Moves the player's head around the cockpit with keyboard keys or joystick axes/buttons ("Camera Move" config).
// It adjusts the Cockpit Head Offsets live and, once movement stops, writes them to the config and saves them for
// the current aircraft. Joysticks are read through Unity's Input System by control path, which works even when
// the game's own Rewired input isn't available to NOVR.
[DefaultExecutionOrder(-110)]
public class CameraMoveController : NOVRBehaviour
{
    private const float LogAxisThreshold = 0.25f;
    private const float ButtonPressThreshold = 0.5f;

    // Logged through BepInEx so the lines reach LogOutput.log even when Unity log output is disabled there.
    private static readonly ManualLogSource InputLog = BepInEx.Logging.Logger.CreateLogSource("NOVR Input");
    private static readonly KeyCode[] LoggableKeys = BuildLoggableKeys();

    private readonly Dictionary<string, AxisControl?> _resolvedControls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _previousButtonStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<InputControl, float> _loggedValues = new();
    private bool _controlsDirty = true;

    protected override void Awake()
    {
        base.Awake();
        InputSystem.onDeviceChange += OnDeviceChange;
        HidReportFilter.Install();
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }

    protected override void OnSettingChanged()
    {
        base.OnSettingChanged();
        _controlsDirty = true;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        _controlsDirty = true;
    }

    private void Update()
    {
        var config = ModConfiguration.Instance;
        if (_controlsDirty)
        {
            _resolvedControls.Clear();
            _controlsDirty = false;
        }

        if (config.LogControllerInput.Value)
        {
            LogInput();
        }

        if (WasControlPressed(config.RecenterButton.Value))
        {
            NativeVrUiRoot.TriggerRecenterShortcut();
        }

        var resetPressed = Input.GetKeyDown(config.CameraMoveKeyReset.Value) | WasControlPressed(config.CameraMoveButtonReset.Value);

        if (!GameManager.GetLocalAircraft(out var aircraft) || aircraft == null)
        {
            CommitAdjustment(config);
            return;
        }

        if (resetPressed)
        {
            ResetOffsets(config);
            return;
        }

        var input = ReadMoveInput(config);
        if (input == Vector3.zero)
        {
            CommitAdjustment(config);
            return;
        }

        var configured = NOVRHeadsetData.CockpitOffset - NOVRHeadsetData.CockpitOffsetAdjustment;
        var max = Mathf.Max(0f, config.CameraMoveMaxOffset.Value);
        var target = NOVRHeadsetData.CockpitOffset + Vector3.ClampMagnitude(input, 1f) * (config.CameraMoveSpeed.Value * Time.unscaledDeltaTime);
        target = new Vector3(
            Mathf.Clamp(target.x, -max, max),
            Mathf.Clamp(target.y, -max, max),
            Mathf.Clamp(target.z, -max, max));
        NOVRHeadsetData.CockpitOffsetAdjustment = target - configured;
    }

    // Folds the live adjustment into the Cockpit Head Offset settings and saves them for the current aircraft.
    private static void CommitAdjustment(ModConfiguration config)
    {
        var adjustment = NOVRHeadsetData.CockpitOffsetAdjustment;
        if (adjustment == Vector3.zero) return;

        var offset = NOVRHeadsetData.CockpitOffset;
        NOVRHeadsetData.CockpitOffsetAdjustment = Vector3.zero;
        SetOffsets(config, offset);
    }

    private static void ResetOffsets(ModConfiguration config)
    {
        NOVRHeadsetData.CockpitOffsetAdjustment = Vector3.zero;
        SetOffsets(config, new Vector3(
            (float)config.CockpitHeadRightOffset.DefaultValue,
            (float)config.CockpitHeadUpOffset.DefaultValue,
            (float)config.CockpitHeadForwardOffset.DefaultValue));
    }

    private static void SetOffsets(ModConfiguration config, Vector3 offset)
    {
        config.CockpitHeadRightOffset.Value = offset.x;
        config.CockpitHeadUpOffset.Value = offset.y;
        config.CockpitHeadForwardOffset.Value = offset.z;
        config.SaveCurrentOffsetFor(Core.CurrentAircraftId, offset.z, offset.x, offset.y);
    }

    private Vector3 ReadMoveInput(ModConfiguration config)
    {
        var input = new Vector3(
            KeyAxis(config.CameraMoveKeyRight, config.CameraMoveKeyLeft),
            KeyAxis(config.CameraMoveKeyUp, config.CameraMoveKeyDown),
            KeyAxis(config.CameraMoveKeyForward, config.CameraMoveKeyBack));

        var deadzone = config.CameraMoveDeadzone.Value;
        var axisX = ApplyDeadzone(ReadAxis(config.CameraMoveAxisLeftRight.Value), deadzone);
        var axisY = ApplyDeadzone(ReadAxis(config.CameraMoveAxisForwardBack.Value), deadzone);
        var axisUp = ApplyDeadzone(ReadAxis(config.CameraMoveAxisUpDown.Value), deadzone);
        if (config.CameraMoveInvertLeftRight.Value) axisX = -axisX;
        if (config.CameraMoveInvertForwardBack.Value) axisY = -axisY;
        if (config.CameraMoveInvertUpDown.Value) axisUp = -axisUp;

        var forwardBack = axisY + ButtonAxis(config.CameraMoveButtonForward.Value, config.CameraMoveButtonBack.Value);
        input.x += axisX + ButtonAxis(config.CameraMoveButtonRight.Value, config.CameraMoveButtonLeft.Value);
        input.y += axisUp + ButtonAxis(config.CameraMoveButtonUp.Value, config.CameraMoveButtonDown.Value);

        if (IsControlHeld(config.CameraMoveButtonVerticalModifier.Value))
        {
            input.y += forwardBack;
        }
        else
        {
            input.z += forwardBack;
        }

        return input;
    }

    private static float KeyAxis(ConfigEntry<KeyCode> positive, ConfigEntry<KeyCode> negative)
    {
        return (IsKeyHeld(positive.Value) ? 1f : 0f) - (IsKeyHeld(negative.Value) ? 1f : 0f);
    }

    private static bool IsKeyHeld(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKey(key);
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        deadzone = Mathf.Clamp(deadzone, 0f, 0.95f);
        var magnitude = Mathf.Abs(value);
        if (magnitude <= deadzone) return 0f;
        return Mathf.Sign(value) * (magnitude - deadzone) / (1f - deadzone);
    }

    // ------------------------------------------------------------------ joystick controls (by control path)

    private static bool IsUnbound(string? path)
    {
        return string.IsNullOrWhiteSpace(path);
    }

    private AxisControl? ResolveControl(string? path)
    {
        if (IsUnbound(path)) return null;
        path = path!.Trim();
        if (_resolvedControls.TryGetValue(path, out var cached)) return cached;

        AxisControl? found = null;
        foreach (var device in InputSystem.devices)
        {
            foreach (var control in device.allControls)
            {
                if (control is AxisControl axisControl && string.Equals(control.path, path, StringComparison.OrdinalIgnoreCase))
                {
                    found = axisControl;
                    break;
                }
            }

            if (found != null) break;
        }

        _resolvedControls[path] = found;
        return found;
    }

    private float ReadAxis(string? path)
    {
        var control = ResolveControl(path);
        return control != null && control.device.enabled ? control.ReadValue() : 0f;
    }

    private bool IsControlHeld(string? path)
    {
        return ReadAxis(path) > ButtonPressThreshold;
    }

    private float ButtonAxis(string? positive, string? negative)
    {
        return (IsControlHeld(positive) ? 1f : 0f) - (IsControlHeld(negative) ? 1f : 0f);
    }

    // Edge detection per path; call once per frame per binding.
    private bool WasControlPressed(string? path)
    {
        if (IsUnbound(path)) return false;
        var key = path!.Trim();
        var held = IsControlHeld(key);
        _previousButtonStates.TryGetValue(key, out var wasHeld);
        _previousButtonStates[key] = held;
        return held && !wasHeld;
    }

    // ------------------------------------------------------------------ input logging (Log Controller Input)

    private void LogInput()
    {
        if (Input.anyKeyDown)
        {
            foreach (var key in LoggableKeys)
            {
                if (Input.GetKeyDown(key))
                {
                    InputLog.LogInfo($"Input: f={Time.frameCount} Key {key} pressed");
                }
            }
        }

        foreach (var device in InputSystem.devices)
        {
            if (!IsJoystickLike(device)) continue;

            foreach (var control in device.allControls)
            {
                if (control is not AxisControl axisControl || control.synthetic || control.noisy) continue;

                var value = axisControl.ReadValue();
                _loggedValues.TryGetValue(control, out var lastValue);

                if (control is ButtonControl)
                {
                    _loggedValues[control] = value;
                    if (value > ButtonPressThreshold && lastValue <= ButtonPressThreshold)
                    {
                        InputLog.LogInfo($"Input: f={Time.frameCount} Button {control.path} pressed");
                    }

                    continue;
                }

                if (Mathf.Abs(value - lastValue) < LogAxisThreshold) continue;
                _loggedValues[control] = value;
                InputLog.LogInfo($"Input: f={Time.frameCount} Axis {control.path} = {value.ToString("0.00", CultureInfo.InvariantCulture)}");
            }
        }
    }

    private static bool IsJoystickLike(InputDevice device)
    {
        if (device is Keyboard || device is Pointer) return false;
        var layout = device.layout ?? "";
        return layout.IndexOf("XR", StringComparison.OrdinalIgnoreCase) < 0;
    }

    // Keyboard keys only; mouse and legacy joystick buttons are excluded.
    private static KeyCode[] BuildLoggableKeys()
    {
        var keys = new List<KeyCode>();
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (key == KeyCode.None || key >= KeyCode.Mouse0) continue;
            keys.Add(key);
        }

        return keys.ToArray();
    }
}
