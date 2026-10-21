using System;
using VRageMath;
// This older offscreen lane does not own a game scene. The actual assembly and
// renderer transaction is exercised in SE-Vulkan/Tests/GuestDepthGpuTests.
namespace ClientPlugin.Shaders {
    static class PlanetAtmosphere {
        internal struct Snapshot {public Vector3D Center;public bool IsValid;public float TerrainRadius, AirTop, VisualCeil;}
        internal static Snapshot Current;
        internal static Snapshot Copy()=>Current;
    }
}
namespace ClientPlugin.ShaderFramework {
    static class VolumetricInteriorSnapshot {
        internal sealed class Grid {internal long Id,Generation;internal MatrixD WorldToLocal;internal float CellSize;internal Vector3I[] Cells;internal bool IsCurrent;}
        internal sealed class Snapshot {internal Grid[] Grids=Array.Empty<Grid>();internal bool IsFresh,Complete;internal string Status="No scene in offscreen fixture";}
        internal static Snapshot Capture()=>new Snapshot();
    }
}
