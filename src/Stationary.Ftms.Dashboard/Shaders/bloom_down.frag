#version 450

layout(location = 0) in vec2 uv;
layout(set = 0, binding = 0) uniform sampler2D source;

layout(push_constant) uniform Push {
    vec4 settings; // xy = source texel size, z = bright-pass threshold, w = 1 for the first (bright-pass) level
    vec4 extra;
} push;

layout(location = 0) out vec4 outColor;

float luminance(vec3 color) {
    return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

// Weighting by 1 / (1 + luma) keeps single bright pixels from flickering in the bloom.
vec3 karis(vec3 a, vec3 b, vec3 c, vec3 d) {
    float wa = 1.0 / (1.0 + luminance(a));
    float wb = 1.0 / (1.0 + luminance(b));
    float wc = 1.0 / (1.0 + luminance(c));
    float wd = 1.0 / (1.0 + luminance(d));
    return (a * wa + b * wb + c * wc + d * wd) / (wa + wb + wc + wd);
}

// Bit test for Inf/NaN: MoltenVK compiles with fast math, which may fold isnan() and isinf() to false.
bool isFinite(vec3 c) {
    uvec3 exponent = floatBitsToUint(c) & 0x7F800000u;
    return all(notEqual(exponent, uvec3(0x7F800000u)));
}

vec3 fetch(vec2 p) {
    vec3 c = texture(source, p).rgb;
    return isFinite(c) ? clamp(c, 0.0, 64.0) : vec3(0.0);
}

// 13-tap downsample from "Next Generation Post Processing in Call of Duty: Advanced Warfare".
void main() {
    vec2 t = push.settings.xy;
    vec3 a = fetch(uv + t * vec2(-2.0, -2.0));
    vec3 b = fetch(uv + t * vec2(0.0, -2.0));
    vec3 c = fetch(uv + t * vec2(2.0, -2.0));
    vec3 d = fetch(uv + t * vec2(-2.0, 0.0));
    vec3 e = fetch(uv);
    vec3 f = fetch(uv + t * vec2(2.0, 0.0));
    vec3 g = fetch(uv + t * vec2(-2.0, 2.0));
    vec3 h = fetch(uv + t * vec2(0.0, 2.0));
    vec3 i = fetch(uv + t * vec2(2.0, 2.0));
    vec3 j = fetch(uv + t * vec2(-1.0, -1.0));
    vec3 k = fetch(uv + t * vec2(1.0, -1.0));
    vec3 l = fetch(uv + t * vec2(-1.0, 1.0));
    vec3 m = fetch(uv + t * vec2(1.0, 1.0));

    vec3 color;
    if (push.settings.w > 0.5) {
        color = karis(j, k, l, m) * 0.5
            + karis(a, b, d, e) * 0.125 + karis(b, c, e, f) * 0.125
            + karis(d, e, g, h) * 0.125 + karis(e, f, h, i) * 0.125;
        // Soft-knee bright pass: only light above the threshold blooms.
        float threshold = push.settings.z;
        float brightness = max(color.r, max(color.g, color.b));
        float knee = threshold * 0.5;
        float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-4);
        color *= max(soft, brightness - threshold) / max(brightness, 1e-4);
    } else {
        color = (j + k + l + m) * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + e * 0.125;
    }

    outColor = vec4(max(color, vec3(0.0)), 1.0);
}
