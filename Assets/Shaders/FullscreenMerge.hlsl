#pragma pack_matrix(row_major)

Texture2D Isolated : register(t0);
Texture2D Dest : register(t1);

#ifndef MERGE_MODE
#define MERGE_MODE 0
#endif

SamplerState LinearSampler : register(s0);

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    uint2 p = uint2(pos.xy);
#if MERGE_MODE == 3
    float4 d = Dest[p];
    float3 occ = saturate(Isolated.SampleLevel(LinearSampler, uv, 0).rgb);
    return float4(d.rgb * (1 - occ), d.a);
#else
    float4 s = Isolated.SampleLevel(LinearSampler, uv, 0);
#if MERGE_MODE == 1
    float4 d = Dest[p];
    return float4(s.rgb + d.rgb, d.a);
#elif MERGE_MODE == 2
    float4 d = Dest[p];
    return s + d * (1 - saturate(s.a));
#else
    return s;
#endif
#endif
}
