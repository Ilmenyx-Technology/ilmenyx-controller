# Memória e GameSim — auditoria e incremento 0.6.0

Referência: upstream/ResetExplode no commit ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6,
MemoryBench.cs e GameSimBench.cs lidos integralmente, integração em MainWindow.xaml.cs
(BtnMemBench_Click, BtnSimBench_Click e DrawSim). O original permanece sem alterações.

## Memória original e decisões

| Item | Código original | Incremento Silex |
|---|---|---|
| Cadeia | 256 MiB em long[], ordem em int[] (~16 MiB), saltos de 64 bytes, Random(42), shuffle Sattolo e ciclo por ordem | mesmos cálculos em 256 KiB; ordem com 4096 posições |
| Percurso | 200000 warmup, 20000000 leituras dependentes; ns calculados pelo relógio | 1024 warmup, 32768 passos; somente índice final e hash, sem ns |
| Banda | dois buffers de 1 GiB, todas as CPUs, melhor de três | dois buffers de 256 KiB; uma repetição; 1, 2 ou 4 workers |
| Leitura | inicialização t+1 por partição, soma desenrolada em quatro acumuladores | mesma inicialização/soma; reporta checksum inteiro, não GB/s |
| Cópia | Array.Copy por partição, taxa conta leitura+escrita | mesma cópia; soma e SHA256 do destino, sem taxa |
| Cancelamento | métodos podem dividir pelo trabalho planejado após execução parcial; UI Windows descarta resultados cancelados após etapas de RAM | resultado cancelled/timedOut explícito; sem hashes/somas/score em resultado interrompido |
| Score integrado | MainWindow usa 1000/latência + 10/tempoCarregamentoMapa | score null; não existe pontuação de RAM isolada equivalente |
| Carregamento | pacote 512 MiB + 1000 arquivos, Windows NO_BUFFERING, 8 leitores, diretório fixo | NÃO PORTADO/NÃO EXECUTADO |

A banda original usa n/threads e ignora restos; o laço de leitura também ignora sobras inferiores
a quatro longs, mas o numerador usa buffer completo. O novo perfil fixa dimensões divisíveis por
1/2/4 workers e por quatro; não corrige silenciosamente essa metodologia para produzir scores.
Para um futuro perfil completo, cobertura integral, bytes reais e cancelamento exigirão medição
versionada. Não transportar automaticamente a fórmula Windows ou substituir flags por O_DIRECT.

## O que a validação faz

CLI bench memory e UI executam somente silex-memory-validation-v1, algoritmo
sattolo64-seed42-sum4-copy-v1. Arrays principais somam 802816 bytes (784 KiB): cadeia, origem,
destino e ordem. Há overhead de runtime, objetos e buffers de hash de 8 KiB; isso não é limite
de memória total do processo. Não existe opção de buffers grandes ou duração sustentada.

Não são publicados latência, GB/s, FPS, estabilidade ou saúde. O perfil pequeno pode caber em
cache e serve para validar implementação, não desempenho da DRAM. performanceStatus é
not-measured e score é null. Sem clock de performance; TimeProvider é usado somente para
registro UTC e timeout cooperativo. Timeout padrão 5 s, máximo 10 s na API; CLI/UI usam 5 s.
Checks de cancelamento limitados por estágio/blocos; o caller não recebe hashes parciais válidos.

O relatório JSON próprio schema 1.0 inclui hardware real, início, runtime/arquitetura, workers,
versões, SHA upstream, buffers, passos, timeout configurado, repetição, checksums e limitações. Schema do hardware continua 1.2.
Hashes usam longs little-endian explícitos. Mudanças de runtime/RNG devem passar pela fixture,
sem regenerá-la para esconder diferenças. Não há inclusão desse resultado no HTML de hardware.

Somas/hashes de cópia diferem por worker porque a origem é preenchida com t+1 por partição;
isso é a metodologia de origem, não uma corrida. O hash da cadeia e índice final são iguais
em 1/2/4 workers. A UI usa um worker; CLI aceita somente 1/2/4. Workers são limitados pelo
ParallelOptions; o algoritmo não usa automaticamente todas as CPUs disponíveis.

## Referência independente de testes

scripts/generate-memory-reference.sh valida SHA e ausência de mudanças em MemoryBench.cs,
extrai somente os dois métodos de RAM para um temporário e compila com .NET 10. Não compila
I/O Windows, usa unsafe ou chama MeasureLoading. O template está fora da solução de produto.
Alterações controladas: buffers 256 KiB, warmup 1024, percurso 32768, uma repetição e workers
fixos 1/2/4; instrumenta somente saída de estado/checksums. Taxas que os métodos originais
calculam são descartadas e não são usadas como benchmark ou fixture de desempenho.

A fixture memory-reference.json deriva desses métodos MIT, com atribuição a GustavoReset.
O novo runner não gera sua própria referência: testes comparam índices, hashes e somas ao
código upstream reduzido. Seed/shuffle, elo de 64 bytes, inicialização por worker e soma/cópia
coincidiram no CachyOS/.NET 10.0.0. Não foi validado o perfil completo ou a execução Windows.

```bash
. scripts/env-local-sdk.sh
./scripts/generate-memory-reference.sh
dotnet build Silex.Audit.slnx -c Release
dotnet test Silex.Audit.slnx -c Release --no-build
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --threads 4 --json --output ./silex-memory.json
```

O helper .sh é Bash; no fish, execute as linhas que dependem dele dentro de Bash. Com SDK já
no PATH, os comandos dotnet são os mesmos em ambos os shells. A UI oferece Validar memória
(reduzido), Cancelar memória e Exportar memória JSON; nada inicia automaticamente.

## GameSim revisado — não implementado neste incremento

O original usa 30000 entidades, mundo 1000, grade 64x64, dt=1/60 e xorshift32 seed 12345.
BuildGrid faz counting sort; Simulate lê estado atual e escreve buffers seguintes/dano próprio;
GameLogic aplica dano/respawn e RNG em série; PrepareDraw faz culling e ordenação em profundidade.
A UI WPF lê arrays públicos em timer enquanto a simulação pode modificá-los; futura UI precisa
snapshot coerente, sem expor buffers de trabalho mutáveis.

O warmup dura dois segundos por relógio e avança número variável de Steps. Depois _frames é
zerado sem reiniciar posições/vida/RNG: semente fixa não garante estado inicial medido igual
entre máquinas. Cancelamento ocorre entre Steps, sem token nos workers. Score é FPS médio da
simulação; Low1Fps vem do percentil 99 dos tempos, não da média do pior 1%. Não representa FPS
renderizado de um jogo real ou GPU. MainWindow descarta resultado quando ct foi cancelado.

Próximo incremento recomendado: simulação reduzida com número fixo de passos a partir de Reset,
fixture independente do upstream, limite de entidades/workers e exportação coerente de estado,
sem warmup por tempo ou FPS/score. Qualquer benchmark completo posterior deve preservar ou
versionar explicitamente warmup, medição, definição de low1 e comparação. Nenhum GameSim,
carregamento de mapa, teste completo de RAM, stress ou teste de disco foi executado agora.
