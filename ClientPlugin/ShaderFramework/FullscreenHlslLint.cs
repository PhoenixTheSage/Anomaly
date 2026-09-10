using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using VRage.Utils;

namespace ClientPlugin.Shaders;

/// <summary>
/// Load-time scan of pack fullscreen HLSL. Warns on <c>while</c> and
/// <c>for</c> bounds that are not an integer literal or <c>#define</c>
/// integer. Does not fail compile — denoisers and owned passes are out
/// of scope (Anomaly only lints <c>Fullscreen/</c> programs it compiles).
/// </summary>
static class FullscreenHlslLint
{
    static readonly Regex DefineInt = new(
        @"^\s*#define\s+([A-Za-z_]\w*)\s+\(?(\d+)",
        RegexOptions.CultureInvariant);
    static readonly Regex WhileLoop = new(
        @"\bwhile\s*\(",
        RegexOptions.CultureInvariant);
    static readonly Regex ForLoop = new(
        @"\bfor\s*\(",
        RegexOptions.CultureInvariant);
    static readonly Regex LineComment = new(
        @"//.*$",
        RegexOptions.CultureInvariant);

    internal static void WarnIfUnbounded(string programId, string file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
            return;
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("Anomaly fullscreen lint id=" + programId +
                                    " read failed: " + e.Message);
            return;
        }

        text = StripBlockComments(text);
        var caps = new HashSet<string>(StringComparer.Ordinal);
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = LineComment.Replace(lines[i], string.Empty);
            var def = DefineInt.Match(line);
            if (def.Success)
                caps.Add(def.Groups[1].Value);
            if (WhileLoop.IsMatch(line))
            {
                Warn(programId, "while-loop without a compile-time cap");
                return;
            }

            if (!ForLoop.IsMatch(line))
                continue;
            var cond = ForCondition(line, lines, i);
            if (string.IsNullOrEmpty(cond) || HasIntegerCap(cond, caps))
                continue;
            Warn(programId, "dynamic for-bound (no integer literal or #define cap)");
            return;
        }
    }

    static string ForCondition(string line, string[] lines, int index)
    {
        var acc = line;
        var guard = 0;
        while (CountChar(acc, ';') < 2 && index + 1 < lines.Length && guard < 4)
        {
            index++;
            guard++;
            acc += " " + LineComment.Replace(lines[index], string.Empty);
        }

        var open = acc.IndexOf('(');
        if (open < 0)
            return null;
        var first = acc.IndexOf(';', open + 1);
        if (first < 0)
            return null;
        var second = acc.IndexOf(';', first + 1);
        if (second < 0)
            return null;
        return acc.Substring(first + 1, second - first - 1);
    }

    static bool HasIntegerCap(string cond, HashSet<string> caps)
    {
        for (var i = 0; i < cond.Length; i++)
        {
            if (char.IsDigit(cond[i]))
                return true;
        }

        var ident = 0;
        while (ident < cond.Length)
        {
            if (!IsIdentStart(cond[ident]))
            {
                ident++;
                continue;
            }

            var end = ident + 1;
            while (end < cond.Length && IsIdentPart(cond[end]))
                end++;
            if (caps.Contains(cond.Substring(ident, end - ident)))
                return true;
            ident = end;
        }

        return false;
    }

    static bool IsIdentStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    static bool IsIdentPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    static int CountChar(string s, char c)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == c)
                n++;
        }

        return n;
    }

    static string StripBlockComments(string text)
    {
        var start = 0;
        while (true)
        {
            var open = text.IndexOf("/*", start, StringComparison.Ordinal);
            if (open < 0)
                return text;
            var close = text.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0)
                return text.Substring(0, open);
            text = text.Remove(open, close + 2 - open);
            start = open;
        }
    }

    static void Warn(string programId, string reason)
    {
        MyLog.Default.WriteLine("Anomaly fullscreen lint id=" + programId + ": " + reason);
    }
}
