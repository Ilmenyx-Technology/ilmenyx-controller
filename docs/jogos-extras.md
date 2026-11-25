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

## Janela só de Jogos (atalho "Jogos — Ilmenyx Controller", `--games`, `--play CHAVE`)

Vitrine estilo console (Xbox/PS5). Barra de cima com "Ilmenyx **Jogos**" e abas em pílula (Jogos, Conquistas, Compatibilidade, Jogo não abre? e Meu PC roda isso?; a ativa em pílula branca `Palette.Fg` com texto `OnFg`, as outras em `Muted`, hover `Hover`;
setas esquerda/direita trocam de aba), o **relógio** (HH:mm na hora local, atualizado a cada virada de minuto até a janela fechar; sem indicadores de rede ou bateria) e, na página Jogos, a busca em pílula à direita dele (abaixo de ~1120 px ela desce para uma segunda linha); a barra é transparente sobre o fundo ambiente; cada página é criada ao ser aberta pela primeira vez.
A página Jogos usa o layout `LauncherLayout.Showcase` da mesma `LauncherPage` do app completo (que continua no layout clássico; toda a lógica é compartilhada), numa só rolagem:

- **Hero** (~45% da altura, mínimo 240 px, cresce se o texto quebrar): a arte mais larga do jogo escolhido (hero da Steam, header, capa própria, arte baixada; as mesmas fontes de `ArtFor`) preenchendo o banner, com um véu `Palette.Bg` com alfa (`HeroScrim`) à esquerda e embaixo,
  para o texto manter o contraste AA sobre qualquer imagem (o teste `HeroTextKeepsItsContrastOverAnyPicture` mede contra o véu sobre uma imagem toda branca e toda preta, não contra a imagem real). Sem arte: `Palette.Card` com as iniciais grandes.
  Nele: "Continuar jogando" (só no último jogado; senão a loja), o nome (30 px), loja · Proton/Wine, **Jogar** (único botão cheio: pílula `Palette.Fg` com texto `OnFg`, o botão branco do PS5; contraste de ~16:1; Abrindo…/Em execução, Fechar jogo e Parar acompanhamento como no app completo), **Configurações do jogo** e pílulas com dados reais
  (FPS típico medido de `FpsHistory`, conquistas lidas dos arquivos locais da Steam, tempo jogado); o que não é conhecido não aparece. Ao trocar de jogo a nova imagem surge sobre a anterior (220 ms).
- **Sua biblioteca**: filtros Todos / Steam / Windows (programas .exe com Proton/Wine) / Linux / Recentes (só quando algum jogo tem última jogada; `GameFilters`), capas largas 16:9 (190–250 px, colunas pelo espaço), nome e loja embaixo.
  Capa escolhida ou com o foco do teclado: cresce 6%, ganha anel branco (`Palette.Fg`) de 3 px separado da capa por 3 px de vão (o anel é desenhado fora da capa: tamanho e posição das capas não mudam) e mostra nome e loja em opacidade total (todas as capas mostram nome e loja; as demais com 70% de opacidade, pedido do mantenedor em 2026-11-24); hover: elevação de 3% e anel fraco (160 ms); a grade é uma só parada de Tab e as setas movem entre capas; clicar numa capa a leva ao hero. As ações da biblioteca (Atualizar lista, + Adicionar executável,
  Instalar jogo de Windows, Atalho dos Jogos, Mostrar ocultos) ficam no menu **Gerenciar**, ao lado do título.
- **Configurações do jogo**: gaveta à direita (400 px; largura toda abaixo de 1000 px) sobre a vitrine escurecida e desativada, com os cartões de sempre (Gerenciar o jogo no topo, logo abaixo do título, depois capa, desempenho medido, perfil, gráficos, qualidade, programas auxiliares). Esc, o botão ✕ ou um clique fora fecham;
  Tab fica preso dentro da gaveta e o foco volta ao botão. Os avisos "vale na próxima vez" continuam valendo com o jogo aberto.
- Ao abrir, o jogo escolhido é o de `LastPlayed` mais recente, senão o primeiro da biblioteca.
- **Fundo ambiente**: o fundo da janela ganha um tom escuro da arte do jogo escolhido, com transição de ~400 ms ao trocar de jogo. A cor vem de uma cópia da imagem do hero lida fora da thread de UI
  (cerca de 2.000 pixels amostrados, média ponderada pela saturação, `AmbientTint`/`GameArt.AverageColor`); depois a saturação é limitada a 0,32, a luminosidade vai para a faixa 0,07–0,17 (HSL) e é baixada até todo texto do tema
  (`Fg`, `Muted`, `Faint`) ter contraste WCAG ≥ 4,5:1 com ela (testado em 10 cores de arte, inclusive branco, preto e vermelho puro). Sem arte, ou no tema claro, vale `Palette.Bg`. Só a janela de Jogos usa o tom; o app completo não.

**Última jogada e tempo jogado** (`GameProfile.LastPlayed` e `PlayedSeconds`, em `settings.json`): só para o jogo que o Ilmenyx abriu e acompanhou. O início é quando o processo do jogo é visto e o tempo soma até o Ilmenyx parar de acompanhar
(o jogo fechou, Parar acompanhamento ou fechar a janela). Jogo aberto fora do Ilmenyx (direto pela Steam, menu do sistema) não entra na conta, nem o que foi jogado antes desta versão. O app completo grava os mesmos dados, mas só a janela de Jogos os mostra.
Fica de fora o Detector de gargalo (precisa do monitor de uso): a janela não inicia monitor, sensores nem ícone da bandeja. Em "Meu PC roda isso?" o modelo
da CPU e a RAM total vêm de `/proc/cpuinfo` e `/proc/meminfo` (somente leitura; vazio/0 se indisponível). Em "Jogo não abre?", os botões que levam a páginas só do app completo (Otimiza, Driver NVIDIA)
não aparecem nesta janela. Abrir um jogo por um atalho (`--play`) volta para a página Jogos; abrir a janela de novo pelo atalho "Jogos" a traz de volta na página em que estava.

