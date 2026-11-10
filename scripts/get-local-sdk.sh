#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Downloads the official .NET 10 SDK (linux-x64) into /tmp/silex-dotnet, the path scripts/env-local-sdk.sh expects.
# Verifies the SHA-512 published by Microsoft; never uses sudo or touches system directories. /tmp is cleared on reboot.
# Uso: bash scripts/get-local-sdk.sh   e depois, num shell bash: source scripts/env-local-sdk.sh
set -euo pipefail
target=/tmp/silex-dotnet
if [[ -x $target/dotnet ]] && "$target/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "SDK ja presente em $target:"; "$target/dotnet" --list-sdks; exit 0
fi
for command in curl tar sha512sum python3; do command -v "$command" >/dev/null || { echo "Falta: $command" >&2; exit 1; }; done
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
curl -sSfL https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json -o "$work/releases.json"
read -r version url hash < <(python3 -I -c '
import json, sys
release = json.load(open(sys.argv[1]))["releases"][0]["sdk"]
file = next(f for f in release["files"] if f["rid"] == "linux-x64" and f["name"].endswith(".tar.gz"))
print(release["version"], file["url"], file["hash"])' "$work/releases.json")
echo "Baixando .NET SDK $version"
curl -sSfL "$url" -o "$work/sdk.tar.gz"
echo "$hash  $work/sdk.tar.gz" | sha512sum -c - >/dev/null || { echo 'SHA-512 nao confere; abortado.' >&2; exit 1; }
rm -rf -- "$target"
mkdir -p -- "$target"
tar -xzf "$work/sdk.tar.gz" -C "$target"
echo "SHA-512 conferido. SDK instalado em $target:"
"$target/dotnet" --list-sdks
