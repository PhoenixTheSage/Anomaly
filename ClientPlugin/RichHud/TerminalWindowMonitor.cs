using System;
using System.Reflection;
using HarmonyLib;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Master’s terminal is a <c>WindowBase</c> in the Master assembly, not
/// Anomaly’s vendored copy. Rich HUD has no resize event; this Harmony
/// postfix watches <c>resizeDir</c> after <c>HandleInput</c> (same polling
/// loop a <see cref="ResizableWindow"/> subclass uses) and publishes the
/// live size to <see cref="TerminalConfigRegistry"/>.
/// </summary>
static class TerminalWindowMonitor
{
    const string WindowBaseTypeName = "RichHudFramework.UI.WindowBase";
    static readonly Harmony Harmony = new("Anomaly.TerminalWindowMonitor");
    static bool patched;
    static FieldInfo resizeDirField;
    static PropertyInfo sizeProperty;
    static PropertyInfo windowActiveProperty;
    static Vector2 lastSize;
    static bool sampled;
    static bool wasResizing;
    static bool sizeChangedDuringDrag;
    static object terminalWindow;

    public static void EnsurePatched()
    {
        if (patched)
            return;

        var ours = typeof(WindowBase).Assembly;
        var found = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, ours))
                continue;

            Type type;
            try
            {
                type = assembly.GetType(WindowBaseTypeName, false, false);
            }
            catch
            {
                continue;
            }

            if (type == null)
                continue;

            var method = type.GetMethod(
                "HandleInput",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(Vector2) },
                null);
            if (method == null)
                continue;

            if (resizeDirField == null)
            {
                resizeDirField = type.GetField("resizeDir", BindingFlags.Instance | BindingFlags.NonPublic);
                sizeProperty = type.GetProperty("Size", BindingFlags.Instance | BindingFlags.Public);
                windowActiveProperty = type.GetProperty("WindowActive", BindingFlags.Instance | BindingFlags.Public);
            }

            Harmony.Patch(method, postfix: new HarmonyMethod(typeof(TerminalWindowMonitor), nameof(HandleInputPostfix)));
            found++;
        }

        if (found == 0 || resizeDirField == null || sizeProperty == null)
            return;

        patched = true;
        MyLog.Default.WriteLine("Anomaly: Rich HUD terminal resize monitor is live");
    }

    public static void Reset()
    {
        sampled = false;
        wasResizing = false;
        sizeChangedDuringDrag = false;
        lastSize = Vector2.Zero;
        terminalWindow = null;
    }

    static void HandleInputPostfix(object __instance)
    {
        if (__instance == null || resizeDirField == null || sizeProperty == null)
            return;
        if (!IsTerminalWindow(__instance))
            return;

        Vector2 size;
        Vector2 dir;
        try
        {
            size = (Vector2)sizeProperty.GetValue(__instance);
            dir = (Vector2)resizeDirField.GetValue(__instance);
        }
        catch
        {
            return;
        }

        var resizing = dir != Vector2.Zero;
        if (!sampled)
        {
            sampled = true;
            lastSize = size;
            wasResizing = resizing;
            TerminalConfigRegistry.NotifyWindowResized(size);
            return;
        }

        if (!TerminalWindowLayout.NearlyEqual(size, lastSize))
        {
            lastSize = size;
            sizeChangedDuringDrag = true;
            TerminalConfigRegistry.ApplyWindowSize(size, completed: false);
        }

        if (resizing)
        {
            wasResizing = true;
            return;
        }

        if ((wasResizing && sizeChangedDuringDrag) || sizeChangedDuringDrag)
        {
            wasResizing = false;
            sizeChangedDuringDrag = false;
            TerminalConfigRegistry.NotifyWindowResized(size);
            return;
        }

        wasResizing = false;
    }

    static bool IsTerminalWindow(object instance)
    {
        if (ReferenceEquals(terminalWindow, instance))
            return true;

        var type = instance.GetType();
        if (NameLooksLikeTerminal(type))
        {
            terminalWindow = instance;
            return true;
        }

        if (!MenuIsOpen() || sizeProperty == null)
            return false;

        try
        {
            var size = (Vector2)sizeProperty.GetValue(instance);
            if (size.X + 1f < TerminalWindowLayout.MinWindowWidth * 0.75f)
                return false;
            var active = windowActiveProperty?.GetValue(instance);
            if (active is bool b && b)
            {
                terminalWindow = instance;
                return true;
            }
        }
        catch
        {
            // Leave unmatched; another WindowBase tick may qualify.
        }

        return false;
    }

    static bool NameLooksLikeTerminal(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var name = t.Name;
            if (name.IndexOf("SettingsMenu", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("HudTerminal", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("TerminalWindow", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    static bool MenuIsOpen()
    {
        try
        {
            return RichHudClient.Registered && RichHudTerminal.Open;
        }
        catch
        {
            return false;
        }
    }
}
