#pragma pack_matrix(row_major)

cbuffer Constants : register(b0)
{
    float Scale;
    float HistoryValid;
    float2 _pad;
};

Texture2D VelocityTex : register(t0);
SamplerState PointSamp : register(s0);

// Mid-gray = no motion. R/G = signed X/Y pixel delta. B = speed.
// Magenta = history invalid (first frame or camera cut).
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    if (HistoryValid < 0.5)
        return float4(1, 0, 1, 1);

    float2 v = VelocityTex.SampleLevel(PointSamp, uv, 0).rg;
    float mag = length(v);
    float3 rgb;
    rgb.r = saturate(v.x * Scale + 0.5);
    rgb.g = saturate(v.y * Scale + 0.5);
    rgb.b = saturate(mag * Scale);
    return float4(rgb, 1);
}
