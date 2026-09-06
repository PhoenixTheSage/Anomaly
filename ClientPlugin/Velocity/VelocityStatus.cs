using System.Text;
using ClientPlugin.RichHud;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;

namespace ClientPlugin.Velocity;

public static class VelocityStatus
{
    /// <summary>User-facing operational health. Keep this concise.</summary>
    public static string CurrentText => OperationalText;

    public static string OperationalText
    {
        get
        {
            var cfg = Config.Current;
            var buf = VelocityRegistry.Active;
            var sb = new StringBuilder();
            sb.AppendLine("Anomaly Shader Framework");
            sb.Append("Velocity source: ").AppendLine(cfg != null ? cfg.VelocitySource.ToString() : "—");
            sb.Append("Compile intercept: ").AppendLine(FormatInterceptSummary());
            sb.Append("Rich HUD: ").AppendLine(RichHudSupport.StatusLine);
            sb.Append("Shader packs: ").AppendLine(ShaderPackRegistry.StatusLine);
            sb.Append("GBuffer attachments: ").AppendLine(GBufferAttachments.StatusLine);
            sb.Append("Pass binds: ").AppendLine(ShaderBindRegistry.StatusLine);
            sb.Append("Owned passes: ").AppendLine(OwnedPassRegistry.StatusLine);
            sb.Append("Fullscreen: ").AppendLine(FullscreenPassRegistry.StatusLine);
            sb.Append("Camera velocity: ").AppendLine(FormatCameraPass());
            sb.Append("Owned buffers: ").AppendLine(OwnedBuffersPass.StatusLine);
            sb.Append("GBuffer injection: ").AppendLine(FormatGBuffer());
            sb.Append("Catalog debug: ").AppendLine(FormatDebug());
            sb.Append("Velocity probe: ").AppendLine((cfg?.VelocityProbe ?? VelocityProbe.Off).ToString());
            AppendVelocityBuffer(sb, buf);
            return sb.ToString();
        }
    }

