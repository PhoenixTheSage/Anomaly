using System;
using System.Collections;
using System.Reflection;
using RichHudFramework.Client;
using RichHudFramework.Internal;
using RichHudFramework.UI;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ClientPlugin.RichHud;

/// <summary>
/// Optional handshake with Rich HUD Master (workshop 1965654081).
/// <see cref="RichHudCore"/>, <see cref="ExceptionHandler"/>, and
/// <see cref="CmdManager"/> are session components. Keen scans assemblies when
/// the world loads; dynamically compiled plugins may arrive after that scan, so
/// this shared host registers them on the live session on either renderer. If Master is not in the world,
/// <see cref="RichHudClient.Registered"/> stays false and the Pulsar MyGui
/// dialog remains the settings UI.
/// </summary>
public static class RichHudSupport
{
    public const string MasterWorkshopId = "1965654081";

    static bool initAttempted;
    static bool ready;
    static bool initFailed;
    static bool hostFailed;
    static object sessionToken;
    static ExceptionHandler hostedHandler;
    static RichHudCore hostedCore;
    static CmdManager hostedCommands;
    static RichHudSession hostedHook;

    public static bool Available => ready && RichHudClient.Registered;

    public static string StatusLine
    {
        get
        {
            if (MySession.Static == null)
                return "idle (no session)";
            if (initFailed)
                return "unavailable (init failed)";
            if (hostFailed && !HostIsLive())
                return "unavailable (session host)";
            if (Available)
                return "registered  " + TerminalConfigRegistry.StatusLine + "  " + HudOverlayRegistry.StatusLine;
            if (initAttempted)
            {
                if (TerminalConfigRegistry.PageCount > 0)
                    return "waiting (Master handshake)  queued " + TerminalConfigRegistry.StatusLine;
                return "waiting (Master handshake)";
            }
            return "idle";
        }
    }

    public static void TryInitialize()
    {
        var session = (object)MySession.Static;
        if (session == null)
            return;

        if (!ReferenceEquals(session, sessionToken))
        {
            TerminalConfigRegistry.Unmount();
            HudOverlayRegistry.Unmount();
            InteractiveOverlayRegistry.Unmount();
            sessionToken = session;
            initAttempted = false;
            ready = false;
            initFailed = false;
            hostFailed = false;
            if (RichHudCore.Instance != null && !HostIsLive())
                ReleaseStaleHost();
        }

        if (ExceptionHandler.ExceptionReported == null)
        {
            ExceptionHandler.ExceptionReported = e =>
            {
                DebugLog.Write("RHF exception: " + e);
                try
                {
                    MyLog.Default.WriteLine("[Anomaly Shaders] RHF exception: " + e);
                }
                catch
                {
                    // ignored
                }
            };
        }

        if (RichHudClient.Registered)
        {
            initAttempted = true;
            EnsureMounted();
            return;
        }

        // RemoteReset / ExceptionHandler.Close nulls RichHudClient.Instance.
        // Official Init is a no-op only while that singleton still exists; after
        // Close we must handshake again. Do not wait for a world reload.
        if (ready)
        {
            ready = false;
            TerminalConfigRegistry.Unmount();
            HudOverlayRegistry.Unmount();
            InteractiveOverlayRegistry.Unmount();
            DebugLog.Write("RichHudClient lost Master; will re-handshake");
        }

        if (!HostIsLive())
        {
            if (!hostFailed)
                AttachSessionHost();
            if (!HostIsLive())
                return;
        }

        if (initFailed)
            return;

        try
        {
            if (!initAttempted)
            {
                initAttempted = true;
                RichHudClient.Init(TerminalConfigRegistry.RootName, OnReady, OnReset);
                MyLog.Default.WriteLine("Anomaly Rich HUD client handshake started.");
                DebugLog.Write("RichHudClient handshake started");
            }
            else
            {
                // Instance is null after Close — Init creates a new client.
                RichHudClient.Init(TerminalConfigRegistry.RootName, OnReady, OnReset);
                RichHudClient.Pulse();
            }

            if (RichHudClient.Registered)
                OnReady();
        }
        catch (Exception e)
        {
            initFailed = true;
            MyLog.Default.WriteLine("Anomaly Rich HUD client init failed: " + e.Message);
            DebugLog.Write("RichHudClient init failed: " + e);
        }
    }

    public static void Shutdown()
    {
        TerminalConfigRegistry.Unmount();
        HudOverlayRegistry.Unmount();
        InteractiveOverlayRegistry.Unmount();
        try
        {
            RichHudClient.Reset();
        }
        catch
        {
            // Master absent or already torn down.
        }

        ready = false;
        initAttempted = false;
        initFailed = false;
    }

