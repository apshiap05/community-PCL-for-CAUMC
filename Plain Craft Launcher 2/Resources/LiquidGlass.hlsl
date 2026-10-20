// CAUMC native WPF port. See THIRD_PARTY_NOTICES.md.
// Rounded SDF / circular lens falloff: LiquidGlassUI (MIT, Wei-Zhang09).
// Analytical normals, chromatic sampling and directional rim: inspired by
// liquid_glass_widgets (MIT, Sebastian Degenaar). Foreground is a separate layer.
sampler2D Backdrop : register(s0);
float4 Viewport : register(c0); // sampled width,height; viewport origin in shape
float4 Shape : register(c1);    // full width,height; radius; bezel width
float4 Optics : register(c2);   // thickness, dispersion, saturation, refraction on
float4 Light : register(c3);    // normalized xy, specular strength, unused
float4 Tint : register(c4);     // RGB + density

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 pos = uv * Viewport.xy + Viewport.zw;
    float2 p = pos - Shape.xy * 0.5;
    float r = min(Shape.z, min(Shape.x, Shape.y) * 0.5);
    float2 q = abs(p) - Shape.xy * 0.5 + r;
    float2 outside = max(q, 0.0);
    float len = length(outside);
    float sd = len + min(max(q.x, q.y), 0.0) - r;
    float2 normal = sign(p) * outside / max(len, 0.001);
    float inward = max(-sd, 0.0);
    float t = saturate(1.0 - inward / Shape.w);
    // Snell's law at IOR 1.5, evaluated continuously across the convex rim.
    float normalZ = sqrt(saturate(1.0 - t * t));
    float transmittedZ = sqrt(1.0 - (4.0 / 9.0) * t * t);
    float ray = t * (1.0 - (2.0 / 3.0) * normalZ / transmittedZ);
    float2 bend = -normal * ray * Optics.x;
    float2 delta = bend * Optics.w / Viewport.xy;
    // A single achromatic sample keeps edges quiet, without rainbow fringes.
    float3 rgb = tex2D(Backdrop, uv + delta).rgb;
    rgb = lerp(rgb, Tint.rgb, Tint.a);
    float rim = exp2(-inward * 0.65);
    float shine = saturate(dot(normal, Light.xy));
    shine *= shine;
    shine *= shine;
    rgb += (shine * Light.z + 0.012) * rim;
    // The host clips the surface, including antialiasing; never distort content.
    return float4(saturate(rgb), 1.0);
}
