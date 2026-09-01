using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using ClientPlugin.Velocity;
using Sandbox.Graphics.GUI;
using VRageMath;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    private VelocitySource velocitySource = VelocitySource.GBuffer;
    private bool debugVelocity;
    private DebugBuffer debugBuffer = DebugBuffer.Off;
    private int debugVelocityScale = 32;

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

    [Dropdown(visibleRows: 5, label: "Debug buffer",
        description: "Fullscreen overlay of a catalog texture after the scene. Off keeps the game picture. Velocity uses the debug-velocity color map. Linear depth / Hi-Z are log grayscale. History color is the previous HDR LBuffer copy.")]
    public DebugBuffer DebugBuffer
    {
        get => debugBuffer;
        set => SetField(ref debugBuffer, value);
    }

    [Slider(min: 8, max: 128, step: 1, type: SliderAttribute.SliderType.Integer, label: "Debug scale (px)",
        description: "Pixel motion that maps to full color. Lower is more sensitive.")]
    public int DebugVelocityScale
    {
        get => debugVelocityScale < 8 ? 32 : debugVelocityScale;
        set => SetField(ref debugVelocityScale, value < 8 ? 32 : (value > 128 ? 128 : value));
    }

    [Separator("Status")]

    [Button(label: "Show Status", description: "Compile intercept, velocity, owned buffers, debug overlay, and history")]
    // ReSharper disable once UnusedMember.Global
    public static void ShowStatus()
    {
        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
            MyMessageBoxStyleEnum.Info,
            buttonType: MyMessageBoxButtonsType.OK,
            messageText: new StringBuilder(VelocityStatus.CurrentText),
            messageCaption: new StringBuilder("Anomaly Status"),
            size: new Vector2(0.65f, 0.58f)
        ));
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
