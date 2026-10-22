# Validação 0.26.0 em VMs (Ubuntu 24.04, Debian 13, Fedora 44) — QEMU/KVM, modo usuário

Pacote `ilmenyx-0.26.0-r2-local` (árvore limpa, commit 0fac075 + correção de tipo de disco) em imagens cloud oficiais, mesma preparação de
`ilmenyx-vm-ubuntu-2404-0.25.0/README.md` (runtime .NET 10.0.0 do SDK oficial, Xvfb só para teste).
Em cada VM: validate-linux-machine PASS (globalização padrão), build do código-fonte sem avisos/erros, 514 testes aprovados, UiSmoke X11 sob
Xvfb com 26 PASS (inclui a casca nova, 10 páginas, monitor, SMART importado). Pastas `ilmenyx-vm-{ubuntu-2404,debian-13,fedora-44}-0.26.0/`.
Achado e corrigido: discos VirtIO apareciam como "Rotacional (HDD)", CD-ROM e disquete eram rotulados como SSD/HDD; agora `disks.jsonl` mostra
VirtIO com ressalva de disco virtual, "Unidade óptica" e "Disquete/dispositivo legado".
Wayland (Weston e GNOME Shell headless no Fedora) foi executado com a casca nova no primeiro pacote (26 PASS em ambos), mas as saídas não foram
arquivadas; repetir se necessário. NÃO TESTADO: compositor/sessão real, GPU, interação humana/Orca, hardware físico.
A página Sistema (Fase 1) é posterior e ainda não foi validada nas VMs.
