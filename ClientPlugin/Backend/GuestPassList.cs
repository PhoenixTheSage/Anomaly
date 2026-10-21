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
    public string Owner;
    public int Policy = 1;
    public string Slot;
    public string File;
    public string Compose;
    public int Mark;
    public int Priority;
    public bool Enabled = true;
    public bool ConflictDisabled;
    public bool Helper;
    public float[] Uniforms;
    public string Status = "registered";
    public string Error;
    public ulong Submissions;
    public bool SubmittedThisFrame;
    public bool HistoryReady;
    public uint HistoryFrame = uint.MaxValue;
    public byte[] VertexSpirv;
    public byte[] PixelSpirv;
    public string PixelEntry = "main";
    public string VertexFile;
    public string VertexEntry = "main";
    public int ResourceContract;
    public ulong Pipeline;
    public long ResourceEpoch;
    public ulong StorageImage;
    public ulong HistoryImage;
    public int ImageWidth;
    public int ImageHeight;
    public int ImageFormat;
    public float Scale = 1f;
    public string HistoryName;
    public string OutputName;
    public int Flags;
    public int Format;
    public string SourceFile;
    public string IncludeRoot;
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
    static readonly List<GuestProgram> Retired = new List<GuestProgram>();
    internal static GuestProgram[] TakeRetired()
    {
        lock (Gate) { var result = Retired.ToArray(); Retired.Clear(); return result; }
    }
    static readonly List<GuestAttachment> Attachments = new List<GuestAttachment>();
    static readonly Dictionary<string, PackDefinition> Packs = new Dictionary<string, PackDefinition>(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyList<GuestProgram> Snapshot()
    {
        lock (Gate)
            return Programs.ToArray();
    }
    internal static void SubmitFrame(Action<IReadOnlyList<GuestProgram>> submit)
    {
        // Registration, settings and reload cannot change the frame while its
        // copied native command list and history references are constructed.
        lock(Gate) submit(Programs.ToArray());
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Retired.AddRange(Programs);
            Programs.Clear();
            Attachments.Clear();
            Packs.Clear();
        }
    }

    public static IReadOnlyList<GuestAttachment> SnapshotAttachments()
    {
        lock (Gate)
            return Attachments.ToArray();
    }

    public static int RegisterRoot(string id, string root, string requiredProgram = null)
    {
        root = PackDefinition.ResolveRoot(root);
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Pack registration requires an id.");
        string manifest = Path.Combine(root, "anomaly.json");
        if (!File.Exists(manifest)) throw new InvalidDataException("Pack root requires anomaly.json: " + root);
        var definition = PackDefinition.Parse(File.ReadAllText(manifest));
        var found = Load(root, id, definition);
        var attachments = LoadAttachments(root, definition);
        if (!string.IsNullOrEmpty(requiredProgram) && !found.Exists(p =>
            string.Equals(p.Id, requiredProgram, StringComparison.OrdinalIgnoreCase) && File.Exists(p.File)))
            throw new InvalidDataException("Required fullscreen program is missing: " + requiredProgram);
        lock (Gate)
        {
            foreach (var program in found)
                if (Programs.Exists(p => string.Equals(p.Id, program.Id, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(p.Owner, id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Fullscreen program is owned by another registration: " + program.Id);
            var candidate = Attachments.FindAll(a => !string.Equals(a.Owner, id, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < attachments.Count; i++)
            {
                attachments[i].Owner = id;
                if (Admit(attachments[i], candidate)) candidate.Add(attachments[i]);
                else if (attachments[i].Status != "shared")
                    throw new InvalidDataException("Attachment " + attachments[i].Name + ": " + attachments[i].Status);
            }
            Retired.AddRange(Programs.FindAll(p => string.Equals(p.Owner, id, StringComparison.OrdinalIgnoreCase)));
            Programs.RemoveAll(p => string.Equals(p.Owner, id, StringComparison.OrdinalIgnoreCase));
            Programs.AddRange(found);
            Attachments.Clear();
            Attachments.AddRange(candidate);
            if (definition != null) Packs[id] = definition;
            ApplyExclusiveConflicts();
            return found.Count;
        }
    }

    static void ApplyExclusiveConflicts()
    {
        var claims = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Packs)
            foreach (string stage in pair.Value.Exclusive)
            {
                if (!claims.TryGetValue(stage, out var owners)) claims.Add(stage, owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                owners.Add(pair.Key);
            }
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in claims.Values) if (claim.Count > 1) disabled.UnionWith(claim);
        foreach (var program in Programs) program.ConflictDisabled = program.Owner != null && disabled.Contains(program.Owner);
    }

    public static void UnregisterRoot(string id)
    {
        lock (Gate)
        {
            Retired.AddRange(Programs.FindAll(p => string.Equals(p.Owner, id, StringComparison.OrdinalIgnoreCase)));
            Programs.RemoveAll(p => string.Equals(p.Owner, id, StringComparison.OrdinalIgnoreCase));
            Attachments.RemoveAll(a => string.Equals(a.Owner, id, StringComparison.OrdinalIgnoreCase));
            Packs.Remove(id); ApplyExclusiveConflicts();
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

    public static bool SetHistory(string id, string catalogName)
    {
        lock (Gate)
        {
            var program = Find(id);
            if (program == null) return false;
            if (!string.Equals(program.HistoryName,catalogName,StringComparison.Ordinal))
            {
                if(!string.IsNullOrEmpty(program.HistoryName))ClientPlugin.Buffers.BufferCatalog.Set(program.HistoryName,null);
                program.HistoryReady=false;
            }
            program.HistoryName = catalogName;
            return true;
        }
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
                program.ImageWidth = 0;
                program.ImageHeight = 0;
                program.HistoryReady = false;
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

    public static List<GuestProgram> Load(string root, string fallbackId, PackDefinition definition = null)
    {
        var list = new List<GuestProgram>();
        var manifest = Path.Combine(root, "anomaly.json");
        if (File.Exists(manifest))
            ParseManifest(definition ?? PackDefinition.Parse(File.ReadAllText(manifest), fallbackId), root, fallbackId, list);
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

    static void ParseManifest(PackDefinition pack, string root, string fallbackId, List<GuestProgram> list)
    {
        foreach (var pass in pack.Passes)
        {
            int mark = GuestMark.FromSlot(pass.Slot);
            if (mark == 0) throw new InvalidDataException("Unknown fullscreen slot: " + pass.Slot);
            string id = pass.Id ?? pack.Id + "." + pass.Slot + "." + list.Count.ToString(CultureInfo.InvariantCulture);
            string path = PackDefinition.ResolveFile(root, pass.File);
            var binds = new List<SrvBind>();
            foreach (var bind in pass.Binds)
            {
                int slot = bind.Slot < 0 ? NextPackSlot(binds) : bind.Slot;
                if (slot != 7 && slot != 8 && slot != 9 && slot != 12 && slot != 13)
                    throw new InvalidDataException("Reserved or unavailable catalog register: t" + slot);
                if (binds.Exists(b => b.Slot == slot)) throw new InvalidDataException("Duplicate catalog register: t" + slot);
                binds.Add(new SrvBind { Name = bind.Catalog, Slot = slot });
            }
            int policy = 0;
            foreach (string temporal in pass.Temporal)
            {
                switch (temporal.ToLowerInvariant())
                {
                    case "none": break;
                    case "incolor": policy |= 1; break;
                    case "contributevelocity": policy |= 2; break;
                    case "reactive": policy |= 4; break;
                    case "display": policy |= 8; break;
                    default: throw new InvalidDataException("Unknown temporal policy: " + temporal);
                }
            }
            ReplaceOrAdd(list, new GuestProgram {
                Id = id, Owner = fallbackId ?? pack.Id, Slot = pass.Slot, File = path, SourceFile = path,
                Compose = pass.Compose, Mark = mark, Scale = SnapScale(pass.Scale), HistoryName = pass.History,
                OutputName = pass.Output, IncludeRoot = root, Srvs = binds, Priority = pass.Priority ?? pack.Priority,
                Policy = policy == 0 ? 1 : policy,
                Format = string.Equals(pass.Compose, "PublishOnly", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                Status = File.Exists(path) ? "source" : "missing-source"
            });
        }
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
                    Owner = fallbackId,
                    Slot = slot,
                    File = file,
                    IncludeRoot = root,
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
                if (ReferenceEquals(list, Programs)) Retired.Add(list[i]);
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

    public static List<GuestAttachment> LoadAttachments(string root, PackDefinition definition = null)
    {
        var list = new List<GuestAttachment>();
        if (string.IsNullOrWhiteSpace(root))
            return list;
        string manifest = Path.Combine(root, "anomaly.json");
        if (!File.Exists(manifest))
            return list;
        ParseAttachments(definition ?? PackDefinition.Parse(File.ReadAllText(manifest), Path.GetFileName(root)), root, list);
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

    static bool Admit(GuestAttachment attachment, List<GuestAttachment> active = null)
    {
        active = active ?? Attachments;
        if (attachment == null || attachment.NativeFormat < 0)
            return false;
        var taken = new bool[3];
        for (int i = 0; i < active.Count; i++)
        {
            var live = active[i];
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

    static void ParseAttachments(PackDefinition pack, string root, List<GuestAttachment> list)
    {
        foreach (var spec in pack.Attachments)
        {
            var attachment = CreateAttachment(spec.Name, spec.Format, PackDefinition.ResolveFile(root, spec.File), null);
            // Packed GBuffer1.a is handled by the owning material path, not an extra image.
            if (attachment == null) continue;
            if (list.Exists(a => string.Equals(a.Name, attachment.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Duplicate attachment: " + attachment.Name);
            attachment.Slot = list.Count;
            list.Add(attachment);
        }
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
        int pending = 0, submitted = 0;
        string error = null;
        foreach (var p in Snapshot())
        {
            if (p.Pipeline == 0 && p.Status != "compile-failed" && p.Status != "pipeline-failed") pending++;
            if (p.Submissions > 0) submitted++;
            if (error == null && !string.IsNullOrEmpty(p.Error))
                error = p.Id + ": " + p.Error.Split('\r', '\n')[0];
        }
        return "ready=" + pipelines + " pending=" + pending + " failed=" + failures + " submitted=" + submitted
            + (error == null ? "" : " | " + error);
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
                if (program.Status == "capability-missing")
                    failures++;
                else if (program.Pipeline != 0 || program.Status == "pipeline")
                    pipelines++;
                else if (program.Status == "compile-failed" || program.Status == "pipeline-failed")
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
                + "; historyDepth " + HistoryDepthStatus()
                + "; hiZ " + Tex("HiZ")
                + "; historyColor " + Tex("HistoryCopy")
                + "; litMips " + Tex("LitMips")
                + "; occupancy " + GuestOccupancy.StatusLine
                + "; point shadows " + GuestPointShadows.Status;
        }
    }

    static string HistoryDepthStatus()
    {
        var p = FindSuffix("LinearDepth");
        return p != null && p.HistoryReady && p.HistoryImage != 0 ? p.ImageWidth + "x" + p.ImageHeight : "pending";
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
            return program.Status ?? "shaders not ready";
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
        if (program.Status == "capability-missing")
            return "capability missing";
        if (suffix == "LitMips" && ClientPlugin.Buffers.BufferCatalog.Active("litMips") == null)
            return "not requested";
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