## Testes

- `tests/Ilmenyx.Tests/GameExtrasTests.cs`: arquivo `environment.d` em pasta temporária (conteúdo, modo, aplicar/desfazer, arquivo de
  terceiros intocado), tamanho dos caches, variáveis nos jogos manuais, classificação de nomes do Proton, condições e conflitos das
  opções experimentais, linha da Steam, `CompatToolMapping`, pasta do UMU-Proton, catálogo, branch da camada do Gamescope.
- `tests/Ilmenyx.Tests/GamesShowcaseTests.cs`: classificação dos filtros (e Recentes/jogo mais recente), iniciais da capa sem arte, colunas/gaveta/altura do hero por largura, tempo jogado, contraste do texto do hero contra o véu
  (imagem toda branca e toda preta, tema escuro e claro), `RecordPlay` e campos desconhecidos do `settings.json` que sobrevivem a carregar e salvar. Smoke `GamesWindowAsync`: janela de Jogos com biblioteca de fixture
  (abas, hero do último jogado com pílulas, clique numa capa troca o hero, filtros e busca, gaveta com Tab preso/Esc/foco de volta/largura toda a 900 px, 900x560 e 150/200% de texto sem cortes, todas as abas).
- Smoke `GamingExtrasAsync` (`tools/Ilmenyx.UiSmoke/Program.cs`): as três páginas com fixtures e flatpak falso, telas 900x560 nos
  temas escuro e claro.

NÃO TESTADO em uso real (registrado em `Sem_teste.md`): efeito do cache maior após novo login (GNOME/KDE/Cinnamon/sem systemd),
instalação real de cada app do Flathub, camada do Gamescope na Steam Flatpak, jogo real com `PROTON_ENABLE_WAYLAND=1`, Reflex numa NVIDIA.

**Conquistas e Meu PC roda isso? (estilo troféu e medidores).** As duas páginas são as mesmas classes do app completo e valem nos dois lugares:

- *Conquistas*: uma prateleira de cartões, um por jogo (arte da capa própria, do cache local da Steam ou do cache de arte da página Compatibilidade, sem pedido novo; sem arte, iniciais), com um anel dourado (`Palette.Warn`) com a porcentagem sobre o canto da arte; o jogo escolhido ganha moldura branca.
  Abaixo, o cabeçalho do jogo: anel de progresso com a porcentagem no meio (nunca arredonda para 100% enquanto falta alguma), nome, "X de Y conquistas", **"última há N min/h/dias" só quando a fonte registrou horário de desbloqueio**
  (Steam: `AchievementTimes` no cache de estatísticas, quando existe; CODEX/RUNE: `UnlockTime`; Goldberg/GSE: `earned_time`; um desbloqueio sem horário não entra na conta, e sem nenhum horário a linha não aparece) e as pílulas "N obtidas" (troféu) e "N a obter" (cadeado).
  As conquistas são cartões numa grade que quebra em quantas colunas couberem: ícone de 64 px (o do jogo, ou um troféu sobre `WarnBg` quando não há ícone), nome, descrição e "Obtida em dd/MM" (com o ano se não for o corrente; "Obtida" quando a fonte não tem a data);
  as bloqueadas ficam mais apagadas, com cadeado (e o ícone cinza da Steam quando existe); as ocultas e ainda não obtidas mostram "Conquista secreta" e "Continue jogando para descobrir." (não há ação de revelar). A porcentagem de jogadores (raridade) que a página já mostrava nas bloqueadas continua; nada novo de rede.
- *Meu PC roda isso?*: bloco grande de veredito no alto (✓/≈/✗/?, rótulo grande e a frase do resumo), cor pelo resultado de `CanRunCompare`: Recomendado = `Success`; Mínimo = texto de destaque; Mínimo com algum item no limite = **No limite** (`Warn`); Abaixo do mínimo = `Error`; Sem dados = `Muted`. A lógica e as margens da comparação não mudaram.
  Cada componente numérico (processador, placa de vídeo, VRAM, RAM, armazenamento) tem um **medidor**: barra preenchida até o valor deste PC, marca cinza no mínimo e marca branca (escura no tema claro) no recomendado, com a legenda "Marca cinza = mínimo · branca = recomendado". Os valores vêm do próprio relatório
  (índice de desempenho de `HardwareIndex` para CPU/GPU, GB para RAM/VRAM/armazenamento); a escala vai de 0 até o maior valor conhecido + 15% (`RequirementMeter.Layout`), as posições ficam entre 0 e 1 e sem valor deste PC ou sem nenhum requisito a linha fica sem medidor, só com o texto "não deu para comparar".
  O status colorido e a confiança ("por modelo/especificação/estimado por família") continuam em cada cartão. A escolha do jogo é uma prateleira de capas (2:3, do cache local da Steam, uma parada de Tab, setas movem) dos jogos da biblioteca Steam, mais os "Consultados antes" em chips e a busca por texto, sem pedido novo.