    static bool HostIsLive()
    {
        RichHudCore core = RichHudCore.Instance;
        MySession live = MySession.Static;
        if (core == null || live == null)
            return false;
        // RegisterComponent stores the MySession instance. MyAPIGateway.Session
        // can be a different object, so compare with MySession.Static.
        if (ReferenceEquals(core.Session, live))
            return true;
        return hostedCore != null && ReferenceEquals(hostedCore, core) && core.Session != null
            && ReferenceEquals(sessionToken, live);
    }

    /// <summary>
    /// Keen already ran <c>RegisterComponentsFromAssembly</c> before this
    /// satellite existed. Register the same descriptors on the live session
    /// and put them on <c>m_loadOrder</c> so world unload still calls
    /// <c>UnloadData</c>.
    /// </summary>
    static void AttachSessionHost()
    {
        IMySession session = MyAPIGateway.Session;
        if (session == null || HostIsLive())
            return;

        try
        {
            hostedHandler = new ExceptionHandler();
            Register(session, hostedHandler);
            hostedHandler.LoadData();
            hostedHandler.AfterLoadData();

            hostedCore = new RichHudCore();
            Register(session, hostedCore);
            hostedCore.ManualStart();
            hostedCore.AfterLoadData();

            hostedCommands = new CmdManager();
            Register(session, hostedCommands);
            hostedCommands.LoadData();
            hostedCommands.AfterLoadData();

            hostedHook = new RichHudSession();
            Register(session, hostedHook);
            hostedHook.LoadData();
            hostedHook.AfterLoadData();

            MyLog.Default.WriteLine("Anomaly Rich HUD session host attached.");
            DebugLog.Write("Rich HUD session host attached");
        }
        catch (Exception e)
        {
            hostFailed = true;
            MyLog.Default.WriteLine("Anomaly Rich HUD session host failed: " + e.Message);
            DebugLog.Write("Rich HUD session host failed: " + e);
            ReleaseStaleHost();
        }
    }

    static void Register(IMySession session, MySessionComponentBase component)
    {
        session.RegisterComponent(component, component.UpdateOrder, component.Priority);
        RememberForUnload(session, component);
    }

    static void RememberForUnload(object session, MySessionComponentBase component)
    {
        FieldInfo field = FindField(session.GetType(), "m_loadOrder");
        IList list = field?.GetValue(session) as IList;
        if (list == null || list.Contains(component))
            return;
        list.Add(component);
    }

    static void ReleaseStaleHost()
    {
        UnloadComponent(hostedHook);
        UnloadComponent(hostedCommands);
        UnloadComponent(hostedHandler);
        UnloadComponent(hostedCore);
        if (RichHudCore.Instance != null && !ReferenceEquals(RichHudCore.Instance, hostedCore))
            UnloadComponent(RichHudCore.Instance);
        hostedHook = null;
        hostedCommands = null;
        hostedHandler = null;
        hostedCore = null;
    }

    static void UnloadComponent(MySessionComponentBase component)
    {
        if (component == null)
            return;
        try
        {
            if (component.Loaded)
                component.UnloadDataConditional();
            else
                InvokeUnloadData(component);
        }
        catch (Exception e)
        {
            DebugLog.Write("Rich HUD host unload failed: " + e.Message);
        }
    }

    static void InvokeUnloadData(MySessionComponentBase component)
    {
        MethodInfo method = FindMethod(component.GetType(), "UnloadData");
        method?.Invoke(component, null);
    }

    static FieldInfo FindField(Type type, string name)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
            type = type.BaseType;
        }

        return null;
    }

    static MethodInfo FindMethod(Type type, string name)
    {
        while (type != null)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (method != null)
                return method;
            type = type.BaseType;
        }

        return null;
    }

    static void OnReady()
    {
        ready = true;
        EnsureMounted();
        MyLog.Default.WriteLine("Anomaly Rich HUD client registered.");
        DebugLog.Write("RichHudClient registered");
    }

    static void OnReset()
    {
        ready = false;
        TerminalConfigRegistry.Unmount();
        HudOverlayRegistry.Unmount();
        InteractiveOverlayRegistry.Unmount();
        DebugLog.Write("RichHudClient reset");
    }

    static void EnsureMounted()
    {
        AnomalyTerminalPages.Install();
        TerminalConfigRegistry.Mount();
        HudOverlayRegistry.Mount();
        InteractiveOverlayRegistry.Mount();
    }
}
