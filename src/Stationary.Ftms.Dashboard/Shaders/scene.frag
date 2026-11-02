#version 450

layout(location = 0) in vec3 worldNormal;
layout(location = 1) in vec3 worldPosition;
layout(location = 2) in vec3 localPosition;

layout(push_constant) uniform Push {
    mat4 viewProjection;
    mat4 model;
    vec4 color;   // rgb linear neon colour, a = intensity
    vec4 camera;  // xyz camera position, w = fog density
    vec4 fog;     // rgb horizon colour, w = time in seconds
    vec4 effects; // x = material, y = track scroll in metres (period 200 m), z = cadence beat 0..1, w = streak energy
    vec4 heart;   // x = heartbeat phase in beats, y = heart rate bpm, z = lub-dub pulse 0..1, w = 1 when mirrored
    vec4 tint;    // rgb heart-rate zone colour, w = per-draw parameter
} push;

// Output is linear HDR: values above 1 bloom in the post-processing passes.
layout(location = 0) out vec4 outColor;

// Materials match SceneMaterial in RideSceneComposer.cs.
const int Solid = 0;    // dark glass body outlined by glowing neon edges
const int Grid = 1;     // wet Tron floor: scrolling light grid over puddles that mirror the scene
const int Neon = 2;     // fully emissive tube or gem with a white-hot core
const int Glow = 3;     // additive radial halo on a quad
const int Streak = 4;   // additive light streak on a quad, bright along its centre line
const int Sky = 5;      // dome: deep space, synthwave sun, neon horizon and a live ECG trace
const int Track = 6;    // glossy black glass lane with accent edges and cadence scanlines

