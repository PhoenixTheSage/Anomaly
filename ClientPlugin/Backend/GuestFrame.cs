using System;
using VRageMath;

namespace ClientPlugin.Backend;

/// <summary>
/// Last renderer frame published for the fullscreen extras constant buffer.
/// </summary>
static class GuestFrame
{
    public const int BytesLength = 344;
    static byte[] Bytes = new byte[BytesLength];
    static bool have;
    static uint dropUntil;
    static readonly FrameEnvironment.Motion Motion = new FrameEnvironment.Motion();
    internal static void ResetDevice(){have=false;dropUntil=0;Bytes=new byte[BytesLength];Motion.Reset();}

    // Only the render callback captures data. Property reads share this snapshot
    // until the next callback, regardless of elapsed wall-clock milliseconds.
    public static bool Open => RendererRoute.UseGuestImages && have;
    internal static bool Capture()
    {
        if (!RendererRoute.UseGuestImages) return false;
        var next = new byte[BytesLength];
        if (!GuestBridgeClient.ReadNativeFrame(next)) { have=false; return false; }
        uint frame = BitConverter.ToUInt32(next,16);
        if (have && U32(16)==frame) return true;
        var inputs = GuestBridgeClient.CaptureFrameInputs();
        if (inputs != null && inputs.Length==2 && inputs[0] is double[] camera && camera.Length==3 && inputs[1] is float[] light && light.Length==8)
        {
            var lighting = new FrameEnvironment.Lighting(new Vector3(light[0],light[1],light[2]),
                new Vector3(light[4],light[5],light[6]),light[3],light[7]);
            var rotation = new Matrix(
                BitConverter.ToSingle(next,264),BitConverter.ToSingle(next,268),BitConverter.ToSingle(next,272),0,
                BitConverter.ToSingle(next,280),BitConverter.ToSingle(next,284),BitConverter.ToSingle(next,288),0,
                BitConverter.ToSingle(next,296),BitConverter.ToSingle(next,300),BitConverter.ToSingle(next,304),0,0,0,0,1);
            float safety = Motion.Capture(new Vector3D(camera[0],camera[1],camera[2]),rotation);
            var planet = global::ClientPlugin.Shaders.PlanetAtmosphere.Copy();
            void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,next,at,4);
            void V(int at,Vector3 value){F(at,value.X);F(at+4,value.Y);F(at+8,value.Z);}
            F(32,safety);F(36,lighting.SunDiffuse);F(40,lighting.SkyLuma);
            bool planetValid=planet.IsValid && planet.VisualCeil>1f;
            F(44,planetValid?planet.AirTop:0f);F(48,planetValid?planet.VisualCeil:0f);F(52,.38f);F(56,6f);
            V(72,lighting.SunColor);V(88,lighting.SunToward);V(104,lighting.SkyAmbient);V(120,Motion.Delta);
            if (!GuestBridgeClient.SupportsPacketFlags(32768))
                throw new NotSupportedException("Update SE-Vulkan native library: shared frame environment requires guest feature bit15.");
            var packet=new byte[96];
            Buffer.BlockCopy(new[]{96,1},0,packet,0,8);Buffer.BlockCopy(BitConverter.GetBytes(frame),0,packet,8,4);
            Buffer.BlockCopy(next,72,packet,16,12);Buffer.BlockCopy(next,36,packet,28,4);
            Buffer.BlockCopy(next,88,packet,32,12);Buffer.BlockCopy(next,40,packet,44,4);
            Buffer.BlockCopy(next,104,packet,48,12);Buffer.BlockCopy(next,32,packet,60,4);
            Buffer.BlockCopy(next,44,packet,64,16);Buffer.BlockCopy(next,120,packet,80,12);
            if (!GuestBridgeClient.SetFrameEnvironment(packet)) throw new InvalidOperationException("SE-Vulkan refused current frame environment.");
        }
        Bytes=next;have=true;return true;
    }

    internal static bool TryCopy(byte[] destination)
    {
        if (!Open || destination == null || destination.Length < BytesLength) return false;
        Buffer.BlockCopy(Bytes,0,destination,0,BytesLength);return true;
    }

    public static void Invalidate()
    {
        dropUntil = have ? U32(16) + 1 : 1;
        GuestBridgeClient.InvalidateHistory();
        Motion.Reset();
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
