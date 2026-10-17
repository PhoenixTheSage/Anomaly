using System.Globalization;
using System.Linq;
using System.Text;
using ClientPlugin.Velocity;

namespace ClientPlugin.Backend;

/// <summary>
/// Show Status and Velocity Status while the DX11 velocity report is not
/// loaded. Section labels match that report. Values come from the guest bus.
/// </summary>
public static class FrameworkStatus
{
    public static string CurrentText => OperationalText;

    public static string OperationalText
    {
        get
        {
            var cfg = global::ClientPlugin.Config.Current;
            var sb = new StringBuilder();
            sb.AppendLine("Anomaly Shader Framework");
            sb.AppendLine(RenderHost.StatusLine);
            sb.Append("Compile intercept: ").AppendLine(GuestPassList.CompileStatus());
            if (GuestCompiler.HasPending && !string.IsNullOrEmpty(GuestCompiler.LastError))
                sb.Append("Guest compiler: ").AppendLine(GuestCompiler.LastError.Split('\r', '\n')[0]);
            sb.Append("Keen GBuffer patch: ").AppendLine(RendererRoute.UseGuestImages ? "guest attachments" : "pending");
            sb.Append("Rich HUD: ").AppendLine(global::ClientPlugin.RichHud.RichHudSupport.StatusLine);
            sb.Append("Shader packs: ").AppendLine(GuestPassList.PackStatus());
            sb.Append("GBuffer attachments: ").AppendLine(GuestPassList.AttachmentStatus());
            sb.Append("Pass binds: ").AppendLine(GuestPassList.BindStatus());
            sb.Append("Owned passes: ").AppendLine(GuestPassList.OwnedPassStatus());
            sb.Append("Color bus: ").AppendLine(GuestColor.Open ? GuestColor.ColorStatus : "upscale=none upscaledColor=none display=no dest=none");
            sb.Append("Fullscreen: ").AppendLine(GuestPassList.FullscreenStatus());
            sb.Append("Owned buffers: ").AppendLine(GuestPassList.OwnedBufferStatus());
            sb.Append("Shared volumes: ").AppendLine(global::ClientPlugin.Shaders.VolumetricMediumRegistry.StatusLine);
            sb.Append("Velocity service: ").Append(cfg != null ? cfg.VelocitySource.ToString() : "GBuffer")
                .Append(" | GBuffer ").Append(FormatGBuffer(cfg))
                .Append(" | camera ").AppendLine(GuestPassList.CameraStatus());
            sb.Append("Published velocity: ");
            AppendVelocity(sb);
            return sb.ToString();
        }
    }

