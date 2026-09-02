#pragma pack_matrix(row_major)

Texture2D<float2> VelocityTex : register(t0);
Texture2D<float2> OverlayTex : register(t1);
Texture2D<float> MaskTex : register(t2);

void __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0, out float2 output : SV_Target0)
{
    uint2 pixel = uint2(pos.xy);
    float mask = MaskTex[pixel].r;
    float2 base = VelocityTex[pixel];
    float2 overlay = OverlayTex[pixel];
    output = mask > 0.5 ? overlay : base;
}
