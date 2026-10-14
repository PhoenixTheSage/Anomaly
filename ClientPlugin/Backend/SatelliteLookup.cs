using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ClientPlugin.Backend;

/// <summary>
/// Finds a DX11 satellite DLL. Pulsar -Sources compiles the core into
/// DevFolder\&lt;id&gt;\Bin, which does not contain the satellite. The built
/// file stays in the plugin's ClientPlugin\bin or in Pulsar Local.
/// </summary>
public static class SatelliteLookup
{
    static readonly Regex PluginPair = new Regex(
        "<Folder>\\s*(?<folder>[^<]+)</Folder>\\s*<File>\\s*(?<file>[^<]+)</File>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex PluginId = new Regex(
        "<Id>\\s*(?<id>[^<]+)</Id>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Find(string fileName, Assembly caller)
    {
        string dir = DirectoryOf(caller);
        string beside = string.IsNullOrEmpty(dir) ? fileName : Path.Combine(dir, fileName);
        if (File.Exists(beside))
            return beside;

        foreach (var path in Candidates(dir, fileName))
        {
            try
            {
                if (File.Exists(path))
                    return path;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return beside;
    }

    static IEnumerable<string> Candidates(string assemblyDir, string fileName)
    {
        string edition = EditionRoot(assemblyDir);
        string pulsar = string.IsNullOrEmpty(edition) ? "" : Path.GetDirectoryName(edition);
        bool modern = Environment.Version.Major >= 5;
        string[] tfms = modern ? new[] { "net10.0", "net48" } : new[] { "net48", "net10.0" };
        string devId = DevId(assemblyDir);

        foreach (var root in SourceRoots(edition, pulsar))
        {
            if (!string.IsNullOrEmpty(devId) && !IdMatches(root.File, devId))
                continue;
            foreach (var tfm in tfms)
                foreach (var config in new[] { "Debug", "Release" })
                    yield return Path.Combine(root.Folder, "ClientPlugin", "bin", config, tfm, fileName);
        }

        if (!string.IsNullOrEmpty(edition))
            yield return Path.Combine(edition, "Local", fileName);
        if (!string.IsNullOrEmpty(pulsar))
        {
            yield return Path.Combine(pulsar, "Interim", "Local", fileName);
            yield return Path.Combine(pulsar, "Legacy", "Local", fileName);
        }
    }

    static bool IdMatches(string pluginFile, string devId)
    {
        try
        {
            if (!File.Exists(pluginFile))
                return false;
            var match = PluginId.Match(File.ReadAllText(pluginFile));
            if (!match.Success)
                return false;
            string id = match.Groups["id"].Value.Trim();
            return devId.StartsWith(id, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static IEnumerable<(string Folder, string File)> SourceRoots(string edition, string pulsar)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sources in new[]
        {
            Path.Combine(edition ?? "", "Sources", "sources.xml"),
            Path.Combine(pulsar ?? "", "Interim", "Sources", "sources.xml"),
            Path.Combine(pulsar ?? "", "Legacy", "Sources", "sources.xml"),
        })
        {
            if (string.IsNullOrEmpty(sources) || !File.Exists(sources) || !seen.Add(sources))
                continue;
            string text;
            try { text = File.ReadAllText(sources); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (Match match in PluginPair.Matches(text))
            {
                string folder = match.Groups["folder"].Value.Trim();
                string file = match.Groups["file"].Value.Trim();
                if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file))
                    continue;
                yield return (folder, Path.Combine(folder, file));
            }
        }
    }

    static string EditionRoot(string assemblyDir)
    {
        // .../Interim/DevFolder/<id>/Bin
        var bin = string.IsNullOrEmpty(assemblyDir) ? null : new DirectoryInfo(assemblyDir);
        var id = bin?.Parent;
        var dev = id?.Parent;
        if (dev != null && string.Equals(dev.Name, "DevFolder", StringComparison.OrdinalIgnoreCase))
            return dev.Parent?.FullName;
        return "";
    }

    static string DevId(string assemblyDir)
    {
        var bin = string.IsNullOrEmpty(assemblyDir) ? null : new DirectoryInfo(assemblyDir);
        var id = bin?.Parent;
        var dev = id?.Parent;
        if (dev != null && string.Equals(dev.Name, "DevFolder", StringComparison.OrdinalIgnoreCase))
            return id.Name;
        return "";
    }

    static string DirectoryOf(Assembly assembly)
    {
        try
        {
            return Path.GetDirectoryName(assembly?.Location) ?? "";
        }
        catch (NotSupportedException)
        {
            return "";
        }
    }
}
