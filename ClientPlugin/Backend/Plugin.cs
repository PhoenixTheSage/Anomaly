using System.Reflection;
using VRage.Plugins;
using ClientPlugin.RichHud;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Anomaly.Dx11")]

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.13.0.0")]
[assembly: AssemblyFileVersion("1.13.0.0")]
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
        DebugLog.Open();
        Backend.FrameworkSettings.Load();
        Backend.RenderHost.Start(gameInstance);
    }

    public void Update()
    {
        Backend.RenderHost.Update();
        ClientPlugin.Settings.ConfigStorage.FlushPending();
        AnomalyTerminalPages.Install();
        RichHudSupport.TryInitialize();
        InteractiveOverlayRegistry.Update();
    }

    public void Dispose()
    {
        Backend.RenderHost.Dispose();
        RichHudSupport.Shutdown();
        Backend.SatelliteAssembly.Reset();
        DebugLog.Close();
    }

    public void OpenConfigDialog()
    {
        if (Backend.RenderHost.IsDx11)
            Backend.RenderHost.OpenConfigDialog();
        else
            Backend.FrameworkSettings.Open();
    }

    public void LoadAssets(string folder) => Backend.RenderHost.LoadAssets(folder);
}
