# Aba "Driver NVIDIA" (FERRAMENTAS) — 2026-11-07

Ideia do mantenedor: no Linux, instalar o driver NVIDIA é complicado. A aba descobre qual driver serve para aquela placa e
aquele sistema, mostra um plano em linguagem simples, simula antes, instala/repara/atualiza/desfaz com um clique (senha do
sistema via polkit), confere o resultado e pede o reboot. Depois do reboot ela mostra **Funcionando** quando o `nvidia-smi` responde.

Código: `src/Ilmenyx.Core/NvidiaDriver.cs` (tabela de gerações, parsers), `src/Ilmenyx.Core/NvidiaDriverPlan.cs` (estado, plano,
scripts fixos, checagem da simulação, verificação), `src/Ilmenyx.Platform.Linux/NvidiaDriverProbe.cs` (diagnóstico somente leitura,
`apt-get -s`, executor `pkexec`), `src/Ilmenyx.Desktop/NvidiaDriverPage.cs` (aba), `src/Ilmenyx.Cli/NvidiaDriverCommand.cs` (CLI).
Testes: `tests/Ilmenyx.Tests/NvidiaDriverTests.cs` + `tests/Ilmenyx.Tests/Fixtures/nvidia-driver/`; smoke: caso `nvidia driver tab`.

## O caso real que define a aba (Dell Inspiron 7460, 2026-11-07)

GeForce 940MX (10de:134d, Maxwell GM108) + Intel HD 620, Linux Mint 22.3 (base Ubuntu 24.04), kernel 6.14.0-37-generic, Secure Boot
desligado. `ubuntu-drivers install nvidia:580` escolheu os **módulos pré-compilados da trilha HWE** e puxou um kernel 7.0.0-42 novo cujos
cabeçalhos vieram incompletos (`tools/objtool/objtool` e `scripts/module.lds` ausentes). `linux-modules-nvidia-580-7.0.0-42-generic`
falhou no postinst (`iF`), `nvidia-driver-580` ficou `iU` e o comando terminou dizendo "All the available drivers are already installed".
Correção feita pelo lead com o dono: purgar os pacotes 7.0 (meta `linux-modules-nvidia-580-generic-hwe-24.04`, módulos/objetos/assinaturas
NVIDIA 7.0, `linux-image`/`linux-modules`/`linux-main-modules-*` 7.0) e instalar `nvidia-dkms-580` + `nvidia-driver-580` (DKMS montou para
o kernel em uso). Após o reboot: `nvidia-smi` OK, driver 580.178.04, PRIME on-demand.

Consequências no desenho: **DKMS para o kernel em uso, nunca outro kernel**; simulação antes, com recusa se aparecer `linux-image`;
detecção desse cenário e reparo com exatamente esses pacotes; a página Drivers deixou de sugerir `sudo ubuntu-drivers install`.

## Tabela de gerações (PCI 10de:xxxx → ramo)

| Geração | Exemplos | Ramo | Módulos "open" | Observação |
|---|---|---|---|---|
| Tesla e anteriores (NV4x/G8x/G9x/GT21x) | GeForce 8/9/200/300 | nenhum | — | 340 acabou em 2019; usar nouveau |
| Fermi (GF1xx) | GeForce 400/500 | nenhum | — | 390 acabou em 2022; usar nouveau |
| Kepler (GK1xx/GK2xx) | GTX 600/700 | **470 (legado, EOL)** | não | só correções da distribuição; pode deixar de compilar |
| Maxwell (GM1xx/GM2xx) | GTX 750/900, **940MX** | **580 (legado)** | não | 580 é o último que suporta Maxwell/Pascal/Volta |
| Pascal (GP1xx) | GTX 10 | 580 (legado) | não | idem |
| Volta (GV100) | Titan V | 580 (legado) | não | idem |
| Turing (TU1xx) | RTX 20, GTX 16 | **595 (atual)** | sim (usado) | NVIDIA recomenda "open" desde a Turing |
| Ampere / Hopper / Ada | RTX 30/40, H100 | 595 (atual) | sim (usado) | |
| Blackwell (GB1xx/GB2xx) | RTX 50 | 595 (atual) | **obrigatório** | |
| fora da tabela | ids novos | nenhum | — | não instala nada; orienta para o gerenciador da distro |

