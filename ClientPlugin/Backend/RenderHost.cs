using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ClientPlugin.Backend;

/// <summary>
/// Loads Anomaly.Dx11 only after the operational renderer is Keen DX11.
/// A Vulkan choice keeps that assembly unloaded. The choice waits until
/// SE-Vulkan reports a decision or, when that plugin is absent, until
/// <c>SpaceEngineers.MyProgram.m_renderer</c> exists. A later, different
/// choice is recorded and ignored.
/// </summary>
public static class RenderHost
{
    const string RuntimeType = "ClientPlugin.Bootstrap.BackendRuntime";
    static readonly object Gate = new object();
    static readonly List<string[]> Pending = new List<string[]>();
    static object gameInstance;
    static Type runtime;
    static bool runtimeBound;
    static bool listenersAttached;
    static bool committed;
    static int choice = -1;
    static int offered = -1;
    static string assetFolder;
    static string conflict;
    static string mismatchSeen;
    static string loggedStatus = "";

    public static bool IsDx11 => committed && choice == 0;
    public static bool IsVulkan => committed && choice == 1;
    public static bool Decided => committed;
    public static string StatusLine { get; private set; } = "Renderer: pending | waiting for the operational renderer";

    public static void Start(object instance)
    {
        gameInstance = instance;
        BindRuntime();
        Confirm();
    }

    public static void Update()
    {
        if (!committed)
        {
            BindRuntime();
            Confirm();
        }

        if (IsDx11)
            CallPlugin("Update");
        PullScene();
    }

    public static void Dispose()
    {
        FrameworkSettings.Flush();
        if (IsDx11)
            CallPlugin("Dispose");
        GuestBridgeClient.Reset();
    }

    public static void OpenConfigDialog()
    {
        if (IsDx11 && Dx11Call.Ready)
            CallPlugin("OpenConfigDialog");
        else
            FrameworkSettings.Open();
    }

    public static void LoadAssets(string folder)
    {
        assetFolder = folder;
        if (IsDx11)
            CallPlugin("LoadAssets", folder);
        GuestOwned.Register(folder);
        if (IsVulkan)
        {
            GuestColor.Publish();
            GuestCompiler.CompilePending(folder);
        }
    }

    static void CallPlugin(string method, params object[] args)
    {
        Dx11Call.CallInstance(Dx11Call.Get("ClientPlugin.Plugin", "Instance"), method, args);
    }

    public static void QueueRegister(string id, string root)
    {
        GuestPassList.RegisterRoot(id, root);
        lock (Gate)
        {
            if (!Decided)
            {
                Pending.Add(new[] { id, root });
                return;
            }
        }

        if (IsDx11)
            Dx11Call.Call("ClientPlugin.Shaders.ShaderPackRegistry", "Register", id, root);
    }

    public static void SubmitVulkan()
    {
        if (!IsVulkan)
            return;
        GuestBridgeClient.Submit(GuestPassList.Snapshot());
    }

    static void BindRuntime()
    {
        if (runtimeBound)
            return;
        runtime = FindType(RuntimeType);
        if (runtime == null)
            return;
        var addGuest = runtime.GetMethod("AddGuestListener", BindingFlags.Public | BindingFlags.Static);
        var addFrame = runtime.GetMethod("AddFrameListener", BindingFlags.Public | BindingFlags.Static);
        if (addGuest == null || addFrame == null)
        {
            SetStatus("Renderer: pending | BackendRuntime has no guest contract");
            return;
        }

        runtimeBound = true;
        addGuest.Invoke(null, new object[] { (Action<int>)NoteBackend });
        addFrame.Invoke(null, new object[] { (Action)SubmitVulkan });
        listenersAttached = true;
    }

    static void NoteBackend(int backend)
    {
        if (backend != 0 && backend != 1)
        {
            SetStatus("Renderer: pending | ignored backend value " + backend);
            return;
        }

        string clash = null;
        lock (Gate)
        {
            if (offered < 0)
                offered = backend;
            else if (offered != backend)
                clash = "second backend choice " + BackendName(backend) + " after " + BackendName(offered);
        }

        if (clash == null)
            return;
        conflict = clash;
        RecordFailure(clash);
        SetStatus("Renderer: failed | " + clash);
    }

