#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Builds the Flatpak bundle (single .flatpak file) from the self-contained linux-x64 package. User-level tools only (no sudo):
# flatpak install --user flathub org.flatpak.Builder org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08
# Never publishes or installs anything; the result is a local file to review. Uso: bash scripts/package-flatpak.sh /caminho/absoluto/destino-novo
set -euo pipefail
if [[ $# != 1 || $1 != /* ]]; then
  echo 'Uso: bash scripts/package-flatpak.sh /caminho/absoluto/destino-novo' >&2
  exit 2
fi
id=io.github.ilmenyx_technology.IlmenyxController
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
destination=$1
mkdir -- "$destination" # Existing files, directories and symlinks are refused.
destination=$(cd -- "$destination" && pwd)
flatpak info org.flatpak.Builder >/dev/null 2>&1 || { echo 'Falta org.flatpak.Builder: flatpak install --user flathub org.flatpak.Builder' >&2; exit 1; }
# Metadata checks first (cheap): the same files go into the Flatpak.
desktop-file-validate "$repo/packaging/linux/$id.desktop"
appstreamcli validate --no-net --pedantic "$repo/packaging/linux/$id.metainfo.xml" || appstreamcli validate --no-net "$repo/packaging/linux/$id.metainfo.xml"
for p in Cli Desktop; do dotnet restore "$repo/src/Ilmenyx.$p" -r linux-x64 -p:IlmenyxBundleIcu=true; done
bash "$repo/scripts/package-linux-x64.sh" "$destination/linux"
source_dir="$destination/flatpak-src"
mkdir -- "$source_dir"
cp -- "$repo/packaging/flatpak/$id.yml" "$source_dir/"
cp -a -- "$destination/linux/Ilmenyx-linux-x64" "$source_dir/bundle"
version=$(jq -r .version "$source_dir/bundle/package.json")
# org.flatpak.Builder runs sandboxed with access to the host files it is given.
flatpak run --filesystem="$destination" org.flatpak.Builder --user --force-clean --disable-rofiles-fuse \
  --state-dir="$destination/.flatpak-builder" --repo="$destination/repo" "$destination/build" "$source_dir/$id.yml"
bundle="$destination/Ilmenyx-Controller-$version-x86_64.flatpak"
flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo "$destination/repo" "$bundle" "$id"
(cd -- "$destination" && sha256sum "$(basename -- "$bundle")" > "$(basename -- "$bundle").sha256")
printf 'Flatpak: %s\nInstalar (só este usuário): flatpak install --user %s\n' "$bundle" "$bundle"
