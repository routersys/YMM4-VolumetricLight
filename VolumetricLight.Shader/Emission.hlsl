Texture2D SourceTexture : register(t0);
SamplerState SourceSampler : register(s0);

cbuffer Constants : register(b0)
{
    float threshold : packoffset(c0.x);
};

static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);
static const float MinimumLuma = 1e-6;

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0
) : SV_TARGET
{
    float4 source = SourceTexture.SampleLevel(SourceSampler, uv0.xy, 0);
    if (source.a <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float luma = dot(source.rgb / source.a, LumaWeights);
    float gain = max(luma - threshold, 0.0) / max(luma, MinimumLuma);
    return source * gain;
}
