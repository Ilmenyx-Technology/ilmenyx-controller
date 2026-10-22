#version 450
#extension GL_GOOGLE_include_directive : require
#include "common.glsl"
#include "scene_push.glsl"
layout(location = 0) out vec3 vWorld;
void main() {
    const vec2 c[6] = vec2[](vec2(-1, -1), vec2(1, -1), vec2(-1, 1), vec2(1, -1), vec2(1, 1), vec2(-1, 1));
    vec2 xz = c[gl_VertexIndex] * TERRAIN_SIZE * 0.5;
    vWorld = vec3(xz.x, WATER_Y, xz.y);
    gl_Position = pc.viewProj * vec4(vWorld, 1.0);
}
