#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Ilmenyx GPU burn: furry torus drawn as N inflated shells. No vertex buffers: positions come from gl_VertexIndex.
layout(push_constant) uniform PC { mat4 viewProj; float time; float shells; int iterations; float aspect; } pc;
layout(location = 0) out vec3 vNormal;
layout(location = 1) out vec2 vUv;
layout(location = 2) out float vShell;
const int U = 192;
const int V = 96;
const ivec2 offs[6] = ivec2[](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 0), ivec2(1, 1), ivec2(0, 1));
void main() {
    int quad = gl_VertexIndex / 6;
    int corner = gl_VertexIndex % 6;
    int qu = quad % U;
    int qv = quad / U;
    float fu = float(qu + offs[corner].x) / float(U);
    float fv = float(qv + offs[corner].y) / float(V);
    float u = fu * 6.2831853 + pc.time * 0.4;
    float v = fv * 6.2831853;
    vec3 n = vec3(cos(u) * cos(v), sin(v), sin(u) * cos(v));
    vec3 center = vec3(cos(u), 0.0, sin(u));
    float shell = float(gl_InstanceIndex) / max(pc.shells - 1.0, 1.0);
    vec3 pos = center + n * (0.42 + shell * 0.16);
    gl_Position = pc.viewProj * vec4(pos, 1.0);
    vNormal = n;
    vUv = vec2(fu, fv);
    vShell = shell;
}
