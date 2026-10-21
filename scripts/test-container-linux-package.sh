#!/usr/bin/env bash
# New Ilmenyx additions: see LICENSING.md. Local non-root container diagnostics only.
set -euo pipefail
if [[ $# != 4 || $2 != /* || $3 != /* || $4 != /* ]]; then
  echo 'Uso: bash scripts/test-container-linux-package.sh IMAGEM_LOCAL /bundle /runtime-sem-sdk /saida-nova' >&2
  exit 2
fi
[[ $(id -u) != 0 ]] || { echo 'Nao executar como root.' >&2; exit 2; }
endpoint=$(docker context inspect --format '{{.Endpoints.docker.Host}}')
[[ ${DOCKER_HOST:-$endpoint} == unix://* ]] || { echo 'Exige daemon local via socket Unix.' >&2; exit 2; }
mode=${ILMENYX_CONTAINER_INVARIANT:-0}
[[ $mode == 0 || $mode == 1 ]] || { echo 'Modo invariant deve ser 0 ou 1.' >&2; exit 2; }
guard=${ILMENYX_CONTAINER_TEST_ICU_GUARD:-0}
[[ $guard == 0 || $guard == 1 ]] || { echo 'Teste do guard deve ser 0 ou 1.' >&2; exit 2; }
if [[ $guard == 1 ]]; then
  jq -e '.version == "0.18.0" or .version == "0.19.0" or .version == "0.20.0"' "$2/package.json" >/dev/null # Never bypass preflight on older aborting builds.
fi
doctor=${ILMENYX_CONTAINER_TEST_DOCTOR:-0}
[[ $doctor == 0 || $doctor == 1 ]] || { echo 'Teste doctor deve ser 0 ou 1.' >&2; exit 2; }
if [[ $doctor == 1 ]]; then jq -e '.version == "0.19.0" or .version == "0.20.0"' "$2/package.json" >/dev/null; fi
image=$(docker image inspect "$1" --format '{{.Id}}') # Resolve local identity, never pull.
bundle=$(cd -- "$2" && pwd)
runtime=$(cd -- "$3" && pwd)
[[ -x $bundle/cli/ilmenyx && -x $runtime/dotnet && ! -e $runtime/sdk ]]
for path in "$bundle" "$runtime" "$4"; do
  [[ $path != *','* && $path != *$'\n'* ]] || { echo 'Path invalido para bind mount.' >&2; exit 2; }
done
mkdir -- "$4"
output=$(cd -- "$4" && pwd)
mkdir -- "$output/results"
docker image inspect "$image" | jq '[.[] | {Id,RepoDigests,Os,Architecture}]' > "$output/image.json"
# Only a new, empty result directory is writable on the host. No display/device/socket sharing.
docker run --pull=never --rm -i --network=none --read-only \
  --user "$(id -u):$(id -g)" --cap-drop=ALL --security-opt=no-new-privileges \
  --pids-limit=128 --memory=512m --cpus=1 \
  --tmpfs /tmp:rw,nosuid,nodev,size=128m \
  --mount "type=bind,source=$bundle,target=/app,readonly" \
  --mount "type=bind,source=$runtime,target=/dotnet,readonly" \
  --mount "type=bind,source=$output/results,target=/results" \
  --env ILMENYX_TEST_DOCTOR="$doctor" \
  --env ILMENYX_TEST_ICU_GUARD="$guard" \
  --env DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="$mode" \
  --env DOTNET_ROOT=/dotnet --env DOTNET_CLI_HOME=/tmp --env DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  --env AVALONIA_TELEMETRY_OPTOUT=1 --entrypoint /bin/sh "$image" -s > "$output/container.txt" 2>&1 <<'SH'
ulimit -c 0
id
cat /etc/os-release
uname -m
printf 'case\texitCode\n' > /results/status.tsv
run_case() {
  name=$1
  shift
  "$@" > "/results/$name.stdout" 2> "/results/$name.stderr"
  code=$?
  printf '%s\t%s\n' "$name" "$code" >> /results/status.tsv
}
run_case runtimes /dotnet/dotnet --list-runtimes
run_case sdks /dotnet/dotnet --list-sdks
run_case help /app/cli/ilmenyx --help
if test "$ILMENYX_TEST_DOCTOR" = 1; then run_case doctor /app/cli/ilmenyx doctor; fi
# Do not repeat FailFast/abort in an image already known to lack ICU.
if test "$ILMENYX_TEST_ICU_GUARD" = 1 || test "$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" = 1 ||
   { test -x /sbin/ldconfig && /sbin/ldconfig -p 2>/dev/null | grep 'libicuuc\.so' > /results/icu.txt; }; then
  run_case info /app/cli/ilmenyx info --json --output /results/info.json
  run_case capabilities /app/cli/ilmenyx capabilities --json
  run_case sensors /app/cli/ilmenyx sensors --json
else
  printf 'info\t125\ncapabilities\t125\nsensors\t125\n' >> /results/status.tsv
  printf 'Diagnosticos nao iniciados: ICU nao detectada e modo invariant desativado.\n' > /results/preflight.txt
fi
run_case apphost-dependencies ldd /app/cli/ilmenyx
for library in /app/desktop/*.so; do
  printf '\n%s\n' "$library"
  ldd "$library"
done > /results/native-dependencies.txt 2>&1
cat /results/status.tsv
SH
jq -Rn '[inputs | split("\t")] | .[1:] | map({case:.[0],exitCode:(.[1]|tonumber)})' < "$output/results/status.tsv" > "$output/status.json"
if jq -e 'map(select(.case == "runtimes" and .exitCode != 0)) | length > 0' "$output/status.json" >/dev/null; then
  printf 'INCOMPATIVEL: runtime/apphost glibc nao iniciou nesta imagem.\n'; exit 4
fi
if [[ $guard == 1 && $mode == 0 ]] && jq -e 'map(select(.case == "info" or .case == "capabilities" or .case == "sensors")) | length == 3 and all(.exitCode == 5)' "$output/status.json" >/dev/null; then
  for operation in info capabilities sensors; do
    [[ ! -s $output/results/$operation.stdout ]]
    rg -q 'Dependencia ICU indisponivel' "$output/results/$operation.stderr"
  done
  [[ ! -e $output/results/info.json ]]
  if [[ $doctor == 1 ]]; then
    jq -e 'map(select(.case == "doctor")) | length == 1 and all(.exitCode == 5)' "$output/status.json" >/dev/null
    rg -Fq 'ICU (uc/i18n): nao encontrada/carregavel.' "$output/results/doctor.stdout"
    rg -Fq 'GUI nao testada por este comando.' "$output/results/doctor.stdout"
  fi
  printf 'PASS guard ICU: codigo 5, mensagem explicita, sem JSON parcial ou abort.\n'; exit 0
fi
if jq -e 'map(select(.exitCode == 125)) | length > 0' "$output/status.json" >/dev/null; then
  printf 'BLOQUEADO: dependencia ICU nao detectada; diagnosticos nao iniciados.\n'; exit 3
fi
jq -e 'map(select(.exitCode != 0)) | length == 0' "$output/status.json" >/dev/null
[[ ! -s $output/results/sdks.stdout ]] # Runtime only; no SDK inside the test environment.
jq -e -s 'length == 2 and .[0] == .[1] and .[0].schemaVersion == "1.2"' "$output/results/info.stdout" "$output/results/info.json" >/dev/null
jq -e 'type == "array" and length > 0' "$output/results/capabilities.stdout" >/dev/null
jq -e '.schemaVersion == "1.1"' "$output/results/sensors.stdout" >/dev/null
printf 'PASS CLI em container sem SDK; invariant=%s; GUI/VM limpa nao certificadas.\n' "$mode"
