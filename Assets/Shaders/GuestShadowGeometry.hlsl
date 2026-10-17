// Same distance and world-axis cube contract as MeshDepth/BoxDepth. The guest
// receives copied skinned local triangles; World translation is subtracted
// from the light (or camera for playerDepth) in double precision on the host.
cbuffer GuestShadowGeometry : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 World;
    float4 Viewport;
    uint VertexCount;
    uint MeshVertices;
    uint BoxStart;
    uint BoxCount;
};
struct BoxGpu { float3 center; float pad0; float3 halfExt; float pad1; };
StructuredBuffer<float4> Vertices : register(t0);
StructuredBuffer<BoxGpu> Boxes : register(t1);
#include "AnomalyBoxCorners.hlsli"

void geometry_vertex(uint id : SV_VertexID, out float4 clip : SV_Position, out float3 world : TEXCOORD0)
{
    if (id < MeshVertices)
        world = mul(float4(Vertices[id].xyz, 1), World).xyz;
    else
    {
        uint at = id - MeshVertices;
        BoxGpu box = Boxes[BoxStart + at / 36];
        world = box.center + kCube[at % 36] * box.halfExt;
    }
    clip = mul(float4(world, 1), ViewProj);
    // Positive Vulkan viewports invert the D3D clip-Y convention.
    clip.y = -clip.y;
}

float4 __pixel_shader(float4 pos : SV_Position, float3 world : TEXCOORD0) : SV_Target0
{
    float distanceMetres = length(world);
    if (distanceMetres < 0.04) discard;
    return float4(distanceMetres, distanceMetres, distanceMetres, 1);
}
