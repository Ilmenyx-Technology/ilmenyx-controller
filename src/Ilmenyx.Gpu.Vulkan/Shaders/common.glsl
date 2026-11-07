// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Procedural terrain shared by the scene shaders (and mirrored on the CPU for the camera path).
const float TERRAIN_SIZE = 800.0;
const float WATER_Y = 6.0;
float hash21(vec2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float vnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i), b = hash21(i + vec2(1.0, 0.0)), c = hash21(i + vec2(0.0, 1.0)), d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}
float fbm(vec2 p) { float v = 0.0; float a = 0.5; for (int i = 0; i < 5; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(17.0, 9.0); a *= 0.5; } return v; }
float terrainHeight(vec2 xz) {
    float h = fbm(xz * 0.006) * 70.0 - 18.0;
    float d = (xz.x + 60.0 * sin(xz.y * 0.01)) / 55.0;
    return mix(h, -4.0, exp(-d * d) * 0.9);
}
vec3 terrainNormal(vec2 xz) {
    float e = 0.75;
    return normalize(vec3(terrainHeight(xz - vec2(e, 0.0)) - terrainHeight(xz + vec2(e, 0.0)), 2.0 * e, terrainHeight(xz - vec2(0.0, e)) - terrainHeight(xz + vec2(0.0, e))));
}
