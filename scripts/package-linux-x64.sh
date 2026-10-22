#!/usr/bin/env bash
# New Ilmenyx additions: see LICENSING.md; third-party components retain their terms.
# Local review bundle only. Never installs, restores, elevates or publishes remotely.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
if [[ $# != 1 || $1 != /* ]]; then
  echo 'Uso: bash scripts/package-linux-x64.sh /caminho/absoluto/destino-novo' >&2
  exit 2
fi
for command in dotnet jq tar sha256sum rg; do command -v "$command" >/dev/null; done
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
destination=$1
mkdir -- "$destination" # Existing files, directories and symlinks are refused.
destination=$(cd -- "$destination" && pwd)
bundle="$destination/Ilmenyx-linux-x64"
mkdir -- "$bundle" "$bundle/licenses"
for project in Cli Desktop; do
  directory=cli
  if [[ $project == Desktop ]]; then directory=desktop; fi
  dotnet publish "$repo/src/Ilmenyx.$project/Ilmenyx.$project.csproj" -c Release \
    -r linux-x64 --self-contained true -p:IlmenyxBundleIcu=true --no-restore -m:1 -p:UseSharedCompilation=false \
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
    -o "$bundle/$directory"
done
# LTTng tracing provider of the runtime: optional (needs liblttng-ust, absent on most systems) and not used by Ilmenyx.
rm -f -- "$bundle/cli/libcoreclrtraceptprovider.so" "$bundle/desktop/libcoreclrtraceptprovider.so"
for file in LICENSE NOTICE.md LICENSING.md LICENSE-ILMENYX.md THIRD-PARTY-NOTICES.md; do
  cp -- "$repo/$file" "$bundle/$file"
done
cache=${NUGET_PACKAGES:-$HOME/.nuget/packages}
printf 'package\tcache-path\tnotice-status\n' > "$bundle/dependencies.tsv"
jq -r '.libraries[] | select(.type == "package") | .path' \
  "$bundle/desktop/Ilmenyx.Desktop.deps.json" "$bundle/cli/ilmenyx.deps.json" | sort -u > "$destination/packages.list"
# Record runtime package metadata and available upstream license/notice files.
while IFS= read -r path; do
  [[ $path =~ ^[a-z0-9][a-z0-9_.-]*/[0-9][0-9a-zA-Z.+_-]*$ ]] || { echo "Caminho de pacote invalido: $path" >&2; exit 1; }
  source="$cache/$path"
  [[ -d $source ]] || { echo "Pacote fora do cache: $path" >&2; exit 1; }
  target="$bundle/licenses/$path"
  mkdir -p -- "$target"
  shopt -s nullglob nocaseglob
  notices=("$source"/LICENSE* "$source"/NOTICE* "$source"/COPYING* "$source"/THIRD-PARTY* "$source"/*.nuspec)
  for notice in "${notices[@]}"; do [[ ! -f $notice ]] || cp -- "$notice" "$target/"; done
  shopt -u nullglob nocaseglob
  printf '%s\t%s\tPENDENTE DE REVISAO: manifests e avisos disponiveis, sem liberacao de redistribuicao\n' "$path" "$path" >> "$bundle/dependencies.tsv"
done < "$destination/packages.list"
revision=$(git -C "$repo" rev-parse HEAD)
state=clean
if [[ -n $(git -C "$repo" status --porcelain) ]]; then state=dirty; fi
version=$(jq -r '.libraries | keys[] | select(startswith("ilmenyx/")) | split("/")[1]' "$bundle/cli/ilmenyx.deps.json")
[[ -n $version ]] || { echo 'Versao ausente no deps.json' >&2; exit 1; }
jq -n --arg version "$version" --arg revision "$revision" --arg state "$state" \
  '{product:"Ilmenyx",version:$version,maintainer:"Bismuth Technology",rid:"linux-x64",selfContained:true,runtime:"Microsoft.NETCore.App 10.0 (embutido)",icu:"Microsoft.ICU.ICU4C.Runtime 72.1.0.3 (embutido, app-local)",sourceRevision:$revision,sourceWorkingTree:$state,distribution:"local-review-only",legalReview:"pending"}' > "$bundle/package.json"
cat > "$bundle/LEIA-ME.txt" <<'TEXT'
Ilmenyx — by Bismuth Technology. Pacote local experimental, sem publicacao.
Adaptacao independente e nao oficial do RESET EXPLODE, Gustavo (GustavoReset).
Autocontido: runtime .NET 10 e ICU 72 acompanham o pacote; nao ha o que instalar para o .NET.
Requer glibc e as bibliotecas graficas do SO (nao embutiveis).
UI requer bibliotecas graficas do SO, incluindo fontconfig; validar via ldd e GUI.
O RID linux-x64 deste bundle nao suporta Alpine/musl.
CLI: ./cli/ilmenyx --help ; ./cli/ilmenyx info --json
UI X11: ILMENYX_DISPLAY_BACKEND=x11 ./desktop/Ilmenyx.Desktop
UI Wayland: env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland ./desktop/Ilmenyx.Desktop
Nao inclui SDK, smartctl, drivers, instalador ou garantias de portabilidade.
GameMode, MangoHud e Flatpak sao integracoes opcionais do sistema: o app indica quando faltam.
Leia LICENSE, NOTICE.md, LICENSING.md, LICENSE-ILMENYX.md e THIRD-PARTY-NOTICES.md.
MIT/terceiros e direitos anteriores preservados. Revisao humana antes de distribuir.
Licencas coletadas do cache e dependencies.tsv sao inventario, nao liberacao juridica.
Nao execute cargas completas para validar instalacao. Nao exige root.
Verificar integridade neste diretorio: sha256sum --check SHA256SUMS
TEXT
(cd -- "$bundle" && rg --files --hidden -0 | sort -z | xargs -0 sha256sum > "$destination/SHA256SUMS.tmp")
mv -- "$destination/SHA256SUMS.tmp" "$bundle/SHA256SUMS"
tar -czf "$destination/Ilmenyx-linux-x64-local.tar.gz" -C "$destination" Ilmenyx-linux-x64
(cd -- "$destination" && sha256sum Ilmenyx-linux-x64-local.tar.gz > Ilmenyx-linux-x64-local.tar.gz.sha256)
printf 'Pacote local: %s\n' "$destination/Ilmenyx-linux-x64-local.tar.gz"
