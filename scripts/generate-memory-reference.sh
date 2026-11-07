#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_dir/upstream/ResetExplode"
if [ "$(git -C "$source_dir" rev-parse HEAD)" != ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6 ] || ! git -C "$source_dir" diff HEAD --quiet -- MemoryBench.cs; then
    echo 'Pinned upstream mismatch or MemoryBench.cs modified; review required.' >&2
    exit 1
fi
reference_dir="$(mktemp -d /tmp/ilmenyx-memory-reference.XXXXXXXX)"
trap 'rm -rf -- "$reference_dir"' EXIT
cp -- "$repo_dir/tools/MemoryReference/MemoryReference.csproj" "$reference_dir/"
python3 - "$source_dir/MemoryBench.cs" "$repo_dir/tools/MemoryReference/Program.cs" "$reference_dir/Program.cs" <<'PY'
import sys,pathlib
source=pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig')
def method(signature):
 start=source.index(signature); brace=source.index('{',start); level=1; end=brace+1
 while level:
  if source[end]=='{':level+=1
  if source[end]=='}':level-=1
  end+=1
 return source[start:end]
def once(text,old,new):
 if text.count(old)!=1:raise RuntimeError('Unexpected upstream layout: '+old)
 return text.replace(old,new)
lat=method('public static double MeasureLatencyNs(')
lat=once(lat,'200_000','1_024');lat=once(lat,'20_000_000','32_768')
lat=once(lat,'return ns;','FinalIndex = checked((int)p); RingHash = Hash(next); return ns;')
bw=method('public static (double Read, double Copy) MeasureBandwidth(')
bw=once(bw,'int threads = Environment.ProcessorCount;', 'int threads = Threads;')
bw=once(bw,'rep < 3', 'rep < 1')
bw=once(bw,'sw.Restart();','ReadSum = total; sw.Restart();')
bw=once(bw,'return (bestRead, bestCopy);','CopySum = dst.Sum(); CopyHash = Hash(dst); return (bestRead, bestCopy);')
code=lat+'\n'+bw
if any(x in code for x in ['File.', 'NativeMemory', 'MeasureLoading', '20_000_000', '200_000']):raise RuntimeError('Unsafe oracle content')
template=pathlib.Path(sys.argv[2]).read_text()
pathlib.Path(sys.argv[3]).write_text(template.replace('// INJECT_RAM_METHODS',code))
PY
dotnet run --project "$reference_dir/MemoryReference.csproj" -c Release -- "$repo_dir/tests/Ilmenyx.Tests/Fixtures/memory-reference.json"
