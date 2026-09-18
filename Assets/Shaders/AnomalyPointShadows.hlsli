#ifndef ANOMALY_POINT_SHADOWS_INCLUDED
#define ANOMALY_POINT_SHADOWS_INCLUDED

// pointShadowAtlas: header row of (light position in view space, range),
// then six horizontal faces per light. Distances are Euclidean metres.
// Matches PointShadowPass.CubeFaceViewProj and the D3D viewport Y inversion.
void AnomalyPointShadowFaceUv(float3 dir, out uint face, out float2 uv)
{
    float3 a = abs(dir);
    if (a.x >= a.y && a.x >= a.z)
    {
        face = dir.x >= 0 ? 0 : 1;
        uv = float2(dir.x >= 0 ? -dir.z : dir.z, dir.y) / max(a.x, 1e-6);
    }
    else if (a.y >= a.z)
    {
        face = dir.y >= 0 ? 2 : 3;
        uv = float2(dir.x, dir.y >= 0 ? -dir.z : dir.z) / max(a.y, 1e-6);
    }
    else
    {
        face = dir.z >= 0 ? 4 : 5;
        uv = float2(dir.z >= 0 ? dir.x : -dir.x, dir.y) / max(a.z, 1e-6);
    }
    uv = uv * 0.5 + 0.5;
}

// A small separable tent filter softens edges without temporal noise. Clamp taps
// inside this face: no header, adjacent face or next-light row bleed.
float AnomalyPointShadowVisibility(Texture2D atlas, uint row, float3 direction,
    float receiverDistance, float bias)
{
    uint width, height;
    atlas.GetDimensions(width, height);
    uint size = width / 6;
    if (size == 0 || height <= 1 || row >= (height - 1) / size)
        return 1;
    uint face;
    float2 uv;
    AnomalyPointShadowFaceUv(direction, face, uv);
    float2 p = saturate(uv) * size - 0.5;
    int2 base = int2(floor(p));
    float2 f = frac(p);
    float visibility = 0;
    [unroll] for (int y = -1; y <= 2; y++)
    [unroll] for (int x = -1; x <= 2; x++)
    {
        int2 texel = clamp(base + int2(x, y), int2(0, 0), int2(size - 1, size - 1));
        texel += int2(face * size, 1 + row * size);
        float depth = atlas.Load(int3(texel, 0)).r;
        // Bilinear sample convolved with [1, 2, 1]/4 on each axis.
        float wx = x == -1 ? (1-f.x)*0.25 : x == 0 ? (2-f.x)*0.25 : x == 1 ? (1+f.x)*0.25 : f.x*0.25;
        float wy = y == -1 ? (1-f.y)*0.25 : y == 0 ? (2-f.y)*0.25 : y == 1 ? (1+f.y)*0.25 : f.y*0.25;
        float weight = wx * wy;
        visibility += weight * (depth + bias >= receiverDistance ? 1.0 : 0.0);
    }
    return visibility;
}
#endif
