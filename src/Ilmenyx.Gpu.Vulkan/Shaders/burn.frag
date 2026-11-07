#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Strand discard plus a data-dependent ALU loop the compiler cannot remove; the colour depends on the loop result.
layout(push_constant) uniform PC { mat4 viewProj; float time; float shells; int iterations; float aspect; } pc;
layout(location = 0) in vec3 vNormal;
layout(location = 1) in vec2 vUv;
layout(location = 2) in float vShell;
layout(location = 0) out vec4 outColor;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
void main() {
    vec2 cell = floor(vUv * vec2(900.0, 450.0));
    float h = hash(cell);
    if (h < vShell * 0.85) discard;
    float acc = h;
    for (int i = 0; i < pc.iterations; i++) acc = fract(sin(acc * 12.9898 + float(i)) * 43758.5453) + 0.0001 * acc;
    outColor = vec4(acc, 1.0 - vShell, vNormal.y * 0.5 + 0.5, 1.0);
}
