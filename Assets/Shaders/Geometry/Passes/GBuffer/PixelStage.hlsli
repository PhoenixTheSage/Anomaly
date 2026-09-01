struct PixelStageInput
{
	float4 position : SV_Position;
	MaterialVertexPayload custom;

	float3 key_color : TEXCOORD7;
    float2 custom_alpha : TEXCOORD9;
	#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)
		float3 position_ws : TEXCOORD8;
	#endif

	#ifdef USE_SIMPLE_INSTANCING_COLORING
		float4 instance_key_color_dithering : TEXCOORD10;
		float4 instance_color_mult_emissivity : TEXCOORD11;
	#endif

#ifdef ANOMALY_VELOCITY
	float2 anomaly_velocity : TEXCOORD12;
#endif
};

#define ANOMALY_PIXEL_STAGE
#include <Anomaly.hlsli>

#ifdef ANOMALY_PACK_GBUFFER1A
#ifndef ANOMALY_GBUFFER1A_OVERRIDE
float AnomalyGBuffer1A(PixelInterface pixel, MaterialOutputInterface material_output)
{
	return 0;
}
#endif
#endif

#include <GBuffer/GBufferWrite.hlsli>

void __pixel_shader(PixelStageInput input, out GbufferOutput output, uint coverage : SV_Coverage)
{
	PixelInterface pixel;
	pixel.screen_position = input.position.xyz;
	pixel.custom = input.custom;
	init_ps_interface(pixel);

	#ifdef PASS_OBJECT_VALUES_THROUGH_STAGES
		pixel.key_color = input.key_color;
        pixel.custom_alpha = input.custom_alpha;
	#endif

	#ifdef USE_SIMPLE_INSTANCING_COLORING
		pixel.key_color = input.instance_key_color_dithering.xyz;
        pixel.custom_alpha = float2(pixel.custom_alpha.x, input.instance_key_color_dithering.w);
		pixel.color_mul = input.instance_color_mult_emissivity.xyz;
		pixel.emissive = input.instance_color_mult_emissivity.w;
	#endif

	#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)
		pixel.position_ws = input.position_ws;
	#endif

	MaterialOutputInterface material_output = make_mat_interface();
	material_output.coverage = coverage;
    material_output.LOD = pixel.LOD;
	
	pixel_program(pixel, material_output);

    ApplyMultipliers(material_output);

#ifdef ANOMALY_VELOCITY
	float2 anomaly_velocity = input.anomaly_velocity;
#else
	float2 anomaly_velocity = 0;
#endif

	#ifdef STATIC_DECAL
		#ifdef USE_TEXTURE_INDICES
			float4 texIndices = pixel.custom.texIndices;
			float decalAlpha = AlphamaskTexture.Sample(TextureSampler, float3(pixel.custom.texcoord0, texIndices.w));
		#else
			float decalAlpha = AlphamaskTexture.Sample(TextureSampler, pixel.custom.texcoord0);
		#endif
		float ao = material_output.ao;
		float normalAlpha = 0;
		#ifdef USE_NORMALGLOSS_TEXTURE
            normalAlpha = ComputeNormalMapAlphaBestEffort(material_output.normal, input.custom.normal, decalAlpha);
		#endif
		#ifndef USE_EXTENSIONS_TEXTURE
			ao = ComputeAmbientOcclusionBestEffort(pixel.custom.normal, material_output.normal);
		#endif

        #ifdef CUSTOM_DEPTH
            float depth = material_output.depth > 0 ? material_output.depth : input.position.z;
            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,
                material_output.emissive, decalAlpha, normalAlpha, 1, anomaly_velocity, depth);
        #else
            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,
                material_output.emissive, decalAlpha, normalAlpha, 1, anomaly_velocity);
        #endif

	#elif defined(CUSTOM_DEPTH)
		float depth = material_output.depth > 0 ? material_output.depth : input.position.z;
        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, anomaly_velocity, depth);
	#else
        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, anomaly_velocity);
	#endif

#ifndef STATIC_DECAL
#ifdef ANOMALY_PACK_GBUFFER1A
	output.gbuffer1.a = AnomalyGBuffer1A(pixel, material_output);
#endif
#endif
}
