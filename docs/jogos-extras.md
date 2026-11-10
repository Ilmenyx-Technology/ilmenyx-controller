<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0 -->
<!-- SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology) -->
# Extras para jogos (2026-11-08)

Três recursos novos, inspirados na lista de ideias da pesquisa sobre o LinuxToys
(`docs/pesquisa-linuxtoys-howmanyfps-2026-11-07.md`). O LinuxToys é GPL-3.0: **nenhum código, script ou arquivo de configuração dele
foi copiado ou baixado**. Só foram usadas ideias e fatos técnicos públicos (nomes de variáveis e IDs do Flathub), conferidos na
documentação de cada projeto. Todos seguem o modelo do Ilmenyx: **detectar → explicar → confirmar → aplicar → desfazer**, sem root e
em qualquer distribuição.

## 1. Cache de shaders maior (página Otimiza)

| | |
|---|---|
| Detecta | se o arquivo existe e é do Ilmenyx, o valor de `MESA_SHADER_CACHE_MAX_SIZE` na sessão atual e o tamanho de `~/.cache/mesa_shader_cache*`, `~/.cache/nvidia/GLCache` e `<Steam>/steamapps/shadercache` (contagem limitada a 200 mil arquivos por pasta) |
| Aplica | grava `~/.config/environment.d/90-ilmenyx-shader-cache.conf` (modo 0644, escrita atômica) com `MESA_SHADER_CACHE_MAX_SIZE=10G` e `__GL_SHADER_DISK_CACHE_SIZE=10737418240` |
| Quando vale | no próximo login (o systemd do usuário lê `environment.d` ao abrir a sessão) e já nos jogos abertos pelo Ilmenyx (umu/manual), que recebem as mesmas variáveis enquanto a opção estiver ligada |
| Desfaz | desligar a opção ou RESTAURAR ORIGINAIS apaga o arquivo. Um arquivo com o mesmo nome que não foi criado pelo Ilmenyx (sem a primeira linha de marca) nunca é alterado nem apagado |

Fontes e decisões:

- Mesa (`docs.mesa3d.org/envvars.html`): `MESA_SHADER_CACHE_MAX_SIZE` aceita sufixo K/M/G; sem a variável, o limite é 1 GB.
- NVIDIA (README do driver 570/580, "OpenGL environment variable settings"): `__GL_SHADER_DISK_CACHE_SIZE` é um inteiro (bytes);
  o cache fica em `~/.cache/nvidia/GLCache`. O README avisa que um programa rodado **sem** a variável volta o cache ao tamanho
  padrão e o apaga se estiver maior; por isso o valor vai para a sessão inteira, e não só para um jogo.
- `__GL_SHADER_DISK_CACHE_SKIP_CLEANUP` **não** aparece no README da NVIDIA: não é usado.
- `AMD_VULKAN_ICD` (que o LinuxToys força para RADV) **nunca** é gravado: escolher o driver Vulkan não é papel desta opção.
- Sessões sem systemd do usuário, ou que não importam `environment.d`, podem ignorar o arquivo. Nesse caso o limite maior vale só
  nos jogos abertos pelo Ilmenyx. A página mostra se a variável já está em uso na sessão atual.

## 2. Mais apps de jogos (página Instalar apps)

IDs e licenças conferidos em `https://flathub.org/api/v2/appstream/<id>` em 2026-11-08. Nenhum vem pré-selecionado; instalação por
usuário (`flatpak install --user`). Os avisos aparecem na lista e de novo na confirmação.

| App | ID | Licença | Aviso mostrado |
|---|---|---|---|
| ProtonPlus | `com.vysp3r.ProtonPlus` | GPL-3.0-or-later | — |
| ProtonUp-Qt (já existia) | `net.davidotek.pupgui2` | GPL-3.0 | — |
| LACT | `io.github.ilya_zlobintsev.LACT` | MIT | Para clock/energia/ventoinha usa o serviço root `lactd`. A documentação do LACT (`flatpak/README.md`) diz que o próprio Flatpak oferece instalar o serviço fora do sandbox na primeira abertura (via Polkit); **não é preciso pacote nativo**. Só para monitorar, dá para recusar. NVIDIA precisa do driver proprietário. Comando para desfazer o serviço incluído |
| Gamescope (camada Vulkan para a Steam Flatpak) | `org.freedesktop.Platform.VulkanLayer.gamescope` | BSD-2-Clause | Só para a Steam em Flatpak. O app lê a base da Steam (`flatpak info --show-runtime com.valvesoftware.Steam`, ex.: `org.freedesktop.Platform/x86_64/25.08`) e instala o mesmo branch (`…gamescope//25.08`). Sem Steam Flatpak, o item fica desabilitado com o motivo. "Instalado" só conta com o branch igual |
| Sunshine | `dev.lizardbyte.app.Sunshine` | GPL-3.0-only | Rodar uma vez `flatpak run --command=additional-install.sh dev.lizardbyte.app.Sunshine` (exigido pela página do Flathub); certificado próprio na interface web |
| GeForce NOW | — (só link) | — | O app oficial da NVIDIA não está no Flathub (é distribuído pela NVIDIA: `.bin` e repositório Flatpak próprio). O Ilmenyx só abre `https://www.nvidia.com/en-us/geforce-now/download/`. Os "GeForce NOW" do Flathub são de terceiros |
| Piper | `org.freedesktop.Piper` | GPL-2.0-or-later | Precisa do `ratbagd` (libratbag) instalado e rodando no sistema |
| Solaar | `io.github.pwr_solaar.solaar` | GPL-2.0-only | Só Logitech; o Flatpak não instala a regra udev `42-logitech-unify-permissions.rules` |
| input-remapper | — (só link) | — | Não está no Flathub (usa serviço root); pacote `input-remapper` da distribuição. O app só abre `https://github.com/sezanzeb/input-remapper` |

