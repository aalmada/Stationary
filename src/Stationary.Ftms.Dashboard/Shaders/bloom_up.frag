#version 450

layout(location = 0) in vec2 uv;
layout(set = 0, binding = 0) uniform sampler2D source;

layout(push_constant) uniform Push {
    vec4 settings; // xy = source texel size, z = filter radius in texels, w = intensity
    vec4 extra;
} push;

layout(location = 0) out vec4 outColor;

// 3x3 tent filter; the result is added onto the next larger level by the blend state.
void main() {
    vec2 t = push.settings.xy * push.settings.z;
    vec3 color = texture(source, uv).rgb * 4.0;
    color += (texture(source, uv + vec2(-t.x, 0.0)).rgb + texture(source, uv + vec2(t.x, 0.0)).rgb
        + texture(source, uv + vec2(0.0, -t.y)).rgb + texture(source, uv + vec2(0.0, t.y)).rgb) * 2.0;
    color += texture(source, uv + vec2(-t.x, -t.y)).rgb + texture(source, uv + vec2(t.x, -t.y)).rgb
        + texture(source, uv + vec2(-t.x, t.y)).rgb + texture(source, uv + vec2(t.x, t.y)).rgb;
    outColor = vec4(color / 16.0 * push.settings.w, 1.0);
}
