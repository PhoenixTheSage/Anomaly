using System.Text;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;

namespace ClientPlugin.Velocity;

public static class VelocityStatus
{
    public static string CurrentText
    {
        get
        {
            var cfg = Config.Current;
            var buf = VelocityRegistry.Active;
            var sb = new StringBuilder();
            sb.AppendLine("Anomaly Shader Framework");
            sb.Append("Velocity source: ").AppendLine(cfg != null ? cfg.VelocitySource.ToString() : "—");
            sb.Append("Compile intercept: ").AppendLine(FormatIntercept());
            sb.Append("GBuffer overlay: ").AppendLine(ShaderCompileIntercept.GBufferOverlayPresent ? "present" : "missing");
            sb.Append("Shader packs: ").AppendLine(ShaderPackRegistry.StatusLine);
            sb.Append("GBuffer attachments: ").AppendLine(GBufferAttachments.StatusLine);
            sb.Append("Camera velocity: ").AppendLine(FormatCameraPass());
            sb.Append("GBuffer injection: ").AppendLine(FormatGBuffer());
            sb.Append("Velocity debug: ").AppendLine(FormatDebug());
            sb.Append("History actors: ").AppendLine(ActorHistory.Instance.TrackedActorCount.ToString());
            sb.Append("History bones: ").AppendLine(BoneHistory.Instance.TrackedCount.ToString());
            if (buf == null || !buf.IsAvailable)
            {
                sb.AppendLine("Velocity buffer: unavailable");
                return sb.ToString();
            }

            sb.Append("Velocity buffer: ").Append(buf.Width).Append('x').Append(buf.Height).AppendLine();
            sb.Append("Convention: ").AppendLine(buf.Convention.ToString());
            sb.Append("History valid: ").AppendLine(buf.HistoryValid ? "yes" : "no");
            return sb.ToString();
        }
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
            + "  fails=" + ShaderCompileIntercept.FailureCount;
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
        if (cfg == null || !cfg.DebugVelocity)
            return "off";
        var err = VelocityDebugPass.LastError;
        if (!string.IsNullOrEmpty(err))
            return "on (" + err + ")";
        return "on  scale=" + cfg.DebugVelocityScale + "px";
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
