using System;
using System.Reflection;

namespace ClientPlugin.Backend;

/// <summary>
/// Talks to SE-Vulkan <c>ClientPlugin.Rendering.GuestBridge</c> by reflection.
/// Anomaly never records a Vulkan command buffer.
/// </summary>
static class GuestBridgeClient
{
    const string TypeName = "ClientPlugin.Rendering.GuestBridge";
    static ulong nextImage = 1;

    public static void Reset()
    {
        Call("Reset");
    }

    public static bool InitGBufferAttachment(int slot, int format, byte[] pixel)
    {
        var value = Call("InitGBufferAttachment", slot, format, pixel);
        return value is bool ok && ok;
    }

    public static void PublishAttachment(string name, int slot)
    {
        if (string.IsNullOrEmpty(name) || slot < 0 || slot > 2)
            return;
        ClientPlugin.Buffers.BufferCatalog.Set(name, new ClientPlugin.Buffers.GuestRendererImage(Attach0 + (ulong)slot, SceneView.Width, SceneView.Height, 1));
    }

    public static ulong CreatePipeline(byte[] vs, byte[] ps, string vsEntry, string psEntry)
    {
        var value = Call("CreatePipeline", vs, ps, vsEntry, psEntry);
        return value is ulong id ? id : 0;
    }

    public static void CreateImage(ulong id, int width, int height, int depth, int format)
    {
        Call("CreateImage", id, width, height, depth, format);
    }

    public static void UpdateImage(ulong id, byte[] data)
    {
        Call("UpdateImage", id, data);
    }

    public static ulong AllocateImage(int width, int height, int depth, byte[] rgba, int format = 0)
    {
        ulong id = nextImage++;
        CreateImage(id, width, height, depth, format);
        if (rgba != null)
            UpdateImage(id, rgba);
        return id;
    }

    public static void InvalidateHistory() => Call("InvalidateHistory");

    static ulong PinImage(ulong id, int width, int height, int depth, int format)
    {
        CreateImage(id, width, height, depth, format);
        return id;
    }

