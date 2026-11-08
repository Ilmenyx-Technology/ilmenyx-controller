#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
#extension GL_GOOGLE_include_directive : require
#include "common.glsl"
#include "scene_push.glsl"
layout(location = 0) in vec3 vWorld;
layout(location = 0) out vec4 outColor;
void main() {
    vec2 p = vWorld.xz;
    vec3 n = normalize(vec3(
        0.08 * (cos(p.x * 0.35 + ubo.params.x * 1.3) + 0.6 * cos(p.x * 0.9 - p.y * 0.4 + ubo.params.x * 2.1)), 1.0,
        0.08 * (cos(p.y * 0.30 + ubo.params.x * 1.1) + 0.6 * cos(p.y * 0.8 + p.x * 0.5 + ubo.params.x * 1.7))));
    vec3 v = normalize(ubo.eye.xyz - vWorld);
    float fres = pow(1.0 - max(dot(n, v), 0.0), 4.0);
    vec3 sky = vec3(0.55, 0.65, 0.8) * 1.4;
    vec3 refl = reflect(-v, n);
    float spec = pow(max(dot(refl, normalize(-ubo.sunDir.xyz)), 0.0), 900.0);
    vec3 color = mix(vec3(0.02, 0.22, 0.34), sky, fres) + vec3(1.4, 1.2, 0.9) * spec * 3.0;
    float dist = length(vWorld - ubo.eye.xyz);
    color = mix(color, sky, 1.0 - exp(-dist * 0.0022));
    outColor = vec4(color, 0.8);
}
