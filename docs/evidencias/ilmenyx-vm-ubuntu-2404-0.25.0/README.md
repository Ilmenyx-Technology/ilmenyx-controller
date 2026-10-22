# Teste do pacote 0.25.0 em VM Ubuntu 24.04 (QEMU/KVM, modo usuário)

Ambiente: imagem oficial cloud Ubuntu 24.04.5 (noble-server-cloudimg-amd64.img, SHA256 conferido
contra SHA256SUMS), kernel 6.8.0-147, 4 vCPU/4 GiB, rede NAT de usuário, overlay qcow2 descartável.
Disparada com qemu-system-x86_64 direto (sem GNOME Boxes: não havia ISO nem VM no Boxes).
Preparação da VM, fora do produto: sudo local apenas na VM para instalar jq, ripgrep (requisitos
do script) e, para o teste gráfico, xvfb/fonts. Runtime .NET 10.0.0 copiado do SDK oficial já
extraído no host (framework-dependent; nada instalado pelo produto). Imagem tem ICU e fontconfig.

Resultados:
- tar.gz com SHA256 idêntico ao do host; SHA256SUMS do bundle: 0 divergências.
- validate-linux-machine.sh: PASS (doctor 0, smoke 0, globalização modo padrão, bundleVersion 0.25.0).
- smart-import (CLI empacotada) com JSON sintético: rc 0, HTML sem <script>.
- Desktop sem DISPLAY: falha esperada "XOpenDisplay failed". Sob Xvfb: processo ativo 12 s com
  janela "Ilmenyx" 1060x780 (xwininfo).

NÃO TESTADO: GPU/GL real, Wayland, interação com a janela (importação SMART, exportação), Orca,
SMART com disco real, outras distros, ARM, musl. Xvfb não prova renderização em sessão real.
Nenhuma certificação de VM limpa de produção: a VM recebeu ferramentas de teste e Xvfb.
