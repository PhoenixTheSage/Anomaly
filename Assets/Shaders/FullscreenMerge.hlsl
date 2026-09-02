#pragma pack_matrix(row_major)

Texture2D Isolated : register(t0);
Texture2D Dest : register(t1);

#ifndef MERGE_MODE
#define MERGE_MODE 0
#endif

float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    uint2 p = uint2(pos.xy);
    float4 s = Isolated[p];
#if MERGE_MODE == 1
    float4 d = Dest[p];
    return s + d;
#elif MERGE_MODE == 2
    float4 d = Dest[p];
    return s + d * (1 - saturate(s.a));
#else
    return s;
#endif
}
