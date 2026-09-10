using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using SharpDX;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Last-N GPU-submit breadcrumbs. <see cref="Begin"/> / <see cref="End"/> are
/// a few stores (no I/O, no per-frame strings). The ring freezes and dumps to
/// SpaceEngineers.log on the first lost-device / DrawGameScene unwind so the
/// next TDR names what Anomaly queued, not only Keen's Present wrapper.
/// Well-known type for packs: <c>ClientPlugin.ShaderFramework.RenderTrace</c>.
/// </summary>
public static class RenderTrace
{
    public const int Capacity = 64;

    const int DxgiDeviceRemoved = unchecked((int)0x887A0005);
    const int DxgiDeviceHung = unchecked((int)0x887A0006);
    const int DxgiDeviceReset = unchecked((int)0x887A0007);
    const int DxgiDriverInternal = unchecked((int)0x887A0020);

    static readonly string[] Names = new string[Capacity];
    static readonly int[] Ticks = new int[Capacity];
    static readonly byte[] Phases = new byte[Capacity];
    static int cursor;
    static int frozen;
    static int dumped;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Begin(string name) => Push(name, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void End(string name) => Push(name, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Note(string name) => Push(name, 2);

    public static bool DeviceAlreadyLost()
    {
        try
        {
            var device = MyRender11.DeviceInstance;
            if (device == null)
                return false;
            return IsLostHresult(device.DeviceRemovedReason.Code);
        }
        catch (Exception e)
        {
            return IsLostDevice(e);
        }
    }

    public static bool IsLostDevice(Exception e)
    {
        while (e != null)
        {
            if (IsLostHresult(e.HResult))
                return true;
            if (e is SharpDXException sx && IsLostHresult(sx.ResultCode.Code))
                return true;
            if (MessageLooksLost(e.Message))
                return true;
            if (e is AggregateException agg)
            {
                var inners = agg.InnerExceptions;
                for (var i = 0; i < inners.Count; i++)
                {
                    if (IsLostDevice(inners[i]))
                        return true;
                }
            }

            e = e.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Fail-path only. Does not freeze the ring for ordinary compile/bind errors.
    /// </summary>
    public static void DumpIfLost(string where, Exception e)
    {
        if ((e != null && IsLostDevice(e)) || DeviceAlreadyLost())
            Dump(where, e);
    }

    /// <summary>
    /// Writes the ring once. Safe from any thread. Further Begin/End no-ops
    /// after freeze so retry frames cannot wipe the pre-TDR trail.
    /// </summary>
    public static void Dump(string where, Exception e)
    {
        if (Interlocked.CompareExchange(ref dumped, 1, 0) != 0)
            return;
        Volatile.Write(ref frozen, 1);

        string line;
        try
        {
            line = FormatDump(where, e);
        }
        catch (Exception format)
        {
            line = "Anomaly RenderTrace dump failed: " + format.Message;
        }

        try
        {
            MyLog.Default.WriteLine(line);
        }
        catch
        {
            // ignored
        }

        try
        {
            DebugLog.Write(line);
        }
        catch
        {
            // ignored
        }
    }

    static void Push(string name, byte phase)
    {
        if (name == null || Volatile.Read(ref frozen) != 0)
            return;
        var i = Interlocked.Increment(ref cursor) & (Capacity - 1);
        Names[i] = name;
        Ticks[i] = Environment.TickCount;
        Volatile.Write(ref Phases[i], phase);
    }

    static string FormatDump(string where, Exception e)
    {
        var sb = new StringBuilder(1024);
        sb.Append("Anomaly RenderTrace dump at ").Append(where ?? "?");
        sb.Append(" | ").Append(FormatDeviceReason());
        if (e != null)
        {
            sb.Append(" | ").Append(e.GetType().Name).Append(": ").Append(e.Message);
            if (e is SharpDXException sx)
                sb.Append(" hresult=0x").Append(sx.ResultCode.Code.ToString("X8"));
        }

        sb.Append(" | trail (oldest→newest, >begin <end *note): ");
        var end = Volatile.Read(ref cursor);
        var start = end - Capacity + 1;
        if (start < 1)
            start = 1;
        var first = true;
        for (var seq = start; seq <= end; seq++)
        {
            var i = seq & (Capacity - 1);
            var name = Names[i];
            if (string.IsNullOrEmpty(name))
                continue;
            if (!first)
                sb.Append(" | ");
            first = false;
            sb.Append(name);
            var phase = Phases[i];
            if (phase == 0)
                sb.Append('>');
            else if (phase == 1)
                sb.Append('<');
            else
                sb.Append('*');
        }

        if (first)
            sb.Append("(empty)");
        return sb.ToString();
    }

    static string FormatDeviceReason()
    {
        try
        {
            var device = MyRender11.DeviceInstance;
            if (device == null)
                return "DeviceRemovedReason=null";
            var code = device.DeviceRemovedReason.Code;
            return "DeviceRemovedReason=0x" + code.ToString("X8") + " " + ReasonName(code);
        }
        catch (Exception e)
        {
            return "DeviceRemovedReason lookup: " + e.Message;
        }
    }

    static bool MessageLooksLost(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;
        return message.IndexOf("887A0005", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("887A0006", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("887A0007", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("DEVICE_REMOVED", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("DEVICE_HUNG", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsLostHresult(int code)
    {
        return code == DxgiDeviceRemoved || code == DxgiDeviceHung ||
               code == DxgiDeviceReset || code == DxgiDriverInternal;
    }

    static string ReasonName(int code)
    {
        if (code == 0)
            return "OK";
        if (code == DxgiDeviceRemoved)
            return "DEVICE_REMOVED";
        if (code == DxgiDeviceHung)
            return "DEVICE_HUNG";
        if (code == DxgiDeviceReset)
            return "DEVICE_RESET";
        if (code == DxgiDriverInternal)
            return "DRIVER_INTERNAL_ERROR";
        return "other";
    }
}
