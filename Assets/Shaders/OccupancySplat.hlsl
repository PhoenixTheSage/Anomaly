// Camera-relative occupancy splat from this-frame linear depth.
// Clipmap is 64^3 packed as an 8x8 tile atlas of 64x64 slices (512x512).

cbuffer OccupancyCb : register(b0)
{
    float2 InvSize;
    float2 ProjScale;
    float2 Jitter;
    uint Dim;
    uint HistoryValid;
    float3 Origin;
    float VoxelSize;
    uint BoxCount;
    uint3 StampPad;
    row_major float3x4 CameraToWorld;
    row_major float4x4 PrevViewProj;
};

Texture2D<float> LinearDepth : register(t0);
Texture2D<float> HistoryDepth : register(t1);
RWTexture2D<float> Occupancy : register(u0);

float3 ComputeScreenRay(float2 uv)
{
    float ray_x = rcp(max(ProjScale.x, 1e-6));
    float ray_y = rcp(max(ProjScale.y, 1e-6));
    float3 projOffset = float3(Jitter.x * ray_x, Jitter.y * ray_y, 0);
    return projOffset + float3(lerp(-ray_x, ray_x, uv.x), -lerp(-ray_y, ray_y, uv.y), -1.0);
}

void Stamp(float3 worldAt0)
{
    float3 cellF = (worldAt0 - Origin) / max(VoxelSize, 1e-4);
    if (any(cellF < 0) || any(cellF >= (float)Dim))
        return;
    uint3 cell = uint3(cellF);
    uint slice = cell.z;
    uint2 tile = uint2(slice % 8, slice / 8);
    uint2 px = tile * Dim + cell.xy;
    Occupancy[px] = 1;
}

[numthreads(8, 8, 1)]
void __compute_shader(uint3 dtid : SV_DispatchThreadID)
{
    uint2 size = (uint2)(1.0 / max(InvSize, 1e-6));
    if (any(dtid.xy >= size))
        return;

    float z = LinearDepth[dtid.xy];
    if (z < 1e-4)
        return;

    float2 uv = (dtid.xy + 0.5) * InvSize;
    float3 view = z * ComputeScreenRay(uv);
    // InvViewAt0 rows — mul(M, v) transposes and misses the clipmap.
    float3 world = CameraToWorld[0].xyz * view.x
                 + CameraToWorld[1].xyz * view.y
                 + CameraToWorld[2].xyz * view.z;
    Stamp(world);
}
