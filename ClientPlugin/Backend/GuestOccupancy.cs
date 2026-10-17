using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Buffers;

namespace ClientPlugin.Backend;

// Shared occupancy policy stays in Anomaly. The renderer supplies copied scene
// inputs and executes ordinary guest packets; it does not own a second registry.
static class GuestOccupancy
{
    public const string SplatId = "anomaly.owned.Occupancy";
    public const string StampId = "anomaly.owned.OccupancyStamp";
    const int Dim = 64, AtlasSize = 512, MaxBoxes = 256;
    const int RequiredFlags = GuestBridgeClient.Compute | GuestBridgeClient.ClearCompute | GuestBridgeClient.PacketConstants;
    static readonly object Gate = new object();
    static bool requested;
    static int pointCap, pointFace = 128;
    static ulong boxBuffer;
    static readonly HashSet<uint> PreviousLights = new HashSet<uint>();
    internal static void ResetDevice(){boxBuffer=0;PreviousLights.Clear();SelectedLights=Array.Empty<uint>();SelectedLightRecords=Array.Empty<double>();LastBoxCount=0;}
    public static uint LocalActorId;
    public static int LastBoxCount { get; private set; }
    public static uint[] SelectedLights { get; private set; } = Array.Empty<uint>();
    public static double[] SelectedLightRecords { get; private set; } = Array.Empty<double>();
    public static string StatusLine => !Requested ? "not requested" : BufferCatalog.Active("occupancy") == null ? "unavailable"
        : "512x512 boxes=" + LastBoxCount + " lights=" + SelectedLights.Length;
    public static bool Requested { get { lock (Gate) return requested; } }
    public static int PointCap { get { lock (Gate) return pointCap; } }
    public static int PointFace { get { lock (Gate) return pointFace; } }

    public static void Request(bool enabled) { lock (Gate) requested = enabled; }
    public static void RequestPointShadows(int cap, int face)
    {
        lock (Gate)
        {
            pointCap = Math.Min(64, Math.Max(0, cap));
            if (cap > 0) pointFace = Math.Min(1024, Math.Max(32, face <= 0 ? 128 : face));
        }
    }

    public static void Register(string shaders)
    {
        GuestPassList.AddProgram(SplatId, "AfterLighting", Path.Combine(shaders, "OccupancySplat.hlsl"), "PublishOnly");
        GuestPassList.SetFlags(SplatId, RequiredFlags, 2);
        GuestPassList.AddProgram(StampId, "AfterLighting", Path.Combine(shaders, "OccupancyStamp.hlsl"), "PublishOnly");
        GuestPassList.SetFlags(StampId, GuestBridgeClient.Compute | GuestBridgeClient.PacketConstants, 2);
    }

    public static bool Handles(GuestProgram program, IReadOnlyList<GuestProgram> programs)
    {
        if (program.Id == StampId) return true; // Submitted with the splat atomically.
        if (program.Id != SplatId) return false;
        BufferCatalog.Set("occupancy", null);
        LastBoxCount = 0;
        if (!Requested || !program.Enabled)
        {
            if (program.Pipeline != 0) { program.Status = "pipeline"; program.Error = null; }
            return true;
        }
        GuestProgram stamp = null;
        bool haveCurrentDepth = false;
        foreach (var p in programs)
        {
            if (p == null) continue;
            if (p.Id == StampId) stamp = p;
            if (p.Id == "anomaly.owned.LinearDepth" && p.SubmittedThisFrame) haveCurrentDepth = true;
        }
        if (program.Pipeline == 0 || stamp == null || stamp.Pipeline == 0) return true;
        if (!GuestBridgeClient.SupportsPacketFlags(RequiredFlags))
        {
            program.Status = "capability-missing";
            program.Error = "SE-Vulkan native guest lacks occupancy clear/custom constants; update SE-Vulkan";
            return true;
        }
        if (!haveCurrentDepth) { program.Error = "occupancy requires current-frame linear depth"; return true; }
        var frame = new byte[344];
        if (!GuestBridgeClient.TryGetFrame(frame)) return true;
        int width = (int)BitConverter.ToUInt32(frame, 8), height = (int)BitConverter.ToUInt32(frame, 12);
        ulong depth = GuestBridgeClient.Catalog("linearDepth");
        if (width <= 0 || height <= 0 || depth == 0) return true;
        int cap, face;
        lock (Gate) { cap = pointCap; face = pointFace; }
        int effective = Math.Min(cap, Math.Min(16383 / face, (96 * 1024 * 1024) / (6 * face * face * 16)));
        float[] boxes = Array.Empty<float>();
        if (effective > 0)
        {
            var inputs = GuestBridgeClient.CaptureShadowInputs(LocalActorId);
            if (inputs == null) { program.Error = "SE-Vulkan scene snapshot unavailable"; return true; }
            boxes = SelectBoxes(inputs, effective, LocalActorId);
        }
        else { SelectedLights = Array.Empty<uint>(); PreviousLights.Clear(); }
        LastBoxCount = boxes.Length / 8;
        byte[] constants = Constants(frame, width, height, LastBoxCount);
        if (program.StorageImage == 0)
            program.StorageImage = GuestBridgeClient.AllocateImage(AtlasSize, AtlasSize, 1, null, 2);
        program.ImageWidth = program.ImageHeight = AtlasSize;
        var sampled = new ulong[14]; sampled[0] = depth;
        GuestBridgeClient.SubmitPacket(program.Mark, 5, width, height, program.Pipeline, program.StorageImage, constants, sampled, RequiredFlags);
        program.Submissions++;
        program.SubmittedThisFrame = true;
        if (LastBoxCount > 0)
        {
            // Fixed capacity avoids recreating descriptors; native uploads copy
            // into the fence-protected slot that will execute this frame.
            if (boxBuffer == 0) boxBuffer = GuestBridgeClient.AllocateImage(MaxBoxes * 32, 1, 1, null, 4);
            var bytes = new byte[boxes.Length * 4]; Buffer.BlockCopy(boxes, 0, bytes, 0, bytes.Length);
            GuestBridgeClient.UpdateImage(boxBuffer, bytes);
            sampled = new ulong[14]; sampled[0] = boxBuffer;
            GuestBridgeClient.SubmitPacket(program.Mark, 5, LastBoxCount, 1, stamp.Pipeline, program.StorageImage, constants, sampled,
                GuestBridgeClient.Compute | GuestBridgeClient.PacketConstants);
            stamp.Submissions++;
            stamp.SubmittedThisFrame = true;
        }
        program.Status = "pipeline"; program.Error = null;
        BufferCatalog.Set("occupancy", new GuestRendererImage(program.StorageImage, AtlasSize, AtlasSize, 1));
        return true;
    }

