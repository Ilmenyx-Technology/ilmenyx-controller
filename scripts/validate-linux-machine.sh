#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Read-only diagnostics, exports only to a new directory.
# Does not install, elevate, start GUI/workloads/SMART or claim a machine is a clean VM.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
if [[ $# != 2 || $1 != /* || $2 != /* ]]; then
  echo 'Uso: bash scripts/validate-linux-machine.sh /pacote/Ilmenyx-linux-x64 /saida-nova' >&2
  exit 2
fi
[[ $(id -u) != 0 ]] || { echo 'Execute como usuario comum, sem sudo.' >&2; exit 2; }
for command in jq rg sha256sum timeout uname ldd cmp cp sort; do
  command -v "$command" >/dev/null || { echo "Ferramenta ausente: $command" >&2; exit 2; }
done
repo_scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ -f $repo_scripts/test-linux-package.sh ]] || { echo 'Falta test-linux-package.sh junto deste script.' >&2; exit 2; }
bundle=$(cd -- "$1" && pwd)
[[ -f $bundle/SHA256SUMS && -f $bundle/package.json && -x $bundle/cli/ilmenyx ]] || { echo 'Bundle invalido.' >&2; exit 2; }
mkdir -- "$2" # Never reuse, overwrite or remove a result directory.
output=$(cd -- "$2" && pwd)
# Record software/platform metadata, never hostname, environment values, SMART or serials.
cp -- "$bundle/package.json" "$output/package.json"
if [[ -r /etc/os-release ]]; then cp -- /etc/os-release "$output/os-release.txt"; fi
uname -srm > "$output/kernel.txt"
ldd --version > "$output/libc.txt" 2>&1 || true
globalization_mode=unknown
bundle_version=unknown
observed_runtime=unknown
doctor_status=null
check_status=null
write_result() {
  jq -n --arg state "$1" --arg reason "$2" --argjson exitCode "$3" \
    --arg globalizationMode "$globalization_mode" --arg bundleVersion "$bundle_version" \
    --arg observedRuntime "$observed_runtime" --argjson doctorExitCode "$doctor_status" --argjson smokeExitCode "$check_status" \
    '{schemaVersion:"1.1",scope:"CLI package acceptance on the observed machine",state:$state,reason:$reason,exitCode:$exitCode,bundleVersion:$bundleVersion,observedRuntime:$observedRuntime,globalizationMode:$globalizationMode,defaultGlobalizationRequested:($globalizationMode == "default"),doctorExitCode:$doctorExitCode,smokeExitCode:$smokeExitCode,cleanVmCertified:false,guiTested:false,hardwareCertified:false}' > "$output/result.json"
}
if ! (cd -- "$bundle" && sha256sum --check SHA256SUMS) > "$output/integrity.txt" 2>&1; then
  write_result FAILED 'Package integrity failed; no application command executed.' 1
  exit 1
fi
if ! bundle_version=$(jq -er '.version | select(type == "string" and length > 0)' "$bundle/package.json"); then
  write_result FAILED 'Invalid package version metadata; no application command executed.' 1
  exit 1
fi
# A local hash verifies integrity, not authenticity or permission to redistribute.
set +e
timeout --kill-after=2s 15s "$bundle/cli/ilmenyx" doctor > "$output/doctor.txt" 2> "$output/doctor.stderr"
doctor_status=$?
set -e
printf '%s\n' "$doctor_status" > "$output/doctor-exit-code.txt"
default_mode=0
invariant_mode=0
if rg -Fxq 'Globalizacao: modo padrao; requer ICU compativel.' "$output/doctor.txt"; then default_mode=1; fi
if rg -Fxq 'Globalizacao: invariant solicitado explicitamente.' "$output/doctor.txt"; then invariant_mode=1; fi
if [[ $default_mode == 1 && $invariant_mode == 0 ]]; then globalization_mode=default; fi
if [[ $default_mode == 0 && $invariant_mode == 1 ]]; then globalization_mode=invariant; fi
if [[ $doctor_status != 0 ]]; then
  write_result BLOCKED 'Doctor could not confirm CLI requirements; inspect doctor output. No inventory attempted.' 3
  echo 'BLOQUEADO: requisitos CLI nao confirmados; ver result.json e doctor.stderr.'
  exit 3
fi
if [[ $globalization_mode == unknown ]]; then
  write_result BLOCKED 'Unrecognized or ambiguous doctor globalization report; no inventory attempted.' 3
  echo 'BLOQUEADO: modo de globalizacao nao identificado no doctor.'
  exit 3
fi
# The existing smoke collects real inventory and sensors into this new local directory.
# Bound execution; preserve partial evidence on error, without altering package/system.
set +e
timeout --kill-after=2s 90s bash "$repo_scripts/test-linux-package.sh" "$bundle" "$output/package-check" > "$output/package-check.txt" 2>&1
check_status=$?
set -e
printf '%s\n' "$check_status" > "$output/package-check-exit-code.txt"
if [[ $check_status != 0 ]]; then
  write_result FAILED 'CLI package smoke failed or timed out; inspect package-check logs.' 1
  exit 1
fi
observed_runtime=$(jq -er '.runtime | select(type == "string" and length > 0)' "$output/package-check/info.json")
write_result IMPLEMENTED_TESTED 'CLI smoke passed on observed machine; GUI, clean VM and hardware matrix remain unverified.' 0
echo 'PASS: pacote CLI nesta maquina. GUI/VM limpa/hardware adicional NAO TESTADOS.'
echo 'Os JSON locais incluem inventario/sensores reais; revise antes de compartilhar.'
if [[ $globalization_mode == invariant ]]; then
  echo 'INVARIANT explicito: este resultado nao valida globalizacao padrao com ICU.'
fi
