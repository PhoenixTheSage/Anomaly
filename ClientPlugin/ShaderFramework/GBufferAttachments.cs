using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClientPlugin.ShaderFramework;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for pack plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.GBufferAttachments</c> — do not take a
/// compile-time reference to Anomaly. <see cref="Request"/> a named
/// extra (full MRT or packed <c>GBuffer1.a</c>). Velocity owns
/// <c>SV_Target3</c>; packs cannot claim it.
/// </summary>
public static class GBufferAttachments
{
    public const string PackedGBuffer1A = "GBuffer1.a";
    public const int FirstExtraTarget = 4;
    public const int MaxColorTargets = 8;
    public const string GeneratedFieldsPath = "Anomaly/Extras/GBufferAttachmentFields.hlsli";
    public const string GeneratedInitPath = "Anomaly/Extras/GBufferAttachmentInit.hlsli";
    public const string GeneratedDefsPath = "Anomaly/Extras/GBufferAttachmentDefs.hlsli";
    public const string GeneratedReadSrvsPath = "Anomaly/Extras/GBufferReadSrvs.hlsli";

    static readonly object Gate = new();
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly List<QueuedRequest> CsharpRequests = new();
    static readonly List<QueuedRequest> PackRequests = new();
    static readonly List<LiveAttachment> Live = new();
    static readonly Dictionary<string, LiveAttachment> ByName =
        new(StringComparer.OrdinalIgnoreCase);

    static ShaderMacro[] liveMacros = Array.Empty<ShaderMacro>();
    static byte[] fieldsBytes;
    static byte[] initBytes;
    static byte[] defsBytes;
    static byte[] readSrvsBytes;
    static int conflictCount;
    static bool assigned;

    public static int ConflictCount
    {
        get { lock (Gate) return conflictCount; }
    }

    public static ShaderMacro[] LiveDefineMacros
    {
        get { lock (Gate) return liveMacros; }
    }

    public static string StatusLine
    {
        get
        {
            lock (Gate)
            {
                if (Live.Count == 0)
                    return "none";
                var parts = new string[Live.Count];
                for (var i = 0; i < Live.Count; i++)
                    parts[i] = Live[i].StatusToken();
                return string.Join(",", parts);
            }
        }
    }

