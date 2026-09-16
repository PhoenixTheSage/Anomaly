// Instanced unit-cube raster for light-view AABB proxies.
// SV_VertexID 0–35; instance = vertexId / 36.

struct BoxGpu
{
    float3 center;
    float pad0;
    float3 halfExt;
    float pad1;
};

cbuffer FaceCb : register(b0)
{
    row_major float4x4 ViewProj;
    float3 LightPos;
    float Range;
    uint BoxCount;
    uint3 FacePad;
};

StructuredBuffer<BoxGpu> Boxes : register(t0);

static const float3 kCube[36] =
{
    float3(1, -1, -1), float3(1, 1, -1), float3(1, 1, 1),
    float3(1, -1, -1), float3(1, 1, 1), float3(1, -1, 1),
    float3(-1, -1, 1), float3(-1, 1, 1), float3(-1, 1, -1),
    float3(-1, -1, 1), float3(-1, 1, -1), float3(-1, -1, -1),
    float3(-1, 1, -1), float3(-1, 1, 1), float3(1, 1, 1),
    float3(-1, 1, -1), float3(1, 1, 1), float3(1, 1, -1),
    float3(-1, -1, 1), float3(-1, -1, -1), float3(1, -1, -1),
    float3(-1, -1, 1), float3(1, -1, -1), float3(1, -1, 1),
    float3(-1, -1, 1), float3(1, -1, 1), float3(1, 1, 1),
    float3(-1, -1, 1), float3(1, 1, 1), float3(-1, 1, 1),
    float3(1, -1, -1), float3(-1, -1, -1), float3(-1, 1, -1),
    float3(1, -1, -1), float3(-1, 1, -1), float3(1, 1, -1)
};

void __vertex_shader(uint id : SV_VertexID, out float4 clip : SV_Position, out float3 world : TEXCOORD0)
{
    uint instance = id / 36;
    uint vert = id - instance * 36;
    world = 0;
    clip = float4(0, 0, 2, 1);
    if (instance >= BoxCount)
        return;

    BoxGpu box = Boxes[instance];
    if (any(box.halfExt < 1e-4))
        return;

    world = box.center + kCube[vert] * box.halfExt;
    clip = mul(float4(world, 1), ViewProj);
}

float4 __pixel_shader(float4 pos : SV_Position, float3 world : TEXCOORD0) : SV_Target
{
    float dist = length(world - LightPos);
    return float4(dist, dist, dist, 1);
}
