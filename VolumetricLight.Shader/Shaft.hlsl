Texture2D Level0 : register(t0);
Texture2D Level1 : register(t1);
Texture2D Level2 : register(t2);
Texture2D Level3 : register(t3);
Texture2D Level4 : register(t4);
Texture2D Level5 : register(t5);
SamplerState Sampler0 : register(s0);
SamplerState Sampler1 : register(s1);
SamplerState Sampler2 : register(s2);
SamplerState Sampler3 : register(s3);
SamplerState Sampler4 : register(s4);
SamplerState Sampler5 : register(s5);

cbuffer Constants : register(b0)
{
    float4 localFromCamera[4] : packoffset(c0);
    float4 planes[6] : packoffset(c4);
    float4 focus : packoffset(c10);
    float4 axis : packoffset(c11);
    float4 view : packoffset(c12);
    float4 gobo : packoffset(c13);
    float4 beam : packoffset(c14);
    float4 tint : packoffset(c15);
    float4 march : packoffset(c16);
    float4 dust : packoffset(c17);
    float4 wind : packoffset(c18);
    float4 mode : packoffset(c19);
    float4 fade : packoffset(c20);
};

static const int MaximumLevel = 5;
static const float RateEpsilon = 1e-9;
static const float UnitScale = 1.0 / 16777216.0;
static const uint HashStep = 0x9E3779B9u;
static const uint HashMultiplyA = 0x85EBCA6Bu;
static const uint HashMultiplyB = 0xC2B2AE35u;
static const float DustSecondWeight = 0.5;
static const float LinearFalloffLevel = 0.9999;

static float4 SceneUv[6];
static float2 ScenePoint;

uint Mix(uint value)
{
    value ^= value >> 16;
    value *= HashMultiplyA;
    value ^= value >> 13;
    value *= HashMultiplyB;
    value ^= value >> 16;
    return value;
}

float ToUnit(uint value)
{
    return (float)(value >> 8) * UnitScale;
}

float Lattice(int3 cell, uint seed)
{
    return ToUnit(Mix((uint)cell.x * HashStep ^ (uint)cell.y * HashMultiplyA ^ (uint)cell.z * HashMultiplyB ^ seed));
}

float ValueNoise(float3 point3, uint seed)
{
    float3 cellOrigin = floor(point3);
    int3 cell = (int3)cellOrigin;
    float3 fraction = point3 - cellOrigin;
    float3 blend = fraction * fraction * (3.0 - 2.0 * fraction);
    float n000 = Lattice(cell, seed);
    float n100 = Lattice(cell + int3(1, 0, 0), seed);
    float n010 = Lattice(cell + int3(0, 1, 0), seed);
    float n110 = Lattice(cell + int3(1, 1, 0), seed);
    float n001 = Lattice(cell + int3(0, 0, 1), seed);
    float n101 = Lattice(cell + int3(1, 0, 1), seed);
    float n011 = Lattice(cell + int3(0, 1, 1), seed);
    float n111 = Lattice(cell + int3(1, 1, 1), seed);
    return lerp(
        lerp(lerp(n000, n100, blend.x), lerp(n010, n110, blend.x), blend.y),
        lerp(lerp(n001, n101, blend.x), lerp(n011, n111, blend.x), blend.y),
        blend.z);
}

float Dust(float3 point3, uint seed)
{
    return (ValueNoise(point3, seed) + DustSecondWeight * ValueNoise(point3 * 2.0, Mix(seed))) / (1.0 + DustSecondWeight);
}

float4 ReadLevel(int level, float2 imagePosition)
{
    float2 position = imagePosition * exp2(-(float)level);
    float4 uvs = SceneUv[level];
    float2 uv = uvs.xy + (position - ScenePoint) * uvs.zw;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    if (level == 0)
        return Level0.SampleLevel(Sampler0, uv, 0);
    if (level == 1)
        return Level1.SampleLevel(Sampler1, uv, 0);
    if (level == 2)
        return Level2.SampleLevel(Sampler2, uv, 0);
    if (level == 3)
        return Level3.SampleLevel(Sampler3, uv, 0);
    if (level == 4)
        return Level4.SampleLevel(Sampler4, uv, 0);
    return Level5.SampleLevel(Sampler5, uv, 0);
}

float4 SampleGobo(float2 imagePosition, float lod)
{
    float clamped = clamp(lod, 0.0, (float)MaximumLevel);
    int lower = (int)floor(clamped);
    int upper = min(lower + 1, MaximumLevel);
    float blend = clamped - (float)lower;
    float4 low = ReadLevel(lower, imagePosition);
    if (blend <= 0.0)
        return low;

    return lerp(low, ReadLevel(upper, imagePosition), blend);
}

float2 ImagePosition(float3 sourcePoint)
{
    return float2(gobo.x * sourcePoint.x + gobo.y, gobo.z * sourcePoint.y + gobo.w);
}

