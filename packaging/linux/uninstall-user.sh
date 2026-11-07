#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
# Removes only what install-user.sh created for the current user.
set -euo pipefail
id=io.github.bismuth_technology.Ilmenyx
data=${XDG_DATA_HOME:-$HOME/.local/share}
rm -f -- "$data/applications/$id.desktop" "$data/metainfo/$id.metainfo.xml"
find "$data/icons/hicolor" -name "$id*" -type f -delete 2>/dev/null || true
command -v update-desktop-database >/dev/null && update-desktop-database -q "$data/applications" || true
echo "Ilmenyx removido do menu de $USER."
