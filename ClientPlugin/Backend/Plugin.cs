using System.Reflection;
using VRage.Plugins;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.11.0.0")]
[assembly: AssemblyFileVersion("1.11.0.0")]
#endif

namespace ClientPlugin;

/// <summary>
/// Pulsar entry. This assembly does not reference VRage.Render11 or SharpDX
/// and does not patch Keen. <see cref="Backend.RenderHost"/> loads Anomaly.Dx11
/// only when the operational renderer is DX11.
/// </summary>
public sealed class Plugin : IPlugin
{
    public const string Name = "Anomaly";
    public static Plugin Instance { get; private set; }

    public void Init(object gameInstance)
    {
        Instance = this;
        Backend.FrameworkSettings.Load();
        Backend.RenderHost.Start(gameInstance);
    }

    public void Update() => Backend.RenderHost.Update();

    public void Dispose() => Backend.RenderHost.Dispose();

    public void OpenConfigDialog()
    {
        if (Backend.RenderHost.IsDx11)
            Backend.RenderHost.OpenConfigDialog();
        else
            Backend.FrameworkSettings.Open();
    }

    public void LoadAssets(string folder) => Backend.RenderHost.LoadAssets(folder);
}