    /// <summary>Developer diagnostics kept separate from routine health.</summary>
    public static string DebugText
    {
        get
        {
            var cfg = Config.Current;
            var buf = VelocityRegistry.Active;
            var sb = new StringBuilder();
            var objectProof = ShaderCompileIntercept.ExactObjectProofValid;
            var flowProof = ShaderCompileIntercept.BytecodeFlowContractValid;
            var deepProof = ShaderCompileIntercept.DeepCompileProofValid;
            // The tracker-side pre-bind sample can legitimately be empty because
            // Anomaly installs Target3 at the exact native draw boundary.  The
            // authoritative proof is therefore the state queried from the D3D11
            // context for every intercepted draw, after that bind has happened.
            var targetProof = GBufferVelocity.DrawBoundaryDrawsLastFrame > 0 &&
                              GBufferVelocity.NativeDrawStateTarget3MatchLastFrame ==
                              GBufferVelocity.DrawBoundaryDrawsLastFrame &&
                              GBufferVelocity.NativeDrawStateTarget3NullLastFrame == 0 &&
                              GBufferVelocity.NativeDrawStateTarget3MismatchLastFrame == 0 &&
                              GBufferVelocity.NativeDrawStateErrorsLastFrame == 0;
            var overall = ShaderCompileIntercept.IsLive && ShaderCompileIntercept.FailureCount == 0 &&
                          objectProof && targetProof && ShaderCompileIntercept.ObjectRepairHealthy &&
                          GBufferVelocity.ResolvedPairsLastFrame > 0 &&
                          GBufferVelocity.ResolvedVerifiedPairsLastFrame == GBufferVelocity.ResolvedPairsLastFrame;

            sb.AppendLine("ANOMALY MOTION-VECTOR AUDIT");
            sb.Append("Mode ").Append(FormatDebug())
                .Append(" | probe ").Append(cfg?.VelocityProbe ?? VelocityProbe.Off)
                .Append(" | point ").AppendLine(GBufferVelocity.Target3CheckpointCompactStatus);
            AppendProof(sb, "DRAW CONTRACT (not motion proof)", overall,
                ShaderCompileIntercept.ExactObjectProofStatus +
                " pairs=" + GBufferVelocity.ResolvedVerifiedPairsLastFrame + "/" +
                GBufferVelocity.ResolvedPairsLastFrame);

            AppendProof(sb, "Compile", ShaderCompileIntercept.IsLive && ShaderCompileIntercept.FailureCount == 0,
                "runs=" + ShaderCompileIntercept.CompileCount + " fail=" + ShaderCompileIntercept.FailureCount +
                " refresh=" + ShaderCompileIntercept.ResidentRefreshStatus);
            AppendProof(sb, "Include", ShaderCompileIntercept.GBufferOverlayPresent,
                ShaderCompileIntercept.IncludeResolutionStatus);
            AppendProof(sb, "Compile endpoint", deepProof,
                ShaderCompileIntercept.DeepCompileStatus);
            AppendProof(sb, "Object proof", objectProof,
                ShaderCompileIntercept.ExactObjectProofStatus);
            AppendProof(sb, "Object repair", ShaderCompileIntercept.ObjectRepairHealthy,
                ShaderCompileIntercept.ObjectRepairStatus);
            AppendProof(sb, "DXBC VS", flowProof,
                ShaderCompileIntercept.VertexBytecodeProofStatus);
            AppendProof(sb, "DXBC PS", flowProof,
                ShaderCompileIntercept.PixelBytecodeProofStatus);

            var stage2Proof = GBufferVelocity.Stage2ValidLastFrame > 0 &&
                              GBufferVelocity.Stage2ValidLastFrame == GBufferVelocity.Stage2PackedLastFrame;
            AppendProof(sb, "CPU Stage2", stage2Proof,
                "valid/packed=" + GBufferVelocity.Stage2ValidLastFrame + "/" +
                GBufferVelocity.Stage2PackedLastFrame + " groups/drew=" +
                GBufferVelocity.Stage2GroupBindsLastFrame + "/" + GBufferVelocity.Stage2DrewLastFrame +
                " visible=" + ActorHistory.Instance.VisibleMainLast);
            AppendProof(sb, "CPU motion", GBufferVelocity.Stage2MovingSlotsLastFrame > 0,
                "slots=" + GBufferVelocity.Stage2MovingSlotsLastFrame + " max=" +
                (GBufferVelocity.Stage2MaxMotionMmLastFrame / 1000f).ToString("0.###") + "m upload=" +
                GBufferVelocity.Stage2UploadsLastFrame +
                (GBufferVelocity.Stage2UploadWasDeferred ? " deferred" : " immediate"));

            AppendProof(sb, "MRT", targetProof,
                GBufferVelocity.TargetContractStatus + " | audit " + GBufferVelocity.AuditTargetStatus);
            AppendProof(sb, "CB binds", GBufferVelocity.VelocityCbBindsLastFrame > 0,
                "mrt=" + GBufferVelocity.MrtBindsLastFrame + " b6/b7=" +
                GBufferVelocity.VelocityCbBindsLastFrame + " whole=" +
                GBufferVelocity.VelocityCbWholeBindsLastFrame + " immutable=" +
                GBufferVelocity.ImmutableProbeCbBindsLastFrame + " ring=" +
                GBufferVelocity.VelocityCbPoolPeakLastFrame + "/" + GBufferVelocity.VelocityCbPoolCapacity);
            AppendProof(sb, "Draw target", targetProof,
                "native m:n:x=" + GBufferVelocity.NativeDrawStateTarget3MatchLastFrame + ":" +
                GBufferVelocity.NativeDrawStateTarget3NullLastFrame + ":" +
                GBufferVelocity.NativeDrawStateTarget3MismatchLastFrame + " draws=" +
                GBufferVelocity.DrawBoundaryDrawsLastFrame + " tracker pre/check=" +
                GBufferVelocity.DrawBoundaryTarget3LastFrame + "/" +
                GBufferVelocity.DrawBoundaryChecksLastFrame + " rebind RT/b6=" +
                GBufferVelocity.DrawBoundaryRebindTarget3LastFrame + "/" +
                GBufferVelocity.DrawBoundaryVelocityCbRebindsLastFrame);
            AppendProof(sb, "Blend", GBufferVelocity.DrawBoundaryBlendFailuresLastFrame == 0,
                "write/mask/fix/fail=" + GBufferVelocity.DrawBoundaryWritableLastFrame + "/" +
                GBufferVelocity.DrawBoundaryMaskedLastFrame + "/" +
                GBufferVelocity.DrawBoundaryBlendOverridesLastFrame + "/" +
                GBufferVelocity.DrawBoundaryBlendFailuresLastFrame);

            sb.Append("Native CB  b6/b7 m:n:x=")
                .Append(GBufferVelocity.NativeDrawStateVsb6MatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateVsb6NullLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateVsb6MismatchLastFrame).Append(" / ")
                .Append(GBufferVelocity.NativeDrawStatePsb7MatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStatePsb7NullLastFrame).Append(':')
                .AppendLine(GBufferVelocity.NativeDrawStatePsb7MismatchLastFrame.ToString());
            sb.Append("Native shader VS/PS m:n:x=")
                .Append(GBufferVelocity.NativeDrawStateVsMatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateVsNullLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateVsMismatchLastFrame).Append(" / ")
                .Append(GBufferVelocity.NativeDrawStatePsMatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStatePsNullLastFrame).Append(':')
                .AppendLine(GBufferVelocity.NativeDrawStatePsMismatchLastFrame.ToString());
            sb.Append("Native RT  t3/t7 m:n:x=")
                .Append(GBufferVelocity.NativeDrawStateTarget3MatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateTarget3NullLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateTarget3MismatchLastFrame).Append(" / ")
                .Append(GBufferVelocity.NativeDrawStateTarget7MatchLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateTarget7NullLastFrame).Append(':')
                .Append(GBufferVelocity.NativeDrawStateTarget7MismatchLastFrame)
                .Append(" errors=").AppendLine(GBufferVelocity.NativeDrawStateErrorsLastFrame.ToString());

            AppendDrawClass(sb, "Draw VS", GBufferVelocity.DrawBoundaryCurrentVerticesLastFrame,
                GBufferVelocity.DrawBoundaryGBufferVerticesLastFrame,
                GBufferVelocity.DrawBoundaryVelocityVerticesLastFrame,
                GBufferVelocity.DrawBoundaryDepthVerticesLastFrame,
                GBufferVelocity.DrawBoundaryVerifiedVertexFlowLastFrame,
                GBufferVelocity.DrawBoundaryRetiredVerticesLastFrame);
            AppendDrawClass(sb, "Draw PS", GBufferVelocity.DrawBoundaryCurrentPixelsLastFrame,
                GBufferVelocity.DrawBoundaryGBufferPixelsLastFrame,
                GBufferVelocity.DrawBoundaryVelocityPixelsLastFrame,
                GBufferVelocity.DrawBoundaryDepthPixelsLastFrame,
                GBufferVelocity.DrawBoundaryVerifiedPixelFlowLastFrame,
                GBufferVelocity.DrawBoundaryRetiredPixelsLastFrame);
            sb.Append("Pairs seen/full/depth/other=")
                .Append(GBufferVelocity.ResolvedPairsLastFrame).Append('/')
                .Append(GBufferVelocity.ResolvedVerifiedPairsLastFrame).Append('/')
                .Append(GBufferVelocity.ResolvedDepthPairsLastFrame).Append('/')
                .Append(GBufferVelocity.ResolvedOtherPairsLastFrame)
                .Append(" | clear=").AppendLine(GBufferVelocity.PassEndClearsLastFrame.ToString());

            sb.Append("Frame ").AppendLine(FormatTemporal());
            sb.Append("History actors/local/bones=").Append(ActorHistory.Instance.TrackedActorCount).Append('/')
                .Append(ActorHistory.Instance.TrackedLocalCount).Append('/')
                .AppendLine(BoneHistory.Instance.TrackedCount.ToString());
            AppendVelocityBufferCompact(sb, buf);
            return sb.ToString();
        }
    }