    internal static byte[] Constants(byte[] frame, int width, int height, int boxCount)
    {
        var result = new byte[256];
        void F(int offset, float value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, result, offset, 4);
        void U(int offset, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, result, offset, 4);
        F(0, 1f / width); F(4, 1f / height);
        Buffer.BlockCopy(frame, 60, result, 8, 8); // Projection scales.
        Buffer.BlockCopy(frame, 24, result, 16, 8); // Jitter.
        U(24, Dim); U(28, BitConverter.ToUInt32(frame, 20));
        F(32, -64); F(36, -64); F(40, -64); F(44, 2); U(48, (uint)boxCount);
        Buffer.BlockCopy(frame, 264, result, 64, 48); // Camera-to-world rows.
        Buffer.BlockCopy(frame, 200, result, 112, 64);
        return result;
    }

    internal static float[] SelectBoxes(object[] inputs, int cap, uint localActor)
    {
        if (inputs.Length < 5 || inputs[0] is not double[] lights || inputs[1] is not uint[] lightIds ||
            inputs[2] is not double[] bounds || inputs[3] is not uint[] actorIds || inputs[4] is not double[] anchor ||
            lights.Length != lightIds.Length * 4 || bounds.Length != actorIds.Length * 6 || anchor.Length != 6)
            throw new InvalidOperationException("invalid guest shadow scene snapshot");
        var order = new List<int>(); for (int i = 0; i < lightIds.Length; i++) order.Add(i);
        double Rank(int i)
        {
            int at = i * 4; double x = lights[at] - anchor[0], y = lights[at + 1] - anchor[1], z = lights[at + 2] - anchor[2];
            double d = x*x + y*y + z*z;
            if (d > lights[at + 3] * lights[at + 3]) d += 1e12;
            return PreviousLights.Contains(lightIds[i]) ? d * .81 : d;
        }
        order.Sort((a, b) => { int compare = Rank(a).CompareTo(Rank(b));
            if (compare == 0) compare = lights[b*4+3].CompareTo(lights[a*4+3]);
            return compare != 0 ? compare : lightIds[a].CompareTo(lightIds[b]); });
        var output = new List<float>(); var selected = new List<uint>(); var records = new List<double>();
        PreviousLights.Clear();
        for (int light = 0; light < Math.Min(cap, order.Count); light++)
        {
            int index = order[light], at = index * 4;
            uint id = lightIds[index]; selected.Add(id); PreviousLights.Add(id);
            for (int component = 0; component < 4; component++) records.Add(lights[at + component]);
            int added = 0;
            for (int actor = 0; actor < actorIds.Length && added < 48 && output.Count / 8 < MaxBoxes; actor++)
            {
                if (actorIds[actor] == localActor || actorIds[actor] == id) continue;
                int a = actor * 6;
                double hx = bounds[a+3], hy = bounds[a+4], hz = bounds[a+5], size = hx*hx + hy*hy + hz*hz;
                if (!(size >= 1e-4 && size <= 80*80) || hx < 0 || hy < 0 || hz < 0) continue;
                double x = Math.Max(Math.Abs(bounds[a] - lights[at]) - hx, 0),
                    y = Math.Max(Math.Abs(bounds[a+1] - lights[at+1]) - hy, 0), z = Math.Max(Math.Abs(bounds[a+2] - lights[at+2]) - hz, 0);
                if (x*x + y*y + z*z > lights[at+3]*lights[at+3]) continue;
                output.Add((float)(bounds[a] - anchor[3])); output.Add((float)(bounds[a+1] - anchor[4])); output.Add((float)(bounds[a+2] - anchor[5])); output.Add(0);
                output.Add((float)hx); output.Add((float)hy); output.Add((float)hz); output.Add(0); added++;
            }
        }
        SelectedLights = selected.ToArray();
        SelectedLightRecords = records.ToArray();
        return output.ToArray();
    }
}