float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Value noise on a lattice that repeats every `period` cells, so it stays continuous when the scroll wraps.
float noise(vec2 p, float period) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash(vec3(mod(cell, period), 3.0));
    float b = hash(vec3(mod(cell + vec2(1.0, 0.0), period), 3.0));
    float c = hash(vec3(mod(cell + vec2(0.0, 1.0), period), 3.0));
    float d = hash(vec3(mod(cell + vec2(1.0, 1.0), period), 3.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

bool isMirrored() {
    return push.heart.w > 0.5;
}

float fogFactor(float density) {
    return clamp(1.0 - exp(-density * length(worldPosition - push.camera.xyz)), 0.0, 1.0);
}

// Antialiased grid line coverage for a line spacing and width in metres.
float gridLine(vec2 p, float spacing, float widthMetres) {
    vec2 cell = abs(fract(p / spacing - 0.5) - 0.5) * spacing;
    vec2 pixel = max(fwidth(p), vec2(1e-4));
    vec2 line = 1.0 - smoothstep(vec2(widthMetres) * 0.5, vec2(widthMetres) * 0.5 + pixel * 1.5, cell);
    // Lines thinner than a pixel shimmer, so fade them out with distance.
    vec2 fade = 1.0 - smoothstep(0.15, 0.6, pixel / spacing * 4.0);
    return max(line.x * fade.x, line.y * fade.y);
}

// Antialiased coverage of evenly spaced lines across one axis.
float stripe(float v, float spacing, float widthMetres) {
    float cell = abs(fract(v / spacing - 0.5) - 0.5) * spacing;
    float pixel = max(fwidth(v), 1e-4);
    float line = 1.0 - smoothstep(widthMetres * 0.5, widthMetres * 0.5 + pixel * 1.5, cell);
    return line * (1.0 - smoothstep(0.15, 0.6, pixel / spacing * 4.0));
}

vec3 shadeSolid(vec3 normal, vec3 view) {
    vec3 scale = vec3(length(push.model[0].xyz), length(push.model[1].xyz), length(push.model[2].xyz));
    vec3 toFace = (0.5 - abs(localPosition)) * scale;
    vec3 pixel = max(fwidth(toFace), vec3(1e-4));
    vec3 edges = toFace / max(vec3(0.035), pixel * 1.5);
    // An edge is where two faces meet, so use the second-smallest distance.
    float second = min(max(edges.x, edges.y), min(max(edges.y, edges.z), max(edges.x, edges.z)));
    float edge = 1.0 - smoothstep(0.6, 1.0, second);
    float halo = exp(-second * 0.35) * 0.35;
    float fresnel = pow(clamp(1.0 - abs(dot(normal, view)), 0.0, 1.0), 3.0);
    // Faint lit windows on tall buildings.
    vec2 windowCell = floor(vec2(localPosition.x * scale.x + localPosition.z * scale.z, localPosition.y * scale.y) / vec2(0.9, 1.2));
    float window = step(0.86, hash(vec3(windowCell, floor(push.model[3].x)))) * step(abs(normal.y), 0.5) * step(6.0, scale.y);
    vec3 body = push.color.rgb * (0.04 + 0.3 * fresnel + window * 0.22) + vec3(0.003, 0.004, 0.012);
    float energy = push.color.a * (1.0 + 0.6 * push.effects.z);
    return body + push.color.rgb * energy * (edge * 1.6 + halo) + vec3(edge * edge * 0.45 * push.color.a);
}

const float ReflectionStrength = 0.55;

// Fresnel reflectance of a wet floor: puddles mirror strongly, dry patches stay dull.
float wetReflectance(vec3 normal, vec3 view, vec2 p, float dry, float wet) {
    float puddle = noise(p / 5.0, 40.0) * 0.65 + noise(p / 2.5, 80.0) * 0.35;
    float wetness = smoothstep(0.38, 0.62, puddle);
    float fresnel = pow(1.0 - clamp(dot(normal, view), 0.0, 1.0), 4.0);
    return clamp(mix(dry, wet, wetness) + fresnel * 0.3, 0.0, 0.9);
}

// Premultiplied colour: alpha is the share of the surface itself; the rest lets the mirrored scene show through.
vec4 shadeGrid(vec3 normal, vec3 view) {
    vec2 p = vec2(worldPosition.x, worldPosition.z - push.effects.y);
    float minor = gridLine(p, 2.0, 0.05);
    float major = gridLine(p, 10.0, 0.12);
    vec2 cell = abs(fract(p / 2.0 - 0.5) - 0.5) * 2.0;
    float halo = exp(-min(cell.x, cell.y) * 9.0) * 0.18;
    float beat = 0.7 + 0.6 * push.effects.z;
    vec3 lines = push.color.rgb * push.color.a * (minor * 0.7 + major * 1.6 + halo) * beat;
    float reflectance = wetReflectance(normal, view, p, 0.12, 0.7);
    float alpha = 1.0 - reflectance;
    return vec4(vec3(0.002, 0.003, 0.009) * alpha + lines, alpha);
}

vec4 shadeTrack(vec3 normal, vec3 view) {
    float across = abs(localPosition.x) * 2.0;
    vec2 p = vec2(worldPosition.x, worldPosition.z - push.effects.y);
    float edge = smoothstep(0.86, 0.98, across);
    float scan = stripe(p.y, 1.0, 0.04) * (0.18 + 0.7 * push.effects.z);
    float chevron = stripe(p.y + abs(localPosition.x) * 6.0, 6.0, 0.08) * (1.0 - across) * 0.6;
    vec3 emissive = push.color.rgb * push.color.a * (edge * 1.2 + scan + chevron);
    float reflectance = wetReflectance(normal, view, p * 1.7, 0.35, 0.85);
    float alpha = 1.0 - reflectance;
    return vec4(vec3(0.001, 0.0015, 0.004) * alpha + emissive, alpha);
}

vec3 shadeNeon(vec3 normal, vec3 view) {
    float facing = abs(dot(normal, view));
    float core = pow(clamp(facing, 0.0, 1.0), 3.0);
    float flicker = 0.92 + 0.08 * sin(push.fog.w * 23.0 + worldPosition.z);
    vec3 neon = push.color.rgb * push.color.a * (0.9 + 0.9 * core) * flicker;
    return neon + vec3(core * core * 0.6 * push.color.a);
}

// A Gaussian bump; avoids pow() with a negative base, which is undefined in GLSL and NaN on Metal.
float bump(float x, float width) {
    float q = x / width;
    return exp(-q * q);
}

// One heartbeat of an ECG: P wave, QRS spike and T wave.
float ecg(float t) {
    float f = fract(t);
    return 0.12 * bump(f - 0.12, 0.035)
        - 0.15 * bump(f - 0.27, 0.012)
        + 1.0 * bump(f - 0.30, 0.013)
        - 0.28 * bump(f - 0.33, 0.014)
        + 0.25 * bump(f - 0.55, 0.06);
}

const float EcgHalfWidth = 0.55;
const float EcgBeatsPerRadian = 2.6;

float ecgHeight(float azimuth) {
    // The newest beat is drawn at the right edge; older beats scroll to the left.
    return 0.31 + 0.055 * ecg(push.heart.x - (EcgHalfWidth - azimuth) * EcgBeatsPerRadian);
}

vec3 shadeEcg(float azimuth, float elevation) {
    if (push.heart.y < 30.0 || abs(azimuth) > EcgHalfWidth + 0.02 || elevation < 0.2 || elevation > 0.45) {
        return vec3(0.0);
    }

    float e = 0.0015;
    float y = ecgHeight(azimuth);
    float slope = (ecgHeight(azimuth + e) - ecgHeight(azimuth - e)) / (2.0 * e);
    float distance = abs(elevation - y) / sqrt(1.0 + slope * slope);
    float pixel = max(fwidth(elevation), 1e-5);
    float core = 1.0 - smoothstep(pixel * 0.7, pixel * 2.2, distance);
    float glow = exp(-distance / (pixel * 7.0)) * 0.45 + exp(-distance / (pixel * 30.0)) * 0.12;
    float age = smoothstep(-EcgHalfWidth, EcgHalfWidth, azimuth);
    float ends = smoothstep(EcgHalfWidth + 0.02, EcgHalfWidth - 0.04, abs(azimuth));
    vec3 color = mix(push.tint.rgb, vec3(1.0), core * 0.35);
    float head = bump(azimuth - EcgHalfWidth, 0.012) * bump(elevation - y, 0.012) * 3.0;
    return color * ((core * 2.6 + glow) * (0.25 + 0.75 * age) * ends + head * (1.0 + push.heart.z));
}

vec3 shadeSun(float azimuth, float elevation) {
    const float Radius = 0.12;
    vec2 offset = vec2(azimuth, elevation - 0.06);
    float distance = length(offset);
    float halo = exp(-distance * 9.0) * 0.12 + exp(-distance * 24.0) * 0.18;
    vec3 haloColor = mix(vec3(1.0, 0.25, 0.55), vec3(1.0, 0.55, 0.2), clamp(offset.y / Radius + 0.5, 0.0, 1.0));
    float pixel = max(fwidth(distance), 1e-5);
    float disc = 1.0 - smoothstep(Radius - pixel, Radius + pixel, distance);
    // Synthwave bands cut the lower half of the sun and drift downwards.
    float below = clamp(-offset.y / Radius, 0.0, 1.0);
    float band = fract(below * 7.0 - push.fog.w * 0.25);
    float cut = step(band, below * 0.75) * step(0.04, below);
    float t = clamp(offset.y / Radius * 0.5 + 0.5, 0.0, 1.0);
    vec3 face = mix(vec3(1.0, 0.08, 0.5), vec3(1.0, 0.75, 0.15), t) * 1.15;
    return face * disc * (1.0 - cut) + haloColor * halo;
}

vec3 shadeSky(vec3 direction) {
    float height = direction.y;
    float azimuth = atan(direction.x, -direction.z);
    float elevation = asin(clamp(height, -1.0, 1.0));
    vec3 zenith = vec3(0.0, 0.001, 0.008);
    vec3 sky = mix(push.fog.rgb, zenith, smoothstep(0.0, 0.35, height));
    // A thin neon horizon line with a wide soft bloom that breathes with the heart.
    float breathe = 1.0 + 0.35 * push.heart.z;
    sky += push.color.rgb * push.color.a * breathe * (exp(-abs(height) * 90.0) * 1.6 + exp(-abs(height) * 14.0) * 0.25);
    vec3 cell = floor(direction * 320.0);
    float star = step(0.9965, hash(cell)) * smoothstep(0.04, 0.25, height);
    float twinkle = 0.6 + 0.4 * sin(push.fog.w * 2.0 + hash(cell + 7.0) * 40.0);
    sky += vec3(0.7, 0.85, 1.0) * star * twinkle * 1.4;
    sky += shadeSun(azimuth, elevation) + shadeEcg(azimuth, elevation);
    return sky * (height < 0.0 ? exp(height * 30.0) : 1.0);
}

// Bit test for Inf/NaN: MoltenVK compiles with fast math, which may fold isnan() and isinf() to false.
bool isFinite(vec3 c) {
    uvec3 exponent = floatBitsToUint(c) & 0x7F800000u;
    return all(notEqual(exponent, uvec3(0x7F800000u)));
}

void emit(vec4 color) {
    vec3 rgb = isFinite(color.rgb) ? clamp(color.rgb, 0.0, 64.0) : vec3(0.0);
    outColor = vec4(isMirrored() ? rgb * ReflectionStrength * vec3(0.85, 0.95, 1.0) : rgb, color.a);
}

void main() {
    // Mirrored geometry only exists below the floor plane; the wet floor is drawn over it.
    if (isMirrored() && worldPosition.y > 0.0) {
        discard;
    }

    int material = int(push.effects.x + 0.5);
    vec3 normal = normalize(worldNormal);
    vec3 view = normalize(push.camera.xyz - worldPosition);

    if (material == Sky) {
        vec3 direction = -view;
        if (isMirrored()) {
            direction.y = -direction.y;
        }

        emit(vec4(shadeSky(direction), 1.0));
        return;
    }

    if (material == Glow || material == Streak) {
        vec2 q = localPosition.xy * 2.0;
        float shape = material == Glow
            ? exp(-dot(q, q) * 3.5) * (1.0 - smoothstep(0.85, 1.0, length(q)))
            : (exp(-q.x * q.x * 9.0) + exp(-q.x * q.x * 120.0)) * 0.6 * (1.0 - smoothstep(0.7, 1.0, abs(q.y)));
        float fade = 1.0 - fogFactor(push.camera.w * 0.8);
        emit(vec4(push.color.rgb * push.color.a * shape * fade, 0.0));
        return;
    }

    if (material == Grid || material == Track) {
        vec4 surface = material == Grid ? shadeGrid(normal, view) : shadeTrack(normal, view);
        float fog = fogFactor(push.camera.w);
        emit(vec4(surface.rgb * (1.0 - fog) + push.fog.rgb * fog, mix(surface.a, 1.0, fog)));
        return;
    }

    vec3 shaded;
    float density = push.camera.w;
    if (material == Neon) {
        shaded = shadeNeon(normal, view);
        density *= 0.6;
    } else {
        shaded = shadeSolid(normal, view);
    }

    emit(vec4(mix(shaded, push.fog.rgb, fogFactor(density)), 1.0));
}
