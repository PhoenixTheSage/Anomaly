using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ClientPlugin.Buffers;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Velocity;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for pack plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.ShaderBindRegistry</c> — do not take a
/// compile-time reference. <see cref="RequestSrv"/> asks Anomaly to bind a
/// catalog texture at a reserved lighting/post slot. Anomaly owns the
/// Harmony prefixes and the unbind (Rich HUD).
/// </summary>
public static class ShaderBindRegistry
{
    public const int LightingVelocitySlot = 5;
    public const int LightingAttachBase = 6;
    public const int LightingCbSlot = 6;
    public const int PostVelocitySlot = 5;
    public const int PostCbSlot = 6;
    public const int AtmosphereVelocitySlot = 6;
    public const int AtmosphereCbSlot = 6;
    public const int AtmosphereExtraBase = 7;
    public const int FirstExtraSrv = 5;
    public const int LastExtraSrv = 9;

    const int ConstantBufferBytes = 256;
    const string VelocityName = BufferCatalog.Velocity;

    static readonly object Gate = new();
    static readonly List<SrvRequest> Requests = new();
    static readonly Dictionary<string, List<int>> LastSlots =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly List<int> BindScratch = new();

    static IConstantBuffer extrasCb;
    static string statusLine = "none";

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct ExtrasCb
    {
        public Vector2 RenderSize;
        public Vector2 InvRenderSize;
        public uint HasVelocity;
        public uint HistoryValid;
        public uint AttachCount;
        public uint FrameIndex;
        public Vector2 JitterOffset;
        public Vector2 Pad1;
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
    }

    public static string StatusLine
    {
        get
        {
            lock (Gate)
                return string.IsNullOrEmpty(statusLine) ? "none" : statusLine;
        }
    }

    /// <summary>
    /// Bind catalog texture <paramref name="catalogName"/> on
    /// <paramref name="stage"/> (<c>Lighting</c>, <c>Post.Tonemap</c>,
    /// <c>Atmosphere</c>, …). Slot <c>-1</c> assigns the next free reserved
    /// SRV. Lighting/post: t6–t9 (t5 is velocity). Atmosphere: t7–t9
    /// (t5 is Keen <c>DensityLut</c>; t6 is velocity).
    /// </summary>
    public static void RequestSrv(string stage, string catalogName, int slot = -1)
    {
        if (string.IsNullOrWhiteSpace(stage) || string.IsNullOrWhiteSpace(catalogName))
            return;
        lock (Gate)
        {
            try
            {
                QueueRequestUnlocked(stage.Trim(), catalogName.Trim(), slot);
            }
            catch (Exception e)
            {
                Warn("RequestSrv(" + stage + "," + catalogName + "): " + e.Message);
            }
        }
    }

    static ShaderBindRegistry()
    {
        QueueRequestUnlocked(ShaderStages.Lighting, VelocityName, LightingVelocitySlot);
        QueueRequestUnlocked(ShaderStages.PostTonemap, VelocityName, PostVelocitySlot);
        QueueRequestUnlocked(ShaderStages.PostHbao, VelocityName, PostVelocitySlot);
        QueueRequestUnlocked(ShaderStages.Transparent, VelocityName, PostVelocitySlot);
        QueueRequestUnlocked(ShaderStages.Atmosphere, VelocityName, AtmosphereVelocitySlot);
    }

    static void QueueRequestUnlocked(string stage, string catalogName, int slot)
    {
        if (string.Equals(catalogName, VelocityName, StringComparison.OrdinalIgnoreCase))
        {
            if (IsAtmosphereStage(stage))
                slot = AtmosphereVelocitySlot;
            else
                slot = IsLightingStage(stage) ? LightingVelocitySlot : PostVelocitySlot;
        }

        if (slot < 0)
            slot = NextFreeSlotUnlocked(stage);

        if (slot < FirstExtraSrv || slot > LastExtraSrv)
        {
            Warn("RequestSrv slot " + slot + " for '" + catalogName + "' is outside t" +
                 FirstExtraSrv + "–t" + LastExtraSrv);
            return;
        }

        for (var i = 0; i < Requests.Count; i++)
        {
            var existing = Requests[i];
            if (!string.Equals(existing.Stage, stage, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(existing.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase))
            {
                existing.Slot = slot;
                return;
            }

            if (existing.Slot == slot &&
                !string.Equals(existing.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase))
            {
                Warn("RequestSrv slot t" + slot + " on " + stage + " already '" +
                     existing.CatalogName + "', skipped '" + catalogName + "'");
                return;
            }
        }

        Requests.Add(new SrvRequest { Stage = stage, CatalogName = catalogName, Slot = slot });
    }

    static int NextFreeSlotUnlocked(string stage)
    {
        var used = new bool[LastExtraSrv + 1];
        if (IsAtmosphereStage(stage))
        {
            used[5] = true;
            used[AtmosphereVelocitySlot] = true;
        }
        else
            used[IsLightingStage(stage) ? LightingVelocitySlot : PostVelocitySlot] = true;
        for (var i = 0; i < Requests.Count; i++)
        {
            if (string.Equals(Requests[i].Stage, stage, StringComparison.OrdinalIgnoreCase))
                used[Requests[i].Slot] = true;
        }

        var start = IsAtmosphereStage(stage) ? AtmosphereExtraBase : LightingAttachBase;
        for (var slot = start; slot <= LastExtraSrv; slot++)
        {
            if (!used[slot])
                return slot;
        }

        return -1;
    }

    internal static void Bind(MyRenderContext rc, string stage)
    {
        if (rc == null || !rc.IsInitialized || string.IsNullOrEmpty(stage))
            return;
        lock (Gate)
        {
            try
            {
                BindUnlocked(rc, stage);
            }
            catch (Exception e)
            {
                Warn("bind " + stage + ": " + e.Message);
            }
        }
    }

    internal static void Unbind(MyRenderContext rc, string stage)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        lock (Gate)
        {
            try
            {
                UnbindUnlocked(rc, stage);
            }
            catch (Exception e)
            {
                DebugLog.Write("ShaderBindRegistry unbind " + stage + ": " + e.Message);
            }
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            if (extrasCb != null)
            {
                MyManagers.Buffers.Dispose(new[] { extrasCb });
                extrasCb = null;
            }

            LastSlots.Clear();
            statusLine = "none";
        }
    }

