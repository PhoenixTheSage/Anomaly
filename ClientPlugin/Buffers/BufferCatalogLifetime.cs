using System;
using System.Collections.Generic;
using VRage.Utils;

namespace ClientPlugin.Buffers;

/// <summary>
/// Pack resource lifetime. Resolve
/// <c>ClientPlugin.Buffers.BufferCatalog</c> <c>RegisterLifetime</c>;
/// this type is internal. Anomaly calls the callbacks on DRS / device end
/// so packs drop the <c>OnDeviceReset</c> Harmony patch.
/// </summary>
static class BufferCatalogLifetime
{
    static readonly object Gate = new();
    static readonly Dictionary<string, Lifetime> ByPack =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void Register(string packId, Action onResolutionChanged, Action onDeviceEnd)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return;
        lock (Gate)
        {
            ByPack[packId.Trim()] = new Lifetime
            {
                OnResolutionChanged = onResolutionChanged,
                OnDeviceEnd = onDeviceEnd
            };
        }
    }

    internal static void Unregister(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return;
        lock (Gate)
            ByPack.Remove(packId.Trim());
    }

    internal static void NotifyResolutionChanged()
    {
        Action[] callbacks;
        lock (Gate)
        {
            callbacks = new Action[ByPack.Count];
            var i = 0;
            foreach (var kv in ByPack)
                callbacks[i++] = kv.Value.OnResolutionChanged;
        }

        Invoke(callbacks, "resolution");
    }

    internal static void NotifyDeviceEnd()
    {
        Action[] callbacks;
        lock (Gate)
        {
            callbacks = new Action[ByPack.Count];
            var i = 0;
            foreach (var kv in ByPack)
                callbacks[i++] = kv.Value.OnDeviceEnd;
        }

        Invoke(callbacks, "device-end");
    }

    static void Invoke(Action[] callbacks, string reason)
    {
        for (var i = 0; i < callbacks.Length; i++)
        {
            var cb = callbacks[i];
            if (cb == null)
                continue;
            try
            {
                cb();
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine("Anomaly catalog lifetime (" + reason + "): " +
                                        e.GetType().Name + ": " + e.Message);
            }
        }
    }

    sealed class Lifetime
    {
        public Action OnResolutionChanged;
        public Action OnDeviceEnd;
    }
}