Fontes: faixas de device id das listas PCI públicas (pci.ids) — a tabela está em `NvidiaGenerations.Ranges`. Ramo atual: o lead conferiu
em 2026-11-07 que `https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt` responde 595.112.07 e que a série 590+ removeu Maxwell,
Pascal e Volta; o Ubuntu 24.04 desta máquina publica `nvidia-driver-595`/`-595-open` e `nvidia-driver-580` (`apt-cache search`).
Só ramos desta tabela (470, 580, 595) chegam a um script. Ao mudar o ramo atual, atualizar `NvidiaGenerations.CurrentBranch`, a tabela e os testes.

## Diagnóstico (somente leitura, sem root)

GPUs por `/sys/bus/pci/devices` (classe 0x03, vendor, device, driver ligado), outra GPU (Intel/AMD → híbrido), `/etc/os-release`
(ID/ID_LIKE → família), kernel em uso (`/proc/sys/kernel/osrelease`), kernels instalados (`/boot/vmlinuz-*`, `/lib/modules/*/vmlinuz`,
pacotes `linux-image-*`), cabeçalhos de cada kernel presentes **e completos** (`Makefile`, `scripts/module.lds`, `tools/objtool/objtool`
em `/lib/modules/K/build` ou `/usr/src/linux-headers-K`), Secure Boot (`mokutil --sb-state`; sem mokutil, variável EFI), PRIME
(`prime-select query`), módulo carregado (`/sys/module/nvidia/version`), `nvidia-smi --query-gpu=driver_version`, pacotes
(`dpkg-query -W` com status `iF/iU/iH/iW/it/…R` = pela metade), `apt-cache policy` do metapacote instalado (candidato = atualização),
`dkms status` e arquivos `nvidia.ko*` do kernel em uso.

Estados: sem GPU NVIDIA · sem driver suportado · não instalado · instalação com problema (reparo) · instalado, reinicie · carregado mas
`nvidia-smi` falhou · **funcionando**.

## Planos por distribuição

| Família (os-release) | Instalar | Reparar | Atualizar | Voltar ao nouveau | Estado |
|---|---|---|---|---|---|
| Ubuntu e derivadas (ubuntu, linuxmint, pop, elementary, zorin, neon) | `nvidia-dkms-R[-open] nvidia-driver-R[-open] linux-headers-$(uname -r)` | purge dos pacotes do kernel quebrado + meta pré-compilada + módulos NVIDIA pela metade, na **mesma transação** do install DKMS (`apt-get install --purge … pacote-`) | mesmo install, quando o candidato do apt é mais novo ou o ramo instalado é mais antigo que o da tabela | `apt-get purge` dos pacotes NVIDIA numerados (mantém nvidia-prime, nvidia-settings, egl-wayland) | simulação real nesta máquina; execução real **NÃO TESTADA** pelo app |
| Debian | `nvidia-kernel-dkms nvidia-driver firmware-misc-nonfree linux-headers-K` (versão do Debian) | — | — | igual ao Ubuntu | NÃO TESTADO |
| Fedora, Nobara | RPM Fusion (se faltar) + `akmod-nvidia[-580xx/-470xx]` + `xorg-x11-drv-nvidia[...]-cuda` + `kernel-devel-K` + `akmods --force` | — | — | `dnf remove akmod-nvidia* kmod-nvidia* xorg-x11-drv-nvidia*` | NÃO TESTADO; nomes legados do RPM Fusion a confirmar |
| Arch, Manjaro, CachyOS, EndeavourOS, Garuda | Turing+: `pacman -S --needed nvidia-open-dkms nvidia-utils <headers do kernel>`; Maxwell–Volta/Kepler: AUR (só orientação) | — | — | remove os pacotes nvidia instalados | NÃO TESTADO |
| openSUSE | Turing+ com o repositório NVIDIA já configurado: `nvidia-open-driver-G06-signed-kmp-default nvidia-video-G06 nvidia-gl-G06 nvidia-compute-utils-G06`; anteriores: orientação (YaST) | — | — | — | NÃO TESTADO |
| outras | só orientação | | | | |

Riscos mostrados no plano: Secure Boot ligado (o módulo DKMS é assinado por uma chave MOK local: depois de instalar, `sudo mokutil --import
/var/lib/shim-signed/mok/MOK.der`, reiniciar e "Enroll MOK" — **NÃO TESTADO**, esta máquina tem SB desligado), ramo legado (580: não
trocar por 590+; 470: EOL), download/tempo, remoção do kernel quebrado do menu de boot (reparo), perda de CUDA/NVENC (voltar ao nouveau).