    public static bool HasColorTargets
    {
        get
        {
            lock (Gate)
            {
                for (var i = 0; i < Live.Count; i++)
                {
                    if (Live[i].SvTarget >= FirstExtraTarget)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Request a GBuffer extra. <paramref name="format"/> is a DXGI name
    /// (<c>R32_UINT</c>, <c>R16G16_Float</c>) or <c>GBuffer1.a</c>.
    /// Same name + same format from two callers share the slot; mismatched
    /// formats fail closed.
    /// </summary>
    public static void Request(string name, string format, string stage = ShaderStages.GBuffer)
    {
        RequestCore(name, format, stage);
    }

    /// <summary>Alias of <see cref="Request"/> for reflection callers.</summary>
    public static void RequestAttachment(string name, string format, string stage = ShaderStages.GBuffer)
    {
        RequestCore(name, format, stage);
    }

    static void RequestCore(string name, string format, string stage)
    {
        var refresh = false;
        lock (Gate)
        {
            try
            {
                if (!TryNormalizeRequest(name, format, stage, "csharp", out var queued, out var error))
                {
                    Warn(error);
                    return;
                }

                ReplaceOrAdd(CsharpRequests, queued);
                if (assigned)
                {
                    AssignUnlocked();
                    refresh = true;
                }
            }
            catch (Exception e)
            {
                Warn("Request(" + name + "): " + e.Message);
            }
        }

        if (refresh)
            ShaderPackRegistry.RefreshFingerprint();
    }

    public static bool TryGet(string name, out GBufferAttachment info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        lock (Gate)
        {
            if (!ByName.TryGetValue(name, out var live))
                return false;
            info = live.ToPublic();
            return true;
        }
    }

    internal static void SetPackRequests(IReadOnlyList<QueuedRequest> requests)
    {
        lock (Gate)
        {
            PackRequests.Clear();
            if (requests == null)
                return;
            for (var i = 0; i < requests.Count; i++)
                PackRequests.Add(requests[i]);
        }
    }

    internal static void Assign()
    {
        lock (Gate)
            AssignUnlocked();
    }

    internal static bool TryOpenGenerated(string relativeKey, out Stream stream)
    {
        stream = null;
        lock (Gate)
        {
            EnsureGeneratedUnlocked();
            byte[] bytes;
            if (KeysEqual(relativeKey, GeneratedFieldsPath))
                bytes = fieldsBytes;
            else if (KeysEqual(relativeKey, GeneratedInitPath))
                bytes = initBytes;
            else if (KeysEqual(relativeKey, GeneratedDefsPath))
                bytes = defsBytes;
            else if (KeysEqual(relativeKey, GeneratedReadSrvsPath))
                bytes = readSrvsBytes;
            else
                return false;
            stream = new MemoryStream(bytes, writable: false);
            return true;
        }
    }

    internal static void WriteFingerprint(Stream stream)
    {
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
            {
                var a = Live[i];
                WriteUtf8(stream, a.Name);
                WriteUtf8(stream, a.FormatKey);
                WriteUtf8(stream, a.Packed ? PackedGBuffer1A : ("SV_Target" + a.SvTarget));
            }
        }
    }

    internal static void EnsureTargets(int width, int height, int samples, int quality)
    {
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
                Live[i].EnsureTarget(width, height, samples, quality);
        }
    }

    internal static void CopyRtvs(RenderTargetView[] dest)
    {
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
            {
                var a = Live[i];
                if (a.SvTarget < FirstExtraTarget || a.Target?.Rtv == null)
                    continue;
                var index = a.SvTarget;
                if (index < 0 || index >= dest.Length)
                    continue;
                dest[index] = a.Target.Rtv;
            }
        }
    }

    internal static int LightingSrvSlot(int svTarget)
    {
        return ShaderBindRegistry.LightingAttachBase + (svTarget - FirstExtraTarget);
    }

    internal static void ForEachColorSrv(Action<string, int, ISrvBindable> visit)
    {
        if (visit == null)
            return;
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
            {
                var a = Live[i];
                if (a.Packed || a.Target == null)
                    continue;
                visit(a.Name, LightingSrvSlot(a.SvTarget), a.Target);
            }
        }
    }

    internal static int MaxBoundTarget
    {
        get
        {
            lock (Gate)
            {
                var max = 2;
                for (var i = 0; i < Live.Count; i++)
                {
                    if (Live[i].SvTarget > max)
                        max = Live[i].SvTarget;
                }

                return max;
            }
        }
    }

