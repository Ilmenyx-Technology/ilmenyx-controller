#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
#extension GL_GOOGLE_include_directive : require
#include "common.glsl"
#include "scene_push.glsl"
layout(location = 0) out vec3 vWorld;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec4 vShadow;
const int N = 512;
const ivec2 offs[6] = ivec2[](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 0), ivec2(1, 1), ivec2(0, 1));
void main() {
    int quad = gl_VertexIndex / 6;
    int corner = gl_VertexIndex % 6;
    int qi = quad % (N - 1) + offs[corner].x;
    int qj = quad / (N - 1) + offs[corner].y;
    vec2 xz = (vec2(qi, qj) / float(N - 1) - 0.5) * TERRAIN_SIZE;
    vec3 world = vec3(xz.x, terrainHeight(xz), xz.y);
    vWorld = world;
    vNormal = terrainNormal(xz);
    vShadow = pc.lightViewProj * vec4(world, 1.0);
    gl_Position = pc.viewProj * vec4(world, 1.0);
}
