#pragma pack_matrix(row_major)

Texture2D<float> Src : register(t0);

// Min of 2×2 for a Hi-Z pyramid step (SSR / contact shadows).
float __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    int2 dst = int2(pos.xy);
    int2 src = dst * 2;
    uint w, h;
    Src.GetDimensions(w, h);
    int2 maxPix = int2((int)w - 1, (int)h - 1);
    float a = Src.Load(int3(min(src, maxPix), 0)).r;
    float b = Src.Load(int3(min(src + int2(1, 0), maxPix), 0)).r;
    float c = Src.Load(int3(min(src + int2(0, 1), maxPix), 0)).r;
    float d = Src.Load(int3(min(src + int2(1, 1), maxPix), 0)).r;
    return min(min(a, b), min(c, d));
}
