# Estimativa de FPS — metodologia e limites

Página **Estimativa de FPS** (JOGOS). Objetivo: dar ao usuário uma expectativa realista do FPS no hardware dele, sem apresentar
um palpite como se fosse medição. Código: `src/Ilmenyx.Core/GameCatalog.cs`, `FpsAccuracy.cs`, `FpsCalibration.cs`, `GameFpsRow.cs`,
`src/Ilmenyx.Desktop/GamesFpsPage.cs`; sessões medidas: `LinuxGameLauncher.RunAsync` e `MangoHudLogReader.ReadSession`.

Tudo é calculado no PC, sem internet. Desde 2026-11-08 a página tem uma **trava de calibração** (seção 0). Cada cartão mostra
exatamente uma destas fontes, nesta ordem de prioridade:

| Fonte no cartão | De onde vem | Como aparece |
|---|---|---|
| **MEDIDO NESTE PC** | Sessão real registrada pelo MangoHud ao jogar pela página Jogos | `47 FPS`, `1% low 31`, `última sessão dd/MM · 42 min` (borda verde) |
| **ESTIMATIVA** | PC calibrado: cálculo local com a base indicada no cartão | Faixa `38–52 FPS` + `confiança média/baixa/muito baixa` + `base: benchmark deste PC` / `especificações` / `tabela de referência` (+ `suas N sessões medidas`) |
| **PROVISÓRIO** | PC ainda não calibrado (ou calibrado com outro hardware/driver) | Faixa em cinza e itálico, sem nota (JOGÁVEL etc.), "sem confiança" e `provisório — calibre o PC` |
| **SEM ESTIMATIVA** | GPU ou CPU fora das tabelas | `—`; nenhum número é inventado |

## 0. Trava de calibração (método C)

Até o PC ser calibrado, o topo da página diz **"As estimativas ainda não são confiáveis: este PC não foi calibrado"** com o botão
**CALIBRAR AGORA (≈1 min)**. A calibração (após confirmação) roda:

- **GPU:** a cena Vulkan do Ilmenyx (`GpuScene`) por **15 s** em 1920×1080, sombras 4096, 40 000 árvores — as mesmas opções das cenas
  em `docs/evidencias` — no adaptador Vulkan com o mesmo ID PCI da GPU da estimativa (senão a dedicada). Nota da cena → índice de GPU.
- **CPU:** `GameSim` (30 000 entidades) em **uma thread** por **10 s** (+2 s de aquecimento). FPS da simulação → índice de CPU.
- **Calor:** um verificador a cada 1 s lê a CPU (coretemp/k10temp) e a GPU; em **90 °C** cancela tudo e **nada é salvo**. Se o PC já
  está a 90 °C ou mais, a calibração nem começa.
- **Guardado** em `settings.json` (0600), campo `FpsCalibration`: data, impressão digital (ID PCI da GPU, módulo e versão do driver,
  nome curto da CPU), adaptador, nota da cena e FPS da simulação. Se a impressão digital mudar (troca de GPU/CPU, atualização do driver
  NVIDIA, troca nouveau ↔ nvidia), a página volta a **PROVISÓRIO** e pede **RECALIBRAR**. Limite: para `amdgpu`/`i915` a versão do
  módulo é a do kernel; atualizar só o Mesa **não** pede recalibração.
- Se a GPU não puder ser medida (ex.: 940MX com nouveau, sem Vulkan), salva só a CPU e a GPU usa a tabela ou as especificações.

**Âncoras da conversão (dados próprios da equipe):**

