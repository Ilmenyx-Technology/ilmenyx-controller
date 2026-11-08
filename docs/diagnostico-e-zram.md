# "Jogo não abre?" e ZRAM (2026-11-08)

Duas funções inspiradas na pesquisa `docs/pesquisa-linuxtoys-howmanyfps-2026-11-07.md`. Só as **ideias** e fatos técnicos públicos
(nomes de sysctl, de pacotes e de arquivos de configuração) vieram dessa pesquisa; nenhum código, script ou configuração do LinuxToys
(GPL-3.0) ou da CachyOS foi copiado ou baixado. Seguem o modelo detectar → explicar → confirmar → aplicar → desfazer.

## A. "Jogo não abre?" (menu JOGOS)

Página só de leitura (`GameCheckPage`, lógica em `src/Ilmenyx.Core/GameLaunchCheck.cs`, leitura em
`src/Ilmenyx.Platform.Linux/GameLaunchProbe.cs`). Verifica ao abrir e com VERIFICAR. Os itens que não se aplicam ao sistema não aparecem;
os com PROBLEMA e ATENÇÃO vêm primeiro. Cada item mostra: estado, o que foi achado, explicação, risco e os comandos para copiar.

| Item | Onde lê | Quando aparece | Correção por clique |
|---|---|---|---|
| AppArmor: restrição de namespaces | `/proc/sys/kernel/apparmor_restrict_unprivileged_userns`; perfis em `/etc/apparmor.d` com bwrap/flatpak/steam/pressure-vessel no nome ou que citam bwrap + userns | kernel com o sysctl (Ubuntu 23.10+/24.04+ e derivadas) | **Nunca.** Comando para criar um perfil só do `/usr/bin/bwrap` (`/etc/apparmor.d/ilmenyx-bwrap`), desfazer, e a alternativa temporária por sysctl, com aviso de segurança |
| Namespaces de usuário | `kernel.unprivileged_userns_clone` (kernels Debian), `user.max_user_namespaces` | quando algum existe | Nunca (copiar; aviso) |
| SELinux execmod | `/sys/fs/selinux/enforce`, `booleans/selinuxuser_execmod` | SELinux em enforcing (openSUSE Tumbleweed, Fedora) | Nunca (`setsebool`, com aviso) |
| Multilib | `/etc/pacman.conf` | Arch, CachyOS, Manjaro, EndeavourOS… | Nunca (editar o arquivo e `pacman -Syu`) |
| RPM Fusion | `/etc/yum.repos.d/rpmfusion-{free,nonfree}.repo` (`enabled=`) | Fedora | Nunca (comando oficial do rpmfusion.org) |
| Flathub | `flatpak remotes --system/--user --columns=name,url,options` | sempre (Flatpak ausente = informação) | **Sim, só no usuário:** `flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo` ou `flatpak remote-modify --user --enable flathub`. Sistema: só o comando com sudo |
| Steam Flatpak e discos | `flatpak info --show-permissions` + `flatpak override [--user] --show` de `com.valvesoftware.Steam`; montagens reais em `/mnt`, `/media`, `/run/media` e bibliotecas do `libraryfolders.vdf` fora da pasta pessoal | Steam Flatpak instalada | **Sim:** `flatpak override --user --filesystem=<pasta> com.valvesoftware.Steam`; DESFAZER: `--nofilesystem=<pasta>` |
| Vulkan da placa | driver ligado a cada `/sys/class/drm/cardN`; `library_path` dos ICDs em `/usr/share/vulkan/icd.d`, `/etc/vulkan/icd.d`, `/usr/local/share/vulkan/icd.d`; `vulkaninfo --summary` se instalado | sempre | Nunca (pacote da distribuição; NVIDIA aponta para a aba Driver NVIDIA) |
| Bibliotecas de 32 bits | `libvulkan.so.1`, `libGL.so.1` e o ICD do driver em `/usr/lib/i386-linux-gnu` (Debian/Ubuntu), `/usr/lib32` (Arch) ou `/usr/lib` (Fedora/openSUSE) | Steam nativa instalada (`steam` nas pastas de programas) | Nunca |
| vm.max_map_count | `/proc/sys/vm/max_map_count` | sempre | Não duplica: botão ABRIR OTIMIZA |
| Split-lock | `/proc/sys/kernel/split_lock_mitigate` | kernel com o sysctl | Nunca (só informação e comando temporário) |

