using System;
using System.Collections.Generic;
using System.IO;
using ClientPlugin.Buffers;
using VRageMath;

namespace ClientPlugin.Backend;

static class GuestPointShadows
{
    public const string GeometryId = "anomaly.owned.PointShadowGeometry", HeaderId = "anomaly.owned.PointShadowHeader";
    const int RequiredFlags = GuestBridgeClient.Geometry | GuestBridgeClient.Viewport | GuestBridgeClient.ClearFar | GuestBridgeClient.PacketConstants;
    static volatile bool worldBoxes;
    static ulong maskImage, meshBuffer, maskBuffer, boxBuffer, headerBuffer;
    static int maskWidth, maskHeight, meshBytes, maskBytes, boxBytes, headerBytes;
    internal static void ResetDevice(){maskImage=meshBuffer=maskBuffer=boxBuffer=headerBuffer=0;maskWidth=maskHeight=meshBytes=maskBytes=boxBytes=headerBytes=0;Status="device resources pending";}
    public static string Status { get; private set; } = "not requested";
    public static bool WorldBoxes => worldBoxes;
    public static void RequestWorldBoxes(bool enabled) => worldBoxes = enabled;

    public static void Register(string shaders)
    {
        string path = Path.Combine(shaders, "GuestShadowGeometry.hlsl");
        GuestPassList.AddProgram(GeometryId, "AfterLighting", path, "PublishOnly");
        foreach (var p in GuestPassList.Snapshot()) if (p.Id == GeometryId)
        { p.VertexFile = path; p.VertexEntry = "geometry_vertex"; p.Flags = GuestBridgeClient.Geometry | GuestBridgeClient.PacketConstants; p.Format = 6; }
        GuestPassList.AddProgram(HeaderId, "AfterLighting", Path.Combine(shaders, "GuestShadowHeader.hlsl"), "PublishOnly");
        GuestPassList.SetFlags(HeaderId, GuestBridgeClient.Viewport | GuestBridgeClient.PacketConstants, 6);
    }

    public static bool Handles(GuestProgram program, IReadOnlyList<GuestProgram> programs)
    {
        if (program.Id == HeaderId) return true;
        if (program.Id != GeometryId) return false;
        if (GuestOccupancy.PointCap == 0 || !program.Enabled)
        {
            Status = "not requested";
            if (program.Pipeline != 0) { program.Status = "pipeline"; program.Error = null; }
            return true;
        }
        try { Submit(program, programs); }
        catch (Exception error)
        {
            BufferCatalog.Set("pointShadowAtlas", null); BufferCatalog.Set("playerDepth", null);
            Status = program.Error = "point shadows unavailable: " + error.GetBaseException().Message;
        }
        return true;
    }