    static void AppendProof(StringBuilder sb, string label, bool pass, string detail)
    {
        sb.Append(pass ? "[PASS] " : "[WARN] ").Append(label).Append(" | ").AppendLine(detail);
    }

    static void AppendDrawClass(StringBuilder sb, string label, int all, int gbuffer,
        int velocity, int depth, int full, int retired)
    {
        sb.Append(label).Append(" all/g/v/d/full/old=")
            .Append(all).Append('/').Append(gbuffer).Append('/').Append(velocity).Append('/')
            .Append(depth).Append('/').Append(full).Append('/').AppendLine(retired.ToString());
    }

    static void AppendVelocityBufferCompact(StringBuilder sb, IVelocityBuffer buf)
    {
        if (buf == null || !buf.IsAvailable)
        {
            sb.AppendLine("Velocity buffer unavailable");
            return;
        }
        sb.Append("Velocity ").Append(buf.Width).Append('x').Append(buf.Height)
            .Append(" | history=").Append(buf.HistoryValid ? "yes" : "no")
            .Append(" | ").AppendLine(buf.Convention.ToString());
    }

    static void AppendVelocityBuffer(StringBuilder sb, IVelocityBuffer buf)
    {
        if (buf == null || !buf.IsAvailable)
        {
            sb.AppendLine("Velocity buffer: unavailable");
            return;
        }

        sb.Append("Velocity buffer: ").Append(buf.Width).Append('x').Append(buf.Height).AppendLine();
        sb.Append("Convention: ").AppendLine(buf.Convention.ToString());
        sb.Append("History valid: ").AppendLine(buf.HistoryValid ? "yes" : "no");
    }

