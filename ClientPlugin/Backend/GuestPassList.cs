using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClientPlugin.Backend;

/// <summary>
/// One fullscreen program parsed from <c>anomaly.json</c> or <c>Fullscreen/&lt;Slot&gt;</c>.
/// No renderer types. Both backends consume this list.
/// </summary>
public sealed class GuestProgram
{
    public string Id;
    public string Slot;
    public string File;
    public string Compose;
    public int Mark;
    public bool Enabled = true;
    public float[] Uniforms;
    public string Status = "registered";
    public byte[] VertexSpirv;
    public byte[] PixelSpirv;
    public string PixelEntry = "main";
    public ulong Pipeline;
    public ulong StorageImage;
    public ulong HistoryImage;
    public int ImageWidth;
    public int ImageHeight;
    public float Scale = 1f;
    public string HistoryName;
    public string OutputName;
    public int Flags;
    public int Format;
    public string SourceFile;
    public string[] MediumTextures;
    public float OriginX, OriginY, OriginZ, MotionX, MotionY, MotionZ;
    public List<SrvBind> Srvs;
}

public struct SrvBind
{
    public int Slot;
    public string Name;
}

/// <summary>
/// One extra GBuffer color target. Slot 0 is SV_Target4. Vulkan writes it from
/// <see cref="File"/> in a depth-equal pass. Packed GBuffer1.a stays on DX11.
/// </summary>
public sealed class GuestAttachment
{
    public string Name;
    public string Format;
    public string File;
    public string Owner;
    public int Slot = -1;
    public int NativeFormat = -1;
    public byte[] PixelSpirv;
    public string Status = "registered";
}

/// <summary>
/// Pack scan shared by the DX11 forwarder and the Vulkan guest packet.
/// </summary>
public static class GuestPassList
{
    static readonly object Gate = new object();
    static readonly List<GuestProgram> Programs = new List<GuestProgram>();
    static readonly List<GuestAttachment> Attachments = new List<GuestAttachment>();
    static readonly Regex Field = new Regex("\"(?<key>id|slot|file|compose|history|output)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex ScaleField = new Regex("\"scale\"\\s*:\\s*(?<value>[0-9]*\\.?[0-9]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<GuestProgram> Snapshot()
    {
        lock (Gate)
            return Programs.ToArray();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Programs.Clear();
            Attachments.Clear();
        }
    }

    public static IReadOnlyList<GuestAttachment> SnapshotAttachments()
    {
        lock (Gate)
            return Attachments.ToArray();
    }

