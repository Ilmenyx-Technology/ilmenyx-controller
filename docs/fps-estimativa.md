# Estimativa de FPS — metodologia e limites

Página **Estimativa de FPS** (JOGOS). Objetivo: dar ao usuário uma expectativa realista do FPS no hardware dele, sem apresentar
um palpite como se fosse medição. Código: `src/Ilmenyx.Core/GameCatalog.cs`, `FpsAccuracy.cs`, `HowManyFpsClient.cs` (`GameFpsRow`),
`src/Ilmenyx.Desktop/GamesFpsPage.cs`; sessões medidas: `LinuxGameLauncher.RunAsync` e `MangoHudLogReader.ReadSession`.

Cada cartão mostra exatamente uma de quatro fontes, nesta ordem de prioridade:

| Fonte no cartão | De onde vem | Como aparece |
|---|---|---|
| **MEDIDO NESTE PC** | Sessão real registrada pelo MangoHud ao jogar pela página Jogos | `47 FPS`, `1% low 31`, `última sessão dd/MM · 42 min` (borda verde) |
| **HOWMANYFPS** | API externa, só com chave e após confirmação | FPS médio e 1% low medidos por outros jogadores |
| **ESTIMATIVA** | Cálculo local pelas tabelas de referência | Faixa `38–52 FPS` + `confiança média/baixa/muito baixa` |
| **SEM ESTIMATIVA** | GPU ou CPU fora das tabelas | `—`; nenhum número é inventado |

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

- Tabelas de índice e `GpuRef`/`CpuCap` são aproximadas e não foram validadas contra FPS real neste projeto.
- A estimativa assume o driver proprietário/adequado, RAM em dois canais e o notebook na tomada.
- O 1% low medido vem de amostras de 100 ms (ver acima). A GPU registrada é a que o MangoHud reporta; não foi conferido qual nome ele
  escreve no nouveau ou com PRIME offload.
- Páginas diferentes guardam o mesmo `settings.json`; a página de FPS relê as sessões ao abrir e ao salvar a chave, mas outras gravações
  concorrentes do arquivo (pré-existentes) podem sobrescrever campos de outras páginas.
- O caminho real de sessão (jogo + MangoHud + leitura ao fechar) **não foi testado** com um jogo; ver `Sem_teste.md` (FPS-02).
