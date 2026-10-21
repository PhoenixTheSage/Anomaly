using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ClientPlugin.Backend;

/// <summary>Immutable, device-free manifest consumed by both renderer adapters.</summary>
public sealed class PackDefinition
{
    static readonly object ExtractionGate = new object();
    public static string ResolveRoot(string root, string cacheRoot = null)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) return root;
        if (!File.Exists(root) || !root.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pack root is unavailable: " + root);
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(root)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        string extract = Path.Combine(cacheRoot ?? Path.Combine(Path.GetTempPath(), "anomaly-pack-cache"), hash);
        lock (ExtractionGate)
        {
            string complete = Path.Combine(extract, ".anomaly-extracted");
            if (!File.Exists(complete))
            {
                Directory.CreateDirectory(extract);
                using (var archive = ZipFile.OpenRead(root))
                    foreach (var entry in archive.Entries)
                    {
                        string path = ResolveFile(extract, entry.FullName);
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(path); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var source = entry.Open()) using (var output = File.Create(path)) source.CopyTo(output);
                    }
                File.WriteAllText(complete, hash);
            }
            if (File.Exists(Path.Combine(extract, "anomaly.json"))) return extract;
            foreach (string folder in Directory.GetDirectories(extract).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                if (File.Exists(Path.Combine(folder, "anomaly.json"))) return folder;
        }
        throw new InvalidDataException("Pack archive contains no anomaly.json: " + root);
    }
    public string Id { get; }
    public string Name { get; }
    public int Priority { get; }
    public IReadOnlyList<string> Exclusive { get; }
    public IReadOnlyList<string> Defines { get; }
    public IReadOnlyList<PackPassDefinition> Passes { get; }
    public IReadOnlyList<PackAttachmentDefinition> Attachments { get; }

    PackDefinition(XElement root, string fallbackId)
    {
        Object(root);
        Id = Text(root, "id") ?? fallbackId;
        if (string.IsNullOrWhiteSpace(Id)) throw new InvalidDataException("Pack manifest requires an id.");
        Name = Text(root, "name");
        Priority = Integer(root, "priority") ?? 0;
        Exclusive = Strings(root, "exclusive");
        Defines = Strings(root, "defines");
        Passes = Array.AsReadOnly(Items(root, "passes").Select(p => new PackPassDefinition(p)).ToArray());
        Attachments = Array.AsReadOnly(Items(root, "attachments").Select(a => new PackAttachmentDefinition(a)).ToArray());
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pass in Passes)
            if (pass.Id != null && !ids.Add(pass.Id)) throw new InvalidDataException("Duplicate pass id: " + pass.Id);
    }

    public static PackDefinition Parse(string json, string fallbackId = null)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Pack manifest is empty.");
        if (json.Length > 16 * 1024 * 1024) throw new InvalidDataException("Pack manifest exceeds 16 MiB.");
        try
        {
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = 16 * 1024 * 1024, MaxArrayLength = 262144 };
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), quotas))
                return new PackDefinition(XElement.Load(reader), fallbackId);
        }
        catch (XmlException error) { throw new InvalidDataException("Invalid pack JSON: " + error.Message, error); }
        catch (FormatException error) { throw new InvalidDataException("Invalid manifest value: " + error.Message, error); }
    }

    public static string ResolveFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
            throw new InvalidDataException("Pack shader path must be relative: " + relative);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack shader path escapes its root: " + relative);
        return full;
    }

    internal static void Object(XElement value)
    {
        if ((string)value.Attribute("type") != "object") throw new InvalidDataException("Manifest entry must be an object.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in value.Elements())
            if (!keys.Add(child.Name.LocalName)) throw new InvalidDataException("Duplicate manifest field: " + child.Name.LocalName);
    }

    internal static string Text(XElement value, string key, bool required = false)
    {
        var field = value.Element(key);
        if (field == null || (string)field.Attribute("type") == "null")
        {
            if (required) throw new InvalidDataException("Missing manifest field: " + key);
            return null;
        }
        if ((string)field.Attribute("type") != "string" || (required && string.IsNullOrWhiteSpace(field.Value)))
            throw new InvalidDataException("Manifest field must be a string: " + key);
        return field.Value;
    }

    internal static int? Integer(XElement value, string key)
    {
        var field = value.Element(key);
        if (field == null || (string)field.Attribute("type") == "null") return null;
        int result;
        if ((string)field.Attribute("type") != "number" || !int.TryParse(field.Value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out result)) throw new InvalidDataException("Manifest field must be an integer: " + key);
        return result;
    }

    internal static IEnumerable<XElement> Items(XElement value, string key)
    {
        var field = value.Element(key);
        if (field == null || (string)field.Attribute("type") == "null") return Enumerable.Empty<XElement>();
        if ((string)field.Attribute("type") != "array") throw new InvalidDataException("Manifest field must be an array: " + key);
        return field.Elements();
    }

    internal static IReadOnlyList<string> Strings(XElement value, string key)
    {
        return Array.AsReadOnly(Items(value, key).Select(item =>
        {
            if ((string)item.Attribute("type") != "string") throw new InvalidDataException("Manifest array must contain strings: " + key);
            return item.Value;
        }).ToArray());
    }
}

