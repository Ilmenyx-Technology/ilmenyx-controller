# Runner GameSim completo experimental — 0.9.0

Implementado em C# Core e exposto somente pela CLI; a carga real de 30.000 entidades NÃO FOI
EXECUTADA neste incremento. Build, lógica de controle com engine/relógio de teste, entrada pública
pré-cancelada e regressões reduzidas Linux foram verificados. Não declarar aceite completo de
FPS/score real; SIM-01 permanece aberto em Sem_teste.md.

## Fonte e arquitetura

Consultados novamente GameSimBench.cs original integralmente na etapa anterior e Run/Reset/Step
nesta etapa, fonte MIT GustavoReset no SHA ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6;
README, LICENSE, csproj, AGENTS, metodologia/arquitetura e contratos/runners/CLI relacionados.
Original e seu histórico permanecem intactos. Sem nova dependência ou API específica de SO/UI.
Não há alegação clean-room estrita.

GameSimStateEngine agora aceita tamanho interno por instância. A validação continua fixa em
256; o adaptador GameSimTimedEngine usa 30.000. Mesmos métodos matemáticos, counting sort,
RNG/seed 12345, mundo 1000, grade 64, dt float 1/60 e lógica de respawn. Nenhum parâmetro público
permite tamanho arbitrário. Oracle reduzido independente e fixtures existentes permanecem iguais.

Contracts adiciona opções/resultados/report; Core contém engine e runner; CLI compõe o backend
Linux e coleta contexto antes de executar. Core/Contracts não dependem de Avalonia ou Windows.
Full não publica arrays/snapshots mutáveis nem atualiza prévia; a UI oferece somente o reduzido.

## Opt-in e limites

`bench sim` continua reduzido, sem score. `bench sim-plan` só prepara configuração, sem carga;
agora executionAvailable=true indica existência do runner experimental, não aprovação técnica.
O plano continua executionStatus=not-executed e score=null.

`bench sim-full` exige --allow-cpu-load antes de coletar hardware/alocar engine. Workers 1–64,
medição 1–10 s, warmup 2 s; timeout máximo 15 s e mínimo warmup+duração+1 s. Padrão de execução:
1 worker, medição 3 s, timeout 10 s. Para durações maiores, informar timeout suficiente.
Limite 200.000 frame times; se atingido, encerra sem métricas. List pode reservar capacidade
maior que o count ao crescer (até 320.000 doubles com capacidade inicial 20.000); não cresce
indefinidamente. Buffers da física são proporcionais às 30.000 entidades, fixos nesta modalidade.

Cancelamento/timeout são cooperativos antes/depois de cada Step completo, incluindo warmup,
sem abandonar Parallel.For em background. Precisão do timeout depende do término do passo/scheduler;
não promete interrupção instantânea. Cancelamento anterior à execução não cria engine completo.
Falha de worker propaga como erro da CLI, sem inventar resultado completo. Não há proteção térmica.

## Metodologia e diferenças

Warmup por relógio 2 s fora da medição: mundo/RNG avançam, câmera usa frame zero em cada passo,
como original. Depois não reiniciar posições/vida/RNG; contador medido começa em zero. Report
registra warmupSteps, warmupSeconds solicitado e actualWarmupSeconds observado, além de duração,
timeout, frames completos, tempo real medido, workers/chunks, runtime/arquitetura e hardware.

Cada passo mede parte principal (grade + swap/lógica/culling/sort) e trecho paralelo. Médias
são soma dos tempos dividida por frames medidos. FPS médio=frames/tempo medido; score experimental
é esse FPS. Low1=1000/sortedFrameMs[min(n-1,floor(n*0.99))], preservando o recíproco p99 original,
sem substituir por média do pior 1%. Ordenação estatística/coleta/JSON ficam fora do intervalo.

Versões: reset-gamesim-original-math-v1 / silex-gamesim-timed-v1, workload
reset-gamesim-30000-warmup2s. Diferenças de medição explícitas: paralelismo máximo limitado a workers
em vez de depender só do scheduler; timeout/checks e crescimento limitado de amostras; contador
por engine incrementado no retorno do Step (pequeno overhead incluído no frame externo); sem
atualizações CurrentFps/Progress a cada 500 ms. O loop mantém o elapsed total observado, incluindo
controle/armazenamento do runner. Não equiparar os scores Linux e Windows.

Status Completed publica métricas finitas. Cancelled, TimedOut, SampleLimitReached e InvalidTiming
suprimem AverageFps, Low1Fps, MainThreadMs, ParallelMs e ExperimentalScore. Mantêm contagens e tempo
observado como diagnóstico parcial. Duração de frame zero/não finita ou componente negativo/inválido
não produz divisão por zero/infinito. Resultados são schema 1.0 com contexto real separado do algoritmo.

## Verificações executadas

CachyOS x64/.NET SDK 10.0.100/runtime 10.0.0. Build Release sem avisos/erros; 261 testes passaram.
A suíte nunca inicia a física de 30.000 entidades. Engine e relógio de teste reproduzem warmup,
durações e deadlines, sem esperar segundos ou sustentar carga. Casos: FPS/p99 e médias, p99 com
200 frames diferente da média dos dois piores, warmup excluído e Reset único, pré-cancelamento
público sem alocação do engine, cancelamento em ambas etapas, timeout após Step, teto de 200.000
amostras e frame zero. Esses números de teste são fixtures de medição, jamais resultados de hardware.

CLI real: sem opt-in e parâmetros inválidos recusados exit 2; plano JSON real coincide com stdout,
executionAvailable=true mas score null; reduzido 1/2/4 ainda coincide com hash upstream
6815765e7f314653b328268725f02c597dc31bb5334670769ab3accfc772c880. UI Linux real em Wayland e
X11/XWayland preserva fluxos reduzidos, sensores e exportações; nenhuma entrada full adicionada.
Evidências phase2-gamesim-full-* em docs/evidencias; TRX, logs e capturas. Build/testes não
certificam execução full ou equivalência de Windows. A restrição anterior de stress permanece.

Comandos executados em Bash:

```bash
. scripts/env-local-sdk.sh
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase2-gamesim-full.trx' --results-directory docs/evidencias
dotnet build tools/Silex.Audit.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY SILEX_DISPLAY_BACKEND=wayland \
  dotnet tools/Silex.Audit.UiSmoke/bin/Release/net10.0/Silex.Audit.UiSmoke.dll /tmp/silex-full-wayland
SILEX_DISPLAY_BACKEND=x11 \
  dotnet tools/Silex.Audit.UiSmoke/bin/Release/net10.0/Silex.Audit.UiSmoke.dll /tmp/silex-full-x11
```

## Execução manual futura — não executada nesta tarefa

Somente após autorização específica de carga e revisão das condições da máquina, um exemplo curto:

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- \
  bench sim-full --allow-cpu-load --threads 1 --duration-seconds 1 --timeout-seconds 4 --json --output /tmp/silex-sim-full.json
```

Arquivo deve ser novo. Ctrl+C cancela. Exit codes: 0 Completed, 130 Cancelled, 124 TimedOut,
125 SampleLimitReached, 126 InvalidTiming, 2 argumentos inválidos, 1 falha I/O/worker.
Registrar relatório e comportamento de cancelamento/timeout reais antes de fechar SIM-01.
Windows, outras distros/ISAs e comparabilidade continuam em Sem_teste.md; sem sudo, push ou release.
