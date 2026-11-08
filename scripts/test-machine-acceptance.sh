#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
# Local CLI acceptance regression, no workloads/SMART.
# Positive cases execute the real package; negative cases mutate only a newly created copy.
set -euo pipefail
if [[ $# != 2 || $1 != /* || $2 != /* ]]; then
  echo 'Uso: bash scripts/test-machine-acceptance.sh /bundle /saida-nova' >&2; exit 2
fi
[[ $(id -u) != 0 ]] || { echo 'Nao executar como root.' >&2; exit 2; }
bundle=$(cd -- "$1" && pwd)
scripts=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
mkdir -- "$2"
output=$(cd -- "$2" && pwd)
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 bash "$scripts/validate-linux-machine.sh" "$bundle" "$output/default"
jq -e '.schemaVersion == "1.1" and .globalizationMode == "default" and .defaultGlobalizationRequested and .doctorExitCode == 0 and .smokeExitCode == 0 and (.observedRuntime | length > 0) and (.guiTested == false) and (.cleanVmCertified == false)' "$output/default/result.json" >/dev/null
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 bash "$scripts/validate-linux-machine.sh" "$bundle" "$output/invariant"
jq -e '.globalizationMode == "invariant" and (.defaultGlobalizationRequested == false) and .exitCode == 0' "$output/invariant/result.json" >/dev/null
cp -a --reflink=auto -- "$bundle" "$output/fixture"
# An incompatible doctor text fixture, derived from the real report, is NOT a hardware fixture.
rg -v '^Globalizacao:' "$output/default/doctor.txt" > "$output/fixture/cli/.doctor-fixture"
cat > "$output/fixture/cli/ilmenyx" <<'SH'
#!/usr/bin/env bash
if [[ $# != 1 || $1 != doctor ]]; then exit 98; fi
cat -- "$(dirname -- "$0")/.doctor-fixture"
SH
chmod +x "$output/fixture/cli/ilmenyx"
rehash() {
  (cd -- "$output/fixture" && find . -type f ! -path ./SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS)
}
rehash
set +e
bash "$scripts/validate-linux-machine.sh" "$output/fixture" "$output/unknown-doctor"
status=$?
set -e
[[ $status == 3 && ! -e $output/unknown-doctor/package-check ]]
jq -e '.state == "BLOCKED" and .globalizationMode == "unknown" and .doctorExitCode == 0 and .smokeExitCode == null' "$output/unknown-doctor/result.json" >/dev/null
# Conflicting mode lines must also fail closed instead of silently choosing one.
cat -- "$output/default/doctor.txt" > "$output/fixture/cli/.doctor-fixture"
printf 'Globalizacao: invariant solicitado explicitamente.\n' >> "$output/fixture/cli/.doctor-fixture"
rehash
set +e
bash "$scripts/validate-linux-machine.sh" "$output/fixture" "$output/ambiguous-doctor"
status=$?
set -e
[[ $status == 3 && ! -e $output/ambiguous-doctor/package-check ]]
jq -e '.state == "BLOCKED" and .globalizationMode == "unknown" and .smokeExitCode == null' "$output/ambiguous-doctor/result.json" >/dev/null
# Restore the actual executable; modify only version metadata to test real CLI mismatch.
cp -- "$bundle/cli/ilmenyx" "$output/fixture/cli/ilmenyx"
jq '.version = "0.0.0-metadata-mismatch"' "$bundle/package.json" > "$output/fixture/package.json"
rehash
set +e
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0 bash "$scripts/validate-linux-machine.sh" "$output/fixture" "$output/version-mismatch"
status=$?
set -e
[[ $status == 1 ]]
jq -e '.state == "FAILED" and .doctorExitCode == 0 and .smokeExitCode != 0' "$output/version-mismatch/result.json" >/dev/null
[[ -s $output/version-mismatch/package-check/info.json && ! -e $output/version-mismatch/package-check/sensors.json ]]
printf 'PASS: default/invariant reais; doctor desconhecido/ambiguo bloqueia antes da coleta; versao divergente falha com CLI real.\n'