    static void Submit(GuestProgram program, IReadOnlyList<GuestProgram> programs)
    {
        GuestProgram header = null;
        foreach (var p in programs) if (p != null && p.Id == HeaderId) header = p;
        if (program.Pipeline == 0 || header == null || header.Pipeline == 0) { Status = "pipelines pending"; return; }
        if (!GuestBridgeClient.SupportsPacketFlags(RequiredFlags))
        { program.Status = "capability-missing"; Status = program.Error = "SE-Vulkan guest geometry/FP32 atlas support missing; update SE-Vulkan"; return; }
        var frame = new byte[344]; if (!GuestBridgeClient.TryGetFrame(frame)) { Status = "frame unavailable"; return; }
        int width = (int)BitConverter.ToUInt32(frame, 8), height = (int)BitConverter.ToUInt32(frame, 12);
        if (width <= 0 || height <= 0) { Status = "view unavailable"; return; }
        uint actor = GuestOccupancy.LocalActorId;
        var inputs = GuestBridgeClient.CaptureShadowInputs(actor) ?? throw new InvalidOperationException("scene snapshot missing");
        var character = GuestBridgeClient.CaptureCharacter(actor) ?? throw new InvalidOperationException("character snapshot API missing");
        if (character.Length < 5 || character[0] is not float[] mesh || character[1] is not float[] mask ||
            character[2] is not double[] actorWorld || character[3] is not double[] bounds)
            throw new InvalidOperationException("character snapshot layout invalid");
        if (mesh.Length % 12 != 0 || mask.Length % 12 != 0 || (mesh.Length > 0 && actorWorld.Length != 16))
            throw new InvalidOperationException("character triangle layout invalid");
        int face = GuestOccupancy.PointFace, requested = GuestOccupancy.PointCap;
        int effective = Math.Min(requested, Math.Min(16383 / face, (96 * 1024 * 1024) / (6 * face * face * 16)));
        GuestOccupancy.SelectBoxes(inputs, effective, actor);
        uint[] selected = GuestOccupancy.SelectedLights;
        double[] lights = GuestOccupancy.SelectedLightRecords;
        var boxes = AtlasBoxes(inputs, selected, lights, actor, worldBoxes, out int[] starts, out int[] counts);
        int atlasWidth = face * 6, atlasHeight = 1 + face * Math.Max(1, selected.Length);
        if (program.StorageImage == 0) program.StorageImage = GuestBridgeClient.AllocateImage(atlasWidth, atlasHeight, 1, null, 6);
        else if (program.ImageWidth != atlasWidth || program.ImageHeight != atlasHeight)
            GuestBridgeClient.CreateImage(program.StorageImage, atlasWidth, atlasHeight, 1, 6);
        program.ImageWidth = atlasWidth; program.ImageHeight = atlasHeight;
        if (maskImage == 0) maskImage = GuestBridgeClient.AllocateImage(width, height, 1, null, 2);
        else if (maskWidth != width || maskHeight != height) GuestBridgeClient.CreateImage(maskImage, width, height, 1, 2);
        maskWidth = width; maskHeight = height;
        Upload(ref meshBuffer, ref meshBytes, mesh);
        ulong maskSource = meshBuffer;
        if (!ReferenceEquals(mesh, mask)) { Upload(ref maskBuffer, ref maskBytes, mask); maskSource = maskBuffer; }
        Upload(ref boxBuffer, ref boxBytes, boxes);
        double[] camera = (double[])inputs[4];
        var cameraOrigin = new Vector3D(camera[3], camera[4], camera[5]);
        bool cleared = false;
        int atlasDraws = 0;
        for (int light = 0; light < selected.Length; light++)
        {
            int vertices = mesh.Length / 4 + counts[light] * 36;
            if (vertices == 0) continue;
            var origin = new Vector3D(lights[light*4], lights[light*4+1], lights[light*4+2]);
            var world = RelativeWorld(actorWorld, origin);
            for (int f = 0; f < 6; f++)
            {
                var constants = GeometryConstants(CubeProjection(f, (float)lights[light*4+3]), world,
                    f * face, 1 + light * face, face, face, vertices, mesh.Length / 4, starts[light], counts[light]);
                Geometry(program, program.StorageImage, constants, meshBuffer, boxBuffer, !cleared);
                cleared = true; atlasDraws++;
            }
        }
        var maskConstants = GeometryConstants(MatrixFromFrame(frame, 136), RelativeWorld(actorWorld, cameraOrigin),
            0, 0, width, height, mask.Length / 4, mask.Length / 4, 0, 0);
        Geometry(program, maskImage, maskConstants, maskSource, boxBuffer, true);
        float[] headerData = Header(frame, inputs, lights, bounds, mesh.Length > 0 && atlasDraws >= selected.Length * 6 && atlasDraws > 0);
        Upload(ref headerBuffer, ref headerBytes, headerData);
        var headerConstants = GeometryConstants(MatrixD.Identity, MatrixD.Identity, 0, 0, 66, 1, 0, 0, 0, 0);
        var sampled = new ulong[14]; sampled[0] = headerBuffer;
        GuestBridgeClient.SubmitPacket(header.Mark, 5, atlasWidth, atlasHeight, header.Pipeline, program.StorageImage, headerConstants, sampled,
            GuestBridgeClient.PacketConstants | GuestBridgeClient.Viewport | (cleared ? 0 : GuestBridgeClient.ClearFar));
        program.SubmittedThisFrame = header.SubmittedThisFrame = true;
        header.Submissions++; program.Status = "pipeline"; program.Error = null;
        BufferCatalog.Set("pointShadowAtlas", new GuestRendererImage(program.StorageImage, atlasWidth, atlasHeight, 1));
        BufferCatalog.Set("playerDepth", new GuestRendererImage(maskImage, width, height, 1));
        Status = "lights=" + selected.Length + " selected=" + string.Join(",", selected) + " cap=" + effective + "/" + requested
            + " face=" + face + " triangles=" + mesh.Length / 12 + " atlasDraws=" + atlasDraws + " maskTriangles=" + mask.Length / 12
            + " mesh=" + character[4];
    }

    static void Geometry(GuestProgram program, ulong target, byte[] constants, ulong mesh, ulong boxes, bool clear)
    {
        var sampled = new ulong[14]; sampled[0] = mesh; sampled[1] = boxes;
        GuestBridgeClient.SubmitPacket(program.Mark, 5, 1, 1, program.Pipeline, target, constants, sampled,
            GuestBridgeClient.Geometry | GuestBridgeClient.PacketConstants | (clear ? GuestBridgeClient.ClearFar : 0));
        program.Submissions++;
    }

    static void Upload(ref ulong id, ref int capacity, float[] values)
    {
        int bytes = Math.Max(32, checked(values.Length * 4));
        if (bytes > capacity)
        {
            capacity = 32; while (capacity < bytes) capacity = checked(capacity * 2);
            if (id == 0) id = GuestBridgeClient.AllocateImage(capacity, 1, 1, null, 4);
            else GuestBridgeClient.CreateImage(id, capacity, 1, 1, 4);
        }
        var data = new byte[Math.Max(32, values.Length * 4)]; if (values.Length > 0) Buffer.BlockCopy(values, 0, data, 0, values.Length * 4);
        GuestBridgeClient.UpdateImage(id, data);
    }

