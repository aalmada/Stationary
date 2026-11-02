#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;

// Matrices are System.Numerics row-major memory read as column-major, so M * v applies them in .NET order.
layout(push_constant) uniform Push {
    mat4 viewProjection;
    mat4 model;
    vec4 color;   // rgb neon colour, a = intensity
    vec4 camera;  // xyz camera position, w = fog density
    vec4 fog;     // rgb horizon colour, w = time in seconds
    vec4 effects; // x = material, y = track scroll in metres, z = cadence beat 0..1, w = streak energy 0..1
    vec4 heart;   // x = heartbeat phase in beats, y = heart rate bpm, z = lub-dub pulse 0..1, w = 1 when mirrored
    vec4 tint;    // rgb heart-rate zone colour, w = per-draw parameter
} push;

layout(location = 0) out vec3 worldNormal;
layout(location = 1) out vec3 worldPosition;
layout(location = 2) out vec3 localPosition;

void main() {
    vec4 world = push.model * vec4(inPosition, 1.0);
    worldPosition = world.xyz;
    worldNormal = mat3(push.model) * inNormal;
    localPosition = inPosition;
    gl_Position = push.viewProjection * world;
}
