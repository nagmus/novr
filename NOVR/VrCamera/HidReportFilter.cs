using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace NOVR.VrCamera;

// Some HID joysticks (e.g. VKB Gladiator) occasionally send a report that Unity's generic HID layout can't parse:
// every axis snaps to its end stop and the hat reads as "up" for a single update, typically when a button is
// pressed. That would register as a hat press and nudge the camera. A real stick can't have every axis at an end
// stop at once, so state events like that are dropped before they reach the device state.
internal static class HidReportFilter
{
    private const float ExtremeThreshold = 0.99f;
    private const int MinimumAxes = 4;

    private sealed class DeviceInfo
    {
        public AxisControl[] Axes = Array.Empty<AxisControl>();
        public int Dropped;
        public int Kept;
    }

    private static readonly Dictionary<int, DeviceInfo> Devices = new();
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        InputSystem.onEvent += OnEvent;
        InputSystem.onDeviceChange += (device, _) => Devices.Remove(device.deviceId);
    }

    public static string Describe(InputDevice device)
    {
        if (!Devices.TryGetValue(device.deviceId, out var info)) return "glitchFilter=none";
        return info.Axes.Length < MinimumAxes
            ? $"glitchFilter=off ({info.Axes.Length} axes)"
            : $"glitchFilter=on axes={info.Axes.Length} dropped={info.Dropped} kept={info.Kept}";
    }

    private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (device == null || eventPtr.type != StateEvent.Type) return;

        var info = GetInfo(device);
        if (info == null || info.Axes.Length < MinimumAxes) return;

        foreach (var axis in info.Axes)
        {
            if (!axis.ReadValueFromEvent(eventPtr, out var value) || Math.Abs(value) < ExtremeThreshold)
            {
                info.Kept++;
                return;
            }
        }

        eventPtr.handled = true;
        info.Dropped++;
    }

    private static DeviceInfo? GetInfo(InputDevice device)
    {
        if (Devices.TryGetValue(device.deviceId, out var existing)) return existing;
        if (!string.Equals(device.description.interfaceName, "HID", StringComparison.OrdinalIgnoreCase)) return null;

        // Real analog axes only: not buttons/hat directions, and not synthetic controls derived from other controls.
        var info = new DeviceInfo
        {
            Axes = device.allControls
                .OfType<AxisControl>()
                .Where(control => control is not ButtonControl && !control.synthetic && control.parent is not DpadControl)
                .ToArray(),
        };
        Devices[device.deviceId] = info;
        return info;
    }
}