    static void BindUnlocked(MyRenderContext rc, string stage)
    {
        EnsureCbUnlocked();
        BindScratch.Clear();
        var attachCount = 0;
        var taken = new bool[LastExtraSrv + 1];

        if (IsLightingStage(stage))
        {
            GBufferAttachments.ForEachColorSrv((name, slot, srv) =>
            {
                if (srv == null || slot < FirstExtraSrv || slot > LastExtraSrv)
                    return;
                SetSrv(rc, slot, srv);
                BindScratch.Add(slot);
                taken[slot] = true;
                attachCount++;
            });
        }

        for (var i = 0; i < Requests.Count; i++)
        {
            var req = Requests[i];
            if (!RequestMatches(req.Stage, stage))
                continue;
            if (taken[req.Slot])
                continue;
            var buf = BufferCatalog.Active(req.CatalogName);
            var srv = buf != null && buf.IsAvailable ? buf.Srv as ISrvBindable : null;
            SetSrv(rc, req.Slot, srv);
            BindScratch.Add(req.Slot);
            taken[req.Slot] = true;
        }

        WriteExtrasUnlocked(rc, attachCount);
        rc.AllShaderStages.SetConstantBuffer(CbSlot(stage), extrasCb);

        LastSlots[CanonicalStatusStage(stage)] = new List<int>(BindScratch);
        statusLine = FormatStatusUnlocked();
    }

    static void UnbindUnlocked(MyRenderContext rc, string stage)
    {
        if (!LastSlots.TryGetValue(CanonicalStatusStage(stage), out var slots) || slots == null)
            return;
        for (var i = 0; i < slots.Count; i++)
            SetSrv(rc, slots[i], null);
        rc.AllShaderStages.SetConstantBuffer(CbSlot(stage), null);
    }

    static void SetSrv(MyRenderContext rc, int slot, ISrvBindable srv)
    {
        rc.AllShaderStages.SetSrv(slot, srv);
    }

    static void EnsureCbUnlocked()
    {
        if (extrasCb != null)
            return;
        extrasCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.PassExtrasCB",
            ConstantBufferBytes, usage: ResourceUsage.Dynamic);
    }

    static void WriteExtrasUnlocked(MyRenderContext rc, int attachCount)
    {
        if (extrasCb == null)
            return;
        var size = MyRender11.ResolutionI;
        FrameTemporal.EnsureSnapshot();
        var vel = BufferCatalog.Active(VelocityName);
        var hist = VelocityRegistry.Active;
        var w = size.X > 0 ? size.X : 1;
        var h = size.Y > 0 ? size.Y : 1;
        var cb = new ExtrasCb
        {
            RenderSize = new Vector2(w, h),
            InvRenderSize = new Vector2(1f / w, 1f / h),
            HasVelocity = vel != null && vel.IsAvailable ? 1u : 0u,
            HistoryValid = hist != null && hist.HistoryValid ? 1u : 0u,
            AttachCount = (uint)Math.Max(attachCount, 0),
            FrameIndex = FrameTemporal.FrameIndex,
            JitterOffset = new Vector2(FrameTemporal.JitterX, FrameTemporal.JitterY),
            Pad1 = Vector2.Zero,
            UnjitteredViewProj = FrameTemporal.UnjitteredViewProj,
            PrevViewProj = FrameTemporal.PrevViewProj
        };
        var mapping = MyMapping.MapDiscard(rc, extrasCb);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();
    }

    static bool RequestMatches(string requestStage, string bindStage)
    {
        if (string.Equals(requestStage, bindStage, StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsLightingStage(bindStage) &&
            string.Equals(requestStage, ShaderStages.Lighting, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static int CbSlot(string stage)
    {
        if (IsAtmosphereStage(stage))
            return AtmosphereCbSlot;
        return IsLightingStage(stage) ? LightingCbSlot : PostCbSlot;
    }

    static bool IsAtmosphereStage(string stage)
    {
        return string.Equals(stage, ShaderStages.Atmosphere, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsLightingStage(string stage)
    {
        return string.Equals(stage, ShaderStages.Lighting, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stage, ShaderStages.LightingDir, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stage, ShaderStages.LightingPoint, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stage, ShaderStages.LightingSpot, StringComparison.OrdinalIgnoreCase);
    }

    static string CanonicalStatusStage(string stage)
    {
        return IsLightingStage(stage) ? ShaderStages.Lighting : stage;
    }

    static string FormatStatusUnlocked()
    {
        if (LastSlots.Count == 0)
            return "none";
        var keys = new List<string>(LastSlots.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            var slots = LastSlots[keys[i]];
            if (slots == null || slots.Count == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(keys[i]).Append(':');
            for (var s = 0; s < slots.Count; s++)
            {
                if (s > 0)
                    sb.Append(',');
                sb.Append('t').Append(slots[s]);
            }
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    static void Warn(string message)
    {
        MyLog.Default.WriteLine("Anomaly bind registry: " + message);
        DebugLog.Write("ShaderBindRegistry WARN " + message);
    }

    sealed class SrvRequest
    {
        public string Stage;
        public string CatalogName;
        public int Slot;
    }
}
