struct VertexStageOutput
{
	float4 position : SV_Position;
	MaterialVertexPayload custom;

	float3 key_color : TEXCOORD7;
    float2 custom_alpha : TEXCOORD9;
#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)
	float3 position_ws : TEXCOORD8;
#endif

#ifdef USE_SIMPLE_INSTANCING_COLORING // all other key colors and and emissivities are deprecated for simple instancing
	float4 instance_key_color_dithering : TEXCOORD10;
	float4 instance_color_mult_emissivity : TEXCOORD11;
#endif

#ifdef ANOMALY_VELOCITY
	float2 anomaly_velocity : TEXCOORD12;
#endif
};

#include <Anomaly.hlsli>

void __vertex_shader(__VertexInput input, out VertexStageOutput output, uint sv_vertex_id : SV_VertexID, uint sv_instance_id : SV_InstanceID)
{
	VertexShaderInterface vertex = __prepare_interface(input, sv_vertex_id, sv_instance_id);

	vertex_program(vertex, output.custom);

#if defined(STATIC_DECAL_CUTOUT) || defined(STATIC_DECAL)
    output.position = vertex.position_clip + float4(0.0f, 0.0f, 0.0001f, 0.0f);
#else
    output.position = vertex.position_clip;
#endif

	output.key_color = vertex.key_color;
    output.custom_alpha = vertex.custom_alpha;

#ifdef USE_SIMPLE_INSTANCING_COLORING
	output.instance_key_color_dithering = input.instance_keyColorDithering;
	output.instance_color_mult_emissivity = input.instance_colorMultEmissivity;
#endif

#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)
	output.position_ws = vertex.position_local.xyz;
#endif

#ifdef ANOMALY_VELOCITY
	output.anomaly_velocity = AnomalyComputeVelocity(vertex.position_local.xyz, vertex._local_matrix, sv_instance_id);
#endif
}
