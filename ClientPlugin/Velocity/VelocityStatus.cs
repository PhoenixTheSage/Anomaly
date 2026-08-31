using System.Text;

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
            sb.Append("GBuffer injection: ").AppendLine("not live");
            sb.Append("Owned raster: ").AppendLine("not live");
            sb.Append("History actors: ").AppendLine("0");
            if (buf == null || !buf.IsAvailable)
            {
                sb.AppendLine("Velocity buffer: unavailable (compile-and-load stub)");
                return sb.ToString();
            }

            sb.Append("Velocity buffer: ").Append(buf.Width).Append('x').Append(buf.Height).AppendLine();
            sb.Append("Convention: ").AppendLine(buf.Convention.ToString());
            sb.Append("History valid: ").AppendLine(buf.HistoryValid ? "yes" : "no");
            return sb.ToString();
        }
    }
}
