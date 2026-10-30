#!/usr/bin/env bash
# New Ilmenyx additions: see LICENSING.md; third-party components retain their terms.
# Read-only diagnostics plus exports in a new test directory; no workloads or SMART.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
if [[ $# != 2 || $1 != /* || $2 != /* ]]; then
  echo 'Uso: bash scripts/test-linux-package.sh /pacote/Ilmenyx-linux-x64 /destino/novo' >&2
  exit 2
fi
bundle=$(cd -- "$1" && pwd)
mkdir -- "$2"
output=$(cd -- "$2" && pwd)
(cd -- "$bundle" && sha256sum --check SHA256SUMS) > "$output/integrity.txt"
jq -e '.rid == "linux-x64" and .selfContained == true and .distribution == "local-review-only"' "$bundle/package.json" >/dev/null
for directory in cli desktop; do
  for notice in LICENSE NOTICE.md LICENSING.md LICENSE-ILMENYX.md THIRD-PARTY-NOTICES.md; do
    cmp -- "$bundle/$notice" "$bundle/$directory/$notice"
  done
done
[[ -x $bundle/cli/ilmenyx && -x $bundle/desktop/Ilmenyx.Desktop ]]
[[ -f $bundle/linux/io.github.bismuth_technology.Ilmenyx.desktop && -x $bundle/linux/install-user.sh && -f $bundle/linux/hicolor/scalable/apps/io.github.bismuth_technology.Ilmenyx.svg ]]
"$bundle/cli/ilmenyx" --help > "$output/help.txt"
"$bundle/cli/ilmenyx" info --json --output "$output/info.json" > "$output/info-stdout.json"
jq -e -s 'length == 2 and .[0] == .[1] and .[0].schemaVersion == "1.3"' "$output/info.json" "$output/info-stdout.json" >/dev/null
jq -e --slurpfile package "$bundle/package.json" '.applicationVersion == $package[0].version and (.runtime | type == "string" and length > 0)' "$output/info.json" >/dev/null
"$bundle/cli/ilmenyx" capabilities --json > "$output/capabilities.json"
jq -e 'type == "array" and length > 0' "$output/capabilities.json" >/dev/null
"$bundle/cli/ilmenyx" sensors --json > "$output/sensors.json"
jq -e '.schemaVersion == "1.1"' "$output/sensors.json" >/dev/null
cp -- "$output/info.json" "$output/preserved.json"
if "$bundle/cli/ilmenyx" info --json --output "$output/info.json" > "$output/refused.txt" 2>&1; then
  echo 'Sobrescrita nao foi recusada' >&2; exit 1
fi
cmp -- "$output/info.json" "$output/preserved.json"
while IFS= read -r -d '' native; do
  ldd "$native" >> "$output/native-dependencies.txt"
done < <(rg --files -0 "$bundle" | sort -z | while IFS= read -r -d '' file; do
  case "$file" in */components/*) ;; *.so|*/ilmenyx|*/Ilmenyx.Desktop) printf '%s\0' "$file";; esac
done)
if rg 'not found' "$output/native-dependencies.txt"; then exit 1; fi
# Integrated programs (MangoHud, OpenRGB) need ordinary desktop libraries (libxkbcommon, libEGL…): reported, not a failure of the CLI/UI package.
for native in "$bundle"/components/mangohud/lib64/*.so; do [[ ! -f $native ]] || ldd "$native"; done > "$output/components-dependencies.txt" 2>&1 || true
if rg --files "$bundle" | rg -i 'xunit|testhost|UiSmoke|\.pdb$|\.exe$|/Drivers/'; then
  echo 'Artefato inesperado no pacote' >&2; exit 1
fi
printf 'PASS pacote: hashes, licencas identicas, CLI factual, JSON, sensores, overwrite recusado, dependencias nativas resolvidas\n'
