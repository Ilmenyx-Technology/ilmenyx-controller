#!/usr/bin/env bash
# SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
# Disposable-VM check of "Jogo não abre?" and ZRAM on another distribution (docs/diagnostico-e-zram.md). Runs on the HOST as a normal
# user: downloads the distro's official cloud image into a NEW work directory, boots it with QEMU/KVM (overlay disk, nothing on the host
# changes), copies the Ilmenyx CLI bundle, prints `ilmenyx game-check` and `ilmenyx zram plan`, and with --apply runs the ZRAM apply and
# undo scripts INSIDE the VM (sudo there is the VM's own user). Requires (installed by the owner): qemu-system-x86, qemu-utils,
# cloud-image-utils (cloud-localds), openssh-client, curl. NÃO TESTADO: written without QEMU on the development machine.
set -euo pipefail
usage() { echo 'Uso: bash scripts/vm-distro-check.sh ubuntu|debian|fedora|arch|opensuse /pacote/Ilmenyx-linux-x64 /saida-nova [--apply]' >&2; exit 2; }
[[ $# -ge 3 && $2 == /* && $3 == /* ]] || usage
distro=$1 bundle=$2 out=$3 apply=${4:-}
[[ -x $bundle/cli/ilmenyx ]] || { echo "Bundle inválido: $bundle/cli/ilmenyx" >&2; exit 2; }
[[ $(id -u) != 0 ]] || { echo 'Execute como usuário comum (o sudo só existe dentro da VM).' >&2; exit 2; }
[[ -e /dev/kvm ]] || { echo '/dev/kvm ausente.' >&2; exit 2; }
for c in qemu-system-x86_64 qemu-img cloud-localds ssh scp ssh-keygen curl; do command -v "$c" >/dev/null || { echo "Ferramenta ausente: $c" >&2; exit 2; }; done
# Official cloud images (check the URLs before use; override with ILMENYX_VM_IMAGE_URL).
case $distro in
  ubuntu)   url=https://cloud-images.ubuntu.com/noble/current/noble-server-cloudimg-amd64.img ;;
  debian)   url=https://cloud.debian.org/images/cloud/trixie/latest/debian-13-genericcloud-amd64.qcow2 ;;
  fedora)   url=https://download.fedoraproject.org/pub/fedora/linux/releases/42/Cloud/x86_64/images/Fedora-Cloud-Base-Generic-42-1.1.x86_64.qcow2 ;;
  arch)     url=https://geo.mirror.pkgbuild.com/images/latest/Arch-Linux-x86_64-cloudimg.qcow2 ;;
  opensuse) url=https://download.opensuse.org/tumbleweed/appliances/openSUSE-Tumbleweed-Minimal-VM.x86_64-Cloud.qcow2 ;;
  *) usage ;;
esac
url=${ILMENYX_VM_IMAGE_URL:-$url}
mkdir -- "$out"  # never reuse a result directory
cd -- "$out"
curl -fL --proto '=https' -o base.img "$url"
qemu-img create -f qcow2 -F qcow2 -b base.img disk.qcow2 20G >/dev/null 2>&1 || qemu-img create -f qcow2 -F raw -b base.img disk.qcow2 20G
ssh-keygen -q -t ed25519 -N '' -f key
cat > user-data <<EOF
#cloud-config
users:
  - name: tester
    sudo: ALL=(ALL) NOPASSWD:ALL
    shell: /bin/bash
    ssh_authorized_keys: [ "$(cat key.pub)" ]
EOF
printf 'instance-id: ilmenyx-%s\nlocal-hostname: ilmenyx-vm\n' "$distro" > meta-data
cloud-localds seed.iso user-data meta-data
# 4 GiB of RAM: below the 16 GiB limit, so the plan recommends ZRAM where none is active.
qemu-system-x86_64 -enable-kvm -cpu host -smp 2 -m 4096 -nographic -serial file:console.log -monitor none \
  -drive file=disk.qcow2,if=virtio -drive file=seed.iso,if=virtio,format=raw \
  -nic user,model=virtio-net-pci,hostfwd=tcp:127.0.0.1:2222-:22 &
qemu=$!
trap 'kill "$qemu" 2>/dev/null || true' EXIT
opts=(-i key -p 2222 -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=5)
for _ in $(seq 1 90); do ssh "${opts[@]}" tester@127.0.0.1 true 2>/dev/null && break; sleep 5; done
ssh "${opts[@]}" tester@127.0.0.1 'cloud-init status --wait >/dev/null 2>&1 || true; mkdir -p ilmenyx'
scp -P 2222 -i key -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -rq "$bundle/cli" tester@127.0.0.1:ilmenyx/
run() { echo "\$ $*" | tee -a session.txt; ssh "${opts[@]}" tester@127.0.0.1 "$@" 2>&1 | tee -a session.txt; }
run 'cat /etc/os-release | head -3; cat /proc/swaps; free -m'
run 'ilmenyx/cli/ilmenyx game-check'
run 'ilmenyx/cli/ilmenyx zram plan'
if [[ $apply == --apply ]]; then
  run 'ilmenyx/cli/ilmenyx zram script apply > apply.sh && sudo sh apply.sh; echo "exit=$?"; cat /proc/swaps'
  run 'ilmenyx/cli/ilmenyx zram status'
  run 'sudo reboot' || true
  sleep 20; for _ in $(seq 1 60); do ssh "${opts[@]}" tester@127.0.0.1 true 2>/dev/null && break; sleep 5; done
  run 'cat /proc/swaps; ilmenyx/cli/ilmenyx zram status'
  run 'ilmenyx/cli/ilmenyx zram script undo-remove > undo.sh || ilmenyx/cli/ilmenyx zram script undo > undo.sh; sudo sh undo.sh; echo "exit=$?"; cat /proc/swaps'
  run 'ilmenyx/cli/ilmenyx zram status'
fi
run 'sudo poweroff' || true
wait "$qemu" || true
echo "Resultado em $out/session.txt (console do boot: $out/console.log)."
