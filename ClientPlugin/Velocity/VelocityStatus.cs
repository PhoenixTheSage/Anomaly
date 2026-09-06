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
            sb.Append("Compile intercept: ").AppendLine(FormatInterceptSummary());
            sb.Append("Rich HUD: ").AppendLine(RichHudSupport.StatusLine);
            sb.Append("Shader packs: ").AppendLine(ShaderPackRegistry.StatusLine);
            sb.Append("GBuffer attachments: ").AppendLine(GBufferAttachments.StatusLine);
            sb.Append("Pass binds: ").AppendLine(ShaderBindRegistry.StatusLine);
            sb.Append("Owned passes: ").AppendLine(OwnedPassRegistry.StatusLine);
            sb.Append("Fullscreen: ").AppendLine(FullscreenPassRegistry.StatusLine);
            sb.Append("Owned buffers: ").AppendLine(OwnedBuffersPass.StatusLine);
            sb.Append("Velocity service: ").Append(cfg != null ? cfg.VelocitySource.ToString() : "—")
                .Append(" | GBuffer ").Append(FormatGBuffer())
                .Append(" | camera ").AppendLine(FormatCameraPass());
            sb.Append("Published velocity: ");
            AppendVelocityBufferCompact(sb, buf);
            return sb.ToString();
        }
    }

    /// <summary>Velocity-only health, visualization state, and opt-in proofing.</summary>
    public static string VelocityText
    {
        get
        {
            var cfg = Config.Current;
            var buf = VelocityRegistry.Active;
            var sb = new StringBuilder();
            sb.AppendLine("ANOMALY VELOCITY STATUS");
            sb.Append("Output: ");
            AppendVelocityBufferCompact(sb, buf);
            sb.Append("Producer: ").Append(cfg?.VelocitySource ?? VelocitySource.GBuffer)
                .Append(" | GBuffer ").Append(FormatGBuffer())
                .Append(" | camera ").AppendLine(FormatCameraPass());
            sb.Append("Visualization: ").AppendLine(FormatDebug());
            sb.Append("Proofing: probe=").Append(cfg?.VelocityProbe ?? VelocityProbe.Off)
                .Append(" checkpoint=").AppendLine(GBufferVelocity.Target3CheckpointCompactStatus);
            sb.Append("Frame: ").AppendLine(FormatTemporal());
            sb.Append("History: actors/local/bones=").Append(ActorHistory.Instance.TrackedActorCount).Append('/')
                .Append(ActorHistory.Instance.TrackedLocalCount).Append('/')
                .AppendLine(BoneHistory.Instance.TrackedCount.ToString());
            sb.Append("Object history: valid/packed=").Append(GBufferVelocity.Stage2ValidLastFrame).Append('/')
                .Append(GBufferVelocity.Stage2PackedLastFrame).Append(" visible=")
                .AppendLine(ActorHistory.Instance.VisibleMainLast.ToString());
            sb.Append("Observed object motion: slots=").Append(GBufferVelocity.Stage2MovingSlotsLastFrame)
                .Append(" max=").Append((GBufferVelocity.Stage2MaxMotionMmLastFrame / 1000f).ToString("0.###"))
                .AppendLine("m");

            var diagnostics = cfg != null && (cfg.DebugBuffer == DebugBuffer.VelocityPipelineAudit ||
                cfg.VelocityProbe != VelocityProbe.Off || cfg.Target3Checkpoint != Target3Checkpoint.Live);
            if (!diagnostics)
            {
                sb.AppendLine("Audit proofing is idle. Enable a probe, checkpoint, or Pipeline Audit only when diagnosing.");
                return sb.ToString();
            }

            var objectProof = ShaderCompileIntercept.ExactObjectProofValid;
            var targetProof = GBufferVelocity.DrawBoundaryDrawsLastFrame > 0 &&
                              GBufferVelocity.NativeDrawStateTarget3MatchLastFrame == GBufferVelocity.DrawBoundaryDrawsLastFrame &&
                              GBufferVelocity.NativeDrawStateTarget3NullLastFrame == 0 &&
                              GBufferVelocity.NativeDrawStateTarget3MismatchLastFrame == 0 &&
                              GBufferVelocity.NativeDrawStateErrorsLastFrame == 0;
            sb.AppendLine();
            sb.AppendLine("ACTIVE AUDIT PROOF");
            AppendProof(sb, "Shader route", objectProof && ShaderCompileIntercept.BytecodeFlowContractValid,
                ShaderCompileIntercept.ExactObjectProofStatus);
            AppendProof(sb, "Target3 writes", targetProof,
                "match/null/mismatch=" + GBufferVelocity.NativeDrawStateTarget3MatchLastFrame + "/" +
                GBufferVelocity.NativeDrawStateTarget3NullLastFrame + "/" +
                GBufferVelocity.NativeDrawStateTarget3MismatchLastFrame + " draws=" + GBufferVelocity.DrawBoundaryDrawsLastFrame);
            AppendProof(sb, "Blend", GBufferVelocity.DrawBoundaryBlendFailuresLastFrame == 0,
                "write/fixed/fail=" + GBufferVelocity.DrawBoundaryWritableLastFrame + "/" +
                GBufferVelocity.DrawBoundaryBlendOverridesLastFrame + "/" + GBufferVelocity.DrawBoundaryBlendFailuresLastFrame);
            sb.Append("Resolved pairs full/depth/other=")
                .Append(GBufferVelocity.ResolvedPairsLastFrame).Append('/')
                .Append(GBufferVelocity.ResolvedVerifiedPairsLastFrame).Append('/')
                .Append(GBufferVelocity.ResolvedDepthPairsLastFrame).Append('/')
                .AppendLine(GBufferVelocity.ResolvedOtherPairsLastFrame.ToString());
            return sb.ToString();
        }
    }

    static void AppendProof(StringBuilder sb, string label, bool pass, string detail)
    {
        sb.Append(pass ? "[PASS] " : "[WARN] ").Append(label).Append(" | ").AppendLine(detail);
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
        var mode = cfg.DebugBuffer != DebugBuffer.Off ? cfg.DebugBuffer.ToString() : null;
        if (mode == null)
            return "off";
        var err = VelocityDebugPass.LastError;
        if (!string.IsNullOrEmpty(err))
            return mode + " (" + err + ")";
        return mode + "  scale=" + cfg.DebugVelocityScale + "px | current-to-previous | " + VelocityDebugPass.PersistenceStatus +
            (cfg.DebugMotionPersistence ? " gain=" + cfg.DebugMotionGain + " fade=" + cfg.DebugMotionHalfLife + "s (report counters are live)" : "");
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
