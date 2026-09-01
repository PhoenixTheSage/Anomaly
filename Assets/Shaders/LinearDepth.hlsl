#pragma pack_matrix(row_major)

cbuffer Constants : register(b0)
{
    float Proj33;
    float Proj43;
    float2 _pad;
};

Texture2D DepthTex : register(t0);
SamplerState PointSamp : register(s0);

// Complementary depth → positive view-space Z (same as Frame.hlsli compute_depth).
float __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float hw = DepthTex.SampleLevel(PointSamp, uv, 0).r;
    float linear = -Proj43 / (max(hw, 1e-36) + Proj33);
    return -linear;
}
