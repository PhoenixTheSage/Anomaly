using System.Collections.Generic;
using System.Text;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Settings.Tools;

internal static class DescriptionToolTip
{
    const float MaxWidth = 0.4f;
    const float TextScale = 0.7f;
    const string Font = "Blue";

    static readonly StringBuilder MeasureBuffer = new StringBuilder(256);

    public static void Apply(MyGuiControlBase control, string description)
    {
        if (control == null || string.IsNullOrWhiteSpace(description))
            return;

        control.SetToolTip(Wrap(description));
    }

    public static MyToolTips Wrap(string description)
    {
        var tips = new MyToolTips();
        foreach (var line in WrapLines(description, MaxWidth, TextScale, Font))
            tips.AddToolTip(line, TextScale, Font);
        return tips;
    }

    public static string WrapText(string description, float maxWidth, float textScale = TextScale, string font = Font)
    {
        if (string.IsNullOrEmpty(description))
            return description;

        var sb = new StringBuilder();
        var first = true;
        foreach (var line in WrapLines(description, maxWidth, textScale, font))
        {
            if (!first)
                sb.Append('\n');
            first = false;
            sb.Append(line);
        }

        return sb.ToString();
    }

    static IEnumerable<string> WrapLines(string description, float maxWidth, float textScale, string font)
    {
        foreach (var paragraph in description.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' '))
            {
                if (word.Length == 0)
                    continue;

                if (Measure(word, textScale, font) > maxWidth)
                {
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    foreach (var chunk in BreakWord(word, maxWidth, textScale, font))
                        yield return chunk;
                    continue;
                }

                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate, textScale, font) > maxWidth)
                {
                    yield return line.ToString();
                    line.Clear();
                    line.Append(word);
                }
                else
                {
                    if (line.Length > 0)
                        line.Append(' ');
                    line.Append(word);
                }
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }

    static IEnumerable<string> BreakWord(string word, float maxWidth, float textScale, string font)
    {
        var chunk = new StringBuilder();
        foreach (var ch in word)
        {
            chunk.Append(ch);
            if (Measure(chunk.ToString(), textScale, font) > maxWidth && chunk.Length > 1)
            {
                chunk.Length--;
                yield return chunk.ToString();
                chunk.Clear();
                chunk.Append(ch);
            }
        }

        if (chunk.Length > 0)
            yield return chunk.ToString();
    }

    static float Measure(string text, float textScale, string font)
    {
        MeasureBuffer.Clear();
        MeasureBuffer.Append(text);
        return MyGuiManager.MeasureString(font, MeasureBuffer, textScale).X;
    }
}