Regras de segurança:

- Os únicos comandos que o app roda são `flatpak` com argumentos validados por `GameLaunchChecks.IsAllowedFix` (lista fixa; caminho
  absoluto sem `;` `:` `!` `=` aspas ou `..`; sempre `--user`; só o app `com.valvesoftware.Steam`). Sem shell, sem root, sempre após
  confirmação que mostra o comando. `GameUserFixRunner` recusa qualquer outra coisa antes de iniciar um processo.
- AppArmor, SELinux, namespaces e split-lock reduzem proteções: só explicação, comando para copiar e aviso em destaque.
- Leitura: só arquivos e programas de consulta (`flatpak remotes/info/override --show`, `vulkaninfo --summary`). Nenhum acesso à
  internet na verificação.

## D. ZRAM (página Otimiza, cartão MEMÓRIA COMPRIMIDA)

Lógica em `src/Ilmenyx.Core/ZramPlan.cs`, leitura em `ZramProbe` (`GameLaunchProbe.cs`), interface em `ZramPanel.cs`.

Detecção (só leitura): `/proc/meminfo` (MemTotal), `/proc/swaps`, `/sys/block/zram*/{disksize,comp_algorithm}` (o `zramctl` só é
usado se o sysfs não mostrar nada), `/etc/systemd/zram-generator.conf`, `/usr/lib/systemd/zram-generator.conf` (padrão do Fedora),
pastas `zram-generator.conf.d`, `/etc/default/zramswap`, binários/serviços do zram-generator, zram-tools e zram-config, e o estado
`/var/lib/ilmenyx/zram.state`.

| Situação | O que o app faz |
|---|---|
| Já há `/dev/zram*` em `/proc/swaps` | "ZRAM já está ativo"; nada a fazer (se a configuração é do Ilmenyx, oferece DESFAZER) |
| Configuração de outro programa (`/etc/systemd/zram-generator.conf` sem a marca, complementos `.conf.d`, pacote zram-config do Ubuntu) | Explica e não mexe |
| RAM acima de 16 GiB | "Não é necessário"; sem ação |
| Distribuição fora das famílias conhecidas | Sem ação |
| RAM ≤ 16 GiB e nada ativo | Recomenda LIGAR ZRAM |

Aplicação (script fixo via `pkexec`, o mesmo executor da aba Driver NVIDIA, só com valores constantes e o tamanho calculado):

- **Fedora, Arch/CachyOS/Manjaro, openSUSE:** instala `zram-generator` se faltar (`dnf install -y`, `pacman -S --needed --noconfirm`,
  `zypper --non-interactive install`), grava `/etc/systemd/zram-generator.conf` com a marca `# Ilmenyx-ZRAM`, `[zram0]`,
  `zram-size = min(ram / 2, 8192)`, `compression-algorithm = zstd`, `swap-priority = 100`, e roda `systemctl daemon-reload` e
  `systemctl start dev-zram0.swap`. Aborta se o arquivo já existir sem a marca.
- **Ubuntu/Mint, Debian:** instala `zram-tools` se faltar (`DEBIAN_FRONTEND=noninteractive apt-get -y install`, com `apt-get update` só
  se a primeira tentativa falhar), guarda o `/etc/default/zramswap` original em `/etc/default/zramswap.ilmenyx-bak`, grava `ALGO=zstd`,
  `SIZE=<min(RAM/2, 8192) MiB>`, `PRIORITY=100`, e roda `systemctl enable` + `restart zramswap.service`. Aborta se o zram-config estiver
  instalado.
- Os dois scripts abortam sem mudar nada se já houver ZRAM ativo, anotam `package=<nome>` em `/var/lib/ilmenyx/zram.state` só quando o
  Ilmenyx instalou o pacote e conferem `/proc/swaps` no fim. A interface relê o sistema e só diz "conferido" se o estado bater.

Desfazer: DESFAZER desliga o ZRAM do Ilmenyx (`swapoff` / `systemctl stop`; se faltar RAM, aborta sem mudar nada), apaga o arquivo do
Ilmenyx (zram-generator) ou devolve o original (zram-tools) e desativa o serviço; o pacote fica. DESFAZER E REMOVER PACOTE só aparece se o
estado diz que o Ilmenyx instalou o pacote, e o remove (`apt-get -y purge`, `dnf remove -y`, `pacman -R --noconfirm`,
`zypper --non-interactive remove`). Os dois só agem se o arquivo tiver a marca do Ilmenyx.

