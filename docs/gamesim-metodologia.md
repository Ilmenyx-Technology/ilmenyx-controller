# GameSim reduzido — 0.7.0

Implementado e verificado no CachyOS x64/.NET 10.0.0: Core, CLI `bench sim`, Avalonia e JSON.
É validação de correção de simulação sintética; não mede FPS, low1, GPU ou score de jogos.
Não executa o benchmark completo nem inicia na abertura do aplicativo.

## Fonte e decisões

Fonte auditada integralmente: `upstream/ResetExplode/GameSimBench.cs`, commit
`ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, MIT GustavoReset; integração WPF em MainWindow
já revisada no incremento anterior. Também examinados Contracts, parser CLI, exporters, runners
CPU/memória, ViewModels, DashboardWindow e harness Avalonia. O submódulo permanece intacto.

GameSimStateEngine adapta os métodos matemáticos originais com atribuição MIT. Não se afirma
clean-room estrito: auditor e implementador tiveram acesso ao código original. Nenhum asset RESET
foi usado. Não há nova dependência, Rust, API Windows ou ferramenta privilegiada neste incremento.

| Aspecto | Original | Validação Silex |
|---|---|---|
| Entidades | 30.000 | 256 |
| Execução | warmup 2 s + duração por relógio | 16 passos fixos desde Reset; zero warmup |
| Mundo/grade/dt | 1000 / 64×64 / float 1/60 | preservados |
| RNG | xorshift seed 12345 | preservado |
| Física/IA/lógica | counting sort, separação, combate, fuga, respawn | métodos preservados |
| Partições | CPU lógicos × 4 | workers explícitos 1/2/4 × 4; limite de paralelismo igual a workers |
| Medição | FPS médio e recíproco p99 de tempo/frame | nenhuma; score null, performanceStatus not-measured |
| UI | arrays mutáveis consultados por timer | cópia final somente após Step completo; prévia 2D estática |

O warmup original avança um número variável de passos e zera `_frames` sem reiniciar mundo/RNG.
Logo, seed fixa não garante estado inicial medido igual. O low1 original é recíproco de p99,
não média dos piores 1%. Não transportar essas métricas para o perfil reduzido.
A prévia top-down desenha todas as entidades do snapshot, sem reproduzir o renderer WPF.
Culling/ordenação originais continuam no estado e no hash para verificar esses algoritmos.

## Limites, cancelamento e snapshots

Opções não permitem alterar dimensões/passos. Timeout padrão 5 s, máximo 10 s; workers 1/2/4
(UI 1). Timeout e token são cooperativos entre passos completos; não prometem interrupção
instantânea se o scheduler estiver bloqueado. Cancelamento antes da execução, após um passo e
após o último passo suprime State e StateSha256; CompletedSteps informa o trabalho concluído.
Não se publica estado parcial nem score. Coleta factual de hardware ocorre antes da simulação.

Um engine pertence a uma execução; Parallel.For termina antes da troca de buffers/lógica/desenho.
Capture copia registros e listas para coleções somente leitura. Avançar/reiniciar o engine não
muda snapshots já publicados. O ViewModel descarta callbacks após fechamento; exportação JSON
usa o mesmo writer atômico sem sobrescrita. Relatório schema 1.0, hardware schema 1.2.

## Oracle independente e testes

`scripts/generate-gamesim-reference.sh` verifica SHA e integridade do arquivo upstream, copia-o
para temporário exclusivo, reduz entidades, fixa partições e remove integralmente Run, incluindo
warmup e loop temporizado. O oracle chama somente Reset/Step e nunca o runner produtivo Silex.
Alterações por texto têm guardas de ocorrência; alterações futuras da fonte exigem revisão.

12 casos: 1/2/4 workers × Reset, passo 1, passo 16 e passo 1 com cenário de bordas. O cenário de
teste altera oito entidades para exercitar combate/morte, fuga, coincidência, bounce e respawn;
jamais é inventário de hardware nem estado usado na execução normal. O JSON inclui oito amostras,
RNG, visíveis e hash de todo o estado. O gerador usa BinaryWriter; o Core usa BinaryPrimitives.

Hash `silex-gamesim-state-le-v1`, SHA256, inteiros/floats IEEE754 little-endian:

- Cabeçalho: int32 entidades, int32 passos concluídos, uint32 RNG, int32 visíveis.
- Por entidade, na ordem de ID: int32 ID; float32 X,Y,Vx,Vy,Health,Damage; byte Team.
- Por visível: int32 ID, float32 depth key; ordem do sort original.

Hash final no host para 1/2/4 workers:
`6815765e7f314653b328268725f02c597dc31bb5334670769ab3accfc772c880`.
Equivalência bit a bit se limita a estes casos/runtime/arquitetura, sem equivalência de desempenho.

Testes cobrem oracle, invariantes, reset, propriedade dos snapshots, cancelamento, deadline
determinístico, opções inválidas, CLI, ViewModel/fechamento/falha e exportação sem overwrite.
A compilação inicial do oracle detectou campo `_frames` atribuído apenas por reflection; foi
corrigido com setter explícito na cópia temporária. Guardas de partição consideram as três
ocorrências originais. Nenhuma tolerância numérica nem golden foi alterado para esconder divergência.

## Evidências e limites

Build Release: zero avisos/erros. Suíte: 232 aprovados, zero falhas/ignorados. CLI real 1/2/4
coincidiu com oracle; stdout e arquivo iguais; flags de carga/duração rejeitadas. UI Linux real:
sem autostart, cancelar/reiniciar, prévia 256 entidades, JSON validado pelo hash, cancelar diálogo,
regressões CPU/memória/sensores/hardware/HTML e aviso AMD/NVIDIA. Captura dos painéis inspecionada.
Harness usa eventos em controles da própria janela, sem injeção global. Não equivale a teste manual.

Evidências `docs/evidencias/phase4-gamesim-*`: build, testes/TRX, oracle, CLI, JSONs reais, UI e PNG.
NÃO TESTADO: GameSim completo/30 mil entidades, FPS/low1, sustentação, Windows, outras ISAs,
Wayland nativo; AMD/NVIDIA real conforme exceção do usuário. Nenhum stress, sudo, driver,
configuração, I/O pesado, push, PR ou release executado. Aceite técnico apenas deste recorte.

Próxima tarefa recomendada: revisar e especificar o diagnóstico de I/O em arquivo temporário,
com caminho autorizado, quotas, espaço livre, cancelamento e limpeza, antes de qualquer teste de
carga. Não executar automaticamente disco bruto, loading original ou benchmark pesado.
