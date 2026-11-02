#version 450

layout(location = 0) in vec2 uv;
layout(set = 0, binding = 0) uniform sampler2D scene;
layout(set = 0, binding = 1) uniform sampler2D bloom;

layout(push_constant) uniform Push {
    vec4 settings; // x = bloom strength, y = exposure, z = chromatic aberration, w = time in seconds
    vec4 extra;    // x = 1 when the target needs manual sRGB encoding
} push;

layout(location = 0) out vec4 outColor;

// ACES filmic tone curve (Narkowicz fit): rich neon highlights roll off instead of clipping.
vec3 aces(vec3 x) {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

// Bit test for Inf/NaN: MoltenVK compiles with fast math, which may fold isnan() and isinf() to false.
bool isFinite(vec3 c) {
    uvec3 exponent = floatBitsToUint(c) & 0x7F800000u;
    return all(notEqual(exponent, uvec3(0x7F800000u)));
}

vec3 sanitize(vec3 c) {
    return isFinite(c) ? clamp(c, 0.0, 64.0) : vec3(0.0);
}

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void main() {
    vec2 offset = uv - 0.5;
    float edge = dot(offset, offset);
    // Lens-like colour fringing that grows towards the corners.
    vec2 shift = offset * edge * push.settings.z;
    vec3 color = vec3(
        texture(scene, uv - shift).r,
        texture(scene, uv).g,
        texture(scene, uv + shift).b);
    color = sanitize(color) + sanitize(texture(bloom, uv).rgb) * push.settings.x;
    color = aces(color * push.settings.y);
    color *= mix(1.0, smoothstep(0.85, 0.15, length(offset * vec2(1.0, 0.85))), 0.5);
    color += (hash(uv * 1000.0 + push.settings.w) - 0.5) * 0.012;
    if (push.extra.x > 0.5) {
        color = pow(max(color, vec3(0.0)), vec3(1.0 / 2.2));
    }

    outColor = vec4(color, 1.0);
}
