# Metodologia e validação dos benchmarks

BenchRenderer: 960×540, 1024 spp, buckets 32, profundidade 6; ordenação centro para fora,
seed uint(pixel)*9781 + 0x9E3779B9, xorshift32. Thread por worker, Interlocked no bucket/amostras,
cancelamento por linha; Stopwatch inclui criação/join de threads e preparação de cada passada.
Score = Samples / Seconds / 10000. onPass fica no trecho medido. Pixels expostos sem snapshot.
GameSimBench: 30000 entidades, mundo 1000, grade 64², dt 1/60, seed 12345; aquecimento 2 s,
Parallel.For em ProcessorCount*4 chunks, double buffer; score FPS médio, low1 por p99 de frametimes.
MemoryBench: latência 256 MiB, linhas 64 B, Sattolo Random(42), 200000 warmup e 20 milhões passos;
cancelamento divide por contagem planejada (risco); banda 1 GiB melhor de 3, cópia conta ler+escrever.
Loading: pacote 512 MiB, 1000 assets 64 KiB, Random(7), flags Windows sem cache, 8 leitores,
passada prévia antivírus. Diretório fixo temporário apresenta risco de concorrência.
DiskBench: sequencial 1 MiB, aleatório 4 KiB, 4 s por fase; QD1 e leitura 8 workers,
Random(42) dados, seeds 3/7 aleatório e 100+t paralelo; buffer alinhado 4096,
NO_BUFFERING+WRITE_THROUGH, espaço arquivo+512 MiB; diretório fixo deve ser substituído por exclusivo.
Score = média sequencial + (readQ1+writeQ1)/200 + readQ8/2000.
README fala QD32 mas implementação usa 8: implementação é a evidência da metodologia.

Na Fase 1 nenhum benchmark era oferecido. A continuação da Fase 2 abaixo acrescenta validação reduzida; não existe score Linux comparável ao Windows.

## Fase 2 — renderizador CPU Silex

Algoritmo `silex-cpu-pathtracer/1`; medição `silex-cpu-runner/1`.
Especificação e fixture independente do novo renderizador: [cpu-workload-spec.md](cpu-workload-spec.md).
A cena, stream RNG, câmera, materiais, profundidade e tonemapping são preservados numericamente;
validação reduzida comparada ao upstream byte a byte no host disponível.
Fórmula original mantida: amostras/segundos/10000. O campo `experimentalScore` só existe
numericamente no perfil full concluído; validação/cancelamento/timeout exportam null.
`scoreComparability=unvalidated` em todos os casos. Nunca tratar esse campo como pontuação oficial.

Diferenças versionadas no runner: estado isolado por execução, workers unidos antes de retornar,
exceções propagadas, snapshots de pixels copiados, progresso por bucket, timeout, checks de cancelamento
por linha e a cada 64 amostras. Amostras contabilizam pixels concluídos (o original conta linhas).
Resultados parciais preservados sem score. Câmera/scene construídas antes do cronômetro; no original
há inicialização estática preguiçosa da câmera. Stopwatch inclui fill, criação de threads, trabalho e join,
mas exclui coleta de hardware, hash e serialização. Não há warmup explícito, como no CPU original.
Mesmo mantendo matemática e fórmula, JIT/.NET 8 vs 10, layout de structs/métodos, checks, scheduler,
afinidade, energia, throttling e inicializações impedem afirmar equivalência de performance.

### Revisão adicional de GameSimBench.cs (não portado ou executado)

Lido integralmente nesta continuação. BuildGrid usa counting sort; Simulate lê buffers atuais e escreve
apenas seu índice; GameLogic aplica dano/respawn em série; PrepareDraw faz culling e sort de profundidade.
Estado para UI é array exposto, com risco de observar frame em alteração. Seed fixa **não garante** estado
inicial medido igual entre máquinas: warmup é por 2 s de relógio e avança um número variável de passos;
depois `_frames` é zerado, mas posições/vida/RNG não são reinicializados. Para golden usar número fixo
de passos a partir do Reset; qualquer mudança do warmup produtivo requer nova versão metodológica.
Cancelamento só entre Steps; sem token em Parallel.For. No cálculo low1 usa p99 dos tempos de frame,
não média do pior 1%; preservar ou versionar essa diferença de definição. Não há novo score Silex de simulação.

### Revisão adicional de MemoryBench.cs (não portado ou executado)

Lido integralmente. Latência conta 20 milhões de passos mesmo após cancelamento antecipado;
necessário suprimir resultado parcial ou usar contador correto e versão distinta. Banda divide em
`n/threads` e ignora resto; leitura desenrolada também ignora sobra inferior a 4 longs, enquanto
numerador reporta buffer inteiro. RAM aloca ~256 MiB + ordem na latência e 2 GiB na banda,
sem limite por memória disponível; não transplantar automaticamente para o host.
Loading usa diretório fixo e limpeza recursiva: futura implementação deve usar diretório exclusivo.
Leituras RandomAccess.Read não verificam retorno parcial. Flags NO_BUFFERING não traduzem O_DIRECT;
assets têm 8 workers e passada anterior fora do tempo, dependente do comportamento Windows/antivírus.
Banda/latência C# são tecnicamente portáveis; I/O sem cache precisa backend/política Linux próprios.


## Continuação 0.2.1 — escopo dos novos testes

Corpus amostral adicional preserva dimensões 960×540 e SPP=1024 em 16 pixels, sem chamar Run full.
Tanto o golden reduzido quanto esse corpus coincidem com o novo renderizador no host disponível.
A imagem completa, duração sustentada e comparação Windows continuam NÃO TESTADAS.
UI executa apenas Validation com 4.608 amostras, mostra pixels reais e permite JSON sem score.
Não foram alterados math, seeds, agendamento do runner, fórmula ou versões do benchmark.

## Continuação 0.6.0 — validação de memória

Auditoria adicional de MemoryBench, GameSimBench e integração MainWindow registrada em
[memória/metodologia](memoria-metodologia.md). O novo comando não é medição de latência/banda:
é uma validação de algoritmos com buffers pequenos e sem score. Diferenças de dimensões/warmup,
repetições e workers são explícitas; não reusar pontuação original (que inclui carregamento de disco).
GameSim continua não portado/executado; warmup por relógio precisa tratamento próprio antes de golden.


## Estado 0.7.0 — GameSim reduzido

Substitui o estado anterior de GameSim não implementado: Core/CLI/Avalonia/JSON com 256 entidades,
16 passos fixos, sem FPS/score/warmup por relógio. Referência independente upstream em 1/2/4
workers, 232 testes e fluxo gráfico Linux passaram. Aceite técnico restrito ao perfil reduzido;
não implica aprovação humana adicional ou certificação de hardware. AMD/NVIDIA seguem com aviso
visível de testes reais não realizados, conforme exceção autorizada. GameSim completo, RAM de
carga, disco/rede, Windows e Wayland nativo continuam pendentes/NÃO TESTADOS. Próxima tarefa:
especificar I/O temporário seguro antes de implementação/carga. Sem executar fases seguintes
automaticamente. Ver [metodologia e evidências](gamesim-metodologia.md).
