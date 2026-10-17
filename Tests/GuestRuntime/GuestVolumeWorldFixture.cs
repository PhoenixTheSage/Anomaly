using System;
using VRageMath;
// This older offscreen lane does not own a game scene. The actual assembly and
// renderer transaction is exercised in SE-Vulkan/Tests/GuestDepthGpuTests.
namespace ClientPlugin.Shaders {
    static class PlanetAtmosphere {
        internal readonly struct Snapshot {public readonly Vector3D Center;public readonly bool IsValid;public readonly float TerrainRadius;}
        internal static Snapshot Copy()=>default;
    }
}
namespace ClientPlugin.ShaderFramework {
    static class VolumetricInteriorSnapshot {
        internal sealed class Grid {internal long Id,Generation;internal MatrixD WorldToLocal;internal float CellSize;internal Vector3I[] Cells;internal bool IsCurrent;}
        internal sealed class Snapshot {internal Grid[] Grids=Array.Empty<Grid>();internal bool IsFresh,Complete;internal string Status="No scene in offscreen fixture";}
        internal static Snapshot Capture()=>new Snapshot();
    }
}
