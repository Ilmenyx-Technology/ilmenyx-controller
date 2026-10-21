# Critério de aceite Linux

Por determinação do usuário, cada implementação precisa funcionar no Linux, ser testada e ter
seu aceite registrado antes de avançar para a tarefa seguinte. Compilar não basta. Sucesso em
fixture não certifica hardware real. Aprovação humana, quando houver, deve ser registrada como
uma decisão do usuário; os resultados abaixo são verificações técnicas.

## Verificações mínimas por incremento

1. Build Release dos projetos afetados e testes pertinentes sem falhas.
2. Execução real do fluxo no Linux disponível, incluindo saída e fontes observadas.
3. UI afetada testada em sessão gráfica real; CLI e exportação verificadas de ponta a ponta.
4. Falhas, ausência e cancelamento preservam indisponibilidade, sem valores inventados.
5. Evidências, limites e condições não testadas documentados; nenhuma mudança privilegiada ou carga proibida.
6. Funcionalidades parciais não recebem aprovação completa nem autorizam avanço silencioso.

## Revalidação 0.4.0 — CachyOS x64

Ambiente: branch feat/silex-linux-phase1, SDK .NET 10.0.100, sessão XWayland.
Fonte WPF preservada no SHA ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6, sem alterações.

| Funcionalidade | Resultado técnico | Alcance do aceite |
|---|---|---|
| Build Release | zero avisos/erros | solução Linux atual |
| Testes automatizados | 177 passaram, zero falhas/ignorados | código e fixtures cobertos |
| CPU/RAM/SO/discos | dados reais via CLI e UI | host CachyOS atual; capacidade não é saúde |
| hwmon/thermal/energia | leitura real, monitor e fontes verificadas | canais disponíveis deste host; sem calibração |
| JSON | exportação validada e overwrite recusado, bytes preservados | CLI e diálogos Avalonia |
| CPU reduzida | 4608 amostras, prévia, cancelamento e exportação | somente validação reduzida, sem score |
| GPU Intel | IDs PCI reais; carga/VRAM explicitamente indisponíveis | identificação; não telemetria Intel |
| AMD/NVIDIA | fixtures passaram | suporte em hardware real NÃO APROVADO/NÃO TESTADO |
| SMART | opt-in exigido; indisponibilidade sem acesso tratada | caminho real de sucesso NÃO APROVADO/NÃO TESTADO |
| Calibração/proteção térmica | não implementadas | sem aceite; dados brutos não substituem proteção |

O usuário atual continua sem acesso de leitura a /dev/nvme0n1. Não foi feita nova consulta SMART
neste aceite, nem alteradas permissões: a consulta anterior retornou código 2. Não houve nova
validação positiva SMART. Não há máquina AMD/NVIDIA disponível para validar esses caminhos.

**Decisão histórica daquele ensaio: aceite parcial, sem aprovação completa do incremento. Não iniciar o relatório HTML
ou avançar de fase enquanto os caminhos reais de sucesso exigidos estiverem pendentes.**
As condições para resolver cada pendência estão na [matriz](pendencias-validacao.md).
Windows, outras distros/ISAs e Wayland nativo continuam fora do ambiente validado, sem promessa de suporte.

Comandos executados após `. scripts/env-local-sdk.sh`:

```bash
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=aceite-0.4.0-tests.trx' --results-directory docs/evidencias
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- capabilities --json
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-aceite-ui
```

CLI também testou exportação para arquivo próprio, repetição recusada sem modificar bytes e
parser SMART sem opt-in recusado antes de consultar disco. O harness UI aciona controles no
processo Avalonia, sem teclado/mouse globais; testa exportações, preview, cancelamento e monitor.
Interação manual humana não é alegada por esse teste automatizado.

Evidências: docs/evidencias/aceite-0.4.0-build.txt, aceite-0.4.0-tests.txt/trx,
aceite-0.4.0-cli.txt e aceite-0.4.0-ui.txt. Nenhuma instalação, stress, self-test de disco,
configuração de sistema, commit, push, PR ou release.

## Exceção posterior autorizada pelo usuário — AMD/NVIDIA

O usuário informou que a máquina não tem AMD nem NVIDIA e autorizou seguir sem testar esses
fabricantes, desde que a interface mostre “Testes não realizados”. O dashboard agora apresenta
“AMD/NVIDIA · Testes não realizados em hardware real” em destaque no cartão GPU.
Essa ausência de hardware deixa de ser bloqueio de avanço, mantendo a classificação NÃO TESTADO.
A decisão anterior de bloqueio é substituída nesse ponto; não representa aprovação de suporte
AMD/NVIDIA. SMART continua aguardando o relatório da consulta real feita pelo usuário.

## SMART real validado e avanço autorizado

O usuário executou a consulta explícita NVMe e disponibilizou /tmp/silex-smart.hEos4e.json
para leitura. O parser C# de produção, executado por tools/Silex.Audit.SmartReportValidation,
aceitou o arquivo: status available, toolExitCode 0, reportedPassed true. Somente uma projeção
filtrada e uma fixture mínima derivada foram guardadas no repositório; nenhum serial/modelo/JSON
bruto foi incorporado. Evidências smart-real-validation.txt e smart-real-filtered.json.

