# O que ainda falta testar

Estado em **2026-11-11, versão 0.33.2**. Esta lista diz o que o Ilmenyx Controller **ainda não conferiu em hardware ou sistema real**, por
que, e como testar. Um item só sai daqui com evidência real (comando, ambiente, data), guardada em `docs/evidencias/`. Fixture, build e
smoke com serviços falsos ajudam, mas não contam como teste do caminho real.

O registro detalhado de cada incremento até 2026-11-08 (com os defeitos encontrados e corrigidos) está em
[docs/historico/sem-teste-registro-ate-2026-11-08.md](docs/historico/sem-teste-registro-ate-2026-11-08.md).

> **English summary:** what Ilmenyx Controller has not yet verified on real hardware or systems. Tested so far: Intel Iris Xe and HD 620,
> GeForce 940MX (nouveau and the proprietary 580 driver), AMD Radeon 610M (integrated); Linux Mint 22.3 and CachyOS on real machines;
> Ubuntu 24.04, Debian 13, Fedora 44, Arch Linux and openSUSE Tumbleweed in VMs (command line and window). Still missing, above all:
> **dedicated AMD GPUs, NVIDIA desktop/RTX cards, Intel Arc, GNOME and other desktops, real games end to end and RGB devices**.
> If you can test any item below, open an issue with your distro, desktop, GPU and what happened:
> https://github.com/Ilmenyx-Technology/ilmenyx-controller/issues/new/choose

## Como ajudar

