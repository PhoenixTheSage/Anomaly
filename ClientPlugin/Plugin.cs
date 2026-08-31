using System.Reflection;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using ClientPlugin.Velocity;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public sealed class Plugin : IPlugin
{
    public const string Name = "Anomaly";
    public static Plugin Instance { get; private set; }

    private SettingsGenerator settingsGenerator;
    private bool disposed;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        disposed = false;
        Instance = this;
        settingsGenerator = new SettingsGenerator();
        DebugLog.Open();
        VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);

        var harmony = new Harmony(Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        MyLog.Default.WriteLine("Anomaly shader framework initialized.");
        DebugLog.Write("Harmony patched, plugin initialized");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DebugLog.Write("Dispose");
        VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
        settingsGenerator = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        DebugLog.Close();
    }

    public void Update()
    {
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        var generator = settingsGenerator;
        if (disposed || generator == null)
            return;

        generator.SetLayout<Simple>();
        generator.Dialog.RecreateControls(true);
        MyGuiSandbox.AddScreen(generator.Dialog);
    }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(string folder)
    {
        if (disposed)
            return;

        MyLog.Default.WriteLine("Anomaly asset folder: " + folder);
        DebugLog.Write("LoadAssets " + folder);
    }
}
