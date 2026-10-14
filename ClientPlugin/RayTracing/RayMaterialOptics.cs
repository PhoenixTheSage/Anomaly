using System;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRage.Render11.Resources.Textures;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>Append-only, visual thin-glass approximation. Native premultiplied color is not absorption.</summary>
internal static class RayMaterialOptics
{
    internal static object[] Capture(object[] part)
    {
        if (!string.Equals((string)part[5], "GLASS", StringComparison.OrdinalIgnoreCase) ||
            !MyTransparentMaterials.TryGetMaterial(MyStringId.GetOrCompute((string)part[3]), out var material) ||
            material == null || ReferenceEquals(material, MyTransparentMaterials.ErrorMaterial) ||
            material.TextureType != MyTransparentMaterialTextureType.FileTexture || string.IsNullOrEmpty(material.Texture)) return null;
        try
        {
            var texture = MyManagers.Textures.GetTexture(material.Texture, MyFileTextureEnum.COLOR_METAL).Texture?.Srv;
            if (texture == null) return null;
            var c = material.Color; float a = Math.Max(0, Math.Min(1, c.W));
            float inverse = a > 1e-6f ? 1 / a : 0;
            var values = new[] { c.X * inverse, c.Y * inverse, c.Z * inverse, a };
            var uv = new[] { material.UVOffset.X, material.UVOffset.Y, material.UVSize.X, material.UVSize.Y };
            uint revision = unchecked((uint)c.GetHashCode() ^ (uint)material.UVOffset.GetHashCode() ^ (uint)material.UVSize.GetHashCode());
            return new object[] { 1, values, texture, uv, revision, material.TriangleFaceCulling, "thin-glass" };
        }
        catch { return null; }
    }
}