public sealed class PackPassDefinition
{
    public string Id { get; }
    public string Slot { get; }
    public string File { get; }
    public string Compose { get; }
    public int? Priority { get; }
    public IReadOnlyList<string> Temporal { get; }
    public string Output { get; }
    public string History { get; }
    public float Scale { get; }
    public IReadOnlyList<PackBindingDefinition> Binds { get; }
    internal PackPassDefinition(XElement value)
    {
        PackDefinition.Object(value);
        Id = PackDefinition.Text(value, "id");
        Slot = PackDefinition.Text(value, "slot", true);
        File = PackDefinition.Text(value, "file", true);
        Compose = PackDefinition.Text(value, "compose") ?? "IsolatedAdd";
        Priority = PackDefinition.Integer(value, "priority");
        Temporal = PackDefinition.Strings(value, "temporal");
        Output = PackDefinition.Text(value, "output");
        History = PackDefinition.Text(value, "history");
        var scale = value.Element("scale");
        if (scale != null && (string)scale.Attribute("type") != "number") throw new InvalidDataException("Pass scale must be a number.");
        Scale = scale == null ? 1f : XmlConvert.ToSingle(scale.Value);
        if (float.IsNaN(Scale) || float.IsInfinity(Scale) || Scale <= 0) throw new InvalidDataException("Pass scale must be positive and finite.");
        Binds = Array.AsReadOnly(PackDefinition.Items(value, "binds").Select(item => new PackBindingDefinition(item)).ToArray());
    }
}

public sealed class PackBindingDefinition
{
    public string Catalog { get; }
    public int Slot { get; }
    internal PackBindingDefinition(XElement value)
    {
        if ((string)value.Attribute("type") == "string") { Catalog = value.Value; Slot = -1; }
        else { PackDefinition.Object(value); Catalog = PackDefinition.Text(value, "catalog", true); Slot = PackDefinition.Integer(value, "slot") ?? -1; }
        if (string.IsNullOrWhiteSpace(Catalog)) throw new InvalidDataException("Catalog binding requires a name.");
    }
}

public sealed class PackAttachmentDefinition
{
    public string Name { get; }
    public string Format { get; }
    public string Stage { get; }
    public string File { get; }
    internal PackAttachmentDefinition(XElement value)
    {
        PackDefinition.Object(value);
        Name = PackDefinition.Text(value, "name", true);
        Format = PackDefinition.Text(value, "format", true);
        Stage = PackDefinition.Text(value, "stage") ?? "GBuffer";
        File = PackDefinition.Text(value, "file") ?? "GBuffer/" + Name + ".hlsl";
    }
}