| Medida | Pontos | Ajuste |
|---|---|---|
| Cena → índice de GPU | 940MX 172 → 4,5; HD 620 109 → 2,5; Radeon 610M 150 → 3 (`docs/evidencias/gpu-nvidia-2026-11-07`, `gpu-amd-2026-11-06`) | índice = 0,0229 × nota (inclinação fixa em 1: a cena é trabalho fixo; ajuste livre dá 1,16 com só três pontos próximos). Conferência: RTX 4070 ≈ 4370 pontos ≈ 25× a 940MX; razão de FP32 = 31× |
| GameSim 1 thread → índice de CPU | i7-7500U (índice 36): 22,09 / 22,06 / 21,33 FPS em 3 execuções de 10 s, 2026-11-08, Dell 7460 na tomada | índice = 36/22,06 × FPS. **Um só ponto**: CPUs muito diferentes podem errar mais |

Confiança depois de calibrar: GPU medida = **baixa**, ±25 % + distância à referência (a cena já inclui limite de energia e RAM do
notebook, mas é uma cena sintética); CPU medida tira o motivo "CPU de notebook". Muito longe da RTX 4070 (índice < ~9) continua
**muito baixa**.

## 0.1 Fallback por especificações (método B)

Se a GPU não está na tabela nem foi medida, mas o ID PCI está no `GpuSpecDb`: índice = k_arquitetura × TFLOPS^0,703 (TFLOPS = shaders
× 2 × clock de boost; o clock máximo do driver substitui o boost só quando é **menor**, como em notebooks). β e k ajustados por mínimos
quadrados (log) às 29 GPUs que estão nas duas tabelas: erro ≤ 9 %. Maxwell e RDNA 2 têm um ponto cada. Confiança **muito baixa**, ±45 %.
Base no cartão: `especificações`. Sem tabela, sem especificações e sem calibração → SEM ESTIMATIVA.

## 0.2 Ajuste pessoal pelas sessões medidas (método D)

O perfil do jogo (página Jogos) ganhou **"Qualidade usada dentro do jogo"**: qualidade gráfica (Baixo/Médio/Alto/Ultra) e upscaling.
O Ilmenyx **não** aplica isso ao jogo; só grava com cada sessão MangoHud (campos `Preset` e `Upscaling` de `FpsSession`).

Com o PC calibrado, para a qualidade escolhida na página: em cada jogo do catálogo, a sessão mais nova **com a mesma qualidade**, com
resolução conhecida e da mesma GPU é comparada com a estimativa nas configurações da própria sessão. Só entram sessões cujo gargalo
estimado é o mesmo do cartão (GPU com GPU, CPU com CPU; trava do motor não entra).
`fator = exp(mediana(ln(medido/estimado)) × n/(n+2))`, limitado a 0,5…2 (uma sessão move só 1/3 do caminho). Com **3 ou mais**
sessões a confiança sobe um nível e a faixa fica 20 % mais estreita. Cartão: `base: benchmark deste PC · suas N sessões medidas`.

## 0.3 Jogos leves: expoente de GPU (auditoria de 2026-11-08)

Problema relatado pelo mantenedor: nesta máquina (i7-7500U + GeForce 940MX, driver 580) a página dizia **League of Legends 16–44 FPS
no Baixo**; o mantenedor observa **> 100 FPS** nesse hardware. Causa: o FPS da GPU era `GpuRef × índice/100`, proporcional. O índice
de GPU vem de jogos pesados; jogos leves caem muito menos em GPUs fracas. Correção: `FPS da GPU = GpuRef × (índice/100)^GpuExp`.
Na GPU de referência (índice 100) nada muda; jogos pesados continuam com expoente 1.

