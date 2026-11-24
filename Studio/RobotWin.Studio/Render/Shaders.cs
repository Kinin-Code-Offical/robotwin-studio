namespace RobotWin.Studio.Render
{
    /// <summary>
    /// [render] The GPU renderer's shaders (HLSL, compiled once at start): physically based shading (GGX microfacets,
    /// Smith shadowing, Schlick Fresnel, energy-conserving diffuse), a hemisphere of sky and ground light, the sun's shadow
    /// filtered over 3 × 3 taps, and filmic tone mapping (ACES) to sRGB. Matrices are row-major, multiplied as row
    /// vectors (v·M), the same convention as WPF's Matrix3D and System.Numerics.
    /// </summary>
    internal static class Shaders
    {
        public const string Source = @"
cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 LightViewProj;
    float3 CameraPos;   float Exposure;
    float3 SunDir;      float ShadowTexel;      // SunDir: the way the light travels
    float3 SunColor;    float ShadowsOn;
    float3 SkyColor;    float AmbientScale;
    float3 GroundColor; float FillCount;
    float4 FillDir[3];
    float4 FillColor[3];
};

// Every object of the frame in one buffer, written once a frame; a draw names its object through the instance number.
struct ObjectData
{
    row_major float4x4 World;
    row_major float4x4 NormalMatrix;
    float4 BaseColor;
    float3 Emissive;    float Roughness;
    float  Metallic;    float HasTexture;   float Unlit;   float Pad0;
    float4 UvTransform;   // uv * xy + zw: the brush's own mapping (its viewport, or the texture coordinates' bounds)
    float4 Overlay;       // diffuse layers over the first (charring, a tint): their colour and coverage
    float4 GlowUv;        // the glowing picture's own mapping
    float4 GlowTint;      // its tint; w: there is one
};

StructuredBuffer<ObjectData> Objects : register(t3);

cbuffer Lamps : register(b2)
{
    float4 LampInfo;        // x: how many
    float4 LampPos[16];     // xyz: where, w: range
    float4 LampColor[16];   // rgb: radiance, w: 1 for a spot
    float4 LampAtten[16];   // constant, linear, quadratic; w: cos of half the inner cone
    float4 LampDir[16];     // xyz: the way a spot points, w: cos of half the outer cone
};

Texture2D    Albedo    : register(t0);
Texture2D    ShadowMap : register(t1);
Texture2D    GlowMap   : register(t2);
SamplerState Linear    : register(s0);
SamplerState GlowSampler : register(s2);
SamplerComparisonState ShadowCmp : register(s1);

struct VSIn  { float3 Pos : POSITION; float3 Nrm : NORMAL; float2 Uv : TEXCOORD0; uint Object : OBJECT; };
struct PSIn  { float4 Pos : SV_POSITION; float3 WorldPos : TEXCOORD0; float3 Nrm : TEXCOORD1; float2 Uv : TEXCOORD2; float4 LightPos : TEXCOORD3; float2 GlowUv : TEXCOORD4; nointerpolation uint Object : OBJECT; };

PSIn VS(VSIn v)
{
    ObjectData ob = Objects[v.Object];
    PSIn o;
    float4 wp = mul(float4(v.Pos, 1), ob.World);
    o.WorldPos = wp.xyz;
    o.Pos = mul(wp, ViewProj);
    o.Nrm = mul(float4(v.Nrm, 0), ob.NormalMatrix).xyz;
    o.Uv = v.Uv * ob.UvTransform.xy + ob.UvTransform.zw;
    o.GlowUv = v.Uv * ob.GlowUv.xy + ob.GlowUv.zw;
    o.LightPos = mul(wp, LightViewProj);
    o.Object = v.Object;
    return o;
}

float4 VSDepth(VSIn v) : SV_POSITION
{
    return mul(mul(float4(v.Pos, 1), Objects[v.Object].World), LightViewProj);
}

static const float PI = 3.14159265;

float D_GGX(float nh, float a)
{
    float a2 = a * a;
    float d = nh * nh * (a2 - 1) + 1;
    return a2 / (PI * d * d);
}

float G_Smith(float nv, float nl, float a)
{
    float k = (a + 1) * (a + 1) / 8;
    return (nv / (nv * (1 - k) + k)) * (nl / (nl * (1 - k) + k));
}

float3 F_Schlick(float vh, float3 f0)
{
    return f0 + (1 - f0) * pow(1 - vh, 5);
}

float3 Light(float3 n, float3 v, float3 l, float3 radiance, float3 albedo, float3 f0, float a, float metal)
{
    float nl = saturate(dot(n, l));
    if (nl <= 0) return 0;
    float3 h = normalize(v + l);
    float nv = max(dot(n, v), 1e-4), nh = saturate(dot(n, h)), vh = saturate(dot(v, h));
    float3 f = F_Schlick(vh, f0);
    float3 spec = D_GGX(nh, a) * G_Smith(nv, nl, a) * f / (4 * nv * nl + 1e-4);
    float3 diff = (1 - f) * (1 - metal) * albedo / PI;
    return (diff + spec) * radiance * nl;
}

float Shadow(float4 lp, float3 n)
{
    if (ShadowsOn < 0.5) return 1;
    float3 p = lp.xyz / lp.w;
    float2 uv = float2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);
    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 || p.z > 1) return 1;
    float bias = ShadowTexel * 1.5 * (1 + 2 * (1 - saturate(dot(n, -SunDir))));
    float sum = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
        sum += ShadowMap.SampleCmpLevelZero(ShadowCmp, uv + float2(x, y) * ShadowTexel, p.z - bias);
    return sum / 9;
}

