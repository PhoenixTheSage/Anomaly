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

// Nearest header within 5 cm or 2% of range. Exact 1 mm match failed whenever
// Capture and Keen WritePointlightConstants disagreed by float noise — atlas
// umbras only appeared after a lucky camera angle.
uint AnomalyPointShadowMatchRow(Texture2D atlas, float3 lightViewPos, float range)
{
    uint width, height;
    atlas.GetDimensions(width, height);
    if (height < 2 || range < 0.05)
        return 0xffffffffu;
    uint faceRes = max(width / 6, 1);
    uint count = (height - 1) / faceRes;
    float tol = max(0.05, range * 0.02);
    float rangeTol = max(0.05, range * 0.02);
    float best = 1e10;
    uint row = 0xffffffffu;
    [loop] for (uint i = 0; i < 64; i++)
    {
        if (i >= count)
            break;
        float4 slot = atlas.Load(int3(i, 0, 0));
        if (slot.w < 0.05 || abs(slot.w - range) > rangeTol)
            continue;
        float d = distance(slot.xyz, lightViewPos);
        if (d < best)
        {
            best = d;
            row = i;
        }
    }
    return best <= tol ? row : 0xffffffffu;
}

// Point sample. Prefer this for Low/Medium — a 4×4 tent × every tile light
// dominated AfterLighting cost when shadows were on.
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
    int2 texel = clamp(int2(saturate(uv) * size), int2(0, 0), int2(size - 1, size - 1));
    texel += int2(face * size, 1 + row * size);
    float depth = atlas.Load(int3(texel, 0)).r;
    return depth + bias >= receiverDistance ? 1.0 : 0.0;
}

// Face-clamped 4×4 tent. High/Ultra only — 16 Loads per shadowed light.
float AnomalyPointShadowVisibilitySoft(Texture2D atlas, uint row, float3 direction,
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
        float wx = x == -1 ? (1 - f.x) * 0.25 : x == 0 ? (2 - f.x) * 0.25 : x == 1 ? (1 + f.x) * 0.25 : f.x * 0.25;
        float wy = y == -1 ? (1 - f.y) * 0.25 : y == 0 ? (2 - f.y) * 0.25 : y == 1 ? (1 + f.y) * 0.25 : f.y * 0.25;
        visibility += wx * wy * (depth + bias >= receiverDistance ? 1.0 : 0.0);
    }
    return visibility;
}
#endif
