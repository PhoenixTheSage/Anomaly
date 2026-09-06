#pragma pack_matrix(row_major)

cbuffer Constants : register(b0)
{
    float Mode;
    float Scale;
    float HistoryValid;
    float HasDepth;
};

Texture2D Tex : register(t0);
Texture2D DepthTex : register(t1);
Texture2D AuditTex : register(t2);
Texture2D GBuffer0Tex : register(t3);
SamplerState PointSamp : register(s0);

// Complementary GBuffer: 0 is clear / sky. Camera-from-depth on that value
// is unstable and saturates the colormap (magenta / cyan / white on pan).
static const float3 kSky = float3(0.06, 0.06, 0.06);

float3 VelocityColor(float2 v)
{
    float mag = length(v);
    return float3(
        saturate(v.x * Scale + 0.5),
        saturate(v.y * Scale + 0.5),
        saturate(mag * Scale + 0.5));
}

bool IsSky(float2 uv)
{
    return HasDepth > 0.5 && !(DepthTex.SampleLevel(PointSamp, uv, 0).r > 0);
}

// Mode 0: velocity. Sky is dark grey; geometry rest is mid-gray (R/G signed
// delta, B speed). Magenta if history invalid on a geometry pixel.
// Mode 1: linear depth / Hi-Z (log grayscale).
// Mode 2: history color (passthrough).
float4 __pixel_shader(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    // Mode 4: one-frame, same-draw proof matrix. Each panel remaps to the full
    // scene so silhouettes line up and can be compared directly:
    // top: Target3, pixel execution, PS b7
    // bottom: VS->PS velocity, GBuffer0 b7 marker, raw GBuffer0.
    if (Mode > 3.5)
    {
        float2 panel = floor(uv * float2(3, 2));
        float2 sampleUv = frac(uv * float2(3, 2));
        if (IsSky(sampleUv))
            return float4(kSky, 1);

        float4 proof = AuditTex.SampleLevel(PointSamp, sampleUv, 0);
        if (panel.y < 0.5)
        {
            if (panel.x < 0.5)
                return float4(VelocityColor(Tex.SampleLevel(PointSamp, sampleUv, 0).rg), 1);
            if (panel.x < 1.5)
                return proof.r > 0.5 ? float4(0.15, 1, 0.15, 1) : float4(0.35, 0.05, 0.05, 1);
            return proof.g > 0.5 ? float4(1, 0, 1, 1) : float4(0.35, 0.35, 0.35, 1);
        }

        if (panel.x < 0.5)
            return float4(VelocityColor(proof.ba), 1);

        float3 baseColor = GBuffer0Tex.SampleLevel(PointSamp, sampleUv, 0).rgb;
        if (panel.x < 1.5)
        {
            bool marker = baseColor.r > 0.9 && baseColor.g < 0.1 && baseColor.b > 0.9;
            return marker ? float4(0.15, 1, 0.15, 1) : float4(0.35, 0.05, 0.05, 1);
        }
        return float4(baseColor, 1);
    }

    if (Mode < 0.5)
    {
        if (HasDepth > 0.5)
        {
            // Complementary: Keen IsDepthForeground is hw > 0. A 1e-5 cutoff
            // painted distant ships as sky (they vanished into the background).
            float hw = DepthTex.SampleLevel(PointSamp, uv, 0).r;
            if (!(hw > 0))
                return float4(kSky, 1);
        }
        if (HistoryValid < 0.5)
            return float4(1, 0, 1, 1);
        // Rest is mid-gray. Blue used to start at 0, so zero motion was olive (0.5, 0.5, 0).
        return float4(VelocityColor(Tex.SampleLevel(PointSamp, uv, 0).rg), 1);
    }

    if (Mode < 1.5)
    {
        float d = max(Tex.SampleLevel(PointSamp, uv, 0).r, 0);
        float g = saturate(log2(1 + d) * 0.08);
        return float4(g, g, g, 1);
    }

    if (Mode < 2.5)
        return float4(Tex.SampleLevel(PointSamp, uv, 0).rgb, 1);

    // Mode 3: reactive mask (R8, white = do not trust history).
    float r = saturate(Tex.SampleLevel(PointSamp, uv, 0).r);
    return float4(r, r, r, 1);
}
