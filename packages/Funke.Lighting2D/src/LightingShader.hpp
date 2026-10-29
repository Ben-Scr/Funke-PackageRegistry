#pragma once
namespace Funke::Lighting2D {
	inline constexpr auto k_Shader = R"WGSL(
struct Light { geometry: vec4f, color: vec4f, options: vec4f }
struct Occluder { geometry: vec4f, options: vec4f }
struct Params {
    inverseVP: mat4x4f,
    ambient: vec4f,
    counts: vec4u,
    lights: array<Light, 32>,
    occluders: array<Occluder, 64>,
}
@group(0) @binding(0) var sceneTexture: texture_2d<f32>;
@group(0) @binding(1) var sceneSampler: sampler;
@group(0) @binding(2) var<uniform> params: Params;
struct Vertex { @builtin(position) position: vec4f, @location(0) uv: vec2f }
@vertex fn vs_main(@builtin(vertex_index) id: u32) -> Vertex {
    let uv = vec2f(f32((id << 1u) & 2u), f32(id & 2u));
    return Vertex(vec4f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, 0.0, 1.0), uv);
}
fn shadow(origin: vec2f, destination: vec2f) -> f32 {
    var opacity = 0.0;
    for (var i = 0u; i < params.counts.y; i++) {
        let box = params.occluders[i];
        let relative = origin - box.geometry.xy;
        let delta = destination - origin;
        let c = box.options.x;
        let s = box.options.y;
        let localOrigin = vec2f(c * relative.x + s * relative.y, -s * relative.x + c * relative.y);
        let localDelta = vec2f(c * delta.x + s * delta.y, -s * delta.x + c * delta.y);
        if ((abs(localDelta.x) < 0.00001 && abs(localOrigin.x) > box.geometry.z) ||
            (abs(localDelta.y) < 0.00001 && abs(localOrigin.y) > box.geometry.w)) { continue; }
        let safeDelta = select(vec2f(0.00001), localDelta, abs(localDelta) >= vec2f(0.00001));
        let t0 = (-box.geometry.zw - localOrigin) / safeDelta;
        let t1 = (box.geometry.zw - localOrigin) / safeDelta;
        let near = min(t0, t1);
        let far = max(t0, t1);
        if (min(far.x, far.y) > max(max(near.x, near.y), 0.002) && max(near.x, near.y) < 0.998) {
            opacity = max(opacity, box.options.z);
            if (opacity >= 1.0) { break; }
        }
    }
    return opacity;
}
@fragment fn fs_main(input: Vertex) -> @location(0) vec4f {
    let clip = params.inverseVP * vec4f(input.uv.x * 2.0 - 1.0, 1.0 - input.uv.y * 2.0, 0.0, 1.0);
    let world = clip.xy / clip.w;
    var illumination = params.ambient.rgb;
    for (var i = 0u; i < params.counts.x; i++) {
        let light = params.lights[i];
        let distance = length(world - light.geometry.xy);
        if (distance >= light.geometry.z) { continue; }
        var visibility = 1.0;
        if (light.options.y > 0.5) { visibility -= shadow(light.geometry.xy, world); }
        let attenuation = pow(max(0.0, 1.0 - distance / light.geometry.z), light.options.x);
        illumination += light.color.rgb * (light.geometry.w * attenuation * visibility * light.color.a);
    }
    let source = textureSample(sceneTexture, sceneSampler, input.uv);
    return vec4f(source.rgb * illumination, source.a);
}
)WGSL";
}
