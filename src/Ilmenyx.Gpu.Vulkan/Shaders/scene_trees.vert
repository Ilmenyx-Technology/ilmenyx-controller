#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
#extension GL_GOOGLE_include_directive : require
#include "common.glsl"
#include "scene_push.glsl"
layout(location = 0) out vec3 vWorld;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec4 vShadow;
layout(location = 3) out float vMat;
const int VERTS_PER_TREE = 108;
const int SIDES = 6;
void main() {
    int tree = gl_InstanceIndex;
    vec2 seed = vec2(float(tree) * 0.7548776662, float(tree) * 0.5698402909);
    vec2 xz = (vec2(hash21(seed + 1.3), hash21(seed + 7.1)) - 0.5) * (TERRAIN_SIZE - 20.0);
    float y = terrainHeight(xz);
    float slope = length(terrainNormal(xz).xz);
    float scale = 0.8 + 1.4 * hash21(seed + 3.7);
    bool ok = y > WATER_Y + 0.5 && y < 48.0 && slope < 0.55;
    int v = gl_VertexIndex % VERTS_PER_TREE;
    vec3 local; vec3 n; float mat;
    if (v < 36) { // trunk: 6 quads
        int quad = v / 6, corner = v % 6;
        int s = quad + (corner == 1 || corner == 3 || corner == 4 ? 1 : 0);
        float a = 6.2831853 * float(s) / float(SIDES);
        float top = (corner == 2 || corner == 4 || corner == 5) ? 1.0 : 0.0;
        local = vec3(cos(a) * 0.35, top * 2.0, sin(a) * 0.35); n = vec3(cos(a), 0.0, sin(a)); mat = 1.0;
    } else { // three cones of 8 triangles
        int c = (v - 36) / 24, t = ((v - 36) % 24) / 3, corner = (v - 36) % 3;
        float base = 1.6 + float(c) * 1.5, radius = 2.0 - float(c) * 0.5, height = 2.6;
        float a0 = 6.2831853 * float(t) / 8.0, a1 = 6.2831853 * float(t + 1) / 8.0;
        vec3 p0 = vec3(cos(a0) * radius, base, sin(a0) * radius), p1 = vec3(cos(a1) * radius, base, sin(a1) * radius), apex = vec3(0.0, base + height, 0.0);
        local = corner == 0 ? p0 : (corner == 1 ? p1 : apex);
        n = normalize(cross(p1 - p0, apex - p0)); mat = 2.0;
    }
    vec3 world = vec3(xz.x, y, xz.y) + local * scale;
    vWorld = world; vNormal = n; vMat = mat; vShadow = pc.lightViewProj * vec4(world, 1.0);
    gl_Position = ok ? pc.viewProj * vec4(world, 1.0) : vec4(2.0, 2.0, 2.0, 1.0);
}
