#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
# Validates the GPU paths on ANY machine you own or were lent (AMD/NVIDIA desktop, friend's PC): no install, no root, no network.
# Phase 1 (default) is read-only. Phase 2 runs short, bounded GPU loads and needs --allow-gpu-load AND the machine's owner agreeing.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
allow=0
if [[ $# == 3 && $3 == --allow-gpu-load ]]; then allow=1; elif [[ $# != 2 ]]; then
  echo 'Uso: bash scripts/validate-gpu-remote.sh /pacote/Ilmenyx-linux-x64 /saida-nova [--allow-gpu-load]' >&2; exit 2
fi
[[ $1 == /* && $2 == /* ]] || { echo 'Use caminhos absolutos.' >&2; exit 2; }
[[ $(id -u) != 0 ]] || { echo 'Execute como usuario comum, sem sudo.' >&2; exit 2; }
cli=$1/cli/ilmenyx
[[ -x $cli ]] || { echo 'Pacote invalido.' >&2; exit 2; }
mkdir -- "$2"; out=$(cd -- "$2" && pwd)
run() { name=$1; shift; echo "== $name"; { timeout 120 "$@" > "$out/$name.txt" 2>&1; echo "exit=$?" >> "$out/$name.txt"; } || true; tail -3 "$out/$name.txt"; }
{ uname -sr; grep PRETTY_NAME /etc/os-release; } > "$out/ambiente.txt" 2>&1
run gpu-list "$cli" bench gpu-list
run sensors-gpu bash -c "\"$cli\" sensors --json | jq -c '[.readings[]|select(.device|startswith(\"gpu:\"))|{device,channel,label,value:.reading.value,unit:.reading.unit,status:.reading.status,reason:.reading.reason}]'"
run info-gpu bash -c "\"$cli\" info --json | jq -c '.gpus // .gpu // empty'"
if command -v vulkaninfo >/dev/null; then run rt-extensions bash -c "vulkaninfo 2>/dev/null | grep -E 'VK_KHR_(ray_tracing_pipeline|acceleration_structure|ray_query)|deviceName'"; fi  # kept in order: each extension stays under its device
if [[ $allow == 1 ]]; then
  echo "FASE 2: cargas curtas de GPU (autorizadas por --allow-gpu-load)"
  run gpu-burn "$cli" bench gpu-burn --allow-gpu-load --seconds 8 --json
  run gpu-scene "$cli" bench gpu-scene --allow-gpu-load --seconds 8 --json
  run gpu-snapshot "$cli" bench gpu-snapshot --allow-gpu-load --output "$out/cena.png"
else
  echo 'Fase 2 NAO executada (sem --allow-gpu-load).' | tee "$out/fase2.txt"
fi
echo "Relatorio em $out. Revise antes de compartilhar (nao inclui serie nem hostname, mas confira)."
