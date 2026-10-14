using System;
using VRageMath;

namespace ClientPlugin.Backend;

/// <summary>
/// Last renderer frame published for the fullscreen extras constant buffer.
/// </summary>
static class GuestFrame
{
    public const int BytesLength = 344;
    static readonly byte[] Bytes = new byte[BytesLength];
    static int tick = int.MinValue;
    static bool have;
    static uint dropUntil;

    public static bool Open
    {
        get
        {
            if (!RendererRoute.UseGuestImages)
                return false;
            int now = Environment.TickCount;
            if (now != tick)
            {
                tick = now;
                have = GuestBridgeClient.TryGetFrame(Bytes);
            }
            return have;
        }
    }

    public static void Invalidate()
    {
        dropUntil = have ? U32(16) + 1 : 1;
        GuestBridgeClient.InvalidateHistory();
    }

    public static uint U32(int offset) => BitConverter.ToUInt32(Bytes, offset);

    public static float F32(int offset) => BitConverter.ToSingle(Bytes, offset);

    public static Vector3 Vec3(int offset) => new Vector3(F32(offset), F32(offset + 4), F32(offset + 8));

    public static Matrix MatrixAt(int offset)
    {
        return new Matrix(
            F32(offset), F32(offset + 4), F32(offset + 8), F32(offset + 12),
            F32(offset + 16), F32(offset + 20), F32(offset + 24), F32(offset + 28),
            F32(offset + 32), F32(offset + 36), F32(offset + 40), F32(offset + 44),
            F32(offset + 48), F32(offset + 52), F32(offset + 56), F32(offset + 60));
    }

    public static bool History => U32(20) != 0 && U32(16) >= dropUntil;
}