A lista agora é agrupada em Essenciais, Jogos, Streaming e Periféricos e mostra o ID e a licença de cada item.

## 3. Opções experimentais por jogo (página Jogos)

Duas caixas novas no card DESEMPENHO, visíveis em jogos da Steam e jogos `.exe` (umu). **Desligadas por padrão**; ligar pede
confirmação com a explicação; desligar não pergunta. Embaixo aparece, para cada opção, "entra como VAR=1" ou o motivo de ficar de fora.
A opção fica guardada no perfil e só entra na linha quando as condições valem.

| Opção | Variável | Condições | Fonte |
|---|---|---|---|
| Proton nativo no Wayland (experimental) | `PROTON_ENABLE_WAYLAND=1` | sessão Wayland (`XDG_SESSION_TYPE=wayland` ou `WAYLAND_DISPLAY`) **e** GE-Proton ou UMU-Proton ≥ 10. O Proton oficial da Valve (`proton_*`) fica de fora: o README do Proton 10 da Valve não documenta a variável | README do GE-Proton (tabela de opções: `PROTON_USE_WAYLAND` ou `PROTON_ENABLE_WAYLAND` "Enables the Wayland driver"; seção HDR: não usar com o Gamescope) |
| Baixa latência (experimental) | `PROTON_ENABLE_NVAPI=1` | o jogo roda numa NVIDIA com driver proprietário (desktop com uma GPU, ou notebook com "GPU dedicada" ligada). Depois é preciso ligar o Reflex no menu do jogo | README do DXVK-NVAPI (encaminha o NVIDIA Reflex para DXVK/VKD3D-Proton); README do GE-Proton (`PROTON_ENABLE_NVAPI`). No Proton da Valve o NVAPI já vem ligado (só existe `PROTON_DISABLE_NVAPI`); a variável não atrapalha |

Qual Proton o jogo usa:

- Steam: `config/config.vdf` → `CompatToolMapping` do jogo, ou o padrão (`"0"`).
- Jogos `.exe`: a pasta `UMU-Proton-*` mais nova em `~/.local/share/Steam/compatibilitytools.d` (onde o umu baixa). Antes da
  primeira execução não há pasta: a opção Wayland fica de fora até o Proton ser baixado.

**AMD e Intel (baixa latência): não implementado.** O Proton não documenta uma opção para AMD/Intel. O DXVK-NVAPI diz que com outros
drivers ele precisa de `DXVK_NVAPI_ALLOW_OTHER_DRIVERS=1` e tem "uso muito limitado". O Anti-Lag 2/Reflex para Mesa só existe numa
camada Vulkan de terceiros (`low_latency_layer`, `LOW_LATENCY_LAYER=1`, citada no README do GE-Proton), que o usuário precisa instalar à
parte. O Ilmenyx não liga nada nesse caso e explica o motivo.

### Conflitos (mesma tabela da página Compatibilidade)

As duas opções passam por `LaunchOptionTriage.Conflict` contra as outras peças do perfil (variáveis de PRIME, proteção de tela cheia,
MangoHud, GameMode e argumentos). Regras novas na tabela (`GameCompatTriage.cs`):

- `PROTON_ENABLE_WAYLAND=1` e `PROTON_USE_WAYLAND=1` são a mesma chave: só uma entra.
- Wayland × `gamescope`: não combinam (o GE-Proton recomenda o XWayland padrão dentro do Gamescope).
- Regras que já existiam e agora valem para a baixa latência: `PROTON_USE_WINED3D=1` anula as opções NVAPI;
  `PROTON_DISABLE_NVAPI=1` e `PROTON_HIDE_NVIDIA_GPU=1` desfazem o NVAPI; uma variável só guarda um valor.

Na página Compatibilidade, se o perfil do jogo tem alguma opção experimental ligada, a Combinação recomendada mostra se ela combina ou
quais opções da recomendação brigam com ela.

Onde entram: na linha da Steam (APLICAR NAS OPÇÕES DA STEAM, antes dos wrappers e do `%command%`) e no ambiente do `umu-run` dos jogos
`.exe` adicionados à mão.

## Testes

- `tests/Ilmenyx.Tests/GameExtrasTests.cs`: arquivo `environment.d` em pasta temporária (conteúdo, modo, aplicar/desfazer, arquivo de
  terceiros intocado), tamanho dos caches, variáveis nos jogos manuais, classificação de nomes do Proton, condições e conflitos das
  opções experimentais, linha da Steam, `CompatToolMapping`, pasta do UMU-Proton, catálogo, branch da camada do Gamescope.
- Smoke `GamingExtrasAsync` (`tools/Ilmenyx.UiSmoke/Program.cs`): as três páginas com fixtures e flatpak falso, telas 900x560 nos
  temas escuro e claro.

NÃO TESTADO em uso real (registrado em `Sem_teste.md`): efeito do cache maior após novo login (GNOME/KDE/Cinnamon/sem systemd),
instalação real de cada app do Flathub, camada do Gamescope na Steam Flatpak, jogo real com `PROTON_ENABLE_WAYLAND=1`, Reflex numa NVIDIA.