    public static string VelocityText
    {
        get
        {
            var cfg = global::ClientPlugin.Config.Current;
            int width, height;
            bool history = Motion(out width, out height);
            var sb = new StringBuilder();
            sb.AppendLine("ANOMALY VELOCITY STATUS");
            sb.Append("Output: ");
            AppendVelocity(sb);
            sb.Append("Producer: ").Append(cfg != null ? cfg.VelocitySource.ToString() : "GBuffer")
                .Append(" | GBuffer ").Append(FormatGBuffer(cfg))
                .Append(" | camera ").AppendLine(GuestPassList.CameraStatus());
            sb.Append("Visualization: ").AppendLine(FormatDebug(cfg));
            sb.Append("Proofing: probe=").Append(cfg != null ? cfg.VelocityProbe.ToString() : "Off")
                .Append(" checkpoint=").AppendLine(cfg != null ? cfg.Target3Checkpoint.ToString() : "Live");
            sb.Append("Frame: ").AppendLine(FormatTemporal());
            sb.Append("History: actors/local/bones=motion ")
                .Append(width).Append('x').Append(height).AppendLine();
            sb.Append("Object history: valid/packed=")
                .Append(history ? "yes" : "no").Append("/renderer visible=renderer").AppendLine();
            sb.Append("Observed object motion: slots=renderer max=")
                .Append(width).Append('x').Append(height).AppendLine();

            bool diagnostics = cfg != null && (cfg.DebugBuffer == global::ClientPlugin.DebugBuffer.VelocityPipelineAudit ||
                cfg.VelocityProbe != global::ClientPlugin.VelocityProbe.Off ||
                cfg.Target3Checkpoint != global::ClientPlugin.Target3Checkpoint.Live);
            if (!diagnostics)
            {
                sb.AppendLine("Audit proofing is idle. Enable a probe, checkpoint, or Pipeline Audit only when diagnosing.");
                return sb.ToString();
            }

            int pipelines, failures;
            GuestPassList.CountPrograms(out pipelines, out failures);
            sb.AppendLine();
            sb.AppendLine("ACTIVE AUDIT PROOF");
            AppendProof(sb, "Shader route", pipelines > 0 && failures == 0,
                "pipelines=" + pipelines + " failed=" + failures);
            AppendProof(sb, "Target3 writes", width > 0 && height > 0 && GuestFrame.Open,
                "motion=" + width + "x" + height);
            AppendProof(sb, "Blend", failures == 0, "guest compile failures=" + failures);
            sb.Append("Resolved attachments=").AppendLine(GuestPassList.AttachmentCount().ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }

    static string FormatGBuffer(global::ClientPlugin.Config cfg)
    {
        if (cfg != null && cfg.VelocitySource == VelocitySource.CameraOnly)
            return "disabled";
        return GuestFrame.Open ? "frame bus connected (writes unverified)" : "not live";
    }

    static string FormatDebug(global::ClientPlugin.Config cfg)
    {
        if (cfg == null || cfg.DebugBuffer == global::ClientPlugin.DebugBuffer.Off)
            return "off";
        string persistence = cfg.DebugMotionPersistence ? "PERSISTENT (screen-space trails)" : "LIVE";
        string line = cfg.DebugBuffer + "  scale=" + cfg.DebugVelocityScale + "px | current-to-previous | " + persistence;
        if (cfg.DebugMotionPersistence)
            line += " gain=" + cfg.DebugMotionGain + " fade=" + cfg.DebugMotionHalfLife + "s (report counters are live)";
        return line;
    }

    static string FormatTemporal()
    {
        if (!GuestFrame.Open)
            return "frame=0 jitter=0,0 history=no";
        return "frame=" + GuestFrame.U32(16).ToString(CultureInfo.InvariantCulture)
            + " jitter=" + GuestFrame.F32(24).ToString("0.###", CultureInfo.InvariantCulture)
            + "," + GuestFrame.F32(28).ToString("0.###", CultureInfo.InvariantCulture)
            + " history=" + (GuestFrame.History ? "yes" : "no");
    }

    static bool Motion(out int width, out int height)
    {
        width = GuestFrame.Open ? (int)GuestFrame.U32(8) : 0;
        height = GuestFrame.Open ? (int)GuestFrame.U32(12) : 0;
        if (width <= 0 || height <= 0)
            GuestPassList.ImageSize("CameraVelocity", out width, out height);
        return GuestFrame.Open && GuestFrame.History;
    }

    static void AppendVelocity(StringBuilder sb)
    {
        int width, height;
        bool history = Motion(out width, out height);
        if (width <= 0 || height <= 0)
        {
            sb.AppendLine("Velocity buffer unavailable");
            return;
        }
        sb.Append("Velocity ").Append(width).Append('x').Append(height)
            .Append(GuestPassList.Snapshot().Any(p => p.Id == "anomaly.owned.CameraVelocity" && p.Submissions > 0)
                ? " | catalog submitted" : " | renderer motion fallback")
            .Append(" | history=").Append(history ? "yes" : "no")
            .AppendLine(" | Unjittered, PixelSpace, MatchesRenderResolution, CurrentToPrevious");
    }

    static void AppendProof(StringBuilder sb, string label, bool pass, string detail)
    {
        sb.Append(pass ? "[PASS] " : "[WARN] ").Append(label).Append(" | ").AppendLine(detail);
    }
}
