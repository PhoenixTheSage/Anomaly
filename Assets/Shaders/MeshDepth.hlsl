// Local-character light-space distance. VS skins with Keen VertexTemplateBase
// (object CB bones) and interpolates pass-relative world like BoxDepth. Atlas
// origin is the light; playerDepth origin is the camera. Clip is
// projection_.view_proj_matrix with no Depth VertexStage z-clamp. PS is
// length(world - LightPos). Do not reconstruct from SV_Position.

#ifndef MESH_DEPTH_PS

struct MaterialConstants
{
    float dummy;
};

struct MaterialVertexPayload
{
    float dummy;
};

#include <Geometry/VertexTemplateBase.hlsli>

void __vertex_shader(__VertexInput input, out float4 clip : SV_Position,
    out float3 world : TEXCOORD0, uint sv_vertex_id : SV_VertexID)
{
    VertexShaderInterface vertex = __prepare_interface(input, sv_vertex_id);
    world = vertex.position_local.xyz;
    clip = vertex.position_clip;
}

#else

cbuffer FaceCb : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 InvViewProj;
    float3 LightPos;
    float Range;
    uint BoxCount;
    float ViewportX;
    float ViewportY;
    float FaceRes;
};

float4 __pixel_shader(float4 pos : SV_Position, float3 world : TEXCOORD0) : SV_Target
{
    float dist = length(world - LightPos);
    if (dist < 0.04)
        discard;
    return float4(dist, dist, dist, 1);
}

#endif
