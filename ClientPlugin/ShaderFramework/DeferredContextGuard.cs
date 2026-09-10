using System;
using System.Reflection;
using System.Threading;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.RenderContext;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// AfterLighting / AfterAtmosphere / AfterTransparent record on Keen's
/// transparent deferred worker. Pack C# that reads <c>MyRender11.RC</c>
/// or <c>Device.ImmediateContext</c> from those callbacks is redirected
/// to the slot <c>rc</c> so the immediate context is not raced.
/// </summary>
static class DeferredContextGuard
{
    [ThreadStatic] static int depth;
    [ThreadStatic] static MyRenderContext slotRc;
    [ThreadStatic] static OwnedPassSlot slot;
    [ThreadStatic] static bool redirecting;

    static readonly FieldInfo ImmediateField = AccessTools.Field(typeof(MyRender11), "m_rc");
    static readonly NopDisposable Nop = new();
    static int warned;

    internal static bool IsWorkerSlot(OwnedPassSlot ownedSlot)
    {
        return ownedSlot == OwnedPassSlot.AfterLighting ||
               ownedSlot == OwnedPassSlot.AfterAtmosphere ||
               ownedSlot == OwnedPassSlot.AfterTransparent;
    }

    internal static IDisposable Push(OwnedPassSlot ownedSlot, MyRenderContext rc)
    {
        if (!IsWorkerSlot(ownedSlot) || rc == null || !rc.IsInitialized)
            return Nop;

        var immediate = ImmediateField?.GetValue(null) as MyRenderContext;
        depth++;
        slotRc = rc;
        slot = ownedSlot;
        redirecting = immediate != null && !ReferenceEquals(rc, immediate);
        return new Scope();
    }

    internal static bool TryRedirect(out MyRenderContext rc, out bool steal)
    {
        if (depth > 0 && slotRc != null && slotRc.IsInitialized)
        {
            rc = slotRc;
            steal = redirecting;
            return true;
        }

        rc = null;
        steal = false;
        return false;
    }

    internal static bool TryRedirectDeviceContext(out SharpDX.Direct3D11.DeviceContext context, out bool steal)
    {
        if (TryRedirect(out var rc, out steal) && rc.DeviceContext != null)
        {
            context = rc.DeviceContext;
            return true;
        }

        context = null;
        steal = false;
        return false;
    }

    internal static void Warn(string api)
    {
        if (Interlocked.Exchange(ref warned, 1) != 0)
            return;
        var message = "Anomaly: redirected " + api + " to the " + slot +
                      " deferred rc. Use OwnedPassContext.Rc — MyRender11.RC is illegal on this worker.";
        MyLog.Default.WriteLine(message);
        DebugLog.Write(message);
    }

    static void Pop()
    {
        depth--;
        if (depth > 0)
            return;
        depth = 0;
        slotRc = null;
        redirecting = false;
    }

    sealed class Scope : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Pop();
        }
    }

    sealed class NopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
