using System;
using System.Reflection;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// LCD / TargetView / TargetCamera hijack Keen’s renderer for an
/// offscreen view. HDR pack slots (AfterLighting through BeforeTonemap)
/// skip those views so every pack does not need its own ViewGuard.
/// AfterTonemap / AfterUpscale still run — they are display-referred.
/// </summary>
static class MainViewGate
{
    static readonly Func<bool> CameraLcd;
    static readonly Func<bool> TargetCamera;
    static readonly Func<bool> TargetView;

    static MainViewGate()
    {
        CameraLcd = BindProperty("CameraLCD.CameraViewRenderer", "IsDrawing");
        TargetCamera = BindField("SETargetCamera.Patches.Patch_MyRender11", "_drawingCameraLcds");
        TargetView = BindProperty("TargetView.TargetViewRenderer", "IsDrawing");
    }

    public static bool IsOffscreen()
    {
        return CameraLcd() || TargetCamera() || TargetView();
    }

    static Func<bool> BindProperty(string typeName, string propertyName)
    {
        try
        {
            var type = FindType(typeName);
            var prop = type?.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prop == null || prop.PropertyType != typeof(bool))
                return False;
            return () =>
            {
                try
                {
                    return prop.GetValue(null) is true;
                }
                catch
                {
                    return false;
                }
            };
        }
        catch
        {
            return False;
        }
    }

    static Func<bool> BindField(string typeName, string fieldName)
    {
        try
        {
            var type = FindType(typeName);
            var field = type?.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(bool))
                return False;
            return () =>
            {
                try
                {
                    return field.GetValue(null) is true;
                }
                catch
                {
                    return false;
                }
            };
        }
        catch
        {
            return False;
        }
    }

    static Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                continue;
            }

            if (type != null)
                return type;
        }

        return null;
    }

    static bool False() => false;
}