float3 Aces(float3 x)
{
    return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
}

float3 ToSrgb(float3 c)
{
    return c <= 0.0031308 ? 12.92 * c : 1.055 * pow(c, 1 / 2.4) - 0.055;
}

float3 ToLinear(float3 c)
{
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

float4 PS(PSIn i, bool front : SV_IsFrontFace) : SV_TARGET
{
    ObjectData ob = Objects[i.Object];
    float4 base = ob.BaseColor;
    if (ob.HasTexture > 0.5)
    {
        float4 t = Albedo.Sample(Linear, i.Uv);
        base.rgb *= ToLinear(t.rgb);
        base.a *= t.a;
    }

    base.rgb = lerp(base.rgb, ob.Overlay.rgb, ob.Overlay.a);
    base.a = base.a + ob.Overlay.a * (1 - base.a);

    // Light of its own only: its colour as chosen, added to what is behind (blended One + One).
    if (ob.Unlit > 0.5) return float4(ToSrgb(saturate(base.rgb)) * base.a, 0);

    float3 n = normalize(i.Nrm);
    if (!front) n = -n;
    float3 v = normalize(CameraPos - i.WorldPos);
    float a = max(ob.Roughness * ob.Roughness, 0.002);
    float3 albedo = base.rgb;
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), albedo, ob.Metallic);

    float3 c = Light(n, v, -SunDir, SunColor, albedo, f0, a, ob.Metallic) * Shadow(i.LightPos, n);
    for (int k = 0; k < (int)FillCount; k++)
        c += Light(n, v, -FillDir[k].xyz, FillColor[k].rgb, albedo, f0, a, ob.Metallic);

    // Lamps and LEDs, lit per pixel (WPF lights per vertex): a real pool of light on the floor.
    for (int j = 0; j < (int)LampInfo.x; j++)
    {
        float3 d = LampPos[j].xyz - i.WorldPos;
        float dist = length(d);
        if (dist > LampPos[j].w) continue;
        float3 l = d / max(dist, 1e-6);
        float att = 1 / max(LampAtten[j].x + LampAtten[j].y * dist + LampAtten[j].z * dist * dist, 1e-4);
        if (LampColor[j].w > 0.5)
            att *= smoothstep(LampDir[j].w, max(LampAtten[j].w, LampDir[j].w + 1e-4), dot(-l, LampDir[j].xyz));
        c += Light(n, v, l, LampColor[j].rgb * att, albedo, f0, a, ob.Metallic);
    }

    // Light from the sky above and the ground below: diffusely, and mirrored in the reflected direction, weighted by the
    // split-sum environment BRDF (Karis' analytic fit), so a rough plastic is not lit up like a mirror at grazing angles.
    float nv = saturate(dot(n, v));
    float3 hemi = lerp(GroundColor, SkyColor, n.z * 0.5 + 0.5) * AmbientScale;
    float3 r = reflect(-v, n);
    float3 hemiSpec = lerp(GroundColor, SkyColor, saturate(lerp(r.z, n.z, ob.Roughness) * 0.5 + 0.5)) * AmbientScale;
    float4 q = ob.Roughness * float4(-1, -0.0275, -0.572, 0.022) + float4(1, 0.0425, 1.04, -0.04);
    float a004 = min(q.x * q.x, exp2(-9.28 * nv)) * q.x + q.y;
    float2 ab = float2(-1.04, 1.04) * a004 + q.zw;
    float3 envSpec = f0 * ab.x + ab.y;
    // The workshop around the scene is dim (dark walls, a few lamps): what a glossy surface mirrors is darker than the
    // light that reaches it from all sides, so the mirrored part is weighed down (a red wire stays red, not pink).
    c += hemi * albedo * (1 - ob.Metallic) * (1 - envSpec) + hemiSpec * envSpec * lerp(0.4, 1.0, ob.Metallic);
    c += ob.Emissive;
    if (ob.GlowTint.w > 0.5)
    {
        float4 g = GlowMap.Sample(GlowSampler, i.GlowUv);
        c += ToLinear(g.rgb) * g.a * ob.GlowTint.rgb;
    }
    return float4(ToSrgb(Aces(c * Exposure)), base.a);
}
";
    }
}
