#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
set -euo pipefail
# Bounded test oracles only: no full benchmark and no changes to the upstream worktree.
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_dir/upstream/ResetExplode"
expected_commit=ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6
if [ "$(git -C "$source_dir" rev-parse HEAD)" != "$expected_commit" ]; then
    echo 'Upstream SHA mismatch; review before regenerating fixtures.' >&2
    exit 1
fi
if ! git -C "$source_dir" diff HEAD --quiet -- BenchRenderer.cs; then
    echo 'Upstream renderer modified; refusing to generate reference.' >&2
    exit 1
fi
oracle_mode="${1:-reduced}"
case "$oracle_mode" in
    reduced|original-pixels) ;;
    *) echo 'Use reduced or original-pixels.' >&2; exit 1 ;;
esac
reference_dir="$(mktemp -d /tmp/ilmenyx-reference.XXXXXXXX)"
trap 'rm -rf -- "$reference_dir"' EXIT
cp -- "$repo_dir/tools/CpuReference/CpuReference.csproj" "$repo_dir/tools/CpuReference/Program.cs" "$reference_dir/"
if [ "$oracle_mode" = original-pixels ]; then
    cp -- "$source_dir/BenchRenderer.cs" "$reference_dir/BenchRenderer.cs"
else
    sed -e 's/Width = 960, Height = 540, BucketSize = 32/Width = 32, Height = 18, BucketSize = 8/' \
        -e 's/public const int Spp = 1024;/public const int Spp = 8;/' \
        "$source_dir/BenchRenderer.cs" > "$reference_dir/BenchRenderer.cs"
fi
# Program.cs checks the four constants before any render call.
dotnet run --project "$reference_dir/CpuReference.csproj" -c Release -- "$repo_dir/tests/Ilmenyx.Tests/Fixtures" "$oracle_mode"
