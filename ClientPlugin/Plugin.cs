using System;
using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.RichHud;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.10.1.0")]
[assembly: AssemblyFileVersion("1.10.1.0")]
#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public sealed class Plugin : IPlugin
{
    public const string Name = "Anomaly";
    public static Plugin Instance { get; private set; }

    // Pulsar injects this before LoadAssets. (name, extension) → Data/{name}[/ or .{ext}].
    public static Func<string, string, string> GetConfigPath;

    private SettingsGenerator settingsGenerator;
    private SettingsGenerator velocityDebugGenerator;
    private bool disposed;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Init(object gameInstance)
    {
        disposed = false;
        Instance = this;
        settingsGenerator = new SettingsGenerator(Config.Current.Title, new[]
        {
            nameof(Config.VelocitySource),
            nameof(Config.ScanLocalPacks),
            nameof(Config.ShowStatus),
            nameof(Config.ShowVelocityDebug),
        });
        velocityDebugGenerator = new SettingsGenerator("Velocity Debug", new[]
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
        DebugLog.Open();
        VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);

        var harmony = new Harmony(Name);
        // PatchAll applies every [HarmonyPatch] in this assembly. Do not add
        // NGX / AA / jitter / DRS patches here — those live in SE-DLSS.
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        LogHarmonyPatches();
        ShaderPackRegistry.ScanLocalDrop(GetConfigPath);
        ShaderPackRegistry.Apply();
        ShaderCompileIntercept.Activate();
        ShaderPackRegistry.ValidateStages();
        ShaderCompileIntercept.ArmResidentShaderRefresh();
        ShaderCompileIntercept.RequestResidentShaderRefresh();
        ShaderWarmup.Request();
        CameraVelocityPass.Enabled = true;
        OwnedBuffersPass.Enabled = true;
        CelestialBackgroundRegistry.Install();
        SharedVolumetricRenderer.Install();
        GBufferVelocity.Enabled = true;
        AnomalyTerminalPages.Install();
        MyLog.Default.WriteLine("Anomaly shader framework initialized.");
        MyLog.Default.WriteLine("Anomaly RenderTrace ready (last-N GPU submit crumbs).");
        DebugLog.Write("Harmony patched, plugin initialized, intercept live=" + ShaderCompileIntercept.IsLive);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DebugLog.Write("Dispose");
        ConfigStorage.FlushPending(true);
        PlanetAtmosphere.Clear();
        LocalCharacter.Clear();
        VolumetricInteriorSnapshot.Clear();
        VolumetricMediumRegistry.Release();
        DirectionalVolumeShadows.Release();
        VolumetricIntegrator.Release();
        SharedVolumetricRenderer.Release();
        RichHudSupport.Shutdown();
        CameraVelocityPass.Enabled = false;
        OwnedBuffersPass.Enabled = false;
        GBufferVelocity.Enabled = false;
        ActorHistory.Instance.Clear();
        BoneHistory.Instance.Clear();
        VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
        ShaderBindRegistry.Release();
        CelestialBackgroundRegistry.Release();
        OwnedPassRegistry.Release();
        settingsGenerator = null;
        velocityDebugGenerator = null;
        if (ReferenceEquals(Instance, this))
            Instance = null;
        DebugLog.Close();
    }

    public void Update()
    {
        if (disposed)
            return;

        RichHudSupport.TryInitialize();
        ConfigStorage.FlushPending();
        PlanetAtmosphere.UpdateFromGameThread();
        LocalCharacter.UpdateFromGameThread();
        VolumetricInteriorSnapshot.UpdateFromGameThread();
        ShaderCompileIntercept.RequestResidentShaderRefresh();
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

    internal void OpenVelocityDebugDialog()
    {
        var generator = velocityDebugGenerator;
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

        ShaderCompileIntercept.SetAssetFolder(folder);
        ShaderPackRegistry.ScanLocalDrop(GetConfigPath);
        ShaderPackRegistry.Apply();
        if (Instance != null)
        {
            ShaderCompileIntercept.Activate();
            ShaderPackRegistry.ValidateStages();
            ShaderCompileIntercept.ArmResidentShaderRefresh();
            ShaderCompileIntercept.RequestResidentShaderRefresh();
            ShaderWarmup.Request();
        }
        MyLog.Default.WriteLine("Anomaly asset folder: " + folder);
        DebugLog.Write("LoadAssets " + folder);
    }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(IReadOnlyDictionary<string, string> assets)
    {
        if (disposed || assets == null)
            return;

        string shaders = null;
        if (assets.TryGetValue("Shaders", out shaders) && !string.IsNullOrEmpty(shaders))
            ShaderCompileIntercept.SetIncludeDirectory(shaders);
        if (assets.TryGetValue("AssetFolder", out var folder) && !string.IsNullOrEmpty(folder))
            ShaderCompileIntercept.SetAssetFolder(folder);

        ShaderPackRegistry.ScanLocalDrop(GetConfigPath);
        ShaderPackRegistry.Apply();
        if (Instance != null)
        {
            ShaderCompileIntercept.Activate();
            ShaderPackRegistry.ValidateStages();
            ShaderCompileIntercept.ArmResidentShaderRefresh();
            ShaderCompileIntercept.RequestResidentShaderRefresh();
            ShaderWarmup.Request();
        }
        MyLog.Default.WriteLine("Anomaly named assets: " + assets.Count
            + (shaders != null ? " Shaders=" + shaders : ""));
        DebugLog.Write("LoadAssets(dict) count=" + assets.Count);
    }

    static void LogHarmonyPatches()
    {
        var names = new List<string>();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.Namespace != null &&
                type.Namespace.StartsWith("ClientPlugin.Dlss", StringComparison.Ordinal))
            {
                MyLog.Default.WriteLine("Anomaly: leftover DLSS type compiled: " + type.FullName);
                continue;
            }

            if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: true).Length == 0)
                continue;
            names.Add(type.Name);
        }

        names.Sort(StringComparer.Ordinal);
        var line = "Harmony patch types (" + names.Count + "): " + string.Join(", ", names);
        MyLog.Default.WriteLine("Anomaly " + line);
        DebugLog.Write(line);
    }
}
