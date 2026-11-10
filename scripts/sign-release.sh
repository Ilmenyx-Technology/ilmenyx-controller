#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Detached ASCII signatures (<file>.asc) for release files, made with the Ilmenyx release signing subkey (docs/assinatura-gpg.md).
# The primary key stays offline with the maintainer; this machine only holds the signing subkey. Signs; publishes nothing.
set -euo pipefail
[[ $# -ge 1 ]] || { echo 'Uso: bash scripts/sign-release.sh arquivo...' >&2; exit 2; }
key=3D9716572A398DCDD0CFF0EE733D2EC78436DCE8
pass=${ILMENYX_SIGNING_PASSFILE:-$HOME/.config/ilmenyx-release/subchave-senha}
for file in "$@"; do
  [[ -f $file && $file != *.asc ]] || continue
  if [[ -f $pass ]]; then gpg --batch --yes --pinentry-mode loopback --passphrase-file "$pass" --local-user "$key" --armor --detach-sign --output "$file.asc" "$file"
  else gpg --yes --local-user "$key" --armor --detach-sign --output "$file.asc" "$file"; fi  # no password file: gpg asks once
  gpg --batch --verify "$file.asc" "$file" 2>/dev/null || { echo "Assinatura inválida: $file" >&2; exit 1; }
  echo "assinado: $file.asc"
done
