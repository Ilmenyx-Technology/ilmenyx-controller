#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
# Third-party components retain their terms.
# Builds Ilmenyx-<version>-x86_64.AppImage from the self-contained bundle of package-linux-x64.sh.
# AppImage = the official type2 runtime (pinned release, SHA-256 checked against the value GitHub publishes) followed by
# a squashfs image of the AppDir. Never installs, elevates or publishes anything.
set -euo pipefail
if [[ $# != 1 || $1 != /* ]]; then
  echo 'Uso: bash scripts/package-appimage.sh /caminho/absoluto/destino-novo' >&2
  exit 2
fi
for command in mksquashfs sha256sum curl jq; do command -v "$command" >/dev/null || { echo "Falta: $command" >&2; exit 1; }; done
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
destination=$1
mkdir -- "$destination"
destination=$(cd -- "$destination" && pwd)
runtime_release=20251108
runtime_sha=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d
runtime_cache=${ILMENYX_APPIMAGE_RUNTIME_CACHE:-$repo/.artifacts/appimage-runtime}
runtime="$runtime_cache/runtime-x86_64-$runtime_release"
mkdir -p -- "$runtime_cache"
if ! echo "$runtime_sha  $runtime" | sha256sum --check --status 2>/dev/null; then
  curl -fsSL --proto '=https' -o "$runtime.part" "https://github.com/AppImage/type2-runtime/releases/download/$runtime_release/runtime-x86_64"
  echo "$runtime_sha  $runtime.part" | sha256sum --check --status || { rm -f -- "$runtime.part"; echo 'Runtime AppImage com SHA-256 inesperado' >&2; exit 1; }
  mv -- "$runtime.part" "$runtime"
fi
# package-linux-x64.sh publishes with --no-restore: restore the linux-x64 RID first (a plain build resets the assets file).
for p in Cli Desktop; do dotnet restore "$repo/src/Ilmenyx.$p" -r linux-x64 -p:IlmenyxBundleIcu=true; done
bash "$repo/scripts/package-linux-x64.sh" "$destination/bundle"
bundle="$destination/bundle/Ilmenyx-linux-x64"
version=$(jq -r .version "$bundle/package.json")
id=io.github.bismuth_technology.Ilmenyx
appdir="$destination/Ilmenyx.AppDir"
mkdir -p -- "$appdir/usr/lib" "$appdir/usr/share/applications" "$appdir/usr/share/metainfo" "$appdir/usr/share/icons"
cp -a -- "$bundle" "$appdir/usr/lib/ilmenyx"
printf '%s\n' "$version" > "$appdir/usr/lib/ilmenyx/appimage-version"
install -m 0755 -- "$repo/packaging/appimage/AppRun" "$appdir/AppRun"
# Exec is the AppImage itself when integrated by the user's tool (appimaged, Gear Lever...); the name is a placeholder they rewrite.
sed 's|^Exec=.*|Exec=Ilmenyx|' "$repo/packaging/linux/$id.desktop" > "$appdir/$id.desktop"
printf 'X-AppImage-Version=%s\n' "$version" >> "$appdir/$id.desktop"
cp -- "$appdir/$id.desktop" "$appdir/usr/share/applications/$id.desktop"
cp -- "$repo/packaging/linux/$id.metainfo.xml" "$appdir/usr/share/metainfo/$id.metainfo.xml"
cp -r -- "$repo/packaging/linux/hicolor" "$appdir/usr/share/icons/hicolor"
cp -- "$repo/packaging/linux/hicolor/256x256/apps/$id.png" "$appdir/$id.png"
ln -s -- "$id.png" "$appdir/.DirIcon"
image="$destination/Ilmenyx-$version-x86_64.AppImage"
mksquashfs "$appdir" "$destination/image.squashfs" -root-owned -noappend -comp zstd -Xcompression-level 19 -quiet
cat -- "$runtime" "$destination/image.squashfs" > "$image"
rm -f -- "$destination/image.squashfs"
chmod 0755 -- "$image"
(cd -- "$destination" && sha256sum "$(basename -- "$image")" > "$(basename -- "$image").sha256")
printf 'AppImage: %s\n' "$image"