    public static int RegisterRoot(string id, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return 0;
        var found = Load(root, id);
        var attachments = LoadAttachments(root);
        lock (Gate)
        {
            Programs.RemoveAll(p => string.Equals(PackOf(p.Id), id, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(id) && p.Id != null && p.Id.StartsWith(id + ".", StringComparison.OrdinalIgnoreCase)));
            Programs.AddRange(found);
            Attachments.RemoveAll(a => string.Equals(a.Owner, id, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < attachments.Count; i++)
            {
                attachments[i].Owner = id;
                if (Admit(attachments[i]))
                    Attachments.Add(attachments[i]);
            }
            return found.Count;
        }
    }

    public static void AddProgram(string id, string slot, string file, string compose)
    {
        int mark = GuestMark.FromSlot(slot);
        if (mark == 0 || string.IsNullOrEmpty(id))
            return;
        lock (Gate)
        {
            ReplaceOrAdd(Programs, new GuestProgram
            {
                Id = id,
                Slot = slot,
                File = file,
                SourceFile = file,
                Compose = string.IsNullOrEmpty(compose) ? "PublishOnly" : compose,
                Mark = mark,
                Status = file != null && File.Exists(file) ? "source" : "missing-source",
            });
        }
    }

    public static bool SetUniforms(string id, float[] values)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            program.Uniforms = values == null ? null : (float[])values.Clone();
            return true;
        }
    }

    public static bool SetFlags(string id, int flags, int format)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            program.Flags = flags;
            program.Format = format;
            return true;
        }
    }

    public static bool RequestSrv(string id, string catalogName, int slot)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(catalogName))
            return false;
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            if (program.Srvs == null)
                program.Srvs = new List<SrvBind>();
            if (slot < 0)
                slot = NextPackSlot(program.Srvs);
            if (slot != 7 && slot != 8 && slot != 9 && slot != 12 && slot != 13)
                return false;
            for (int i = 0; i < program.Srvs.Count; i++)
            {
                if (string.Equals(program.Srvs[i].Name, catalogName, StringComparison.OrdinalIgnoreCase))
                {
                    program.Srvs[i] = new SrvBind { Slot = slot, Name = catalogName };
                    return true;
                }
                if (program.Srvs[i].Slot == slot)
            {
                program.Srvs[i] = new SrvBind { Slot = slot, Name = catalogName };
                return true;
            }
            }
            program.Srvs.Add(new SrvBind { Slot = slot, Name = catalogName });
            return true;
        }
    }

    public static bool SetMedium(string id, string[] textures, float originX, float originY, float originZ, float motionX, float motionY, float motionZ)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            if (textures != null)
                program.MediumTextures = (string[])textures.Clone();
            program.OriginX = originX;
            program.OriginY = originY;
            program.OriginZ = originZ;
            program.MotionX = motionX;
            program.MotionY = motionY;
            program.MotionZ = motionZ;
            return true;
        }
    }

    public static bool SetOrigin(string id, float x, float y, float z)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            program.OriginX = x;
            program.OriginY = y;
            program.OriginZ = z;
            return true;
        }
    }

    public static bool SetMotion(string id, float x, float y, float z)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            program.MotionX = x;
            program.MotionY = y;
            program.MotionZ = z;
            return true;
        }
    }

    public static bool SetFile(string id, string file)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null || string.IsNullOrEmpty(file))
                return false;
            program.File = file;
            program.Status = File.Exists(file) ? "source" : "missing-source";
            return true;
        }
    }

    public static bool Contains(string id)
    {
        lock (Gate)
            return Find(id) != null;
    }

    public static float SnapScale(float scale)
    {
        if (scale <= 0.375f)
            return 0.25f;
        if (scale <= 0.75f)
            return 0.5f;
        return 1f;
    }

    public static bool SetScale(string id, float scale)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        float snapped = SnapScale(scale);
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            if (program.Scale != snapped)
            {
                program.Scale = snapped;
                program.StorageImage = 0;
                program.HistoryImage = 0;
                program.ImageWidth = 0;
                program.ImageHeight = 0;
            }
            return true;
        }
    }

    public static bool SetEnabled(string id, bool enabled)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null)
                return false;
            program.Enabled = enabled;
            return true;
        }
    }

    public static bool TryGetProgramStatus(string id, out string status)
    {
        lock (Gate)
        {
            var program = Find(id);
            status = program == null ? null : program.Status;
            return program != null;
        }
    }

    public static List<GuestProgram> Load(string root, string fallbackId)
    {
        var list = new List<GuestProgram>();
        var manifest = Path.Combine(root, "anomaly.json");
        if (File.Exists(manifest))
            ParseManifest(File.ReadAllText(manifest), root, list);
        ScanFolder(root, fallbackId, list);
        return list;
    }

    public static int MarkCount(IEnumerable<GuestProgram> programs, int mark)
    {
        int count = 0;
        if (programs == null)
            return 0;
        foreach (var program in programs)
            if (program != null && program.Enabled && program.Mark == mark)
                count++;
        return count;
    }

    static void ParseManifest(string json, string root, List<GuestProgram> list)
    {
        if (string.IsNullOrEmpty(json))
            return;
        int passes = json.IndexOf("\"passes\"", StringComparison.Ordinal);
        if (passes < 0)
            return;
        int array = json.IndexOf('[', passes);
        if (array < 0)
            return;
        int depth = 0;
        int start = -1;
        for (int i = array; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '{')
            {
                if (depth == 1)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 1 && start >= 0)
                {
                    AddObject(json.Substring(start, i - start + 1), root, list);
                    start = -1;
                }
            }
            else if (c == '[')
                depth++;
            else if (c == ']')
            {
                depth--;
                if (depth <= 0)
                    break;
            }
        }
    }

    static void AddObject(string block, string root, List<GuestProgram> list)
    {
        string id = null, slot = null, file = null, compose = "IsolatedAdd", history = null, output = null;
        foreach (Match match in Field.Matches(block))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value.Replace("\\\"", "\"");
            if (key == "id") id = value;
            else if (key == "slot") slot = value;
            else if (key == "file") file = value;
            else if (key == "compose") compose = value;
            else if (key == "history") history = value;
            else if (key == "output") output = value;
        }
        float scale = 1f;
        var scaleMatch = ScaleField.Match(block);
        if (scaleMatch.Success && float.TryParse(scaleMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            scale = SnapScale(parsed);

        if (string.IsNullOrEmpty(slot))
            return;
        int mark = GuestMark.FromSlot(slot);
        if (mark == 0)
            return;
        if (string.IsNullOrEmpty(id))
            id = slot + "." + list.Count.ToString(CultureInfo.InvariantCulture);
        string path = string.IsNullOrEmpty(file) ? null : Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        bool publish = string.Equals(compose, "PublishOnly", StringComparison.OrdinalIgnoreCase);
        ReplaceOrAdd(list, new GuestProgram
        {
            Id = id,
            Slot = slot,
            File = path,
            Compose = compose,
            Mark = mark,
            Scale = scale,
            HistoryName = history,
            OutputName = output,
            Format = publish ? 1 : 0,
            Status = path != null && File.Exists(path) ? "source" : "missing-source",
        });
    }

    static void ScanFolder(string root, string fallbackId, List<GuestProgram> list)
    {
        string folder = Path.Combine(root, "Fullscreen");
        if (!Directory.Exists(folder))
            return;
        foreach (var slotDir in Directory.GetDirectories(folder))
        {
            string slot = Path.GetFileName(slotDir);
            int mark = GuestMark.FromSlot(slot);
            if (mark == 0)
                continue;
            foreach (var file in Directory.GetFiles(slotDir, "*.hlsl"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string id = string.IsNullOrEmpty(fallbackId) ? slot + "." + name : fallbackId + "." + name;
                if (list.Exists(p => string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase)))
                    continue;
                list.Add(new GuestProgram
                {
                    Id = id,
                    Slot = slot,
                    File = file,
                    Compose = "IsolatedAdd",
                    Mark = mark,
                    Status = "source",
                });
            }
        }
    }

    static void ReplaceOrAdd(List<GuestProgram> list, GuestProgram program)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i].Id, program.Id, StringComparison.OrdinalIgnoreCase))
            {
                list[i] = program;
                return;
            }
        }

        list.Add(program);
    }

    static int NextPackSlot(List<SrvBind> binds)
    {
        int[] slots = { 7, 8, 9, 12, 13 };
        for (int i = 0; i < slots.Length; i++)
        {
            bool used = false;
            for (int j = 0; j < binds.Count; j++)
                used |= binds[j].Slot == slots[i];
            if (!used)
                return slots[i];
        }
        return -1;
    }

    static GuestProgram Find(string id)
    {
        for (int i = 0; i < Programs.Count; i++)
            if (string.Equals(Programs[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return Programs[i];
        return null;
    }

    public static List<GuestAttachment> LoadAttachments(string root)
    {
        var list = new List<GuestAttachment>();
        if (string.IsNullOrWhiteSpace(root))
            return list;
        string manifest = Path.Combine(root, "anomaly.json");
        if (!File.Exists(manifest))
            return list;
        ParseAttachments(File.ReadAllText(manifest), root, list);
        return list;
    }

    public static bool RequestAttachment(string name, string format, string file)
    {
        var attachment = CreateAttachment(name, format, file, "csharp");
        if (attachment == null)
            return false;
        lock (Gate)
        {
            if (!Admit(attachment))
                return attachment.Status == "shared";
            Attachments.Add(attachment);
            return true;
        }
    }

    static bool Admit(GuestAttachment attachment)
    {
        if (attachment == null || attachment.NativeFormat < 0)
            return false;
        var taken = new bool[3];
        for (int i = 0; i < Attachments.Count; i++)
        {
            var live = Attachments[i];
            if (live.Slot >= 0 && live.Slot < 3)
                taken[live.Slot] = true;
            if (!string.Equals(live.Name, attachment.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (live.NativeFormat != attachment.NativeFormat)
            {
                attachment.Status = "format-conflict";
                attachment.Slot = -1;
                return false;
            }
            attachment.Slot = live.Slot;
            attachment.Status = "shared";
            return false;
        }
        int slot = 0;
        while (slot < 3 && taken[slot])
            slot++;
        if (slot == 3)
        {
            attachment.Status = "capacity";
            attachment.Slot = -1;
            return false;
        }
        attachment.Slot = slot;
        return true;
    }

    static void ParseAttachments(string json, string root, List<GuestAttachment> list)
    {
        if (string.IsNullOrEmpty(json))
            return;
        int at = json.IndexOf("\"attachments\"", StringComparison.Ordinal);
        if (at < 0)
            return;
        int array = json.IndexOf('[', at);
        if (array < 0)
            return;
        int depth = 0;
        int start = -1;
        for (int i = array; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '{')
            {
                if (depth == 1)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 1 && start >= 0)
                {
                    AddAttachment(json.Substring(start, i - start + 1), root, list);
                    start = -1;
                }
            }
            else if (c == '[')
                depth++;
            else if (c == ']')
            {
                depth--;
                if (depth <= 0)
                    break;
            }
        }
    }

    static void AddAttachment(string block, string root, List<GuestAttachment> list)
    {
        string name = null, format = null, file = null;
        foreach (Match match in Regex.Matches(block, "\"(?<key>name|format|file)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant))
        {
            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value.Replace("\\\"", "\"");
            if (key == "name") name = value;
            else if (key == "format") format = value;
            else if (key == "file") file = value;
        }
        if (string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(name))
            file = "GBuffer/" + name + ".hlsl";
        string path = string.IsNullOrEmpty(file) ? null : Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        var attachment = CreateAttachment(name, format, path, null);
        if (attachment == null)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (!string.Equals(list[i].Name, attachment.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (list[i].NativeFormat != attachment.NativeFormat)
                return;
            return;
        }
        if (list.Count >= 3)
            return;
        attachment.Slot = list.Count;
        list.Add(attachment);
    }

    static GuestAttachment CreateAttachment(string name, string format, string file, string owner)
    {
        if (!IsIdent(name) || IsReservedAttachment(name))
            return null;
        int native = NativeAttachmentFormat(format);
        if (native < 0)
            return null;
        return new GuestAttachment
        {
            Name = name.Trim(),
            Format = format.Trim(),
            File = file,
            Owner = owner,
            NativeFormat = native,
            Slot = 0,
            Status = file != null && File.Exists(file) ? "source" : "missing-source",
        };
    }

    static int NativeAttachmentFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return -1;
        switch (format.Trim().Replace("-", "_").ToUpperInvariant())
        {
            case "R8G8B8A8_UNORM": return 0;
            case "R16G16B16A16_FLOAT": return 1;
            case "R32_FLOAT": return 2;
            case "R16G16_FLOAT": return 3;
            case "R32_UINT": return 4;
            case "R32G32_FLOAT": return 5;
            default: return -1;
        }
    }

    static bool IsIdent(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;
        for (int i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        return true;
    }

    static bool IsReservedAttachment(string name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "velocity":
            case "lineardepth":
            case "hiz":
            case "historycolor":
            case "reactivemask":
            case "hdrcolor":
            case "upscaledcolor":
            case "displaycolor":
            case "gbuffer0":
            case "gbuffer1":
            case "gbuffer2":
            case "depth":
                return true;
            default:
                return false;
        }
    }

    public static string CompileStatus()
    {
        int pipelines, failures;
        CountPrograms(out pipelines, out failures);
        if (pipelines == 0)
            return failures == 0 ? "not live" : "not live (" + failures + " compile failures)";
        return failures == 0 ? "live" : "live (" + failures + " compile failures)";
    }

    public static void CountPrograms(out int pipelines, out int failures)
    {
        lock (Gate)
        {
            pipelines = 0;
            failures = 0;
            for (int i = 0; i < Programs.Count; i++)
            {
                var program = Programs[i];
                if (program.Pipeline != 0 || program.Status == "pipeline")
                    pipelines++;
                else if (program.Status == "compile-failed")
                    failures++;
            }
        }
    }

    public static string PackStatus()
    {
        lock (Gate)
        {
            var packs = new List<string>();
            var stages = new List<string>();
            for (int i = 0; i < Programs.Count; i++)
            {
                var program = Programs[i];
                if (program == null || IsOwned(program.Id) || !program.Enabled)
                    continue;
                string pack = PackOf(program.Id);
                if (!string.IsNullOrEmpty(pack) && !packs.Exists(item => string.Equals(item, pack, StringComparison.OrdinalIgnoreCase)))
                    packs.Add(pack);
                if (!string.IsNullOrEmpty(program.Slot) && !stages.Exists(item => string.Equals(item, program.Slot, StringComparison.OrdinalIgnoreCase)))
                    stages.Add(program.Slot);
            }
            for (int i = 0; i < Attachments.Count; i++)
            {
                string pack = Attachments[i].Owner;
                if (!string.IsNullOrEmpty(pack) && !packs.Exists(item => string.Equals(item, pack, StringComparison.OrdinalIgnoreCase)))
                    packs.Add(pack);
            }
            if (packs.Count == 0)
                return "none";
            var line = packs.Count + " live  conflicts=0";
            if (stages.Count > 0)
                line += "  stages=" + string.Join(",", stages);
            return line;
        }
    }

    public static string AttachmentStatus()
    {
        lock (Gate)
        {
            if (Attachments.Count == 0)
                return "none";
            var parts = new string[Attachments.Count];
            for (int i = 0; i < Attachments.Count; i++)
            {
                var attachment = Attachments[i];
                parts[i] = (attachment.Name ?? "attachment")
                    + (attachment.Slot >= 0 ? "@" + attachment.Slot : "")
                    + ":" + (attachment.Status ?? "registered");
            }
            return string.Join(",", parts);
        }
    }

    public static int AttachmentCount()
    {
        lock (Gate)
            return Attachments.Count;
    }

    public static string BindStatus()
    {
        lock (Gate)
        {
            var stages = new List<string>();
            var slots = new List<List<int>>();
            for (int i = 0; i < Programs.Count; i++)
            {
                var program = Programs[i];
                if (program == null || !program.Enabled || program.Srvs == null || program.Srvs.Count == 0)
                    continue;
                string stage = string.IsNullOrEmpty(program.Slot) ? "pass" : program.Slot;
                int index = stages.FindIndex(item => string.Equals(item, stage, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    stages.Add(stage);
                    slots.Add(new List<int>());
                    index = stages.Count - 1;
                }
                for (int s = 0; s < program.Srvs.Count; s++)
                {
                    int slot = program.Srvs[s].Slot;
                    if (slot >= 0 && !slots[index].Contains(slot))
                        slots[index].Add(slot);
                }
            }
            if (stages.Count == 0)
                return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < stages.Count; i++)
            {
                if (slots[i].Count == 0)
                    continue;
                slots[i].Sort();
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(stages[i]).Append(':');
                for (int s = 0; s < slots[i].Count; s++)
                {
                    if (s > 0)
                        sb.Append(',');
                    sb.Append('t').Append(slots[i][s]);
                }
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }

    public static string OwnedPassStatus()
    {
        lock (Gate)
        {
            var sb = new StringBuilder();
            string slot = null;
            for (int i = 0; i < Programs.Count; i++)
            {
                var program = Programs[i];
                if (program == null || !IsOwned(program.Id) || !program.Enabled)
                    continue;
                if (slot != program.Slot)
                {
                    if (sb.Length > 0)
                        sb.Append(' ');
                    slot = program.Slot;
                    sb.Append(slot).Append(':');
                }
                else
                    sb.Append(',');
                sb.Append(program.Id);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }

    public static string FullscreenStatus()
    {
        lock (Gate)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Programs.Count; i++)
            {
                var program = Programs[i];
                if (program == null || IsOwned(program.Id))
                    continue;
                if (program.Status == "compile-failed")
                {
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(program.Id).Append("!compile");
                    continue;
                }
                if (!program.Enabled)
                    continue;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(program.Slot).Append('/').Append(program.Compose).Append(':').Append(program.Id);
                float scale = SnapScale(program.Scale);
                if (scale < 1f)
                    sb.Append('@').Append(scale <= 0.25f ? "1/4" : "1/2");
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }
    }

    public static string OwnedBufferStatus()
    {
        lock (Gate)
        {
            bool any = false;
            for (int i = 0; i < Programs.Count && !any; i++)
                any = IsOwned(Programs[i].Id);
            if (!any)
                return "disabled";
            return "linearDepth " + Tex("LinearDepth")
                + "; historyDepth " + Tex("HistoryDepth")
                + "; hiZ " + Tex("HiZ")
                + "; historyColor " + Tex("HistoryCopy")
                + "; litMips " + Tex("LitMips");
        }
    }

    public static string CameraStatus()
    {
        lock (Gate)
        {
            var program = FindSuffix("CameraVelocity");
            if (program == null)
                return "disabled";
            if (program.Status == "compile-failed")
                return "error (compile)";
            if (program.Pipeline != 0 || program.Status == "pipeline")
                return "live";
            return "shaders not ready";
        }
    }

    public static bool ImageSize(string suffix, out int width, out int height)
    {
        lock (Gate)
        {
            var program = FindSuffix(suffix);
            width = program == null ? 0 : program.ImageWidth;
            height = program == null ? 0 : program.ImageHeight;
            return width > 0 && height > 0;
        }
    }

    static string Tex(string suffix)
    {
        var program = FindSuffix(suffix);
        if (program == null)
            return "none";
        if (program.Status == "compile-failed")
            return "error";
        if (program.ImageWidth > 0 && program.ImageHeight > 0)
            return program.ImageWidth.ToString(CultureInfo.InvariantCulture) + "x" + program.ImageHeight.ToString(CultureInfo.InvariantCulture);
        if (program.Pipeline != 0 || program.Status == "pipeline")
            return "pending";
        return "none";
    }

    static GuestProgram FindSuffix(string suffix)
    {
        for (int i = 0; i < Programs.Count; i++)
        {
            var id = Programs[i].Id;
            if (id != null && id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return Programs[i];
        }
        return null;
    }

    static bool IsOwned(string id) =>
        id != null && id.StartsWith("anomaly.owned.", StringComparison.OrdinalIgnoreCase);

    static string PackOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return "";
        int dot = id.IndexOf('.');
        return dot < 0 ? id : id.Substring(0, dot);
    }
}