    internal static void ClearTargets(MyRenderContext rc)
    {
        if (rc == null)
            return;
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
            {
                var tex = Live[i].Target;
                if (tex != null)
                    rc.ClearRtv(tex, default(RawColor4));
            }
        }
    }

    internal static void OnResolutionChanged()
    {
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
                Live[i].DisposeTarget();
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            for (var i = 0; i < Live.Count; i++)
                Live[i].DisposeTarget();
        }
    }

    static void AssignUnlocked()
    {
        conflictCount = 0;
        for (var i = 0; i < Live.Count; i++)
            Live[i].DisposeTarget();
        Live.Clear();
        ByName.Clear();

        var merged = new List<QueuedRequest>();
        MergeRequests(CsharpRequests, merged);
        MergeRequests(PackRequests, merged);
        merged.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var nextTarget = FirstExtraTarget;
        var packedTaken = false;
        for (var i = 0; i < merged.Count; i++)
        {
            var req = merged[i];
            if (req.Rejected)
            {
                conflictCount++;
                continue;
            }

            if (req.Packed)
            {
                if (packedTaken)
                {
                    conflictCount++;
                    Warn("packed " + PackedGBuffer1A + " already assigned; skipped '" + req.Name + "'");
                    continue;
                }

                packedTaken = true;
                AddLive(req, svTarget: -1, packed: true);
                continue;
            }

            if (nextTarget >= MaxColorTargets)
            {
                conflictCount++;
                Warn("no free GBuffer SV_Target for '" + req.Name + "' (max " + (MaxColorTargets - 1) + ")");
                continue;
            }

            AddLive(req, nextTarget, packed: false);
            nextTarget++;
        }

        BuildGeneratedUnlocked();
        assigned = true;
        Log("assigned " + Live.Count + " extra(s) conflicts=" + conflictCount + " " + StatusLine);
    }

    static void MergeRequests(List<QueuedRequest> source, List<QueuedRequest> dest)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var req = source[i];
            var existing = Find(dest, req.Name);
            if (existing == null)
            {
                dest.Add(req.Clone());
                continue;
            }

            if (!string.Equals(existing.FormatKey, req.FormatKey, StringComparison.OrdinalIgnoreCase) ||
                existing.Packed != req.Packed ||
                existing.Dxgi != req.Dxgi)
            {
                existing.Rejected = true;
                req.Rejected = true;
                Warn("attachment '" + req.Name + "' format conflict: " + existing.FormatKey +
                     " (" + existing.Owner + ") vs " + req.FormatKey + " (" + req.Owner +
                     ") — fail closed");
            }
        }
    }

    static QueuedRequest Find(List<QueuedRequest> list, string name)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return list[i];
        }

        return null;
    }

    static void AddLive(QueuedRequest req, int svTarget, bool packed)
    {
        var live = new LiveAttachment
        {
            Name = req.Name,
            FormatKey = req.FormatKey,
            HlslType = req.HlslType,
            Dxgi = req.Dxgi,
            Packed = packed,
            SvTarget = svTarget,
            Macro = AttachMacro(req.Name)
        };
        Live.Add(live);
        ByName[live.Name] = live;
    }

    static void BuildGeneratedUnlocked()
    {
        var fields = new StringBuilder();
        var init = new StringBuilder();
        var defs = new StringBuilder();
        var read = new StringBuilder();
        var macros = new List<ShaderMacro>();

        fields.AppendLine("#ifndef ANOMALY_GBUFFER_ATTACHMENT_FIELDS_HLSLI");
        fields.AppendLine("#define ANOMALY_GBUFFER_ATTACHMENT_FIELDS_HLSLI");
        init.AppendLine("#ifndef ANOMALY_GBUFFER_ATTACHMENT_INIT_HLSLI");
        init.AppendLine("#define ANOMALY_GBUFFER_ATTACHMENT_INIT_HLSLI");
        init.AppendLine("void AnomalyInitAttachments(inout GbufferOutput output)");
        init.AppendLine("{");
        // FXC X3508: SV_Target structs passed inout/out must be fully written
        // in the callee. Empty body crashes GBuffer / Decals / Foliage / Sprites
        // (old pipeline included) even with zero extra attachments.
        init.AppendLine("    output = (GbufferOutput)0;");
        defs.AppendLine("#ifndef ANOMALY_GBUFFER_ATTACHMENT_DEFS_HLSLI");
        defs.AppendLine("#define ANOMALY_GBUFFER_ATTACHMENT_DEFS_HLSLI");
        read.AppendLine("#ifndef ANOMALY_GBUFFER_READ_SRVS_HLSLI");
        read.AppendLine("#define ANOMALY_GBUFFER_READ_SRVS_HLSLI");
        read.AppendLine("#include <Anomaly/LightingSlots.hlsli>");
        read.AppendLine("#ifdef ANOMALY_VELOCITY");
        read.AppendLine("Texture2D<float2> AnomalyVelocityBuffer : register(MERGE(t, ANOMALY_LIGHTING_VELOCITY_SLOT));");
        read.AppendLine("#endif");

        for (var i = 0; i < Live.Count; i++)
        {
            var a = Live[i];
            macros.Add(new ShaderMacro(a.Macro, "1"));
            if (a.Packed)
            {
                macros.Add(new ShaderMacro("ANOMALY_PACK_GBUFFER1A", "1"));
                defs.Append("#define ANOMALY_ATTACH_PACKED_NAME ").AppendLine(a.Name);
                continue;
            }

            fields.Append("#ifdef ").AppendLine(a.Macro);
            fields.Append("    ").Append(a.HlslType).Append(' ').Append(a.Name)
                .Append(" : SV_Target").Append(a.SvTarget).AppendLine(";");
            fields.AppendLine("#endif");
            init.Append("#ifdef ").AppendLine(a.Macro);
            init.Append("    output.").Append(a.Name).AppendLine(" = 0;");
            init.AppendLine("#endif");
            defs.Append("#ifdef ").AppendLine(a.Macro);
            defs.Append("#define ").Append(a.Macro).Append("_TARGET ").Append(a.SvTarget)
                .AppendLine();
            defs.AppendLine("#endif");
            var srvSlot = LightingSrvSlot(a.SvTarget);
            read.Append("#ifdef ").AppendLine(a.Macro);
            read.Append("#define ").Append(a.Macro).Append("_SRV ").Append(srvSlot).AppendLine();
            read.Append("Texture2D<").Append(a.HlslType).Append("> AnomalyAttach_").Append(a.Name)
                .Append(" : register(MERGE(t, ").Append(a.Macro).Append("_SRV));").AppendLine();
            read.AppendLine("#endif");
        }

        fields.AppendLine("#endif");
        init.AppendLine("}");
        init.AppendLine("#endif");
        defs.AppendLine("#endif");
        read.AppendLine("#endif");

        fieldsBytes = Utf8.GetBytes(fields.ToString());
        initBytes = Utf8.GetBytes(init.ToString());
        defsBytes = Utf8.GetBytes(defs.ToString());
        readSrvsBytes = Utf8.GetBytes(read.ToString());
        liveMacros = macros.ToArray();
    }

    static void EnsureGeneratedUnlocked()
    {
        if (fieldsBytes != null && initBytes != null && defsBytes != null && readSrvsBytes != null)
            return;
        if (!assigned)
            AssignUnlocked();
    }

    internal static bool TryNormalizeRequest(string name, string format, string stage, string owner,
        out QueuedRequest queued, out string error)
    {
        queued = null;
        error = null;
        if (string.IsNullOrWhiteSpace(name) || !IsHlslIdent(name))
        {
            error = "attachment name must be an HLSL identifier: '" + name + "'";
            return false;
        }

        if (IsReservedName(name))
        {
            error = "attachment name '" + name + "' is reserved (velocity owns SV_Target3)";
            return false;
        }

        if (!string.IsNullOrEmpty(stage) &&
            !string.Equals(stage, ShaderStages.GBuffer, StringComparison.OrdinalIgnoreCase))
        {
            error = "attachment '" + name + "' stage '" + stage + "' is not GBuffer (slice O)";
            return false;
        }

        if (!TryParseFormat(format, out var dxgi, out var hlsl, out var packed, out var formatKey))
        {
            error = "attachment '" + name + "' unknown format '" + format + "'";
            return false;
        }

        queued = new QueuedRequest
        {
            Name = name.Trim(),
            FormatKey = formatKey,
            HlslType = hlsl,
            Dxgi = dxgi,
            Packed = packed,
            Owner = owner ?? "unknown"
        };
        return true;
    }

    static bool TryParseFormat(string format, out Format dxgi, out string hlsl, out bool packed,
        out string formatKey)
    {
        dxgi = Format.Unknown;
        hlsl = "float4";
        packed = false;
        formatKey = "";
        if (string.IsNullOrWhiteSpace(format))
            return false;
        var n = format.Trim();
        if (string.Equals(n, PackedGBuffer1A, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, "gbuffer1.a", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n, "pack:GBuffer1.a", StringComparison.OrdinalIgnoreCase))
        {
            packed = true;
            hlsl = "float";
            formatKey = PackedGBuffer1A;
            return true;
        }

        n = n.Replace("-", "_");
        if (MatchDxgi(n, "R32_UINT", Format.R32_UInt, "uint", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R16_UINT", Format.R16_UInt, "uint", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R8_UINT", Format.R8_UInt, "uint", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R32_FLOAT", Format.R32_Float, "float", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R16_FLOAT", Format.R16_Float, "float", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R16G16_FLOAT", Format.R16G16_Float, "float2", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R32G32_FLOAT", Format.R32G32_Float, "float2", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R8G8B8A8_UNORM", Format.R8G8B8A8_UNorm, "float4", out dxgi, out hlsl, out formatKey) ||
            MatchDxgi(n, "R16G16B16A16_FLOAT", Format.R16G16B16A16_Float, "float4", out dxgi, out hlsl,
                out formatKey) ||
            MatchDxgi(n, "R32G32B32A32_FLOAT", Format.R32G32B32A32_Float, "float4", out dxgi, out hlsl,
                out formatKey))
            return true;
        return false;
    }

    static bool MatchDxgi(string n, string key, Format format, string hlslType, out Format dxgi,
        out string hlsl, out string formatKey)
    {
        dxgi = Format.Unknown;
        hlsl = hlslType;
        formatKey = key;
        if (!string.Equals(n, key, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(n, format.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;
        dxgi = format;
        return true;
    }

    static bool IsReservedName(string name)
    {
        return string.Equals(name, "velocity", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "gbuffer0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "gbuffer1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "gbuffer2", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "depth", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsReservedDefine(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        if (name.StartsWith("ANOMALY_ATTACH_", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(name, "ANOMALY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "ANOMALY_VELOCITY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "ANOMALY_PACK_GBUFFER1A", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "RENDERING_PASS", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "DEPTH_ONLY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "CUSTOM_DEPTH", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsHlslIdent(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        var c0 = name[0];
        if (!(c0 == '_' || (c0 >= 'A' && c0 <= 'Z') || (c0 >= 'a' && c0 <= 'z')))
            return false;
        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                continue;
            return false;
        }

        return true;
    }

    static string AttachMacro(string name)
    {
        return "ANOMALY_ATTACH_" + name.ToUpperInvariant();
    }

    static bool KeysEqual(string a, string b)
    {
        return string.Equals((a ?? "").Replace('\\', '/').Trim('/'),
            (b ?? "").Replace('\\', '/').Trim('/'), StringComparison.OrdinalIgnoreCase);
    }

    static void ReplaceOrAdd(List<QueuedRequest> list, QueuedRequest queued)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (!string.Equals(list[i].Name, queued.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            list[i] = queued;
            return;
        }

        list.Add(queued);
    }

    static void WriteUtf8(Stream stream, string text)
    {
        var bytes = Utf8.GetBytes(text ?? "");
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    static void Log(string message)
    {
        MyLog.Default.WriteLine("Anomaly GBuffer attachments: " + message);
        DebugLog.Write("GBufferAttachments " + message);
    }

    static void Warn(string message)
    {
        MyLog.Default.WriteLine("Anomaly GBuffer attachments: " + message);
        DebugLog.Write("GBufferAttachments WARN " + message);
    }

    internal sealed class QueuedRequest
    {
        public string Name;
        public string FormatKey;
        public string HlslType;
        public Format Dxgi;
        public bool Packed;
        public string Owner;
        public bool Rejected;

        public QueuedRequest Clone()
        {
            return new QueuedRequest
            {
                Name = Name,
                FormatKey = FormatKey,
                HlslType = HlslType,
                Dxgi = Dxgi,
                Packed = Packed,
                Owner = Owner,
                Rejected = Rejected
            };
        }
    }

    sealed class LiveAttachment
    {
        public string Name;
        public string FormatKey;
        public string HlslType;
        public Format Dxgi;
        public bool Packed;
        public int SvTarget;
        public string Macro;
        public IRtvTexture Target;
        public int Width;
        public int Height;
        public int Samples;

        public string StatusToken()
        {
            if (Packed)
                return Name + ":" + PackedGBuffer1A;
            return Name + ":SV_Target" + SvTarget;
        }

        public GBufferAttachment ToPublic()
        {
            return new GBufferAttachment
            {
                Name = Name,
                Format = FormatKey,
                SvTarget = SvTarget,
                PackedChannel = Packed ? PackedGBuffer1A : null,
                Srv = Target,
                NativeResource = Target?.Resource != null ? Target.Resource.NativePointer : IntPtr.Zero
            };
        }

        public void EnsureTarget(int width, int height, int samples, int quality)
        {
            if (Packed || Dxgi == Format.Unknown)
                return;
            samples = Math.Max(samples, 1);
            if (Target != null && Width == width && Height == height && Samples == samples)
                return;
            DisposeTarget();
            Target = MyManagers.RwTextures.CreateRtv("Anomaly.GBuffer." + Name, width, height, Dxgi,
                samples, quality);
            Width = width;
            Height = height;
            Samples = samples;
        }

        public void DisposeTarget()
        {
            if (Target != null)
                MyManagers.RwTextures.DisposeTex(ref Target);
            Target = null;
            Width = 0;
            Height = 0;
            Samples = 0;
        }
    }
}

/// <summary>
/// Snapshot of an assigned extra. <see cref="Srv"/> is Keen
/// <c>ISrvBindable</c> as <c>object</c> (no compile-time reference).
/// </summary>
public sealed class GBufferAttachment
{
    public string Name;
    public string Format;
    public int SvTarget;
    public string PackedChannel;
    public object Srv;
    public IntPtr NativeResource;
}
