#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
# Opt-in: registers the bundle's desktop entry, icons and AppStream metadata for the CURRENT USER only
# (~/.local/share). Never uses sudo, never touches system directories, never runs automatically.
# Uso: bash linux/install-user.sh            (a partir da pasta do pacote Ilmenyx-linux-x64)
set -euo pipefail
id=io.github.bismuth_technology.Ilmenyx
here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
bundle=$(cd -- "$here/.." && pwd)
binary="$bundle/desktop/Ilmenyx.Desktop"
[[ -x $binary ]] || { echo "Ilmenyx.Desktop não encontrado em $bundle/desktop" >&2; exit 1; }
data=${XDG_DATA_HOME:-$HOME/.local/share}
mkdir -p -- "$data/applications" "$data/metainfo" "$data/icons"
cp -r -- "$here/hicolor" "$data/icons/"
# Exec aponta para este pacote; caminhos com espaço vão entre aspas, como pede a especificação.
sed "s|^Exec=.*|Exec=\"$binary\"|" "$here/$id.desktop" > "$data/applications/$id.desktop"
cp -- "$here/$id.metainfo.xml" "$data/metainfo/"
command -v update-desktop-database >/dev/null && update-desktop-database -q "$data/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$data/icons/hicolor" 2>/dev/null || true
echo "Ilmenyx registrado para $USER: $data/applications/$id.desktop"
