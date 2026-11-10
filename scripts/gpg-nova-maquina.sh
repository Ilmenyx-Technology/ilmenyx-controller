#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Prepares this machine to sign releases (docs/assinatura-gpg.md): imports the primary key from the maintainer's offline folder,
# adds the "Ilmenyx Technology" UID (once), creates a signing subkey for this machine, refreshes packaging/ilmenyx-release-key.asc
# and removes the primary secret key again, keeping only the subkeys. Run it in your own terminal: gpg asks for the key password.
set -euo pipefail
[[ $# == 1 && -f $1/chave-privada-PRINCIPAL.asc ]] || { echo 'Uso: bash scripts/gpg-nova-maquina.sh /pasta/com/chave-privada-PRINCIPAL.asc' >&2; exit 2; }
fpr=3D9716572A398DCDD0CFF0EE733D2EC78436DCE8
uid='Ilmenyx Technology (Ilmenyx releases) <meirafhenrique@gmail.com>'
repo=$(cd -- "$(dirname -- "$0")/.." && pwd)
tmp=$(mktemp -d); trap 'rm -rf -- "$tmp"' EXIT

gpg --import "$1/chave-privada-PRINCIPAL.asc"
gpg --with-colons --list-keys "$fpr" | grep -qF "Ilmenyx Technology (Ilmenyx releases)" || gpg --quick-add-uid "$fpr" "$uid"
gpg --quick-set-primary-uid "$fpr" "$uid"
gpg --quick-add-key "$fpr" ed25519 sign 3y
gpg --armor --export "$fpr" > "$repo/packaging/ilmenyx-release-key.asc"

# Keep only the subkeys here: the primary key (certify, renew, revoke) goes back to living only on the offline folder.
gpg --armor --export-secret-subkeys "$fpr" > "$tmp/subchaves.asc"
gpg --batch --yes --delete-secret-keys "$fpr"
gpg --import "$tmp/subchaves.asc"
shred -u -- "$tmp/subchaves.asc"
gpg --list-secret-keys "$fpr" | grep -q '^sec#' || { echo 'A chave principal ainda está neste PC; confira com gpg -K.' >&2; exit 1; }
echo "Pronto: UID novo, subchave de assinatura deste PC e chave pública em packaging/ilmenyx-release-key.asc."
echo "A chave principal saiu deste PC (gpg -K mostra sec#). Pode remover o pendrive."
