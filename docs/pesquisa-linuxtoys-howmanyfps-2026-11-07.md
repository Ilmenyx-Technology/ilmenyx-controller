# Pesquisa: LinuxToys e API HowManyFPS (2026-11-07)

Pesquisa só de leitura. Nada no Ilmenyx foi alterado.

---

## Parte 1: LinuxToys (psygreg/linuxtoys)

### 1.1 Dados do projeto

- Repositório: https://github.com/psygreg/linuxtoys (branch `master`, cerca de 1,37 mil estrelas, último push em 2026-11-07).
- **Licença: GPL-3.0** (arquivo `LICENSE` na raiz e `p3/LICENSE`). As configurações que ele baixa também são GPL-3.0:
  CachyOS-Settings (https://github.com/CachyOS/CachyOS-Settings) e psygreg/shader-booster.
- Formato: app em Python com GTK e TUI (`p3/app`), mais cerca de 200 scripts bash em `p3/scripts/**`. Cada script tem cabeçalho
  `# compat:`, `# reboot:`, `# gpu:`, `# revert:` e `# systemd:`. Também tem um servidor MCP opcional para agentes de IA.
- Reversão: `p3/app/revert_helper.py` lê um "transmap", o registro das operações de cada execução (pacote instalado, serviço
  ligado, arquivo criado ou editado com `.bak`), e gera o script que desfaz. Alguns scripts declaram `# revert: no`
  (codecfix, kisak-mesa).
- Distros declaradas: Debian/Ubuntu e derivadas, Fedora/RHEL, Arch/CachyOS/Manjaro, openSUSE, Fedora Atomic/Universal Blue,
  Solus, Deepin, SteamOS. Só x86. Sem systemd, o suporte é limitado.

### 1.2 Lista de funções (por pasta de `p3/scripts`)

**Otimizações (`extra/`)**

| Script | O que faz (pelo código lido) |
|---|---|
| `pdefaults` ("Recommended settings") | Pacote de otimizações que roda **sem confirmação** (`noconfirm: yes`): sboost, dsplitm, latencydevil, earlyoom, intelxe, gtk-bmg-fix, `check-alive-timeout` do GNOME, preemptfedora, ubuntuv3, bbr, mtuprobe |
| `cachyconfs` | Baixa do GitHub as regras do CachyOS-Settings (udev de IO scheduler, `cpu-dma-latency`, energia do áudio; tmpfiles de THP e coredump; modprobe de amdgpu/nvidia/blacklist; journald; touchpad) e um sysctl próprio (`swappiness=60`, `page-cluster=1`, `dirty_*_bytes`, `file-max`, `netdev_max_backlog`) para `/usr/lib/...` |
| `cachykrn` | **Só Fedora/RHEL**: kernel CachyOS via COPR `bieszczaders/kernel-cachyos`, torna esse kernel o padrão (grubby), muda o SELinux (`domain_kernel_load_modules`) e, com Secure Boot, **gera uma chave MOK e assina o kernel** |
| `optivram` | Arch/Fedora: `dmemcg-booster` e, no KDE, `plasma-foreground-booster` (prioridade de VRAM do SteamOS: manda páginas frias de VRAM para a RAM; ajuda GPUs com 6 GB ou menos). No Fedora instala RPMs **hospedados no próprio repositório do LinuxToys** |
| `ananicy` | ananicy-cpp com as regras do CachyOS (Arch; no Fedora, via COPR do CachyOS) |
| `earlyoom` | earlyoom com a configuração deles (no RHEL usa systemd-oomd; exclui Fedora/Ubuntu, que já têm systemd-oomd) |
| `zram` | ZSWAP para 16 GB de RAM ou menos (parâmetros do kernel no GRUB/systemd-boot/grubby); ZRAM para mais (zram-generator, zram-config, zram-tools) |
| `swapfile` | Cria um arquivo de swap |
| `minfreefix` | Serviço systemd que ajusta `vm.min_free_kbytes` para 1 % da RAM |
| `sboost` (Shader Booster) | Acrescenta ao `/etc/environment` `MESA_SHADER_CACHE_MAX_SIZE=10G`, `__GL_SHADER_DISK_CACHE_SIZE=10000000000` **e também `AMD_VULKAN_ICD=RADV`** |
| `dsplitm` | Desliga a mitigação de split-lock (`kernel.split_lock_mitigate=0` por sysctl, ou `split_lock_detect=off` no kernel) |
| `preemptfedora` | `preempt=full` no kernel (Fedora/Ubuntu/Atomic) |
| `ondemand` | AMD com amd-pstate-epp: serviço que grava `balance_performance` no EPP |
| `psaver` | power-options (repositório de terceiros) para notebooks, se não houver TLP/PPD |
| `ubuntuv3` | Ubuntu: `APT::Architecture-Variants "amd64v3"` e `apt upgrade -y` |
| `umip` | `clearcpuid=umip` no kernel (alguns apps Windows no Wine) |
| `bbr`, `mtuprobe` | TCP BBR mais buffers por sysctl; sondagem de MTU (problemas em alguns jogos online) |
| `wayproton` | Acrescenta `PROTON_ENABLE_WAYLAND=1` ao `/etc/environment` (a descrição cita outra variável, `PROTON_USE_WINE_WAYLAND`: o texto e o código não batem) |
| `bwrapfix` | Ubuntu: perfil AppArmor `bwrap` sem confinamento (corrige Flatpak/Proton quebrados pela restrição de userns) |
| `susefix` | openSUSE: `setsebool -P selinuxuser_execmod 1` (Wine/Proton) |
| `gtk-bmg-fix` | `GSK_RENDERER=gl` global no Intel Arc e na NVIDIA |
| `preload`, `iwd`, `dnsmasq` (cache de DNS, "corrige download da Steam"), `ufw`, `apparmor`, `archsb` (Secure Boot no Arch), `grubtrfs`, `btassist` (snapper), `kconnect`, `cnfh` (distrobox), `lsw` (WinBoat), `corefonts`, `codecfix`, `ostree-autoupd` | Utilitários diversos |

**Drivers (`sys/drivers/`)**: NVIDIA por família de distro (`nv-deb` usa `nvidia-driver-N-open` no Ubuntu e o repositório CUDA
`nvidia-open` + `cuda-toolkit` no Debian; `nv-rpm`, `nv-pacman`, `nv-pacman-470`, `nv-suse`, `nv-solus`, `nv-ostree`, `nv-580*`
para Maxwell/Pascal/Volta), `amdgpu` (amdgpu-install da AMD no Ubuntu LTS/RHEL), `kisak-mesa` (PPA), `intelxe`, `rocm`, `rusticl`,
`optimusui` (bbswitch/bumblebee + OptimusUI), `xpadneo` (controle Xbox por Bluetooth, DKMS a partir do git), `modsign` (MOK),
Wi-Fi rtl8821ce/bcm/ax900, `damx`, `icr`.

**Repositórios (`repos/`)**: Flathub (repara ou adiciona o remoto, de usuário primeiro), RPM Fusion, multilib, Chaotic-AUR, Terra,
Pacstall, Snap, Homebrew, Nix, Cargo, pip, Flatpark.

**Jogos (`game/` e `lists/`)**: Gamescope (pacote mais a VulkanLayer Flatpak), GeForce NOW, Sunshine, Steam (Flatpak + override de
acesso aos discos), Epic, LatencyDevil (wrapper que exporta `PROTON_USE_WAYLAND=1`, `LOW_LATENCY_LAYER=1` e, na NVIDIA, Reflex
mais `dxgi.hideAmdGpu`), OptiScaler client, Bedrock (Minecraft). Os jogos em si e Heroic/Lutris/GOverlay vêm do catálogo
AppStream/Flatpak (só os ícones estão no repositório).

**Utilitários e periféricos**: OpenRGB, OpenLinkHub, OpenRazer, Piper, Solaar, input-remapper, udevpass (WebHID para ATK HUB
etc.), WiVRn, Touchegg, OCCT, Waydroid, DistroShelf, Nerd Fonts, temas Flatpak. **Sistema**: atualizador geral, topgrade, yay/paru,
VPNs, MediCat.

**Dev, criativo, escritório e educação**: Docker, IDEs, agentes de IA, DaVinci Resolve etc. Fora do escopo do Ilmenyx.

### 1.3 O que é útil e prático para o Ilmenyx

O Ilmenyx já tem `vm.max_map_count`, `vm.swappiness`, governador da CPU e limite de arquivos (`OptimizerLogic.cs`), além de
GameMode, MangoHud, Flatpak, OpenRGB e a aba de driver NVIDIA. As sugestões abaixo complementam isso e seguem o modelo
**detectar → explicar → confirmar → aplicar → desfazer**, com o comando root mostrado para o usuário copiar ou rodado via `pkexec`
com confirmação.

1. **Cache de shader maior ("Shader Booster", sem o RADV forçado)**
   - **O que faz:** aumenta o limite do cache de shaders (Mesa: 1 GB por padrão; NVIDIA: o padrão varia com a versão do driver, não conferido)
     para cerca de 10 GB.
   - **Por que ajuda:** jogos novos estouram o limite, o cache é descartado e o jogador vê engasgos de compilação a cada sessão.
   - **Distros:** todas.
   - **Risco:** baixo; só usa disco. **Reversível:** sim.
   - **Como fazer:**
     - **Detectar:** tamanho de `~/.cache/mesa_shader_cache*` e `~/.cache/nvidia/GLCache`, e o valor atual da variável.
     - **Aplicar sem root:** `~/.config/environment.d/90-ilmenyx-shader-cache.conf` (systemd user) ou só no perfil de jogo do
       Ilmenyx/Steam. Não usar o `/etc/environment` global.
     - **Desfazer:** apagar o arquivo.
     - **Não copiar** o `AMD_VULKAN_ICD=RADV` global. É um efeito colateral escondido que quebra quem usa AMDVLK de propósito.
     - O Steam já tem um cache próprio (`shadercache`), então o ganho vale mais para Heroic, Lutris e umu.
2. **Parâmetros de jogo no lançador (PROTON_ENABLE_WAYLAND, latência, escolha de GPU)**
   - **O que faz:** a ideia do LatencyDevil e do wayproton, só que **por jogo** no perfil do Ilmenyx, e não global. O Ilmenyx já
     gera opções da Steam e usa umu, então encaixa direto.
   - **Distros:** todas.
   - **Risco:** baixo, porque vale por jogo. **Reversível:** sim (a opção sai do perfil).
   - **Como fazer:**
     - **Detectar** a sessão Wayland e a versão do Proton (o modo Wayland só existe em versões recentes do Proton/GE).
     - **Explicar** que é experimental, deixar desligado por padrão e confirmar com o usuário.
3. **ZRAM / swap comprimida**
   - **O que faz:** evita travamentos por falta de RAM em PCs com 8 a 16 GB jogando com navegador aberto.
   - **Já vem por padrão em:** Fedora (zram-generator) e CachyOS. Não vem em Ubuntu, Mint e Arch.
   - **Risco:** baixo a médio (mexe em swap). **Reversível:** sim.
   - **Como fazer:**
     - **Detectar:** `/proc/swaps` e `zramctl`; se já existir zram, mostrar "já ativo" e parar.
     - **Aplicar:** só pela via oficial da distro (`systemd-zram-generator` com `/etc/systemd/zram-generator.conf`, ou
       `zram-tools` no Debian/Mint).
     - **Desfazer:** remover o arquivo e o pacote.
     - **Não copiar** o ZSWAP via parâmetro de kernel do LinuxToys: edita GRUB/systemd-boot, depende do bootloader e precisa reiniciar.
4. **Verificações de "por que o Proton/Flatpak não abre"**
   - **O que faz:** só diagnóstico, que é a força do Ilmenyx. Os problemas reais que o LinuxToys corrige são:
     - **Ubuntu 24.04+:** AppArmor `kernel.apparmor_restrict_unprivileged_userns=1` bloqueando o bubblewrap (bwrapfix).
     - **openSUSE:** SELinux `selinuxuser_execmod` (susefix).
     - **Arch:** falta do multilib, necessário para a Steam nativa.
     - **Fedora:** falta do RPM Fusion e de codecs (ffmpeg-free).
     - **Flatpak:** remoto Flathub quebrado ou ausente.
     - **Steam Flatpak:** sem acesso ao disco de jogos (`flatpak override --filesystem=...`).
     - **Split-lock:** mitigação ativa (`kernel.split_lock_mitigate`), que causa queda de FPS em alguns jogos.
   - **Como fazer:** detectar e explicar cada caso, com o comando exato para copiar e o comando para desfazer. Aplicar com um clique
     só nos casos sem root (override do Flatpak e remoto Flathub em `--user`).
   - **Risco:** só leitura no diagnóstico; a correção depende do caso. bwrapfix e susefix **reduzem a segurança** e precisam de aviso claro.
5. **IO scheduler e sysctl de responsividade (da CachyOS)**
   - **O que faz:** uma regra udev escolhe o scheduler por tipo de disco (CachyOS: HDD `bfq`, SSD SATA/eMMC `mq-deadline`,
     NVMe `kyber`; vários kernels já usam `none` no NVMe). O sysctl ajusta `vm.dirty_bytes=256M`,
     `dirty_background_bytes=64M`, `vfs_cache_pressure=50` e `page-cluster=0` quando há zram.
   - **Por que ajuda:** pouco no FPS médio, mais em engasgos ao copiar arquivos grandes ou instalar jogos durante o uso.
   - **Distros:** todas com udev.
   - **Risco:** baixo. **Reversível:** sim.
   - **Como fazer:**
     - **Aplicar** num arquivo próprio e identificável (`/etc/udev/rules.d/60-ilmenyx-iosched.rules`,
       `/etc/sysctl.d/90-ilmenyx.conf`) e guardar os valores anteriores.
     - **Desfazer:** apagar os arquivos e rodar `sysctl --system` e `udevadm trigger`.
     - **Detectar** o CachyOS ou o pacote `cachyos-settings` e não duplicar.
6. **Instalação de ferramentas de jogo pelo Flatpak/pacote** (o Ilmenyx já instala apps via Flatpak). O catálogo pode crescer com:
   - **LACT** (`io.github.ilya_zlobintsev.LACT`, OC/ventoinha de AMD/NVIDIA/Intel; o daemon precisa de pacote nativo).
   - **CoreCtrl** (só pacote nativo).
   - **ProtonPlus / ProtonUp-Qt.**
   - **Gamescope.** Pacote nativo; no Flatpak da Steam usar `org.freedesktop.Platform.VulkanLayer.gamescope`.
   - **Sunshine.**
   - **GeForce NOW.**
   - **Piper / Solaar / input-remapper** (periféricos).
   - **xpadneo:** só como link e explicação, porque precisa de DKMS e Secure Boot.

   - **Risco:** baixo no Flatpak. Desfazer com `flatpak uninstall`.
7. **ananicy-cpp / earlyoom**
   - **O que faz:** melhora a responsividade com pouca RAM.
   - **Recomendação:** só **detectar e explicar**: se systemd-oomd ou earlyoom estiverem ativos, dizer isso; senão, sugerir o pacote
     da distro.
   - **Conflitos:** ananicy disputa prioridade com o GameMode, que o Ilmenyx já usa; earlyoom conflita com systemd-oomd.
     Prioridade baixa.
8. **optivram (dmemcg-booster)**
   - **Por que é tentador:** tem valor real para GPUs com até 6 GB de VRAM, que é o público de entrada.
   - **Limitação:** depende de kernel com dmem cgroup (6.14+ com patches/CachyOS) e de pacotes que só existem no Arch/CachyOS e em
     RPMs não oficiais.
   - **Recomendação:** apenas detectar e informar no CachyOS/Arch ("disponível no seu repositório: dmemcg-booster"); não instalar
     binários de terceiros.
9. **Kernel CachyOS**
   - **Recomendação:** **não instalar pelo app.** Só informar:
     - em Arch e derivadas, os repositórios oficiais da CachyOS (https://wiki.cachyos.org/features/optimized_repos/);
     - no Fedora, o COPR `bieszczaders/kernel-cachyos`;
     - no Ubuntu/Debian, não há canal oficial equivalente.
   - Mostrar o kernel atual (`uname -r`, sched-ext disponível) e um link.
   - **Riscos:** boot quebrado, DKMS da NVIDIA sem build para o kernel novo, Secure Boot/MOK, SELinux alterado.

### 1.4 O que NÃO copiar, e por quê

- **Código:** o LinuxToys e as configurações da CachyOS são **GPL-3.0**. O Ilmenyx declara `MIT AND PolyForm-Noncommercial-1.0.0`
  (cabeçalho SPDX do `HowManyFpsClient.cs`). Copiar ou adaptar código GPL obrigaria a distribuir o Ilmenyx sob GPL-3.0, o que é
  incompatível com a cláusula não comercial da PolyForm. **Só as ideias podem ser reaproveitadas**, com implementação própria.
  Valores de sysctl e nomes de variáveis são fatos técnicos e podem ser usados. Os arquivos de regra da CachyOS não devem ser
  copiados literalmente, e não se deve baixá-los em tempo de execução: isso também cria uma dependência de rede escondida.
- **`pdefaults` sem confirmação:** viola o princípio do Ilmenyx de nunca mudar o sistema sem confirmação explícita.
- **Downloads em tempo de execução de `raw.githubusercontent.com` (configs, RPMs) gravados em `/usr/lib`:** não há verificação de
  hash, eles são sobrescritos por atualizações da distro e não são "arquivos de admin" (o lugar certo é `/etc`).
- **Escrever no `/etc/environment` global** (sboost, wayproton, gtk-bmg-fix): afeta todos os apps e usuários, e o sboost ainda
  força o RADV. O certo é um perfil por jogo ou o `environment.d` do usuário.
- **Parâmetros de kernel e bootloader** (umip, preempt, zswap, split_lock_detect, kernel CachyOS): dependem de GRUB, systemd-boot
  ou grubby e podem impedir o boot. Se for oferecer algo, prefira sysctl em tempo de execução (ex.: `kernel.split_lock_mitigate`).
- **MOK, Secure Boot e chaves geradas automaticamente** (cachykrn, modsign, archsb): alto risco e difícil de desfazer.
- **Afrouxar AppArmor ou SELinux sem explicar** (bwrapfix, susefix, `setsebool domain_kernel_load_modules`): reduz a segurança.
- **`ubuntuv3`** (`amd64v3` mais `apt upgrade -y` automático) e **`kisak-mesa`** (PPA mais `upgrade -y`, `revert: no`): fazem
  upgrade grande e não interativo, e voltar atrás é difícil.
- **`amdgpu-install`** (driver proprietário da AMD): no desktop, o Mesa da distro é o recomendado; o amdgpu-install serve para ROCm.
- **Repositórios de terceiros** (Chaotic-AUR, Terra, distropack, Pacstall): muda a cadeia de confiança do sistema.
- **Servidor MCP e auto-update de scripts via git:** grande superfície de ataque, fora do escopo.

### 1.5 Top 5 por valor/esforço

| # | Função | Valor | Esforço | Por quê |
|---|---|---|---|---|
| 1 | Diagnóstico "Proton/Flatpak não abre" (userns/AppArmor do Ubuntu, multilib, Flathub, override da Steam Flatpak, split-lock) | Alto | Baixo | Só leitura mais comando para copiar; combina com "Por que não aparece?" |
| 2 | Cache de shader maior, por usuário ou por jogo | Médio-alto | Baixo | Sem root, reversível, beneficia todas as GPUs |
| 3 | Mais apps de jogo no instalador Flatpak (LACT, ProtonPlus, Gamescope layer, Sunshine, GeForce NOW, Piper/Solaar) | Médio-alto | Baixo | O mecanismo já existe no Ilmenyx |
| 4 | ZRAM pela via oficial da distro, com detecção de "já ativo" | Médio | Médio | Grande efeito em PCs com 8 GB; precisa de um caminho por família de distro |
| 5 | Opções por jogo: Proton Wayland, escolha de GPU, low-latency/Reflex | Médio | Médio | Usa os perfis de jogo e o umu existentes; precisa de teste real |

Ficam fora do top 5: IO scheduler/sysctl (valor baixo e mensurável só em casos específicos), optivram e kernel CachyOS (só informar).

---

## Parte 2: API HowManyFPS

### 2.1 O que foi possível ler

- **Nenhuma página do site pôde ser lida diretamente.** `https://howmanyfps.com/api`, `https://howmanyfps.com/developer-api` e
  `https://be.howmanyfps.com/developer-api` devolveram **HTTP 403** pela ferramenta de busca de páginas. Um `curl` com outro
  user-agent foi bloqueado pela política de permissões desta sessão e não foi repetido.
- As informações abaixo vêm **apenas dos trechos dos resultados de busca** dessas páginas
  (https://howmanyfps.com/developer-api e https://be.howmanyfps.com/developer-api). Precisam ser conferidas no navegador.
- **Termos de uso (ToS) e termos da API: não encontrados.** A busca por "howmanyfps terms of service" não achou página de termos
  do HowManyFPS; achou só https://be.howmanyfps.com/affiliate-disclosure, que diz que os dados vêm de sessões gravadas pela
  comunidade com o app HowManyFPS e que o site participa do Amazon Associates. **Não dá para afirmar se embutir a chave é permitido
  ou proibido**: não há texto público lido que diga isso.

### 2.2 Como se obtém uma chave (segundo os trechos de busca)

- **Não há cadastro self-service.** O acesso é **por solicitação por e-mail** ("Get API Access"). O endereço apareceu ofuscado nos
  resultados e não foi confirmado.
- **Prazo de resposta:** uma das páginas diz que a equipe responde em 24 h para discutir **preço e requisitos técnicos**; a outra
  diz que as credenciais e uma coleção Postman chegam em geral em um dia útil. É preciso dizer o que você está construindo.
- **Planos e preço:** não há tabela pública nos trechos, só "Volume pricing available" e um bloco "Need something custom?".
  A página cita "10K requests" e resposta abaixo de 250 ms, mas **não diz se 10K é uma cota mensal, diária ou um limite**, nem se é
  gratuita. Calculadora, app de benchmark e benchmarks públicos para consumidor são grátis; existe um plano "Pro" para consumidores
  (não é a API).
- **Formato descrito:** `POST` em **`v1.howmanyfps.com`**, com IDs de CPU e GPU, ID do jogo e configurações; a resposta traz FPS
  médio, 1% low e notas de desempenho. Cobre mais de 10 mil combinações de hardware e mais de 100 jogos.

**Achado no código (só leitura):** `src/Ilmenyx.Core/HowManyFpsClient.cs` faz `POST https://howmanyfps.com/api` com **nomes em
texto** (`cpu`, `gpu`, `game`, `resolution`, `upscaling`), manda a chave em dois cabeçalhos (`Authorization: Bearer` e
`x-api-key`) e espera `avgFps` e `min1Fps`. Isso **não corresponde** ao que a página pública descreve (host `v1.howmanyfps.com`,
**IDs** de hardware e jogo). O contrato do cliente parece suposto, não verificado. Com uma chave real, é provável que o Ilmenyx
receba 404 ou 400. Recomendo pedir a documentação e a coleção Postman antes de anunciar a função e marcar essa parte como NÃO TESTADA.

### 2.3 Riscos de embutir uma chave num app aberto ou distribuído

- **Extração:** qualquer chave dentro do binário .NET ou do pacote sai com `strings`, ILSpy ou observando o tráfego (mitmproxy).
  Ofuscar só atrasa. Em repositório público, a chave vaza no primeiro commit.
- **Cota:** a cota é única para todos os usuários. Um único abusador (ou o próprio app em loop) esgota a cota e a função para para
  todo mundo (o app já trata 429 como "cota esgotada").
- **Revogação:** se a HowManyFPS revogar ou trocar a chave, todas as versões já distribuídas param ao mesmo tempo, e só um novo
  release resolve.
- **Custo e responsabilidade:** se o plano for pago por volume, a Bismuth paga pelo uso de terceiros. Se os termos proibirem
  redistribuição (não se sabe), há risco contratual.
- **Privacidade:** a chave da Bismuth identifica a Bismuth, não o usuário. Hoje o Ilmenyx promete "sem rede escondida", e um proxy
  ou chave da Bismuth faz o hardware do usuário chegar a um servidor da Bismuth ou do parceiro em nome da Bismuth. Isso precisa
  estar explícito na UI e na política de privacidade.

### 2.4 Alternativas

1. **Chave de cada usuário (modelo atual).** Seguro e sem custo para a Bismuth, mas na prática quase ninguém terá chave, porque o
   acesso é negociado por e-mail e voltado a empresas. A função fica quase morta. Vale manter, com o texto explicando como pedir.
2. **Proxy pequeno da Bismuth com cache.** O app chama `api.bismuth.../fps`, e o servidor guarda a chave e consulta a HowManyFPS.
   - **Prós:** a chave nunca sai do servidor; dá para fazer cache agressivo (a combinação CPU+GPU+jogo+configuração muda pouco,
     então 7 a 30 dias de cache servem muitos usuários com poucas chamadas), limite por IP e troca de chave sem novo release.
   - **Custos:** pequenos, servidor de borda (Cloudflare Workers/KV, Fly.io etc.) com custo de zero até poucos dólares por mês nesse
     volume. Existe manutenção, monitoramento, LGPD (o servidor recebe hardware e IP) e um ponto único de falha.
   - **Termos:** **só com autorização escrita da HowManyFPS**. Proxy com cache compartilhado é redistribuição de dados, e os termos
     não foram encontrados, então não se pode supor que é permitido.
3. **Parceria / chave de app com a HowManyFPS.** Esse é o caminho que a própria página sugere ("Need something custom?", "Volume
   pricing"). No contato, pedir:
   - se é permitido o uso num app desktop gratuito e não comercial com proxy e cache;
   - limites, preço e atribuição exigida;
   - o contrato real da API (host `v1`, IDs, autenticação, formato);
   - um endpoint para mapear nomes de CPU, GPU e jogo para IDs;
   - termos por escrito.

   Combinado com a alternativa 2, é a solução mais robusta.
4. **Sem API externa:** reforçar o "MEDIDO NESTE PC" (MangoHud) e, no futuro, uma base comunitária própria e opcional, com
   consentimento. Não depende de terceiros.

**Recomendação:** não embutir chave no app. Contatar a HowManyFPS (alternativa 3), corrigir o cliente conforme a documentação
recebida e, se autorizado, usar o proxy com cache (alternativa 2). Até lá, manter a chave do usuário como opcional e marcada
como não testada.

### Fontes

- https://github.com/psygreg/linuxtoys (README, `LICENSE`, `p3/scripts/**`, `p3/libs/optimizers.bash`, `p3/libs/sysd.bash`,
  `p3/app/revert_helper.py`, `p3/libs/lang/en.json`, `resources/70-linuxtoys-settings.conf`), lidos via `gh api`
- https://github.com/psygreg/shader-booster (`patch-mesa`, `patch-nvidia`; GPL-3.0)
- https://github.com/CachyOS/CachyOS-Settings (`60-ioschedulers.rules`, `70-cachyos-settings.conf`; GPL-3.0)
- https://howmanyfps.com/developer-api e https://be.howmanyfps.com/developer-api (HTTP 403 na leitura direta; conteúdo só pelos
  trechos de busca)
- https://howmanyfps.com/api (HTTP 403)
- https://be.howmanyfps.com/affiliate-disclosure (trecho de busca)
- Ilmenyx: `README.md`, `docs/CONFIABILIDADE.md`, `docs/fps-estimativa.md`, `src/Ilmenyx.Core/HowManyFpsClient.cs`,
  `src/Ilmenyx.Core/OptimizerLogic.cs` (só leitura)