    internal static MatrixD RelativeWorld(double[] values, Vector3D origin)
    {
        if (values.Length == 0) return MatrixD.Identity;
        var m = MatrixFromDoubles(values);
        m.Translation -= origin; return m;
    }
    internal static MatrixD CubeProjection(int face, float range)
    {
        Vector3D forward, up;
        switch (face)
        {
            case 0: forward = Vector3D.Right; up = Vector3D.Down; break;
            case 1: forward = Vector3D.Left; up = Vector3D.Down; break;
            case 2: forward = Vector3D.Up; up = Vector3D.Backward; break;
            case 3: forward = Vector3D.Down; up = Vector3D.Forward; break;
            case 4: forward = Vector3D.Backward; up = Vector3D.Down; break;
            default: forward = Vector3D.Forward; up = Vector3D.Down; break;
        }
        return MatrixD.CreateLookAt(Vector3D.Zero, forward, up) * MatrixD.CreatePerspectiveFieldOfView(Math.PI / 2, 1, .02, Math.Max(range, .25));
    }
    internal static byte[] GeometryConstants(MatrixD projection, MatrixD world, int x, int y, int width, int height, int vertices, int mesh, int boxStart, int boxes)
    {
        var result = new byte[256];
        void F(int at, float value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, result, at, 4);
        void U(int at, int value) => Buffer.BlockCopy(BitConverter.GetBytes((uint)value), 0, result, at, 4);
        double[] MatrixValues(MatrixD m) => new double[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        var p = MatrixValues(projection); var w = MatrixValues(world);
        for (int i = 0; i < 16; i++) { F(i*4, (float)p[i]); F(64+i*4, (float)w[i]); }
        F(128,x); F(132,y); F(136,width); F(140,height); U(144,vertices); U(148,mesh); U(152,boxStart); U(156,boxes);
        return result;
    }
    static MatrixD MatrixFromDoubles(double[] m) => new MatrixD(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
    static MatrixD MatrixFromFrame(byte[] frame, int offset)
    { var m = new double[16]; for (int i = 0; i < 16; i++) m[i] = BitConverter.ToSingle(frame, offset+i*4); return MatrixFromDoubles(m); }

    internal static float[] Header(byte[] frame, object[] inputs, double[] lights, double[] bounds, bool meshDrawn)
    {
        var output = new float[66*4]; var anchor = (double[])inputs[4];
        float F(int at) => BitConverter.ToSingle(frame, at);
        for (int i = 0; i < lights.Length / 4; i++)
        {
            double x = lights[i*4]-anchor[3], y = lights[i*4+1]-anchor[4], z = lights[i*4+2]-anchor[5];
            for (int axis = 0; axis < 3; axis++) output[i*4+axis] = (float)(x*F(264+axis*16)+y*F(268+axis*16)+z*F(272+axis*16));
            output[i*4+3] = (float)lights[i*4+3];
        }
        if (bounds.Length == 6)
        {
            for (int i = 0; i < 3; i++) { output[256+i] = (float)(bounds[i]-anchor[3+i]); output[260+i] = (float)bounds[3+i]; }
            output[259] = 1; output[263] = meshDrawn ? 1 : 0;
        }
        return output;
    }

    internal static float[] AtlasBoxes(object[] inputs, uint[] selected, double[] lights, uint localActor, bool enabled, out int[] starts, out int[] counts)
    {
        starts = new int[selected.Length]; counts = new int[selected.Length]; var result = new List<float>();
        if (!enabled) return result.ToArray();
        var boxes = (double[])inputs[2]; var ids = (uint[])inputs[3];
        for (int light = 0; light < selected.Length; light++)
        {
            starts[light] = result.Count / 8;
            for (int actor = 0; actor < ids.Length && counts[light] < 48; actor++)
            {
                if (ids[actor] == localActor || ids[actor] == selected[light]) continue;
                int a = actor*6, l = light*4;
                double hx=boxes[a+3], hy=boxes[a+4], hz=boxes[a+5], extent=hx*hx+hy*hy+hz*hz;
                if (!(extent >= 1e-4 && extent <= 6400) || hx < 0 || hy < 0 || hz < 0) continue;
                double x=Math.Max(Math.Abs(boxes[a]-lights[l])-hx,0), y=Math.Max(Math.Abs(boxes[a+1]-lights[l+1])-hy,0), z=Math.Max(Math.Abs(boxes[a+2]-lights[l+2])-hz,0);
                if (x*x+y*y+z*z > lights[l+3]*lights[l+3]) continue;
                result.Add((float)(boxes[a]-lights[l])); result.Add((float)(boxes[a+1]-lights[l+1])); result.Add((float)(boxes[a+2]-lights[l+2])); result.Add(0);
                result.Add((float)hx); result.Add((float)hy); result.Add((float)hz); result.Add(0); counts[light]++;
            }
        }
        return result.ToArray();
    }
}