void FindSource(float3 volumePoint, out float3 sourcePoint, out float travel)
{
    if (focus.w > 0.5)
    {
        float span = focus.z - volumePoint.z;
        float ratio = abs(span) > RateEpsilon ? -volumePoint.z / span : 0.0;
        float3 toFocus = focus.xyz - volumePoint;
        sourcePoint = volumePoint + toFocus * ratio;
        travel = length(toFocus) * abs(ratio);
    }
    else
    {
        float along = volumePoint.z / axis.z;
        sourcePoint = volumePoint - axis.xyz * along;
        travel = along;
    }
}

float GetLod(float divisor, float travel)
{
    return log2(max(max(divisor, 2.0 * beam.y * travel) / march.z, 1.0));
}

float GetPhase(float3 direction, float3 toEye)
{
    float anisotropy = beam.z;
    float scatterBase = 1.0 + anisotropy * anisotropy - 2.0 * anisotropy * dot(direction, toEye);
    return (1.0 - anisotropy * anisotropy) * rsqrt(scatterBase) / scatterBase;
}

float GetFalloff(float travel)
{
    if (fade.x >= LinearFalloffLevel)
        return saturate(1.0 - travel * fade.y);

    return saturate((exp(beam.x * travel) - fade.x) / (1.0 - fade.x));
}

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1,
    float4 uv2 : TEXCOORD2,
    float4 uv3 : TEXCOORD3,
    float4 uv4 : TEXCOORD4,
    float4 uv5 : TEXCOORD5
) : SV_TARGET
{
    SceneUv[0] = uv0;
    SceneUv[1] = uv1;
    SceneUv[2] = uv2;
    SceneUv[3] = uv3;
    SceneUv[4] = uv4;
    SceneUv[5] = uv5;
    ScenePoint = scenePosition.xy;

    float footprint = mode.z;
    float2 screenPoint = ScenePoint * footprint;
    float3 viewBase = screenPoint.x * localFromCamera[0].xyz + screenPoint.y * localFromCamera[1].xyz;
    float3 rayOrigin = viewBase + localFromCamera[3].xyz;
    float3 rayStep = view.x * viewBase + localFromCamera[2].xyz;

    float zLow = -1e30;
    float zHigh = march.w;
    bool inside = true;
    [unroll]
    for (int index = 0; index < 6; ++index)
    {
        float4 plane = planes[index];
        float numerator = dot(plane.xyz, rayOrigin) - plane.w;
        float rate = dot(plane.xyz, rayStep);
        if (abs(rate) > RateEpsilon)
        {
            float z = -numerator / rate;
            if (rate > 0.0)
                zLow = max(zLow, z);
            else
                zHigh = min(zHigh, z);
        }
        else if (numerator < 0.0)
        {
            inside = false;
        }
    }

    if (!inside || zHigh <= zLow)
        return float4(0.0, 0.0, 0.0, 0.0);

    float3 nearSource;
    float nearTravel;
    float3 farSource;
    float farTravel;
    FindSource(rayOrigin + zLow * rayStep, nearSource, nearTravel);
    FindSource(rayOrigin + zHigh * rayStep, farSource, farTravel);
    float rayLength = length(rayStep);
    float pathLength = rayLength * (zHigh - zLow);
    float middleLod = 0.5 * (GetLod((1.0 + view.x * zLow) * footprint, nearTravel) + GetLod((1.0 + view.x * zHigh) * footprint, farTravel));
    float texelTravel = length(ImagePosition(farSource) - ImagePosition(nearSource));
    float wanted = texelTravel * exp2(-middleLod) / march.x;
    if (dust.x > 0.0)
        wanted = max(wanted, pathLength * dust.y * mode.y);

    int count = (int)clamp(ceil(wanted), mode.x, march.y);
    float lodFloor = log2(max(texelTravel / (float)count, 1.0));
    float dz = (zHigh - zLow) / (float)count;
    float stepWeight = rayLength * dz * beam.w;
    uint seed = (uint)dust.z;
    int2 cell = (int2)floor(ScenePoint);
    float jitter = ToUnit(Mix((uint)cell.x * HashStep ^ (uint)cell.y * HashMultiplyA));
    bool isPoint = focus.w > 0.5;
    float3 toEye = rayStep / rayLength;
    float parallelPhase = isPoint ? 0.0 : GetPhase(axis.xyz, toEye);
    float3 sum = float3(0.0, 0.0, 0.0);

    [loop]
    for (int sampleIndex = 0; sampleIndex < count; ++sampleIndex)
    {
        float z = zLow + ((float)sampleIndex + jitter) * dz;
        float3 volumePoint = rayOrigin + z * rayStep;
        float3 sourcePoint;
        float travel;
        FindSource(volumePoint, sourcePoint, travel);

        float lod = max(GetLod((1.0 + view.x * z) * footprint, travel), lodFloor);
        float3 emission = SampleGobo(ImagePosition(sourcePoint), lod).rgb;
        float phase = isPoint ? GetPhase(axis.w * normalize(volumePoint - focus.xyz), toEye) : parallelPhase;

        float density = 1.0;
        if (dust.x > 0.0)
            density = max(1.0 + dust.x * (2.0 * Dust((volumePoint + wind.xyz) * dust.y, seed) - 1.0), 0.0);

        sum += emission * (GetFalloff(travel) * phase * density);
    }

    return float4(sum * stepWeight * tint.rgb, 0.0);
}
