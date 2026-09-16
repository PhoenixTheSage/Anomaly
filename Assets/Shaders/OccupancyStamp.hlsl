// Stamp camera-relative AABBs into the 64^3 occupancy atlas (512x512).

struct BoxGpu
{
    float3 center;
    float pad0;
    float3 halfExt;
    float pad1;
};

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

StructuredBuffer<BoxGpu> Boxes : register(t0);
RWTexture2D<float> Occupancy : register(u0);

#define MAX_STAMP_AXIS 16

void WriteCell(int3 cell)
{
    if (any(cell < 0) || any(cell >= (int)Dim))
        return;
    uint2 tile = uint2((uint)cell.z % 8, (uint)cell.z / 8);
    Occupancy[tile * Dim + (uint2)cell.xy] = 1;
}

[numthreads(64, 1, 1)]
void __compute_shader(uint3 dtid : SV_DispatchThreadID)
{
    if (dtid.x >= BoxCount)
        return;

    BoxGpu box = Boxes[dtid.x];
    if (any(box.halfExt < 1e-4))
        return;

    float3 mn = (box.center - box.halfExt - Origin) / max(VoxelSize, 1e-4);
    float3 mx = (box.center + box.halfExt - Origin) / max(VoxelSize, 1e-4);
    int3 i0 = clamp((int3)floor(mn), 0, (int)Dim - 1);
    int3 i1 = clamp((int3)floor(mx), 0, (int)Dim - 1);
    if (any(i1 < i0))
        return;

    int3 span = i1 - i0 + 1;
    int3 count = min(span, MAX_STAMP_AXIS);
    float3 incr = float3(span) / float3(max(count, 1));
    [loop]
    for (int z = 0; z < MAX_STAMP_AXIS; z++)
    {
        if (z >= count.z)
            break;
        [loop]
        for (int y = 0; y < MAX_STAMP_AXIS; y++)
        {
            if (y >= count.y)
                break;
            [loop]
            for (int x = 0; x < MAX_STAMP_AXIS; x++)
            {
                if (x >= count.x)
                    break;
                int3 cell = i0 + int3((float3(x, y, z) + 0.5) * incr);
                WriteCell(cell);
            }
        }
    }
}
