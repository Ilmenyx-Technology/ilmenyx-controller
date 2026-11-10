// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Per-draw matrices (exactly 128 bytes: the guaranteed push-constant size) and per-frame values in a small dynamic UBO.
layout(push_constant) uniform PC { mat4 viewProj; mat4 lightViewProj; } pc;
layout(set = 0, binding = 2) uniform Frame { vec4 eye; vec4 sunDir; vec4 params; } ubo; // params: x = time, y = shadow texel
