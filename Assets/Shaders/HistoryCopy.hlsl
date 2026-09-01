#pragma pack_matrix(row_major)

Texture2D Src : register(t0);
SamplerState PointSamp : register(s0);

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return Src.SampleLevel(PointSamp, uv, 0);
}
