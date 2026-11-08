#version 450
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
layout(set = 0, binding = 1) uniform sampler2D hdr;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;
void main() {
    vec3 c = texture(hdr, uv).rgb;
    c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14); // ACES approximation
    vec2 d = uv * 2.0 - 1.0;
    c *= 1.0 - 0.18 * dot(d, d);
    outColor = vec4(pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2)), 1.0);
}
