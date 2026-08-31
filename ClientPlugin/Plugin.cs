using System;
using System.Collections.Generic;
using System.Reflection;
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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
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
        ShaderPackRegistry.ScanLocalDrop(GetConfigPath);
        ShaderPackRegistry.Apply();
        ShaderCompileIntercept.Activate();
        CameraVelocityPass.Enabled = true;
        GBufferVelocity.Enabled = true;
        MyLog.Default.WriteLine("Anomaly shader framework initialized.");
        DebugLog.Write("Harmony patched, plugin initialized, intercept live=" + ShaderCompileIntercept.IsLive);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        DebugLog.Write("Dispose");
        CameraVelocityPass.Enabled = false;
        GBufferVelocity.Enabled = false;
        ActorHistory.Instance.Clear();
        BoneHistory.Instance.Clear();
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

        ShaderCompileIntercept.SetAssetFolder(folder);
        ShaderPackRegistry.ScanLocalDrop(GetConfigPath);
        ShaderPackRegistry.Apply();
        if (Instance != null)
            ShaderCompileIntercept.Activate();
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
            ShaderCompileIntercept.Activate();
        MyLog.Default.WriteLine("Anomaly named assets: " + assets.Count
            + (shaders != null ? " Shaders=" + shaders : ""));
        DebugLog.Write("LoadAssets(dict) count=" + assets.Count);
    }
}
