#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
# Corresponding source of the GPL programs that ship inside the Ilmenyx packages (GPL-2.0 section 3): pinned, SHA-256 checked,
# written to a new directory so it can be attached to the release next to the binaries. Downloads only; publishes nothing.
set -euo pipefail
if [[ $# != 1 || $1 != /* ]]; then echo 'Uso: bash scripts/fetch-gpl-sources.sh /caminho/absoluto/destino-novo' >&2; exit 2; fi
mkdir -- "$1"
out=$(cd -- "$1" && pwd)
# OpenRGB 1.1: archive of commit 8e16de2 (tag release_1.1), the commit of the bundled OpenRGB_1.1_x86_64_8e16de2.AppImage.
url=https://codeberg.org/OpenRGB/OpenRGB/archive/release_1.1.tar.gz
sha=8b919ad446de7f0415e2271a1881f94cd4f14e384a3c59acfcb3f540125f0107
curl -fsSL --proto '=https' --max-time 600 -o "$out/OpenRGB-1.1-source.tar.gz.part" "$url"
echo "$sha  $out/OpenRGB-1.1-source.tar.gz.part" | sha256sum --check --status || { rm -f -- "$out/OpenRGB-1.1-source.tar.gz.part"; echo 'Fonte do OpenRGB com SHA-256 inesperado' >&2; exit 1; }
mv -- "$out/OpenRGB-1.1-source.tar.gz.part" "$out/OpenRGB-1.1-source.tar.gz"
(cd -- "$out" && sha256sum OpenRGB-1.1-source.tar.gz > OpenRGB-1.1-source.tar.gz.sha256)
echo "Fontes GPL em $out"
