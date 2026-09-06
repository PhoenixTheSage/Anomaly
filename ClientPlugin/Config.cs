using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using ClientPlugin.Velocity;
using Sandbox.Graphics.GUI;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    private VelocitySource velocitySource = VelocitySource.GBuffer;
    private bool debugVelocity;
    private DebugBuffer debugBuffer = DebugBuffer.Off;
    private int debugVelocityScale = 32;
    private VelocityProbe velocityProbe = VelocityProbe.Off;
    private Target3Checkpoint target3Checkpoint = Target3Checkpoint.Live;

    #endregion

    #region User interface

    public readonly string Title = "Anomaly";

    [Separator("Velocity")]

    [Dropdown(visibleRows: 2, label: "Velocity source",
        description: "GBuffer writes object motion on Keen's geometry pixels. CameraOnly is fullscreen depth reprojection.")]
    public VelocitySource VelocitySource
    {
        get => velocitySource;
        set => SetField(ref velocitySource, value);
    }

    [Checkbox(label: "Debug velocity",
        description: "Legacy toggle for the velocity overlay. Prefer Debug buffer. Mid-gray is no motion. Red/green are X/Y pixel delta; blue is speed. Magenta is first frame or a camera cut.")]
    public bool DebugVelocity
    {
        get => debugVelocity;
        set => SetField(ref debugVelocity, value);
    }

    [Dropdown(visibleRows: 9, label: "Debug buffer",
        description: "Velocity is the final composite. GBufferVelocityRaw is SV_Target3 before camera/depth gap fill. VelocityPipelineAudit shows Target3, pixel execution, PS b7, VS velocity, a GBuffer0 b7 marker, and raw GBuffer0 together. Other entries inspect owned catalog buffers.")]
    public DebugBuffer DebugBuffer
    {
        get => debugBuffer;
        set => SetField(ref debugBuffer, value);
    }

    [Slider(min: 1, max: 128, step: 1, type: SliderAttribute.SliderType.Integer, label: "Debug scale (px)",
        description: "Pixel motion that maps to full color. Lower is more sensitive. 1 shows sub-pixel motion.")]
    public int DebugVelocityScale
    {
        get => debugVelocityScale < 1 ? 32 : debugVelocityScale;
        set => SetField(ref debugVelocityScale, value < 1 ? 32 : (value > 128 ? 128 : value));
    }

    [Dropdown(visibleRows: 5, label: "Velocity probe",
        description: "Developer diagnostic. TargetClear tests the frame clear; MrtWrite: pink = live pixel-output-to-Target3 write, cyan = no geometry write, gray = explicit zero; PassEndClear clears the final target after all deferred geometry lists execute; HistoryCoverage: pink = previous-world hit, cyan = miss. Probe changes apply immediately. Return to Off after testing.")]
    public VelocityProbe VelocityProbe
    {
        get => velocityProbe;
        set => SetField(ref velocityProbe, value);
    }

    [Dropdown(visibleRows: 9, label: "Target3 checkpoint",
        description: "Developer diagnostic for GBufferVelocityRaw. Live samples the final target. Other choices copy only that scheduler boundary, letting one build isolate where Target3 changes without affecting the published velocity buffer.")]
    public Target3Checkpoint Target3Checkpoint
    {
        get => target3Checkpoint;
        set => SetField(ref target3Checkpoint, value);
    }

    [Separator("Status")]

    [Button(label: "Show Status", description: "Operational health for shader integration, passes, buffers, velocity, and active debug modes")]
    // ReSharper disable once UnusedMember.Global
    public static void ShowStatus()
    {
        MyGuiSandbox.AddScreen(new StatusScreen("Anomaly Status", "AnomalyStatus", VelocityStatus.CurrentText));
    }

    [Button(label: "Debug Status", description: "Detailed shader, GBuffer, Stage 2, MRT, history, and draw-boundary diagnostics")]
    // ReSharper disable once UnusedMember.Global
    public static void ShowDebugStatus()
    {
        MyGuiSandbox.AddScreen(new StatusScreen("Anomaly Debug Status", "AnomalyDebugStatus", VelocityStatus.DebugText));
    }

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    #endregion
}