    static ulong PinnedStorage(GuestProgram program)
    {
        string id = program == null ? "" : program.Id ?? "";
        if (id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            return CatalogVelocity;
        if (id.EndsWith("ReactiveClear", StringComparison.Ordinal))
            return CatalogReactiveMask;
        string output = program == null ? "" : program.OutputName ?? "";
        if (string.Equals(output, "volumeReactive", StringComparison.OrdinalIgnoreCase))
            return CatalogVolumeReactive;
        return 0;
    }

    public static ulong AllocateVolumeMips(int dim, byte[][] mips)
    {
        if (mips == null || mips.Length == 0 || dim <= 0)
            return 0;
        int bytes = 0;
        for (int i = 0; i < mips.Length; i++)
            bytes += mips[i] == null ? 0 : mips[i].Length;
        var packed = new byte[bytes];
        int offset = 0;
        for (int i = 0; i < mips.Length; i++)
        {
            if (mips[i] == null)
                continue;
            System.Buffer.BlockCopy(mips[i], 0, packed, offset, mips[i].Length);
            offset += mips[i].Length;
        }
        int format = (mips.Length << 16);
        return AllocateImage(dim, dim, dim, packed, format);
    }

    public static void Submit(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        Reset();
        if (programs == null)
            return;
        ulong previous = Hdr;
        foreach (var program in programs)
        {
            if (program == null || !program.Enabled || program.Pipeline == 0 || program.Mark == 0)
                continue;
            int compose = ComposeOf(program.Compose);
            bool catalog = compose == 5 || compose == 3 || (program.Flags & Compute) != 0;
            int width, height;
            PassSize(program, out width, out height);
            if (catalog && (program.StorageImage == 0 || program.ImageWidth != width || program.ImageHeight != height))
            {
                int format = program.Format;
                ulong pinned = PinnedStorage(program);
                program.StorageImage = pinned != 0
                    ? PinImage(pinned, width, height, 1, format)
                    : AllocateImage(width, height, 1, null, format);
                program.HistoryImage = string.IsNullOrEmpty(program.HistoryName) ? 0 : AllocateImage(width, height, 1, null, format);
                program.ImageWidth = width;
                program.ImageHeight = height;
                PublishOwned(program);
            }
            if (!string.IsNullOrEmpty(program.HistoryName) && program.HistoryImage != 0)
                ClientPlugin.Buffers.BufferCatalog.Set(program.HistoryName, new ClientPlugin.Buffers.GuestRendererImage(program.HistoryImage, width, height, 1));
            ulong storage = catalog ? program.StorageImage : Hdr;
            if (storage == 0)
                continue;
            byte[] uniforms = PackUniforms(program.Uniforms);
            var sampled = ImagesFor(program, previous, compose);
            Call("Submit", program.Mark, compose, width, height, program.Pipeline, storage, uniforms, sampled, program.Flags);
            if (catalog)
                PublishPass(program, storage, width, height);
            if (!string.IsNullOrEmpty(program.HistoryName) && program.HistoryImage != 0)
            {
                ulong written = program.StorageImage;
                program.StorageImage = program.HistoryImage;
                program.HistoryImage = written;
            }
            if (compose == 3)
                previous = storage;
        }
        Call("SetNativeSunGlare", !OwnsGlare(programs));
    }

    static bool OwnsGlare(System.Collections.Generic.IReadOnlyList<GuestProgram> programs)
    {
        for (int i = 0; i < programs.Count; i++)
        {
            var program = programs[i];
            if (program == null || !program.Enabled || program.Pipeline == 0 || (program.Flags & DepthTest) == 0)
                continue;
            if (!GuestColor.NativeGlare(program.Id))
                return true;
        }
        return false;
    }

    public const ulong Hdr = 0xFFFF000000000001UL;
    public const ulong Depth = 0xFFFF000000000002UL;
    public const ulong GBuffer0 = 0xFFFF000000000003UL;
    public const ulong GBuffer1 = 0xFFFF000000000004UL;
    public const ulong GBuffer2 = 0xFFFF000000000005UL;
    public const ulong Upscale = 0xFFFF000000000006UL;
    public const ulong Display = 0xFFFF000000000007UL;
    public const ulong Motion = 0xFFFF000000000008UL;
    public const ulong Attach0 = 0xFFFF000000000009UL;
    public const ulong CatalogVelocity = 0xFFFF00000000000AUL;
    public const ulong CatalogVolumeReactive = 0xFFFF00000000000BUL;
    public const ulong CatalogReactiveMask = 0xFFFF00000000000CUL;
    public const int DepthTest = 1;
    public const int Compute = 2;
    public const int Volume = 4;

    public static bool TryGetFrame(byte[] frame)
    {
        var value = Call("TryGetFrame", frame);
        return value is bool ready && ready;
    }

    static ulong[] ImagesFor(GuestProgram program, ulong previous, int compose)
    {
        var sampled = new ulong[14];
        string id = program.Id ?? "";
        if (id.EndsWith("LinearDepth", StringComparison.Ordinal) || id.EndsWith("HiZ", StringComparison.Ordinal) || id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            sampled[0] = Depth;
        else if (id.EndsWith("HistoryCopy", StringComparison.Ordinal))
            sampled[0] = Hdr;
        else if (compose == 3)
            sampled[0] = previous;
        else
            sampled[0] = program.Mark >= 5 ? Upscale : Hdr;
        sampled[1] = Catalog("linearDepth");
        sampled[2] = Catalog("velocity");
        sampled[3] = Catalog("reactiveMask");
        if (program.Mark < 5)
        {
            sampled[4] = GBuffer0;
            sampled[5] = GBuffer1;
            sampled[6] = GBuffer2;
        }
        sampled[7] = Catalog("historyColor");
        if (program.Mark >= 5)
        {
            sampled[4] = Catalog("avgLuminance");
            sampled[5] = Catalog("bloom");
            sampled[6] = Catalog("dirt");
        }
        if (program.Srvs != null)
        {
            for (int i = 0; i < program.Srvs.Count; i++)
            {
                int slot = program.Srvs[i].Slot;
                if (slot >= 0 && slot < 14)
                    sampled[slot] = Catalog(program.Srvs[i].Name);
            }
        }
        if ((program.Flags & DepthTest) != 0)
            sampled[0] = Depth;
        if ((program.Flags & Volume) != 0)
        {
            sampled[8] = Pack(program.OriginX, program.OriginY);
            sampled[9] = Pack(program.OriginZ, program.MotionX);
            sampled[10] = Pack(program.MotionY, program.MotionZ);
            var textures = program.MediumTextures;
            if (textures != null)
            {
                if (textures.Length > 0) sampled[11] = Catalog(textures[0]);
                if (textures.Length > 1) sampled[12] = Catalog(textures[1]);
                if (textures.Length > 2) sampled[13] = Catalog(textures[2]);
            }
        }
        if (id.EndsWith(".present", StringComparison.Ordinal))
            sampled[7] = Catalog("sharedVolume." + id.Substring(0, id.Length - ".present".Length));
        if (GuestColor.Celestial.Data.TryGetValue(id, out var data))
            sampled[1] = data;
        if (id.EndsWith("CameraVelocity", StringComparison.Ordinal))
            sampled[1] = Motion;
        if (GuestColor.Celestial.Art.TryGetValue(id, out var art))
            sampled[2] = art;
        return sampled;
    }

    static ulong Catalog(string name)
    {
        var active = ClientPlugin.Buffers.BufferCatalog.Active(name);
        if (active is ClientPlugin.Buffers.PublishedBuffer published)
            return published.ImageId;
        if (active is ClientPlugin.Buffers.GuestRendererImage image)
            return image.ImageId;
        return 0;
    }

    public static void SetDisplayFlags(int flags) => Call("SetDisplayFlags", flags);

    public static void SetUpscaleOwner(string id) => Call("SetUpscaleOwner", id ?? "");

    public static void SetViewClip(float farPlane) => Call("SetViewClip", farPlane);

    static void PassSize(GuestProgram program, out int width, out int height)
    {
        int sceneW = Math.Max(SceneView.Width, 1);
        int sceneH = Math.Max(SceneView.Height, 1);
        float scale = GuestPassList.SnapScale(program == null ? 1f : program.Scale);
        string compose = program == null ? "" : program.Compose ?? "";
        bool replace = string.Equals(compose, "Replace", StringComparison.OrdinalIgnoreCase);
        if (replace || scale >= 1f)
        {
            width = sceneW;
            height = sceneH;
            return;
        }
        width = Math.Max(1, (int)Math.Round(sceneW * scale));
        height = Math.Max(1, (int)Math.Round(sceneH * scale));
    }

    static void PublishPass(GuestProgram program, ulong image, int width, int height)
    {
        if (program == null || image == 0 || string.IsNullOrEmpty(program.Id))
            return;
        var published = new ClientPlugin.Buffers.GuestRendererImage(image, width, height, 1);
        ClientPlugin.Buffers.BufferCatalog.Set("pass." + program.Id, published);
        if (!string.IsNullOrEmpty(program.OutputName)
            && !string.Equals(program.OutputName, "pass." + program.Id, StringComparison.OrdinalIgnoreCase))
            ClientPlugin.Buffers.BufferCatalog.Set(program.OutputName, published);
    }

    static void PublishOwned(GuestProgram program)
    {
        string name = null;
        string id = program.Id ?? "";
        if (id.EndsWith("LinearDepth", StringComparison.Ordinal)) name = "linearDepth";
        else if (id.EndsWith("CameraVelocity", StringComparison.Ordinal)) name = "velocity";
        else if (id.EndsWith("HiZ", StringComparison.Ordinal)) name = "hiZ";
        else if (id.EndsWith("HistoryCopy", StringComparison.Ordinal)) name = "historyColor";
        else if (id.EndsWith("ReactiveClear", StringComparison.Ordinal)) name = "reactiveMask";
        else if ((program.Flags & Volume) != 0) name = "sharedVolume." + id;
        if (name == null)
            return;
        ClientPlugin.Buffers.BufferCatalog.Set(name, new ClientPlugin.Buffers.GuestRendererImage(program.StorageImage, SceneView.Width, SceneView.Height, 1));
    }

    static ulong Pack(float a, float b)
    {
        uint ua = BitConverter.ToUInt32(BitConverter.GetBytes(a), 0);
        uint ub = BitConverter.ToUInt32(BitConverter.GetBytes(b), 0);
        return ua | ((ulong)ub << 32);
    }

    static int ComposeOf(string compose)
    {
        switch ((compose ?? "").Trim().ToLowerInvariant())
        {
            case "isolatedmix": return 1;
            case "isolatedsub": return 2;
            case "chain": return 3;
            case "replace": return 4;
            case "publishonly": return 5;
            case "directadd": return 6;
            default: return 0;
        }
    }

    static byte[] PackUniforms(float[] values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<byte>();
        int count = Math.Min(values.Length, 64);
        var bytes = new byte[count * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    static object Call(string method, params object[] args)
    {
        var type = Find();
        if (type == null)
            return null;
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != method || candidate.GetParameters().Length != (args?.Length ?? 0))
                continue;
            return candidate.Invoke(null, args);
        }

        return null;
    }

    static Type Find()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(TypeName, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }
}