A consulta do dispositivo foi feita pelo usuário via smartctl com privilégios pontuais; o agente
apenas leu o arquivo e chamou o parser sem privilégios. Isso não certifica execução privilegiada
do Silex nem concede ao app acesso ao dispositivo. O caminho padrão sem acesso continua indisponível.
O relato positivo do firmware não é garantia de saúde. Não houve nova consulta ou self-test.

Com esse caminho real validado e a exceção AMD/NVIDIA já autorizada, o bloqueio de avanço para o
relatório HTML foi resolvido. Permanecem as limitações de hardware/ambiente e recursos não implementados.


## Aceite técnico do incremento HTML 0.5.0

Build Release sem avisos/erros; 190 testes passaram. CLI validada com hardware real, exportação
HTML idêntica ao stdout e overwrite recusado; UI real com cancelamento, extensão e snapshot
selecionado preservados. Chromium Linux verificou estrutura, ausência de scripts/recursos externos
e layouts de 1280 e 390 px. O incremento HTML atende aos critérios técnicos do ambiente disponível;
nenhuma aprovação humana adicional é inferida. Detalhes em [relatórios HTML](relatorios-html.md).
Fase 4 foi iniciada somente pelos relatórios; RAM/disco/rede continuam sem implementação de carga.

## Aceite técnico da validação de memória 0.6.0

Build sem avisos/erros e 209 testes passaram. Core/CLI coincidiram com métodos RAM upstream
reduzidos para 1/2/4 workers; UI real em 1 worker passou por início explícito, cancelamento,
resultado e exportação. JSON preservado em overwrite, sem métricas ou pontuação de desempenho.
Esse aceite se restringe à validação de algoritmos com buffers pequenos: não é aceite de
latência/banda, estabilidade/saúde de RAM, GameSim ou perfil completo. Detalhes/evidências em
[memória/metodologia](memoria-metodologia.md) e validacao.md. Próximo recorte pode avançar para
GameSim reduzido, mantendo as restrições de carga e verificação Linux antes da próxima etapa.


## Estado 0.7.0 — GameSim reduzido

Substitui o estado anterior de GameSim não implementado: Core/CLI/Avalonia/JSON com 256 entidades,
16 passos fixos, sem FPS/score/warmup por relógio. Referência independente upstream em 1/2/4
workers, 232 testes e fluxo gráfico Linux passaram. Aceite técnico restrito ao perfil reduzido;
não implica aprovação humana adicional ou certificação de hardware. AMD/NVIDIA seguem com aviso
visível de testes reais não realizados, conforme exceção autorizada. GameSim completo, RAM de
carga, disco/rede, Windows e Wayland nativo continuam pendentes/NÃO TESTADOS. Próxima tarefa:
especificar I/O temporário seguro antes de implementação/carga. Sem executar fases seguintes
automaticamente. Ver [metodologia e evidências](gamesim-metodologia.md).


## Atualização 0.8.0 — Wayland e registro de testes

Wayland nativo agora validado no CachyOS/KWin: Avalonia 12.1.4 opt-in, DISPLAY removido e
implementação real Avalonia.Wayland.WindowImpl, sem fallback X11. Regressão X11/XWayland também
passou; 250 testes, build sem avisos/erros. Isso substitui o estado anterior de nativo não
implementado. Outras distros/compositores, Windows, AMD/NVIDIA reais e perfis completos seguem
no registro contínuo Sem_teste.md, conforme nova orientação do usuário. Preparação de GameSim
completo disponível em bench sim-plan, sem carga/medição; não equivale a runner/FPS/score testado.
[Comandos, decisões e evidências](wayland-validacao.md). Commits locais, sem publicação automática.


## Continuação 0.9.0 — runner GameSim experimental

Runner full CLI implementado com opt-in e limites, reutilizando engine matemático parametrizado;
validação padrão/UI continua 256 entidades/16 passos. Build sem erros/avisos e 261 testes passaram,
com relógio/engine controlados para controle/estatística. Carga real de 30.000 entidades NÃO
EXECUTADA; Sem_teste.md SIM-01 permanece aberto. Não declarar aceite pleno de FPS/score nem
comparabilidade Windows. [Metodologia, comandos e evidências](gamesim-completo.md).


## Identidade Ilmenyx 0.10.0

Renomeação controlada de projetos novos/CLI/UI/metadados, preservando formatos e origem MIT.
Diretrizes permanentes acrescentadas ao AGENTS sem apagar anteriores. 272 testes e fluxos Linux
CLI/Wayland/X11 passaram; Sobre/avisos de compartilhamento implementados. Não é liberação legal,
privacidade garantida ou empacotamento final. Marca/manual/organização/titularidade/licenças nativas
permanecem em revisão humana; veja branding/RENAME-DECISIONS.md, ../Sem_teste.md e
legal/PRE-RELEASE-CHECKLIST.md. Exemplos Silex de incrementos anteriores são históricos.
