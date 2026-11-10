#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
get() { # url sha256 file
  curl -fsSL --proto '=https' --max-time 600 -o "$out/$3.part" "$1"
  echo "$2  $out/$3.part" | sha256sum --check --status || { rm -f -- "$out/$3.part"; echo "Fonte com SHA-256 inesperado: $3" >&2; exit 1; }
  mv -- "$out/$3.part" "$out/$3"
}
# umu-launcher 1.4.4 (GPL-3.0): archive of tag 1.4.4 (commit cf3d1b1), the source of the bundled zipapp.
get https://github.com/Open-Wine-Components/umu-launcher/archive/refs/tags/1.4.4.tar.gz 19767e226b0b937b1dcdcd00282eb31d1a7bb42ee01d54f6dae78e44ab8e3284 umu-launcher-1.4.4-source.tar.gz
# python-xlib 0.33 (LGPL-2.1), inside the umu zipapp: official PyPI sdist.
get https://files.pythonhosted.org/packages/86/f5/8c0653e5bb54e0cbdfe27bf32d41f27bc4e12faa8742778c17f2a71be2c0/python-xlib-0.33.tar.gz 55af7906a2c75ce6cb280a584776080602444f75815a7aff4d287bb2d7018b32 python-xlib-0.33.tar.gz
(cd -- "$out" && sha256sum OpenRGB-1.1-source.tar.gz umu-launcher-1.4.4-source.tar.gz python-xlib-0.33.tar.gz > SHA256SUMS-fontes.txt && sha256sum OpenRGB-1.1-source.tar.gz > OpenRGB-1.1-source.tar.gz.sha256)
echo "Fontes GPL em $out"