| Jogo | GpuExp | Fonte (lida em 2026-11-08) e conta |
|---|---|---|
| Valorant | 0,73 | Riot, [requisitos](https://support.riotgames.com/en-us/valorant/support-tools/minimum-recommended-pc-specs): 30 FPS Intel HD 4000, 60 FPS GT 730, 144 FPS GTX 1050 Ti. Índices: HD 4000 ≈ 1,8 e GT 730 ≈ 3,3 pelas especificações (16 EU × 1150 MHz contra a HD 620 = 2,5; 384 núcleos × 902 MHz contra a 940MX = 4,5), GTX 1050 Ti = 18 (tabela). Ajuste de mínimos quadrados em log com a âncora RTX 4070 = 600; **suposto Alto/1080p** (a Riot não diz). Resultado: 32 / 50 / 172 FPS contra 30 / 60 / 144 |
| League of Legends | 0,55 | Riot, [requisitos](https://support.riotgames.com/en-us/league-of-legends/performance/minimum-and-recommended-system-requirements-league-of-legends): recomendado Intel UHD 630 (índice 3) em **Alto, 1920x1080**. **Suposto** "recomendado" = 60 FPS (a Riot não diz FPS). Conferência: 940MX no Baixo 1080p = 122 FPS (observação do mantenedor: > 100); mínimo HD 4600 em Baixo/1024x768 ≈ 80 FPS |
| Dota 2, Rocket League, Minecraft | 0,64 | **Sem fonte oficial com FPS** para estes jogos (os requisitos da Epic para Rocket League — GTX 760 / GTX 1060 — não dizem FPS). Usada a média dos dois expoentes acima, por serem jogos leves do mesmo tipo. Marcado como aproximação |
| CS2, Fortnite e os demais | 1 | Sem fonte permitida para mudar; ficam como estavam. Fortnite tem o modo Desempenho (outro renderizador), que o catálogo não modela |

**Índices auditados desta máquina** (sem mudança): 940MX = 4,5 (especificação: 0,95 TFLOPS, o modelo B dá 4,5; a cena dá 3,94);
HD 620 = 2,5 (âncora da cena); i7-7500U = 36 (âncora do GameSim). A razão 940MX/HD 620 pela cena (1,58) fica a 12 % da tabela (1,8).
Resolução 1280x720 passou a ter fator próprio (×1,59, mesma curva do 1440p) para as sessões gravadas em 720p.

## 0.4 Curvas medidas por jogo e por preset (2026-11-08)

O modelo antigo usava um valor de referência por jogo (RTX 4070, 1080p Alto) e um fator fixo por preset (Baixo ×1,6, Médio ×1,3,
Ultra ×0,82) igual para todos os jogos. Comparado com benchmarks reais, ele errava em média **51 %** (até ~170 % em Fortnite e
~110 % em GTA V, The Witcher 3 e Elden Ring), principalmente em GPUs fracas e no preset Baixo (ex.: CS2 numa 940MX: o modelo dizia
~21 FPS no Baixo; GPUs da mesma classe medem 50–75).

Agora, para 29 jogos do catálogo, a FPS limitada pela GPU em 1920×1080 vem de uma curva por preset ajustada a medições:
`FPS = A × (índice/100)^b` (classe `PresetCurves` em `GameCatalog.cs`).

- **Fonte:** tabelas de benchmark por jogo do Notebookcheck (notebookcheck.net, páginas "… laptop and desktop benchmarks"),
  consultadas em 2026-11-08: dezenas de GPUs de desktop, notebook e integradas por jogo. Só os parâmetros ajustados ficam no código;
  as tabelas não são redistribuídas.
- **Ajuste:** mínimos quadrados em log, só com pontos abaixo de 80 % do maior valor da coluna (o platô é limite de CPU ou trava do
  jogo); colunas de 720p/768p de páginas antigas convertidas para 1080p pela curva de resolução (FPS ∝ pixels^−0,57). Um preset
  mais leve nunca fica mais lento que o mais pesado (o maior dos dois vale).
- **Resultado:** nos 2 775 pontos medidos, o erro mediano caiu de 51 % para **17 %**.
- **Casos especiais:** Fortnite (tabela da versão de 2018: usa só o formato da curva, ancorado no nível atual do catálogo);
  Warzone (curvas do Black Ops 6, mesmo motor, ×130/140); Overwatch 2 usa a tabela do Overwatch (mesmo motor).
  League of Legends, Marvel Rivals e Minecraft não têm tabela utilizável e seguem o modelo antigo.
- **Páginas antigas** (R6 2015, PUBG 2017, Rocket League 2017, Dota 2 2017, The Witcher 3 2015): o jogo pode ter ficado mais
  pesado desde então; a faixa de confiança continua valendo.
- **Próximo passo possível:** o mesmo conjunto de dados permite reajustar a tabela de índices de GPU (ajuste conjunto: erro 17 % → 12 %),
  mas isso muda a calibração (SceneK) e o método por especificações; fica para uma revisão separada.

## 1. Medido neste PC

- Só sessões iniciadas pelo botão JOGAR da página Jogos com **MangoHud** ligado no perfil, usando a configuração do MangoHud gerada
  pela página Overlay (é ela que grava o CSV em `~/.local/share/ilmenyx/mangohud`; o `MangoHud.conf` do usuário não é tocado).
  Jogos da Steam precisam das opções de inicialização gravadas pela página Jogos (APLICAR NAS OPÇÕES DA STEAM) para o MangoHud ser carregado.
- Ao fechar o jogo, o Launcher lê os CSV dessa pasta escritos desde o início da sessão; se houver mais de um (ex.: janela do launcher
  do jogo + o jogo), vale o que tem mais amostras. Logs acima de 32 MB são lidos pelo cabeçalho e pelos últimos 32 MB.
- **FPS médio** = média da coluna `fps` (as linhas saem a intervalos fixos de 100 ms, então é quadros/tempo).
  **1% low** = 1000 / média do pior 1 % dos `frametime` registrados — o mesmo método do overlay ao vivo. Como cada linha resume
  100 ms, não é o 1% low de quadros individuais: engasgos de um único quadro ficam suavizados (o valor tende a ser otimista).
- Sessões com **menos de 30 s** ou **sem amostras válidas** não contam.
- Guardado em `~/.config/ilmenyx/settings.json` (arquivo 0600), campo `FpsSessions`, no máximo **50 sessões** (as mais novas).
  Campos: nome do jogo como está na biblioteca, data, FPS médio, 1% low, duração, nome da GPU que o MangoHud escreveu no cabeçalho do
  log, resolução do perfil (se definida; o MangoHud não registra a resolução). **Não** são guardados nomes de arquivos de log,
  nomes de processos, CPU, kernel nem nada de outros aplicativos. Nada sai do PC.
- Casamento com o catálogo: nome normalizado (minúsculas, só letras/dígitos ASCII, sem acentos/™/®/pontuação) e alguns apelidos
  de loja (ex.: "Grand Theft Auto V Enhanced" → GTA V, "Tom Clancy's Rainbow Six Siege" → Rainbow Six Siege). Não há casamento
  parcial ("Minecraft Dungeons" não vira "Minecraft").
- O cartão mostra a **última** sessão do jogo. O valor vale para as configurações usadas naquela sessão, não para as opções
  Resolução/Gráficos/Upscaling da página — por isso ele não muda ao trocar essas opções.

## 2. Estimativa local: faixa + confiança

Valor central (inalterado): `min(GpuRef × índiceGPU/100 × fatores de resolução/preset/upscaling, CpuCap × índiceCPU/100)`, limitado pela
trava do motor do jogo. Referência: RTX 4070 = 100 e Ryzen 7 7700X = 100; `GpuRef`/`CpuCap` são aproximações de benchmarks públicos em
1080p/Alto. O cartão nunca mostra esse número sozinho: mostra `centro × (1 ± spread)`, arredondado, mínimo 1 e nunca acima da trava do motor.

| Situação | Confiança | Spread base |
|---|---|---|
| GPU com chave de desktop e CPU de desktop | média | ±15 % |
| GPU de notebook (chave `Laptop`/`Mobile`/`MX…`/`…M`, ou nome PCI de chip móvel como `GM108M`) | baixa | ±25 % |
| GPU integrada (`HD/UHD Graphics N`, Iris, Vega, Radeon 7xxM etc.) | baixa | ±25 % |
| Chave genérica (`Radeon Graphics`, `UHD Graphics`) | baixa | ±35 % |
| CPU de notebook (sufixo U/H/HS/HX/HQ/P/Y/G7…) | baixa | ±25 % |

Distância da referência: soma `0,04 × |log2(índiceGPU/100)| + 0,03 × |log2(índiceCPU/100)|` ao spread (máx. ±50 %). Mais de 3,5
"dobras" da RTX 4070 (índice < ~9) rebaixa a confiança um nível (média → baixa, baixa → muito baixa), porque a escala linear por
índice erra mais em GPUs muito mais fracas. O texto da fonte explica os motivos ("GPU de notebook: o limite de energia/temperatura
do modelo muda o resultado" etc.).

Exemplo desta máquina (Dell Inspiron 7460, i7-7500U + GeForce 940MX): confiança **muito baixa**, spread ≈ ±47 %; CS2 1080p/Alto
aparece como **8–22 FPS**. Isso é uma estimativa; nenhum FPS real foi medido nesta máquina.

O spread não é um intervalo estatístico: é uma margem escolhida para comunicar a incerteza dos índices (versões do jogo e dos drivers,
cenas diferentes, limites de energia). Não há dados de validação suficientes para calibrá-lo.

## 3. Alertas (só com leitura local)

| Alerta | Condição (lida, nunca suposta) |
|---|---|
| Driver nouveau na GeForce | GPU NVIDIA com driver `nouveau`. Se a GPU é anterior à Turing (ID PCI < 0x1E00: Kepler/Maxwell/Pascal/Volta), diz também que em geral não há Vulkan (Proton/DXVK). Nesta máquina (Mesa 25.0, 940MX) o `vulkaninfo` só lista a HD 620 e o llvmpipe. |
| GPU sem driver | GPU dedicada sem módulo de driver carregado |
| X GB de VRAM | GPU dedicada com VRAM total conhecida < 4 GB (amdgpu pelo sysfs, NVIDIA pelo `nvidia-smi`). No nouveau a VRAM não é exposta, então o alerta **não aparece** (não há palpite de VRAM) |
| Notebook na bateria | bateria presente e `AC/online = 0`, ou bateria em `Discharging` (`LinuxPowerReader`) |
| Notebook com duas GPUs | a GPU da estimativa é dedicada e existe uma integrada: sem "Forçar a placa de vídeo dedicada (PRIME offload)" no perfil do jogo, o jogo roda na integrada. O texto diz quantos perfis têm PRIME |

## Limites conhecidos

- Tabelas de índice e `GpuRef`/`CpuCap` são aproximadas e não foram validadas contra FPS real neste projeto. A calibração mede a
  máquina com cenas sintéticas, não com jogos; o ajuste pessoal só existe depois de sessões reais com a qualidade informada.
- A calibração real (cena + GameSim pela página) só foi exercitada com um executor falso no smoke; a cena e o GameSim em si já rodaram
  pela CLI nesta máquina (ver âncoras).
- A estimativa assume o driver proprietário/adequado, RAM em dois canais e o notebook na tomada.
- O 1% low medido vem de amostras de 100 ms (ver acima). A GPU registrada é a que o MangoHud reporta; não foi conferido qual nome ele
  escreve no nouveau ou com PRIME offload.
- Páginas diferentes guardam o mesmo `settings.json`; a página de FPS relê as sessões ao abrir, mas outras gravações
  concorrentes do arquivo (pré-existentes) podem sobrescrever campos de outras páginas.
- O caminho real de sessão (jogo + MangoHud + leitura ao fechar) **não foi testado** com um jogo; ver `Sem_teste.md` (FPS-02).
- A consulta à API da HowManyFPS foi **removida** em 2026-11-08 (o acesso à API exige um pedido formal e não havia como testá-la).
  Opções para estimar melhor sem API paga ou fechada: [`fps-estimativa-metodos.md`](fps-estimativa-metodos.md).