## Segurança (padrão AGENTS.md "Correções de sensores com um clique")

- O script é **fixo**: montado só com o ramo da tabela (`ValidBranch`: 3 dígitos e presente na tabela), nomes de pacote casados com
  regex estrito (`^[a-z0-9][a-z0-9+.-]{1,90}$`, e para remoção também a regex de pacote NVIDIA numerado ou a lista do kernel quebrado),
  versão de kernel por regex. Todos os valores vão entre aspas simples (defesa extra). Nenhum texto livre, relatório ou dado da rede chega ao script.
- Antes da confirmação, o app roda a simulação **real** `apt-get -s` (sem root) e **recusa** o plano se ela instalar `linux-image-*`,
  `linux-modules-*`/objetos/assinaturas (módulos pré-compilados), cabeçalhos de outro kernel, remover qualquer pacote do kernel em uso,
  remover um kernel que não está na lista do reparo, ou remover pacote que não é do driver NVIDIA.
- O próprio script, já como root, repete a simulação e aborta (código 3, "nada foi alterado") se o apt quiser mexer em kernel — o estado
  pode mudar entre o clique e a execução.
- A confirmação mostra os passos, a lista exata do que será removido (da simulação) e os riscos; o script completo fica em "VER O SCRIPT".
- Execução: `pkexec /bin/sh -c <script>`; o sistema pede a senha (o Ilmenyx não a vê); saída ao vivo no Registro; limite de 40 min.
- Ao final: verificação automática (novo diagnóstico: pacotes pela metade, `dkms status`, arquivo `nvidia.ko*` do kernel em uso) e aviso
  "Reinicie o computador". O botão **COMO REINICIAR só explica**; o Ilmenyx nunca reinicia.
- Smoke e testes usam serviços falsos: nunca chamam `pkexec` nem `apt-get` sem `-s`.

## CLI

`ilmenyx nvidia-driver status` (diagnóstico e ações possíveis) e `ilmenyx nvidia-driver plan` (passos, riscos, simulação real `apt-get -s`
e o script fixo). Somente leitura. Saída real desta máquina em `docs/evidencias/nvidia-driver-tab-2026-11-07/`.

## Página Drivers

- O card NVIDIA e o card "Driver NVIDIA recomendado" (nouveau) ganharam **ABRIR A ABA DRIVER NVIDIA**; a sugestão para Ubuntu/Mint deixou
  de ser `sudo ubuntu-drivers install` (o comando que quebrou esta máquina).
- "VERIFICAR VERSÃO MAIS RECENTE" agora compara **dentro do ramo que suporta a placa**: lê a listagem pública
  `https://download.nvidia.com/XFree86/Linux-x86_64/` e pega o maior `580.x` para uma Maxwell (nunca o `latest.txt` global, 595, que não
  suporta a placa), e diz "Ramo 580 = último que suporta Maxwell; não instale 590 ou mais novo".
- O nome da GPU usa o nome comercial entre colchetes do pci.ids ("GeForce 940MX").

## O que foi testado de verdade nesta máquina e o que não

Testado (2026-11-07, Mint 22.3, kernel 6.14.0-37-generic, driver 580.178.04 já instalado pelo lead):
- Diagnóstico real (CLI e aba): GPU 940MX/Maxwell/GM108 ligada ao `nvidia`, iGPU Intel → híbrido, PRIME on-demand, Secure Boot desligado,
  cabeçalhos completos, DKMS installed, `nvidia-smi` 580.178.04, candidato do apt = instalado → **Funcionando, em dia**.
- Simulação real `apt-get -s` do "voltar ao nouveau" (17 purgas, nenhuma de kernel → aceita) e da forma do reparo/instalação (no-op aqui).
- O cenário quebrado foi testado **só com fixtures** reconstruídas do incidente (o kernel 7.0 já foi removido): detecção, lista de purga
  igual à usada pelo lead, recusa da simulação que puxa o kernel 7.0.

NÃO TESTADO: execução real de qualquer ação pelo app (instalar/reparar/atualizar/voltar ao nouveau via pkexec), MOK/Secure Boot ligado,
Kepler/470, Turing+ com módulos open, Debian, Fedora/Nobara, Arch e derivadas, openSUSE, consulta de rede da listagem NVIDIA por ramo.
Ver `Sem_teste.md` (NVDRV-01…NVDRV-05).
