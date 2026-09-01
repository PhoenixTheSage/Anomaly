#pragma pack_matrix(row_major)

cbuffer Constants : register(b0)
{
    float Mode;
    float Scale;
    float HistoryValid;
    float _pad;
};

Texture2D Tex : register(t0);
SamplerState PointSamp : register(s0);

// Mode 0: velocity (mid-gray rest, R/G signed delta, B speed). Magenta if history invalid.
// Mode 1: linear depth / Hi-Z (log grayscale).
// Mode 2: history color (passthrough).
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    if (Mode < 0.5)
    {
        if (HistoryValid < 0.5)
            return float4(1, 0, 1, 1);
        float2 v = Tex.SampleLevel(PointSamp, uv, 0).rg;
        float mag = length(v);
        float3 rgb;
        rgb.r = saturate(v.x * Scale + 0.5);
        rgb.g = saturate(v.y * Scale + 0.5);
        rgb.b = saturate(mag * Scale);
        return float4(rgb, 1);
    }

    if (Mode < 1.5)
    {
        float d = max(Tex.SampleLevel(PointSamp, uv, 0).r, 0);
        float g = saturate(log2(1 + d) * 0.08);
        return float4(g, g, g, 1);
    }

    return float4(Tex.SampleLevel(PointSamp, uv, 0).rgb, 1);
}
