#pragma pack_matrix(row_major)

// IsolatedAdd / IsolatedMix / DirectAdd / PublishOnly with TemporalPolicy.Reactive:
// stamp dilated isolated luminance into reactiveMask. High = reject DLSS history.
// Max with the current mask so multiple IsolatedAdd programs accumulate.

Texture2D Isolated : register(t0);
Texture2D<float> Current : register(t1);

static const int DilateRadius = 2;
static const float LumaGain = 8.0;

void __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0, out float output : SV_Target0)
{
    uint w, h;
    Isolated.GetDimensions(w, h);
    int2 dim = int2(w, h);
    int2 p = int2(pos.xy);

    float peak = 0;
    [unroll]
    for (int y = -DilateRadius; y <= DilateRadius; y++)
    {
        [unroll]
        for (int x = -DilateRadius; x <= DilateRadius; x++)
        {
            int2 q = clamp(p + int2(x, y), 0, dim - 1);
            float3 c = Isolated.Load(int3(q, 0)).rgb;
            peak = max(peak, max(c.r, max(c.g, c.b)));
        }
    }

    float stamp = saturate(peak * LumaGain);
    float prev = Current.Load(int3(p, 0)).r;
    output = max(prev, stamp);
}