    static string FormatInterceptSummary()
    {
        if (!ShaderCompileIntercept.IsLive)
        {
            var err = ShaderCompileIntercept.LastError;
            return string.IsNullOrEmpty(err) ? "not live" : "not live (" + err + ")";
        }

        var failures = ShaderCompileIntercept.FailureCount;
        return failures == 0
            ? "live"
            : "live (" + failures + " compile failures)";
    }

    static string FormatIntercept()
    {
        if (!ShaderCompileIntercept.IsLive)
        {
            var err = ShaderCompileIntercept.LastError;
            return string.IsNullOrEmpty(err) ? "not live" : "not live (" + err + ")";
        }

        var path = ShaderCompileIntercept.IncludeDirectory ?? "?";
        return "live  include=" + path
            + "  " + ShaderCompileIntercept.MacroName + "=" + ShaderCompileIntercept.MacroValue
            + "  compiles=" + ShaderCompileIntercept.CompileCount
            + "  fails=" + ShaderCompileIntercept.FailureCount
            + "  resident=" + ShaderCompileIntercept.ResidentRefreshStatus;
    }

    static string FormatTemporal()
    {
        return "frame=" + FrameTemporal.FrameIndex
            + " jitter=" + FrameTemporal.JitterX.ToString("0.###") + "," +
            FrameTemporal.JitterY.ToString("0.###")
            + " history=" + (FrameTemporal.HistoryValid ? "yes" : "no");
    }

    static string FormatCameraPass()
    {
        if (!CameraVelocityPass.Enabled)
            return "disabled";
        if (!string.IsNullOrEmpty(CameraVelocityPass.LastError))
            return "error (" + CameraVelocityPass.LastError + ")";
        if (!CameraVelocityPass.ShadersReady)
            return "shaders not ready";
        return "live";
    }

    static string FormatDebug()
    {
        var cfg = Config.Current;
        if (cfg == null)
            return "off";
        var mode = cfg.DebugBuffer != DebugBuffer.Off
            ? cfg.DebugBuffer.ToString()
            : cfg.DebugVelocity ? "Velocity (legacy)" : null;
        if (mode == null)
            return "off";
        var err = VelocityDebugPass.LastError;
        if (!string.IsNullOrEmpty(err))
            return mode + " (" + err + ")";
        return mode + "  scale=" + cfg.DebugVelocityScale + "px";
    }

    static string FormatGBuffer()
    {
        if (!GBufferVelocity.Enabled)
            return "disabled";
        if (!string.IsNullOrEmpty(GBufferVelocity.LastError))
            return "error (" + GBufferVelocity.LastError + ")";
        if (!GBufferVelocity.IsLive)
            return "not live";
        return "live";
    }
}
