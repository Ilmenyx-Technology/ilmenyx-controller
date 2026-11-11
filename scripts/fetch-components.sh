#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Third-party components retain their terms.
# Developer/packaging step (not run by the app, tests or CI): downloads the pinned upstream builds that travel inside the package.
# Every download is verified against a pinned SHA-256 before use. Nothing is installed or elevated.
set -euo pipefail
if [[ $# != 1 || $1 != /* ]]; then echo 'Uso: bash scripts/fetch-components.sh /caminho/absoluto/cache' >&2; exit 2; fi
for command in curl sha256sum tar; do command -v "$command" >/dev/null; done
cache=$1
mkdir -p -- "$cache"
MANGOHUD_VERSION=0.8.4
MANGOHUD_URL=https://github.com/flightlessmango/MangoHud/releases/download/v0.8.4/MangoHud-0.8.4.r0.g992103e.tar.gz
MANGOHUD_SHA256=aa0b2a009552f5e7c50baabf361267a79ea5e5c0fea440786f3b202d51f57753
MANGOHUD_LICENSE_URL=https://raw.githubusercontent.com/flightlessmango/MangoHud/v0.8.4/LICENSE
OPENRGB_VERSION=1.1
OPENRGB_URL=https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_x86_64_8e16de2.AppImage
OPENRGB_SHA256=d556c2273b0ef863dbee7ffdb438304b382add5d685c93c60758b42de3b0268d
OPENRGB_LICENSE_URL=https://codeberg.org/OpenRGB/OpenRGB/raw/tag/release_1.1/LICENSE
# umu-launcher: runs Windows games with Proton outside Steam (the launcher Heroic and Lutris use). Distro-independent Python zipapp
# (needs python3 >= 3.10 on the system); it downloads the Steam Linux Runtime and UMU-Proton itself on first use.
UMU_VERSION=1.4.4
UMU_URL=https://github.com/Open-Wine-Components/umu-launcher/releases/download/1.4.4/umu-launcher-1.4.4-zipapp.tar
UMU_SHA256=eb590691841f7fad3fc3ad8fd5db4ccb87849fe7948e62b28ece7a4ee48cc851
fetch() { # url sha256 file
  if [[ -f $3 ]] && echo "$2  $3" | sha256sum --check --status; then return 0; fi
  curl -fsSL --max-time 600 -o "$3.part" "$1"
  echo "$2  $3.part" | sha256sum --check --status || { rm -f -- "$3.part"; echo "SHA-256 divergente: $1" >&2; exit 1; }
  mv -- "$3.part" "$3"
}
fetch "$MANGOHUD_URL" "$MANGOHUD_SHA256" "$cache/MangoHud.tar.gz"
fetch "$OPENRGB_URL" "$OPENRGB_SHA256" "$cache/OpenRGB.AppImage"
fetch "$UMU_URL" "$UMU_SHA256" "$cache/umu-zipapp.tar"
rm -rf -- "$cache/out"; mkdir -p -- "$cache/out/mangohud" "$cache/out/openrgb" "$cache/out/umu" "$cache/work"
rm -rf -- "$cache/work"; mkdir -p -- "$cache/work"
tar -xzf "$cache/MangoHud.tar.gz" -C "$cache/work"
tar -xf "$cache/work/MangoHud/MangoHud-package.tar" -C "$cache/work" ./usr/lib/mangohud/lib64 ./usr/share/doc/mangohud
cp -r -- "$cache/work/usr/lib/mangohud/lib64" "$cache/out/mangohud/lib64"
cp -- "$cache/work/usr/share/doc/mangohud/MangoHud.conf.example" "$cache/out/mangohud/"
curl -fsSL --max-time 60 -o "$cache/out/mangohud/LICENSE" "$MANGOHUD_LICENSE_URL"
cp -- "$cache/OpenRGB.AppImage" "$cache/out/openrgb/OpenRGB.AppImage"; chmod 755 "$cache/out/openrgb/OpenRGB.AppImage"
curl -fsSL --max-time 60 -o "$cache/out/openrgb/LICENSE" "$OPENRGB_LICENSE_URL"
cat > "$cache/out/openrgb/SOURCE.txt" <<TEXT
OpenRGB $OPENRGB_VERSION (GPL-2.0-only), unmodified official AppImage, bundled by Ilmenyx.
Ilmenyx runs it as a separate program (it does not link to it).
Corresponding source (GPL-2.0 section 3): the exact source of this build (commit 8e16de27858cf2be6af54a33ce003df74cb9fd3e, tag release_1.1)
is published next to the binaries in every Ilmenyx release on GitHub, as OpenRGB-1.1-source.tar.gz
(SHA-256 8b919ad446de7f0415e2271a1881f94cd4f14e384a3c59acfcb3f540125f0107). Upstream: https://codeberg.org/OpenRGB/OpenRGB
Written offer, valid for at least three years from the release date: anyone may ask for a copy of that source, at no charge
beyond the cost of the medium, by writing to ilmenyx.technology@gmail.com.
TEXT
cat > "$cache/out/mangohud/SOURCE.txt" <<TEXT
MangoHud $MANGOHUD_VERSION (MIT), unmodified official release libraries (64-bit), bundled by Ilmenyx.
Source code and license: https://github.com/flightlessmango/MangoHud (tag v0.8.4).
TEXT
tar -xf "$cache/umu-zipapp.tar" -C "$cache/work" umu/umu-run
install -m 0755 -- "$cache/work/umu/umu-run" "$cache/out/umu/umu-run"
# Licenses travel inside the zipapp (umu GPL-3.0, python-xlib LGPL-2.1, urllib3/truststore/six MIT); copied out for the notices.
mkdir -p -- "$cache/out/umu/licenses"
python3 -I - "$cache/out/umu/umu-run" "$cache/out/umu/licenses" <<'PY'
import sys, zipfile, os
z = zipfile.ZipFile(sys.argv[1])
for name in z.namelist():
    base = os.path.basename(name)
    if base.startswith(("LICENSE", "COPYING")) and ".dist-info/" in name:
        package = name.split(".dist-info/")[0]
        with open(os.path.join(sys.argv[2], package + "-" + base), "wb") as out: out.write(z.read(name))
PY
cat > "$cache/out/umu/SOURCE.txt" <<TEXT
umu-launcher $UMU_VERSION (GPL-3.0-only), unmodified official zipapp, bundled by Ilmenyx and run as a separate program.
It contains python-xlib 0.33 (LGPL-2.1-or-later), urllib3 2.7.0, truststore 0.10.4 and six 1.17.0 (MIT); licenses in licenses/.
Corresponding source: published next to the binaries in every Ilmenyx release on GitHub as umu-launcher-1.4.4-source.tar.gz
(tag 1.4.4, commit cf3d1b107147480c447ffbfb3f789dc74335074c) and python-xlib-0.33.tar.gz (PyPI sdist).
Upstream: https://github.com/Open-Wine-Components/umu-launcher
Written offer, valid for at least three years from the release date: anyone may ask for a copy of that source, at no charge
beyond the cost of the medium, by writing to ilmenyx.technology@gmail.com.
TEXT
rm -rf -- "$cache/work"
echo "Componentes prontos em $cache/out"
