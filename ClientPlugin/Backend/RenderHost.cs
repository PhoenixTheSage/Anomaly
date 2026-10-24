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
    static readonly List<HeldOwnedPass> PendingOwned = new List<HeldOwnedPass>();
    static readonly Dictionary<string, bool> PendingEnabled =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    static object gameInstance;
    static Type runtime;
    static bool runtimeBound;
    static bool listenersAttached;
    static bool committed;
    static int choice = -1;
    static int offered = -1;
    static string assetFolder;
    internal static string AssetFolder => assetFolder;
    static string conflict;
    static string mismatchSeen;
    static string loggedStatus = "";
    static int nextGuestCompile;
    static bool guestCompileAttempted;
    // Absolute camera from SE-Vulkan's scene listener. The guest frame's
    // camera_to_world is view-at-zero (DX11 InvViewAt0 parity), so it has no position.
    static double[] guestSceneCamera;

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
        global::ClientPlugin.Shaders.LocalCharacter.UpdateFromGameThread();
        global::ClientPlugin.Shaders.PlanetAtmosphere.UpdateFromGameThread();
        GuestOccupancy.LocalActorId = global::ClientPlugin.Shaders.LocalCharacter.Copy().ActorId;
        if (IsVulkan) {
            global::ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot.UpdateFromGameThread();
        }
        if (!committed)
        {
            BindRuntime();
            Confirm();
        }

        if (IsDx11)
            CallPlugin("Update");
        else if (IsVulkan)
            SetStatus("Renderer: Vulkan | " + Summary() + " | guest listener " + (listenersAttached ? "attached" : "missing"));
        PullScene();
    }

    public static void Dispose()
    {
        FrameworkSettings.Flush();
        global::ClientPlugin.Shaders.PlanetAtmosphere.Clear();
        if (IsDx11)
            CallPlugin("Dispose");
        else
        {
            global::ClientPlugin.ShaderFramework.VolumetricInteriorSnapshot.Clear();
            try { VolumeRegistryState.ResetIntervals(); }
            catch(Exception error) { System.Diagnostics.Trace.WriteLine(error); }
            finally { VolumeRegistryState.Release(); }
        }
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
        }
    }

    static void CallPlugin(string method, params object[] args)
    {
        Dx11Call.CallInstance(Dx11Call.Get("ClientPlugin.Plugin", "Instance"), method, args);
    }

    public static void QueueRegister(string id, string root) => QueueRegister(id, root, null);
    public static void QueueUnregister(string id)
    {
        GuestPassList.UnregisterRoot(id);
        global::ClientPlugin.Buffers.BufferCatalog.UnpublishAll(id);
        lock (Gate) Pending.RemoveAll(item => string.Equals(item[0], id, StringComparison.OrdinalIgnoreCase));
        if (IsDx11) Dx11Call.Call("ClientPlugin.Shaders.ShaderPackRegistry", "Unregister", id);
    }

    public static void QueueRegister(string id, string root, string requiredProgram)
    {
        GuestPassList.RegisterRoot(id, root, requiredProgram);
        root=GuestPassList.SourceFor(id).Root;
        lock (Gate)
        {
            if (!Decided)
            {
                Pending.RemoveAll(item=>string.Equals(item[0],id,StringComparison.OrdinalIgnoreCase));
                Pending.Add(new[] { id, root });
                return;
            }
        }

        if (IsDx11)
            Dx11Call.Call("ClientPlugin.Shaders.ShaderPackRegistry", "Register", id, root);
    }

    /// <summary>
    /// <c>LoadAssets</c> runs before the renderer choice. Hold the callback
    /// until DX11 commits. A dropped register leaves json Replace programs at
    /// their default-on state and they overwrite the world HDR target.
    /// Returns true when the call was held. Vulkan discards the hold.
    /// </summary>
    public static bool HoldOwnedPass(string id, string slot, int priority, int policy, Action<object> draw, int phase)
    {
        if (string.IsNullOrEmpty(id) || draw == null)
            return true;
        lock (Gate)
        {
            if (committed)
                return false;
            for (var i = PendingOwned.Count - 1; i >= 0; i--)
            {
                if (string.Equals(PendingOwned[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    PendingOwned.RemoveAt(i);
            }

            PendingOwned.Add(new HeldOwnedPass
            {
                Id = id,
                Slot = slot,
                Priority = priority,
                Policy = policy,
                Draw = draw,
                Phase = phase
            });
            return true;
        }
    }

    /// <summary>Same hold as <see cref="HoldOwnedPass"/>. Last write for an id wins.</summary>
    public static bool HoldEnabled(string id, bool enabled)
    {
        if (string.IsNullOrEmpty(id))
            return true;
        lock (Gate)
        {
            if (committed)
                return false;
            PendingEnabled[id] = enabled;
            return true;
        }
    }

    /// <summary>True when a pending unregister consumed the hold.</summary>
    public static bool DropHeldOwnedPass(string id)
    {
        lock (Gate)
        {
            if (committed)
                return false;
            if (!string.IsNullOrEmpty(id))
            {
                for (var i = PendingOwned.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(PendingOwned[i].Id, id, StringComparison.OrdinalIgnoreCase))
                        PendingOwned.RemoveAt(i);
                }

                PendingEnabled.Remove(id);
            }

            return true;
        }
    }

    public static void SubmitVulkan()
    {
        if (!IsVulkan)
            return;
        if(!GuestDevice.Synchronize())return;
        // Native resources belong to this frame callback, after GuestBridge.Bind.
        // Asset loading/renderer selection can precede the device by many seconds.
        if (string.IsNullOrEmpty(assetFolder) || !GuestFrame.Capture())
            return;
        if (!GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.LiveResources))
        {
            GuestBridgeClient.Submit(null);
            VolumeRegistryState.SetStatus("Shared volume unavailable: update SE-Vulkan native guest live-frame resources");
            SetStatus("Renderer: Vulkan | update SE-Vulkan native library: guest live-frame resource support missing");
            return;
        }
        PullGuestScene();
        GuestVolumes.PreparePrograms(assetFolder);
        int now = Environment.TickCount;
        if (GuestCompiler.HasPending && (!guestCompileAttempted || unchecked(now - nextGuestCompile) >= 0))
        {
            guestCompileAttempted = true;
            nextGuestCompile = unchecked(now + 1000);
            GuestCompiler.CompilePending(assetFolder);
        }
        if(!GuestPostprocessStages.PreparationBound)GuestNamedStages.Prepare(assetFolder);
        GuestOwned.DebugLitMips = global::ClientPlugin.Config.Current?.DebugBuffer == global::ClientPlugin.DebugBuffer.LitMips;
        GuestPassList.SubmitFrame(GuestBridgeClient.Submit);
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
        var addPreparation=runtime.GetMethod("AddPreparationListener",BindingFlags.Public|BindingFlags.Static);
        if(addPreparation!=null){GuestPostprocessStages.PreparationBound=true;addPreparation.Invoke(null,new object[]{(Action)PrepareShaderGenerations});}
        var addScene=runtime.GetMethod("AddSceneListener",BindingFlags.Public|BindingFlags.Static);
        addScene?.Invoke(null,new object[]{(Action<double[],float[]>)PrepareSceneVulkan});
        listenersAttached = true;
    }
    public static void PrepareShaderGenerations()
    {
        if(!IsVulkan||string.IsNullOrEmpty(assetFolder)||!GuestDevice.Synchronize())return;
        GuestVelocityProbe.Apply();
        GuestColor.SyncDisplay();
        ClientPlugin.Shaders.OpaqueLightingOwnership.BeginFrame();
        GuestNamedStages.DeferActivation=true;
        long revision;lock(GuestPassList.Gate)revision=GuestPassList.SourceRevision;
        if(GuestCompiler.HasPending)GuestCompiler.CompilePending(assetFolder);
        GuestNamedStages.Prepare(assetFolder);
        bool nativeReady=GuestPostprocessStages.Prepare();
        lock(GuestPassList.Gate)
        {
            if(revision!=GuestPassList.SourceRevision||!GuestNamedStages.CanActivate()||!nativeReady)
            {GuestNamedStages.DiscardPrepared();GuestPostprocessStages.Discard();return;}
            if(GuestPostprocessStages.Activate())GuestNamedStages.ActivatePrepared();
            else GuestNamedStages.DiscardPrepared();
        }
    }
    static void PrepareSceneVulkan(double[] camera,float[] sunToward)
    {
        if(IsVulkan && camera!=null && camera.Length>=3)guestSceneCamera=camera;
        if(IsVulkan && !string.IsNullOrEmpty(assetFolder))GuestVolumeShadows.Prepare(camera,sunToward);
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
        HeldOwnedPass[] owned;
        KeyValuePair<string, bool>[] enabled;
        lock (Gate)
        {
            if (committed)
                return;
            choice = offered;
            committed = true;
            queued = Pending.ToArray();
            Pending.Clear();
            owned = PendingOwned.ToArray();
            PendingOwned.Clear();
            enabled = new KeyValuePair<string, bool>[PendingEnabled.Count];
            var n = 0;
            foreach (var pair in PendingEnabled)
                enabled[n++] = pair;
            PendingEnabled.Clear();
        }

        SceneView.BackendName = BackendName(choice);
        if (choice == 0)
        {
            LoadDx11(queued, owned, enabled);
            GuestStageBindings.BindDx11();
            PortablePassRegistry.BindDx11();
            global::ClientPlugin.Buffers.BufferCatalog.ApplyDeferred();
            return;
        }

        global::ClientPlugin.Buffers.BufferCatalog.ApplyDeferred();
        SetStatus("Renderer: Vulkan | " + Summary() + " | guest listener " + (listenersAttached ? "attached" : "missing"));
    }

    static void LoadDx11(string[][] queued, HeldOwnedPass[] owned, KeyValuePair<string, bool>[] enabled)
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
                var assembly = SatelliteAssembly.Load(path, typeof(RenderHost).Assembly);
                Dx11Call.Bind(assembly);
                global::ClientPlugin.Buffers.BufferCatalog.BindDx11Catalog();
                Dx11Call.Call("ClientPlugin.Plugin", "InstallFramework", gameInstance);
            }
            catch (Exception e)
            {
                SetStatus("Renderer: DX11 | " + Summary() + " | Anomaly.Dx11.dll failed (" + path + "): " + e.GetType().Name + ": " + e.GetBaseException().Message);
                return;
            }
        }

        foreach (var item in queued)
            Dx11Call.Call("ClientPlugin.Shaders.ShaderPackRegistry", "Register", item[0], item[1]);
        // Programs exist first, then checkbox state, then the callbacks that
        // keep writing that state. A Replace left at the json default paints
        // over the lit scene.
        foreach (var pair in enabled)
            Dx11Call.Call("ClientPlugin.Shaders.FullscreenPassRegistry", "SetEnabled", pair.Key, pair.Value);
        foreach (var pass in owned)
            Dx11Call.Call("ClientPlugin.Shaders.OwnedPassRegistry", "Register",
                pass.Id, pass.Slot, pass.Priority, pass.Policy, pass.Draw, pass.Phase);
        if (!string.IsNullOrEmpty(assetFolder))
            CallPlugin("LoadAssets", assetFolder);
        global::ClientPlugin.Shaders.VolumetricMediumRegistry.ApplyDeferred();
        SetStatus("Renderer: DX11 | " + Summary() + " | guest listener " +
                  (listenersAttached ? "attached" : "not required") +
                  " | Anomaly.Dx11 loaded | held passes=" + owned.Length + " enabled=" + enabled.Length);
    }

    sealed class HeldOwnedPass
    {
        public string Id;
        public string Slot;
        public int Priority;
        public int Policy;
        public Action<object> Draw;
        public int Phase;
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

    static void PullGuestScene()
    {
        // SceneView carries the absolute camera, as DX11 SceneSnapshot does. The guest
        // frame camera-to-world translation is the fallback for hosts without the scene
        // listener. sun_toward has the opposite convention to SceneView.SunLightDirection.
        var camera = guestSceneCamera;
        SceneView.Set((int)GuestFrame.U32(8), (int)GuestFrame.U32(12),
            camera?[0] ?? GuestFrame.F32(312), camera?[1] ?? GuestFrame.F32(316), camera?[2] ?? GuestFrame.F32(320),
            -GuestFrame.F32(88), -GuestFrame.F32(92), -GuestFrame.F32(96));
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
