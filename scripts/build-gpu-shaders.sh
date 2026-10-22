#!/usr/bin/env bash
# New Ilmenyx additions: see LICENSING.md.
# Compiles the GLSL in src/Ilmenyx.Gpu.Vulkan/Shaders to SPIR-V (embedded resources) and validates them. Needs glslc and spirv-val.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
dir="$repo/src/Ilmenyx.Gpu.Vulkan/Shaders"
for tool in glslc spirv-val; do command -v "$tool" >/dev/null || { echo "Falta $tool" >&2; exit 1; }; done
shopt -s nullglob
for source in "$dir"/*.vert "$dir"/*.frag "$dir"/*.comp; do
  glslc --target-env=vulkan1.3 -O "$source" -o "$source.spv"
  spirv-val --target-env vulkan1.3 "$source.spv"
  echo "ok $(basename "$source")"
done
