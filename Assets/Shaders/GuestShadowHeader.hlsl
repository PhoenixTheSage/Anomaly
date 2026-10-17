StructuredBuffer<float4> Header : register(t0);
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target0
{
    return Header[(uint)pos.x];
}
