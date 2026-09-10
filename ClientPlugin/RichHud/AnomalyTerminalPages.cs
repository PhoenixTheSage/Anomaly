using ClientPlugin.Settings;

namespace ClientPlugin.RichHud;

/// <summary>
/// Mirrors the Pulsar MyGui dialogs under the Rich HUD root
/// <see cref="TerminalConfigRegistry.RootName"/>.
/// </summary>
static class AnomalyTerminalPages
{
    static bool installed;

    public static void Install()
    {
        if (installed)
            return;
        installed = true;

        var settings = TerminalConfigRegistry.RequestReservedPage(TerminalConfigRegistry.SettingsTitle);
        if (settings != null)
        {
            settings
                .Category("Settings")
                .Dropdown("Velocity source", () => Config.Current.VelocitySource, v => Config.Current.VelocitySource = v,
                    "GBuffer writes object motion on Keen's geometry pixels. CameraOnly is fullscreen depth reprojection.")
                .Checkbox("Load local shader packs", () => Config.Current.ScanLocalPacks, v => Config.Current.ScanLocalPacks = v,
                    "Scan Data/Anomaly/Packs for unsigned folders and zips. Off for PluginHub builds. Requires a restart.")
                .Button("Show Status", Config.ShowStatus, "Compile intercept, Rich HUD handshake, packs, passes, and published buffers.")
                .Button("Velocity Debug", Config.ShowVelocityDebug, "Open the Pulsar Velocity Debug dialog. The same options are also a page in this terminal.");
        }

        var debug = TerminalConfigRegistry.RequestReservedPage(TerminalConfigRegistry.VelocityDebugTitle);
        if (debug != null)
        {
            debug
                .Category("Visualization")
                .Dropdown("Debug view", () => Config.Current.DebugBuffer, v => Config.Current.DebugBuffer = v,
                    "Velocity is the final composite. Other entries inspect owned catalog buffers.")
                .IntSlider("Debug scale (px)", 1, 128, () => Config.Current.DebugVelocityScale, v => Config.Current.DebugVelocityScale = v,
                    "Pixel motion that maps to full color. Lower is more sensitive. 1 shows sub-pixel motion.")
                .Checkbox("Persistent visualization", () => Config.Current.DebugMotionPersistence, v => Config.Current.DebugMotionPersistence = v,
                    "Retain screen-space motion trails and preserve the image while paused.")
                .IntSlider("Motion gain", 1, 64, () => Config.Current.DebugMotionGain, v => Config.Current.DebugMotionGain = v,
                    "Additional sensitivity for persistent visualization only.")
                .IntSlider("Fade half-life (seconds)", 1, 10, () => Config.Current.DebugMotionHalfLife, v => Config.Current.DebugMotionHalfLife = v,
                    "Time for retained motion evidence to halve. Pausing stops the fade.")
                .Category("Audit")
                .Dropdown("Velocity probe", () => Config.Current.VelocityProbe, v => Config.Current.VelocityProbe = v,
                    "Developer diagnostic. Return to Off after testing.")
                .Dropdown("Target3 checkpoint", () => Config.Current.Target3Checkpoint, v => Config.Current.Target3Checkpoint = v,
                    "Developer diagnostic for GBufferVelocityRaw.")
                .Button("Velocity Status", Config.ShowVelocityStatus, "Concise motion-vector health and active proofing results.");
        }
    }
}