## CLI (somente leitura)

- `ilmenyx game-check`: a lista da página em texto.
- `ilmenyx zram status` / `ilmenyx zram plan`: diagnóstico e plano (com os scripts). `ilmenyx zram script apply|undo|undo-remove` só
  **imprime** o script da ação do plano atual (para teste numa VM descartável). Nenhum deles executa nada.

## Testes

- `tests/Ilmenyx.Tests/GameCheckZramTests.cs`: famílias por `/etc/os-release` (Mint, Ubuntu, Debian, Fedora, Arch, CachyOS, Manjaro,
  Tumbleweed, Gentoo); parsers (pacman.conf, .repo, remotes, permissões do Flatpak, vulkaninfo, /proc/swaps, meminfo); lista
  permitida de comandos (inclusive tentativas de injeção); cada verificação por família; sonda sobre árvore de fixtures (com
  link simbólico de driver DRM) e só chamadas de leitura; plano de ZRAM de cada família, scripts conferidos com `sh -n`; DESFAZER
  só para a configuração do Ilmenyx; CLI.
- Smoke (`GameCheckAndZramAsync`): página real lida e capturada (escuro/claro, 900x560); página com fatos falsos (Ubuntu 24.04 com
  AppArmor, sem Flathub, Steam Flatpak sem acesso a `/mnt/jogos`): sem botão para AppArmor/32 bits/split-lock, Flathub e acesso ao
  disco só após confirmação (recusar = nada roda), DESFAZER, link para o Otimiza; cartão de ZRAM com executor falso
  (recomendado → confirmado → ativo com desfazer/remover pacote → desfeito); cartão real do Otimiza capturado.

## Resultado nesta máquina (Mint 22.3, Dell Inspiron 7460, 15,5 GiB, swapfile de 2 GiB)

- "Jogo não abre?": 7 itens, nenhum problema (AppArmor com a restrição desligada, namespaces permitidos, Flathub do sistema e do
  usuário ok, Vulkan Intel + NVIDIA presentes, 32 bits presentes para a Steam nativa, max_map_count 1048576, split-lock ligado =
  informação).
- ZRAM: "Recomendado" (zram-tools, 7945 MiB). **Não aplicado** (decisão do dono).

## O que falta (NÃO TESTADO) e como testar numa VM

Aplicar e desfazer o ZRAM de verdade, os botões do Flatpak num sistema real e as outras distribuições não foram executados. A máquina
tem `/dev/kvm`, mas não tem QEMU. Para testar sem mexer neste PC:

1. O dono instala (pede senha): `sudo apt install qemu-system-x86 qemu-utils cloud-image-utils` (Mint/Ubuntu).
2. Gera o pacote: `bash scripts/package-linux-x64.sh /caminho/novo` (sem root).
3. Para cada distro: `bash scripts/vm-distro-check.sh ubuntu /caminho/novo/Ilmenyx-linux-x64 /saida-nova-ubuntu --apply`
   (também `debian`, `fedora`, `arch`, `opensuse`). O script baixa a imagem oficial de nuvem, sobe a VM com 4 GiB de RAM (disco de
   sobreposição; nada muda no PC), roda `game-check` e `zram plan`, aplica o ZRAM **dentro da VM**, reinicia, confere e desfaz.
   O resultado fica em `session.txt`. As URLs das imagens precisam ser conferidas antes (Fedora muda o nome a cada versão);
   `ILMENYX_VM_IMAGE_URL` substitui.
4. Esperado: Ubuntu/Debian recomendam zram-tools; Fedora já vem com ZRAM ativo (zram-generator-defaults) e deve mostrar "já está
   ativo"; Arch e openSUSE recomendam zram-generator. Ubuntu 24.04 deve mostrar a restrição do AppArmor ligada.

O script `vm-distro-check.sh` também é NÃO TESTADO (escrito sem QEMU disponível).

Evidências: `docs/evidencias/diagnostico-zram-2026-11-08/` (capturas do smoke, saída da CLI neste Mint, linhas PASS do smoke).
