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

    private VelocitySource velocitySource = VelocitySource.CameraOnly;

    #endregion

    #region User interface

    public readonly string Title = "Anomaly";

    [Separator("Velocity")]

    [Dropdown(visibleRows: 10, label: "Velocity source",
        description: "GBuffer piggyback is the target. CameraOnly is the stub until intercepts exist. OwnedRaster is bootstrap only.")]
    public VelocitySource VelocitySource
    {
        get => velocitySource;
        set => SetField(ref velocitySource, value);
    }

    [Separator("Status")]

    [Button(label: "Show Status", description: "Velocity source, buffer size, and history")]
    // ReSharper disable once UnusedMember.Global
    public static void ShowStatus()
    {
        MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
            MyMessageBoxStyleEnum.Info,
            buttonType: MyMessageBoxButtonsType.OK,
            messageText: new StringBuilder(VelocityStatus.CurrentText),
            messageCaption: new StringBuilder("Anomaly Status"),
            size: new Vector2(0.6f, 0.5f)
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
