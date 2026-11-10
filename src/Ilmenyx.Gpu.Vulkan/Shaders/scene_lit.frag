#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
#extension GL_GOOGLE_include_directive : require
#include "common.glsl"
#include "scene_push.glsl"
layout(set = 0, binding = 0) uniform sampler2D shadowMap;
layout(location = 0) in vec3 vWorld;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec4 vShadow;
layout(location = 3) in float vMat;
layout(location = 0) out vec4 outColor;
float shadowFactor(vec4 s) {
    vec3 p = s.xyz / s.w;
    vec2 uv = p.xy * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || p.z > 1.0) return 1.0;
    float lit = 0.0;
    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
        lit += (p.z - 0.0015 > texture(shadowMap, uv + vec2(x, y) * ubo.params.y).r) ? 0.0 : 1.0;
    return lit / 9.0;
}
void main() {
    vec3 n = normalize(vNormal);
    vec3 albedo;
    if (vMat > 1.5) albedo = vec3(0.07, 0.30, 0.10);
    else if (vMat > 0.5) albedo = vec3(0.30, 0.18, 0.09);
    else {
        float h = vWorld.y, slope = 1.0 - n.y;
        albedo = mix(vec3(0.76, 0.70, 0.50), vec3(0.20, 0.42, 0.14), smoothstep(WATER_Y, WATER_Y + 3.0, h));
        albedo = mix(albedo, vec3(0.45, 0.43, 0.40), smoothstep(0.25, 0.5, slope));
        albedo = mix(albedo, vec3(0.92), smoothstep(46.0, 56.0, h));
    }
    float ndl = max(dot(n, normalize(-ubo.sunDir.xyz)), 0.0);
    vec3 color = albedo * (vec3(1.2, 1.08, 0.9) * ndl * shadowFactor(vShadow) * 2.0 + vec3(0.35, 0.42, 0.55) * 0.6);
    float dist = length(vWorld - ubo.eye.xyz);
    color = mix(color, vec3(0.55, 0.65, 0.8) * 1.4, 1.0 - exp(-dist * 0.0022));
    outColor = vec4(color, 1.0);
}
