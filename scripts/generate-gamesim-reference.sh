#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
set -euo pipefail
repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_dir/upstream/ResetExplode"
if [ "$(git -C "$source_dir" rev-parse HEAD)" != ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6 ] || ! git -C "$source_dir" diff HEAD --quiet -- GameSimBench.cs; then
    echo 'Pinned upstream mismatch or GameSimBench.cs modified; review required.' >&2; exit 1
fi
reference_dir="$(mktemp -d /tmp/ilmenyx-gamesim-reference.XXXXXXXX)"
trap 'rm -rf -- "$reference_dir"' EXIT
cp -- "$repo_dir/tools/GameSimReference/GameSimReference.csproj" "$repo_dir/tools/GameSimReference/Program.cs" "$reference_dir/"
python3 - "$source_dir/GameSimBench.cs" "$reference_dir/GameSimBench.cs" <<'PY'
import sys,pathlib
s=pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig')
def once(old,new):
 global s
 if s.count(old)!=1:raise RuntimeError('Unexpected upstream layout: '+old)
 s=s.replace(old,new)
once('Entities = 30_000','Entities = 256')
once('public sealed class GameSimBench\n{','public sealed class GameSimBench\n{\n    public int OracleThreads = 1, OracleVisible;\n    public void OracleSetFrames(int value) => _frames = value;')
if s.count('Environment.ProcessorCount * 4')!=3:raise RuntimeError('Unexpected partition')
s=s.replace('Environment.ProcessorCount * 4','OracleThreads * 4')
once('Parallel.For(0, OracleThreads * 4, chunk =>','Parallel.For(0, OracleThreads * 4, new ParallelOptions { MaxDegreeOfParallelism = OracleThreads }, chunk =>')
once('Array.Sort(_drawKey, _drawList, 0, visible);','OracleVisible = visible; Array.Sort(_drawKey, _drawList, 0, visible);')
# A generated oracle must be incapable of executing the timed benchmark, even accidentally.
a=s.index('    public GameSimResult Run(');b=s.index('    private void Reset()',a)
s=s[:a]+s[b:]
if 'Entities = 30_000' in s or ' Run(' in s or 'while (warm.' in s:raise RuntimeError('Unsafe oracle')
pathlib.Path(sys.argv[2]).write_text(s)
PY
dotnet run --project "$reference_dir/GameSimReference.csproj" -c Release -- "$repo_dir/tests/Ilmenyx.Tests/Fixtures/gamesim-reference.json"
