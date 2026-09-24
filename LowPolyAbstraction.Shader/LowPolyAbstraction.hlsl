Texture2D SourceTexture : register(t0);
SamplerState SourceSampler : register(s0);
Texture2D PolyTexture : register(t1);
SamplerState PolySampler : register(s1);

cbuffer Constants : register(b0)
{
    float amount : packoffset(c0.x);
    float pad0 : packoffset(c0.y);
    float pad1 : packoffset(c0.z);
    float pad2 : packoffset(c0.w);
};

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1
) : SV_TARGET
{
    float4 source = SourceTexture.SampleLevel(SourceSampler, uv0.xy, 0);
    if (amount <= 0.0)
        return source;

    float4 poly = PolyTexture.SampleLevel(PolySampler, uv1.xy, 0);
    poly.rgb = min(poly.rgb, poly.a.xxx);
    poly *= poly.a > source.a ? source.a / poly.a : 1.0;
    float covered = source.a > 0.0 ? saturate(poly.a / source.a) : 1.0;
    float4 blended = poly + source * (1.0 - covered);
    return lerp(source, blended, amount);
}