Escolha um item da lista que combine com o seu PC, rode o teste e abra uma
[issue](https://github.com/Ilmenyx-Technology/ilmenyx-controller/issues/new/choose) com a distro, a área de trabalho, a placa de vídeo e
o que aconteceu. A página **Relatório** do app gera um resumo do hardware que pode ser anexado. Os testes com carga (stress, benchmark,
calibração) só rodam com o seu clique e têm corte por temperatura.

## Já testado em hardware real

| Máquina / ambiente | O que foi conferido | Evidência |
|---|---|---|
| HP 250 G8 · i5-1135G7 · Intel Iris Xe · CachyOS · KDE Plasma | Máquina de desenvolvimento: CLI, interface, sensores, monitor, exportações, SMART NVMe importado | `docs/validacao.md` |
| Dell Inspiron 7460 · i7-7500U · HD 620 + GeForce 940MX · Mint 22.3 X11 | NVIDIA no nouveau e no driver 580 (telemetria, PRIME, stress e cena curtos), aba Driver NVIDIA, RAPL com um clique, AppImage e Flatpak, Proton fora da Steam com um jogo real, proteção contra travamento em tela cheia | `docs/evidencias/gpu-nvidia-2026-11-07/`, `flatpak-2026-11-07/`, `appimage-2026-11-07/`, `travamento-tela-cheia-2026-11-07/` |
| ASUS Vivobook Go · Radeon 610M (APU) · Mint 22.3 X11 e Wayland (Muffin aninhado) | GPU AMD integrada (sysfs, carga, VRAM/GTT), stress e cena curtos, ray tracing Vulkan, MangoHud embutido no vkcube | `docs/evidencias/gpu-amd-2026-11-06/`, `gpu-rt-2026-11-06/` |
| VMs: Ubuntu 24.04, Debian 13, Fedora 44, Arch Linux, openSUSE Tumbleweed | Pacote autocontido: CLI e janela abrem sem .NET e sem ICU do sistema | `docs/evidencias/ilmenyx-vm-*-0.30.0/` |

## Pendências

**Estado:** NÃO TESTADO = nunca rodou no caminho real; PARCIAL = parte conferida em hardware real, o resto ainda não.
Os IDs são citados pelo app (página Bloqueados e CLI); não renomear.

### GPU e drivers

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| GPU-01 | GPU AMD: identificação, carga e VRAM | PARCIAL | Só a APU Radeon 610M foi vista; falta GPU **dedicada** (VRAM GDDR, ventoinha, junction, limite de potência) | GPU AMD dedicada com amdgpu: página GPU e `scripts/validate-gpu-remote.sh --allow-gpu-load` |
| GPU-03 / GPU-05 | Intel: carga e VRAM da iGPU, Intel Arc | NÃO TESTADO | O i915/xe só expõe carga com privilégio (perf) ou `intel_gpu_top`; nenhuma Arc disponível | iGPU com `intel_gpu_top` ou CAP_PERFMON; placa Intel Arc |
| GPU-04 | Ficha e telemetria de GPU dedicada | PARCIAL | NVIDIA 940MX (nouveau e 580) e APU AMD conferidas; falta AMD dedicada e NVIDIA de desktop/RTX (power limit, ventoinha, NVENC) | Comparar a página GPU com `nvidia-smi`, `radeontop` ou GPU-Z |
| GPU-06 | Stress de GPU longo e em placa dedicada | PARCIAL | Só cargas de 6 a 8 s (APU AMD e 940MX) | Stress de vários minutos com monitor de temperatura, em GPU dedicada |
| GPU-07 / OC-03 | Cliques reais nas páginas Stress, OC e Benchmark com GPU | NÃO TESTADO | Até agora as cargas rodaram pela CLI e por script | Clique manual em cada página, com o monitor ativo |
| GPU-08 | Benchmark de ray tracing | PARCIAL | Validado na APU Radeon 610M; recusado corretamente na 940MX (sem extensões); falta RX 6000+ ou RTX | `ilmenyx bench gpu-rt --allow-gpu-load --preset alta --resolution 1440p` numa placa com ray tracing |
| DRV-01 / NVDRV-05 | Versão NVIDIA mais recente do ramo | PARCIAL | Versão instalada lida; a consulta online usa rede e nunca roda nos testes automáticos | Clicar "Verificar versão mais recente" com rede e conferir 580.x para a 940MX |
| DRV-02 | Abrir páginas oficiais (`xdg-open`) | NÃO TESTADO | O smoke não abre o navegador do usuário | Clique manual nos botões "Abrir página oficial" |
| DRV-03 | fwupd com atualização disponível | NÃO TESTADO | Nenhum dispositivo com firmware pendente nas máquinas de teste | Máquina com firmware desatualizado no LVFS |
| NVDRV-01 | Instalar, reparar, atualizar ou voltar ao nouveau pela aba Driver NVIDIA | NÃO TESTADO | Nenhuma instalação real foi feita (o driver já estava instalado); só simulações `apt-get -s` | Com autorização do dono: Mint/Ubuntu com NVIDIA no nouveau; clicar INSTALAR, reiniciar, conferir "Funcionando" |
| NVDRV-02 | Secure Boot ligado (chave MOK) | NÃO TESTADO | Secure Boot desligado nas máquinas de teste | Máquina com SB ligado: instalar, `mokutil --import`, "Enroll MOK" |
| NVDRV-03 | Placas Kepler (470) e Turing+ (driver open) | NÃO TESTADO | Só há Maxwell (940MX) | Placas correspondentes |
| NVDRV-04 | Driver NVIDIA em Debian, Fedora/Nobara, Arch e openSUSE | NÃO TESTADO | Scripts só documentados | Máquina real ou VM com passthrough de GPU |

### Sistema, sensores e discos

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| SENSOR-01 | Sensores em outras placas-mãe, CPUs AMD de desktop e outras baterias | NÃO TESTADO | Cobertura real limitada às três máquinas acima | Comparar a página Sensores com `sensors` |
| STRESS-02 | Corte por temperatura em CPU AMD (k10temp/zenpower) | PARCIAL | O Tctl é lido certo na APU; o corte por clique não foi exercitado | Clique na página Stress numa máquina AMD |
| SMART-01 / SMART-02 | SMART em disco real pelo app (temperatura, desgaste, erros), SATA, USB e RAID | NÃO TESTADO | `smartctl` sem privilégio falha; só um relatório NVMe importado foi validado | `sudo smartctl -a -j /dev/nvme0n1 > arq.json` e importar na página Discos |
| DISK-03 | Liberar leitura da saúde (instala o leitor só-leitura e a regra do polkit com a senha), ler a saúde depois sem senha e Remover permissão | NÃO TESTADO | Pede a senha do dono; testado só o roteiro fixo (conteúdo dos arquivos) e a recusa sem permissão | Clicar em Liberar leitura na página Discos, conferir a saúde do SSD, reiniciar, conferir de novo e então Remover permissão |
| DISK-04 | Montar, desmontar, ejetar e formatar pendrive pela página Discos (udisks2) | NÃO TESTADO | Mexeria no pendrive em uso; testadas as recusas (disco do sistema, disco interno, nome e tipo inválidos) | Pendrive descartável: montar, desmontar, ejetar e formatar em exFAT e FAT32 |
| CLEAN-01 | Limpar de verdade pela página Liberar espaço (lixeira, caches, pacotes com paccache/apt/dnf/zypper, journal, Flatpak sem uso, arquivos parados), inteiro ou só as partes marcadas em Exibir detalhes | NÃO TESTADO | Apagaria arquivos do dono; testados a análise real (só leitura), os detalhes reais deste PC (nativo) e a exclusão parcial em pastas temporárias; a listagem pelo `find` do sistema, usada no Flatpak, foi testada fora dele; dnf 4 e `XDG_CACHE_HOME` só em teste automático | Em Exibir detalhes, deixar marcado só um cache descartável e um item da lixeira, limpar e conferir que o resto ficou; depois os itens que pedem senha; no Flatpak, abrir os detalhes de Pacotes baixados e Registros antigos |
| DISK-01 | Temperatura do disco por hwmon (NVMe, drivetemp) | NÃO TESTADO | Os kernels testados não expõem hwmon para os discos | Carregar `drivetemp` ou outro kernel |
| DISK-02 | Tipo e barramento de USB, MMC e SD | NÃO TESTADO | Sem cartão SD ou disco MMC nos testes | Conectar um cartão SD |
| ENERGY-01 | Bateria em descarga, ciclos, autonomia, duas baterias | NÃO TESTADO | Baterias de teste estavam na tomada e reportam 0 ciclos | Usar na bateria até descarregar |
| ENERGY-02 | PC de mesa sem bateria | NÃO TESTADO | Todas as máquinas de teste são notebooks | Abrir a página Energia num desktop |
| START-01 | Alterar serviços systemd reais e `Hidden=true` em GNOME/KDE | NÃO TESTADO | O smoke só mexe em pastas temporárias | Teste manual com uma entrada descartável |
| OC-01 / OC-02 | Comandos de OC aplicados de verdade; erros de hardware reais (MCE, EDAC, PCIe, Xid) | PARCIAL | Só leitura e teto de clock na 940MX; nenhuma ocorrência real de erro | Placa com OC ajustável; máquina com erros registrados |
| OVW-02 / OVW-03 | Consumo da GPU dedicada na Visão geral; checklist de saúde com problemas reais | PARCIAL | RAPL Intel conferido; sem GPU dedicada com consumo e sem disco com aviso | Máquina com essas condições |
| REP-01 | Relatório geral em outras máquinas (desktop, várias GPUs, discos USB) | NÃO TESTADO | Só as máquinas acima | Gerar o relatório e conferir com o hardware |
| CFG-01 / CFG-02 | Duas janelas do app ao mesmo tempo; `settings.json` em NFS | NÃO TESTADO | Testado com processos de console e uma instância gráfica | Abrir duas instâncias e mudar opções nas duas |

### Jogos, overlay e FPS

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| GAME-01 | Iniciar um jogo pela interface e acompanhar o processo | NÃO TESTADO | Os testes automáticos nunca iniciam jogos; o jogo real rodou fora da interface | Clique manual com um jogo instalado; conferir o perfil de energia voltando ao fechar |
| GAME-02 | Heroic (Epic/GOG) e Lutris reais | NÃO TESTADO | Lidos só pelo formato conhecido dos arquivos | Instalar Heroic ou Lutris com um jogo e abrir a página Jogos |
| GAME-04 | Fechar a Steam e gravar opções num `localconfig.vdf` real ("Aplicar na Steam") | NÃO TESTADO | Fechar a Steam derruba jogos; os testes usam pasta temporária | Teste manual com a Steam aberta |
| GAME-05 | Detecção de jogos Proton/Wine em execução | NÃO TESTADO | Sem jogo rodando durante os testes da página | Rodar um jogo Proton e abrir a página Jogos |
| FPS-02 | "MEDIDO NESTE PC": sessão real com MangoHud e leitura ao fechar | NÃO TESTADO | Nenhum FPS real foi medido pelo app | Ligar o MangoHud no perfil, jogar ≥ 1 min pela página Jogos, fechar e comparar com o contador do MangoHud |
| FPS-03 | Faixa de FPS, confiança e alertas contra FPS real | NÃO TESTADO | A faixa não foi calibrada com FPS medido | Medir alguns jogos (FPS-02) em hardware variado |
| OVL-01 | Overlay do Ilmenyx sobre jogo em tela cheia (X11 e Wayland) | NÃO TESTADO | Depende do compositor; sem jogo em tela cheia nos testes | Abrir o overlay com um jogo em tela cheia |
| OVL-02 / OVL-03 | FPS real pela configuração MangoHud do app; opções da Steam com `MANGOHUD_CONFIGFILE` | NÃO TESTADO | Sem jogo real com a configuração salva | Jogar com o overlay configurado pela página Overlay |
| CMP-01 | MangoHud embutido em jogo real e jogos de 32 bits | PARCIAL | Desenhou sobre vkcube e glxgears; falta jogo real; só 64 bits no pacote | Jogo Vulkan pela página Jogos numa máquina sem MangoHud no sistema |
| CMP-02 | GameMode embutido (ionice/renice) | NÃO TESTADO | Efeitos não medidos num jogo | `ionice -p` e `ps -o ni` no jogo iniciado pelo app |
| CMP-04 | Flatpak (página Apps) não cabe no pacote | Não se aplica | O Flatpak é serviço do sistema; o app mostra o comando da distro | — |
| APP-01 | Instalação real de apps pela página Instalar apps | NÃO TESTADO | Baixaria centenas de MB e mudaria o sistema; os testes usam executor falso | Instalar um app pequeno pelo app |
| WINE-01 | "Instalar jogo de Windows" com um instalador real até o fim | NÃO TESTADO | Fluxo testado com Wine falso; um instalador real falhou no Proton e passou no Bottles | Rodar um instalador real nos modos Proton e Wine comum |

### Benchmarks e carga

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| CPU-01 / BENCH-02 | Perfil CPU completo e GameSim completo com carga real | NÃO TESTADO | Regra do projeto: nenhum teste automático gera carga pesada | Clique manual com o monitor ativo e limite de temperatura |
| SIM-01 | GameSim completo (30.000 entidades) | NÃO TESTADO | Runner experimental só por opt-in | `ilmenyx bench sim-full --allow-cpu-load` com autorização |
| STRESS-01 | Carga real de CPU/RAM e pouca RAM (OOM) | NÃO TESTADO | Risco de aquecer ou travar a máquina de teste | Clique manual com limite de temperatura |
| BENCH-03 / BENCH-04 | Disco em btrfs/xfs/f2fs/NTFS; RAM completa em máquinas de 4 GB | NÃO TESTADO | Disco: só um HDD SATA ext4 (o NVMe e outros sistemas de arquivos não); RAM: só com 7 GiB livres | Máquinas com esses sistemas de arquivos e pouca RAM |
| NET-01 | Teste de rede real (ping, DNS, download, upload) | NÃO TESTADO | Contata serviços de terceiros; só roda com clique | Clique em TESTAR, no Wi-Fi e no cabo |

### Interface, áreas de trabalho e pacotes

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| GUI-01 / GUI-03 | GNOME e outros compositores Wayland reais, escala fracionária, vários monitores | PARCIAL | Wayland conferido no Muffin aninhado e na máquina de desenvolvimento (KDE); falta GNOME, escala fracionária e vários monitores | Usar o app numa sessão GNOME e com escala 125 % / 150 % |
| GUI-02 | Teclado físico, leitor de tela (Orca) e contraste medido | NÃO TESTADO | A automação aciona controles no processo | Navegar só pelo teclado e com o Orca ligado |
| PROC-01 | Encerrar e Forçar encerramento na página Processos com programas reais; disco por processo e encerrar dentro do Flatpak | NÃO TESTADO | Testado com processos de teste e testes de unidade; o caminho do Flatpak (comandos no sistema hospedeiro) não rodou | Abrir um programa descartável, encerrar pela página, no app nativo e no Flatpak |
| CHR-01 / CHR-02 | Tema claro em outros DPI; avisos de pendrive em GNOME | NÃO TESTADO | Só nas máquinas acima | Trocar o tema e conectar um pendrive no GNOME |
| OPT-01 / OPT-02 | Fechar apps e mudar perfil de energia pela interface; aceleração do mouse no KDE | NÃO TESTADO | Os testes usam serviços falsos; o mouse só está implementado para o GNOME | Teste manual com apps descartáveis |
| TRAY-01 | Ícone na área de notificação em GNOME (extensão AppIndicator), XFCE e Cinnamon | NÃO TESTADO | Novo na 0.32.0; sem teste registrado fora do KDE. O app verifica se há área de notificação antes de oferecer a opção | Ligar a opção nas preferências e minimizar para a bandeja |
| ALERT-01 | Aviso de CPU quente com superaquecimento real | NÃO TESTADO | Novo na 0.32.0; testado com leituras simuladas, sem aquecer a máquina | Deixar o aviso ligado durante uma carga pesada (aviso após 20 s acima do limite) |
| UPD-01 | Atualizar agora instalando de verdade pelo app (AppImage e Flatpak) e reiniciando na versão nova | PARCIAL | Aviso de versão nova conferido pelo dono (0.32.0 detectou a 0.33.0); download, SHA-256, assinatura GPG e troca do AppImage testados de ponta a ponta contra a release real num arquivo de teste; reinstalar o Flatpak e reiniciar não rodaram | Com a próxima release: clicar na versão do menu lateral, Atualizar agora, Reiniciar agora, no AppImage e no Flatpak |
| I18N-01 | Revisão dos textos em inglês e espanhol por falantes nativos | NÃO TESTADO | Traduções feitas na 0.31.0, sem revisão de falante nativo | Usar o app em EN ou ES e relatar textos estranhos |
| PKG-02 | Pacotes em mais distros: smoke gráfico completo, Wayland nativo, sem libfuse2, Alpine/musl | PARCIAL | CLI e janela conferidas em 5 distros (VMs) e AppImage/Flatpak no Mint | Abrir o AppImage e o Flatpak em outras distros e sessões Wayland |
| PKG-03 | Flatpak: senha do pkexec pela ponte, jogos/MangoHud/PRIME, OpenRGB e benchmarks Vulkan no sandbox, instalação `--system` | NÃO TESTADO | Só as páginas de leitura foram conferidas dentro do Flatpak | Repetir os testes de jogos e benchmark no Flatpak |
| ARCH-01 | ARM64 e outras arquiteturas | NÃO TESTADO | Pacotes só para x86_64 | Hardware ARM64 com SDK e bibliotecas nativas |

### Integrações

| ID | Item | Estado | Por que falta | Como testar |
|---|---|---|---|---|
| RGB-01 / RGB-02 / CMP-03 | OpenRGB real: conversa com o servidor, efeitos em dispositivos e devolução ao modo original | NÃO TESTADO | Servidor embutido iniciou e listou dispositivos, mas nenhuma cor foi enviada; nenhum PC com RGB disponível | Aplicar um efeito pela página Integração num PC com RGB e regras udev |
| RGB-03 | Corsair iCUE e Razer Synapse diretamente | Fora do escopo agora | Programas só para Windows | — |
| MAKER-01 / MAKER-02 | Placa serial USB real (CH340, CP210x, FTDI, Arduino, ESP); brltty e ModemManager atrapalhando | NÃO TESTADO | Nenhuma placa conectada nos testes | Conectar uma placa e abrir Maker · Portas |
| SENS-UNDO | Desfazer a liberação de sensores e persistência após reiniciar | NÃO TESTADO | Só a liberação foi feita de verdade (RAPL Intel no Dell) | Liberar, reiniciar, conferir; depois Desfazer |
| ZRAM-01 | Aplicar e desfazer o ZRAM de verdade | NÃO TESTADO | Plano e scripts testados com fixtures de 5 famílias de distros | Aplicar numa máquina de teste e desfazer |

## Fora do escopo agora

| ID | Item | Motivo |
|---|---|---|
| WIN-01 / WIN-02 / SIM-02 | Versão para Windows e comparação de GameSim Linux × Windows | O Ilmenyx Controller é só para Linux; o projeto original para Windows segue no upstream |
| IO-01 | Benchmarks de carregamento de jogo e de rede com carga | Exigem metodologia própria antes de qualquer execução |