    static void Confirm()
    {
        if (committed || conflict != null)
            return;

        if (offered < 0)
        {
            string waiting = LiveRendererName();
            if (!runtimeBound && waiting == "MyDX11Render")
                offered = 0;
            else if (runtimeBound && waiting == "MyDX11Render")
                offered = 0;
            else if (waiting == "MyVulkanRender")
            {
                SetStatus("Renderer: pending | MyVulkanRender is live and the backend decision has not arrived");
                return;
            }
            else
            {
                string summary = runtimeBound ? Summary() : "waiting for the operational renderer";
                SetStatus("Renderer: pending | " + summary);
                return;
            }
        }

        string live = LiveRendererName();
        if (string.IsNullOrEmpty(live))
        {
            SetStatus("Renderer: pending | " + BackendName(offered) + " | " + Summary());
            return;
        }

        string expect = offered == 1 ? "MyVulkanRender" : "MyDX11Render";
        if (!string.Equals(live, expect, StringComparison.Ordinal))
        {
            if (mismatchSeen != live)
            {
                mismatchSeen = live;
                SetStatus("Renderer: pending | " + BackendName(offered) + " waiting to confirm " + live);
                return;
            }

            conflict = "backend " + BackendName(offered) + " disagreed with " + live;
            RecordFailure(conflict);
            SetStatus("Renderer: failed | " + conflict);
            return;
        }

        mismatchSeen = null;
        Commit();
    }

    static void Commit()
    {
        string[][] queued;
        lock (Gate)
        {
            if (committed)
                return;
            choice = offered;
            committed = true;
            queued = Pending.ToArray();
            Pending.Clear();
        }

        SceneView.BackendName = BackendName(choice);
        if (choice == 0)
        {
            LoadDx11(queued);
            global::ClientPlugin.Buffers.BufferCatalog.ApplyDeferred();
            return;
        }

        GuestCompiler.CompilePending(assetFolder);
        global::ClientPlugin.Buffers.BufferCatalog.ApplyDeferred();
        SetStatus("Renderer: Vulkan | " + Summary() + " | guest listener " + (listenersAttached ? "attached" : "missing"));
    }

    static void LoadDx11(string[][] queued)
    {
        string path = SatelliteLookup.Find("Anomaly.Dx11.dll", typeof(RenderHost).Assembly);
        if (!File.Exists(path))
        {
            SetStatus("Renderer: DX11 | " + Summary() + " | Anomaly.Dx11.dll missing (" + path + ")");
            return;
        }

        if (!Dx11Call.Ready)
        {
            try
            {
                var assembly = Assembly.LoadFrom(path);
                Dx11Call.Bind(assembly);
                Dx11Call.Call("ClientPlugin.Plugin", "InstallFramework", gameInstance);
            }
            catch (Exception e)
            {
                SetStatus("Renderer: DX11 | " + Summary() + " | Anomaly.Dx11.dll failed (" + path + "): " + e.GetType().Name);
                return;
            }
        }

        foreach (var item in queued)
            Dx11Call.Call("ClientPlugin.Shaders.ShaderPackRegistry", "Register", item[0], item[1]);
        if (!string.IsNullOrEmpty(assetFolder))
            CallPlugin("LoadAssets", assetFolder);
        SetStatus("Renderer: DX11 | " + Summary() + " | guest listener " + (listenersAttached ? "attached" : "not required") + " | Anomaly.Dx11 loaded");
    }

    static void PullScene()
    {
        if (!IsDx11)
            return;
        int width = Convert.ToInt32(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "Width") ?? 0);
        int height = Convert.ToInt32(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "Height") ?? 0);
        double x = Convert.ToDouble(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "CameraX") ?? 0d);
        double y = Convert.ToDouble(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "CameraY") ?? 0d);
        double z = Convert.ToDouble(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "CameraZ") ?? 0d);
        float sx = Convert.ToSingle(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "SunX") ?? 0f);
        float sy = Convert.ToSingle(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "SunY") ?? 0f);
        float sz = Convert.ToSingle(Dx11Call.Get("ClientPlugin.ShaderFramework.SceneSnapshot", "SunZ") ?? 0f);
        SceneView.Set(width, height, x, y, z, sx, sy, sz);
    }

    static string LiveRendererName()
    {
        var program = FindType("SpaceEngineers.MyProgram");
        var field = program?.GetField("m_renderer", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || !field.IsStatic)
            return null;
        try
        {
            return field.GetValue(null)?.GetType().Name;
        }
        catch
        {
            return null;
        }
    }

    static string Summary()
    {
        if (runtime == null)
            return "SE-Vulkan is not loaded";
        return runtime.GetProperty("SelectionSummary", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string
            ?? "backend decision recorded";
    }

    static void RecordFailure(string reason)
    {
        try
        {
            runtime?.GetMethod("RecordFailure", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { "Anomaly: " + reason });
        }
        catch
        {
            // The status line still carries the failure when the runtime rejects the note.
        }
    }

    static void SetStatus(string line)
    {
        StatusLine = line;
        if (loggedStatus == line)
            return;
        loggedStatus = line;
        try { VRage.Utils.MyLog.Default.WriteLine("Anomaly: " + line); }
        catch { /* The status property remains readable when the game log is not up. */ }
    }

    static string BackendName(int backend) => backend == 1 ? "Vulkan" : "DX11";

    static Type FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(name, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }
}
