#!/usr/bin/env bash
# New Ilmenyx additions: see LICENSING.md. Read-only diagnostics, exports only to a new directory.
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
write_result() {
  jq -n --arg state "$1" --arg reason "$2" --argjson exitCode "$3" \
    '{schemaVersion:"1.0",scope:"CLI package acceptance on the observed machine",state:$state,reason:$reason,exitCode:$exitCode,cleanVmCertified:false,guiTested:false,hardwareCertified:false}' > "$output/result.json"
}
if ! (cd -- "$bundle" && sha256sum --check SHA256SUMS) > "$output/integrity.txt" 2>&1; then
  write_result FAILED 'Package integrity failed; no application command executed.' 1
  exit 1
fi
# A local hash verifies integrity, not authenticity or permission to redistribute.
set +e
timeout --kill-after=2s 15s "$bundle/cli/ilmenyx" doctor > "$output/doctor.txt" 2> "$output/doctor.stderr"
doctor_status=$?
set -e
printf '%s\n' "$doctor_status" > "$output/doctor-exit-code.txt"
if [[ $doctor_status != 0 ]]; then
  write_result BLOCKED 'Doctor could not confirm CLI requirements; inspect doctor output. No inventory attempted.' 3
  echo 'BLOQUEADO: requisitos CLI nao confirmados; ver result.json e doctor.stderr.'
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
write_result IMPLEMENTED_TESTED 'CLI smoke passed on observed machine; GUI, clean VM and hardware matrix remain unverified.' 0
echo 'PASS: pacote CLI nesta maquina. GUI/VM limpa/hardware adicional NAO TESTADOS.'
echo 'Os JSON locais incluem inventario/sensores reais; revise antes de compartilhar.'
