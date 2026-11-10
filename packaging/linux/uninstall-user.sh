#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Removes only what install-user.sh created for the current user.
set -euo pipefail
id=io.github.ilmenyx_technology.IlmenyxController
old=io.github.bismuth_technology.Ilmenyx  # app id before the rename (v0.30.0)
data=${XDG_DATA_HOME:-$HOME/.local/share}
rm -f -- "$data/applications/$id.desktop" "$data/metainfo/$id.metainfo.xml" "$data/applications/$old.desktop" "$data/metainfo/$old.metainfo.xml"
find "$data/icons/hicolor" \( -name "$id*" -o -name "$old*" \) -type f -delete 2>/dev/null || true
command -v update-desktop-database >/dev/null && update-desktop-database -q "$data/applications" || true
echo "Ilmenyx Controller removido do menu de $USER."
