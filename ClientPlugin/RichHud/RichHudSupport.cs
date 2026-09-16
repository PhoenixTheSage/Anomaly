using System;
using RichHudFramework.Client;
using RichHudFramework.Internal;
using Sandbox.Game.World;
using VRage.Utils;

namespace ClientPlugin.RichHud;

/// <summary>
/// Optional handshake with Rich HUD Master (workshop 1965654081).
/// <see cref="RichHudCore"/> is a <c>MySessionComponentDescriptor</c> the game
/// constructs from this assembly — do not new or tick it. If Master is not in
/// the world, <see cref="RichHudClient.Registered"/> stays false and the Pulsar
/// MyGui dialog remains the settings UI.
/// </summary>
public static class RichHudSupport
{
    public const string MasterWorkshopId = "1965654081";

    static bool initAttempted;
    static bool ready;
    static bool initFailed;
    static object sessionToken;

    public static bool Available => ready && RichHudClient.Registered;

    public static string StatusLine
    {
        get
        {
            if (MySession.Static == null)
                return "idle (no session)";
            if (initFailed)
                return "unavailable (init failed)";
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
            sessionToken = session;
            initAttempted = false;
            ready = false;
            initFailed = false;
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
            DebugLog.Write("RichHudClient lost Master; will re-handshake");
        }

        if (RichHudCore.Instance == null)
            return;

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
        sessionToken = null;
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
        DebugLog.Write("RichHudClient reset");
    }

    static void EnsureMounted()
    {
        AnomalyTerminalPages.Install();
        TerminalConfigRegistry.Mount();
        HudOverlayRegistry.Mount();
    }
}
