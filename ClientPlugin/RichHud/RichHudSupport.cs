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
                return "registered";
            if (initAttempted)
                return "waiting (Master not in world)";
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
            sessionToken = session;
            initAttempted = false;
            ready = false;
            initFailed = false;
        }

        if (initAttempted)
            return;

        if (RichHudCore.Instance == null)
            return;

        initAttempted = true;

        try
        {
            RichHudClient.Init(Plugin.Name, OnReady, OnReset);
            MyLog.Default.WriteLine("Anomaly Rich HUD client handshake started.");
            DebugLog.Write("RichHudClient handshake started");
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
        MyLog.Default.WriteLine("Anomaly Rich HUD client registered.");
        DebugLog.Write("RichHudClient registered");
    }

    static void OnReset()
    {
        ready = false;
        DebugLog.Write("RichHudClient reset");
    }
}
