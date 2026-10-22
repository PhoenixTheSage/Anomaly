using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Shaders;

namespace ClientPlugin.Contracts;

// Device-free reservation and conflict rules shared by the core and DX11 companion.
internal sealed class StageBindingModel
{
    internal sealed class Request
    {
        internal readonly string Stage, CatalogName;
        internal readonly int Slot;
        internal Request(string stage, string name, int slot) { Stage = stage; CatalogName = name; Slot = slot; }
    }

    readonly object gate = new();
    readonly List<Request> requests = new();
    IReadOnlyList<Request> snapshot = Array.AsReadOnly(Array.Empty<Request>());

    internal StageBindingModel()
    {
        Add(ShaderStages.Lighting, "velocity", 5);
        Add(ShaderStages.PostTonemap, "velocity", 5);
        Add(ShaderStages.PostHbao, "velocity", 5);
        Add(ShaderStages.Transparent, "velocity", 5);
        Add(ShaderStages.Atmosphere, "velocity", 6);
    }

    internal bool Add(string stage, string name, int slot = -1)
    {
        if (string.IsNullOrWhiteSpace(stage) || string.IsNullOrWhiteSpace(name)) return false;
        stage = stage.Trim(); name = name.Trim();
        lock (gate)
        {
            bool SameStage(Request r) => string.Equals(r.Stage, stage, StringComparison.OrdinalIgnoreCase);
            bool SameName(Request r) => string.Equals(r.CatalogName, name, StringComparison.OrdinalIgnoreCase);
            int index = requests.FindIndex(r => SameStage(r) && SameName(r));
            bool atmosphere = string.Equals(stage, ShaderStages.Atmosphere, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(name, "velocity", StringComparison.OrdinalIgnoreCase)) slot = atmosphere ? 6 : 5;
            if (slot < 0)
            {
                if (index >= 0) return true;
                var used = new HashSet<int>(requests.Where(SameStage).Select(r => r.Slot)) { 5 };
                if (atmosphere) used.Add(6);
                slot = Enumerable.Range(atmosphere ? 7 : 6, atmosphere ? 3 : 4).FirstOrDefault(s => !used.Contains(s));
            }
            if (slot < 5 || slot > 9) return false;
            // Validate the destination before moving an existing request.
            if (requests.Any(r => SameStage(r) && r.Slot == slot && !SameName(r))) return false;
            if (index >= 0 && requests[index].Slot == slot) return true;
            var request = new Request(stage, name, slot);
            if (index < 0) requests.Add(request); else requests[index] = request;
            // Copy on registration, never on the per-draw binding path. Old frame
            // readers retain an immutable collection of immutable requests.
            snapshot = Array.AsReadOnly(requests.ToArray());
            return true;
        }
    }

    internal IReadOnlyList<Request> Snapshot() { lock (gate) return snapshot; }
    internal static bool Matches(string request, string stage) =>
        string.Equals(request, stage, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request, ShaderStages.Lighting, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(stage, ShaderStages.LightingDir, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(stage, ShaderStages.LightingPoint, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(stage, ShaderStages.LightingSpot, StringComparison.OrdinalIgnoreCase));
}
