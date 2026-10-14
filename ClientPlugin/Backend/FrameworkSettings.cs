using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Backend;

/// <summary>
/// Settings dialog for the Pulsar assembly. The DX11 copy of Config is a
/// different type and is the one the device patches read, so this dialog is
/// used until that assembly is loaded.
/// </summary>
static class FrameworkSettings
{
    static SettingsGenerator settings;
    static SettingsGenerator velocity;

    public static void Load()
    {
        var config = Config.Current;
        try
        {
            VRage.Utils.MyLog.Default.WriteLine(
                "Anomaly: config loaded, velocity " + config.VelocitySource
                + ", local packs " + config.ScanLocalPacks);
        }
        catch
        {
            // Config.Current still holds the deserialized file when the game log is not up.
        }
    }

    public static void Open()
    {
        Ensure();
        settings.SetLayout<Simple>();
        settings.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(settings.Dialog);
    }

    public static void Flush() => ConfigStorage.FlushPending(true);

    static void Ensure()
    {
        if (settings != null)
            return;

        Config.OpenVelocityDebug = OpenVelocity;
        settings = new SettingsGenerator(Config.Current.Title, new[]
        {
            nameof(Config.VelocitySource),
            nameof(Config.ScanLocalPacks),
            nameof(Config.ShowStatus),
            nameof(Config.ShowVelocityDebug),
        });
        velocity = new SettingsGenerator("Velocity Debug", new[]
        {
            nameof(Config.DebugBuffer),
            nameof(Config.DebugVelocityScale),
            nameof(Config.DebugMotionPersistence),
            nameof(Config.DebugMotionGain),
            nameof(Config.DebugMotionHalfLife),
            nameof(Config.VelocityProbe),
            nameof(Config.Target3Checkpoint),
            nameof(Config.ShowVelocityStatus),
        });
    }

    static void OpenVelocity()
    {
        if (velocity == null)
            return;
        velocity.SetLayout<Simple>();
        velocity.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(velocity.Dialog);
    }
}
