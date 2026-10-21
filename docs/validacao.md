# Validação — Silex Audit

As seções iniciais registram a Fase 1. O estado atual da Fase 2 está na seção de continuação ao final.

## Ambiente observado

Workspace inicial `bf5a37a` / `main`; branch de trabalho `feat/silex-linux-phase1`.
Upstream original fixado em `ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`.
CachyOS Linux x86_64, kernel `7.2.11-1-cachyos`, DISPLAY=:0 e WAYLAND_DISPLAY=wayland-1.
Nenhum SDK no PATH inicialmente. SDK oficial 10.0.100 obtido, com autorização, do endereço
`https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-10.0.100-linux-x64.tar.gz`
e extraído em `/tmp/silex-dotnet`; runtime 10.0.0. Não é a alegação de SDK/patch mais recente.
CLI home e pacotes em `/tmp/silex-cli` e `/tmp/silex-nuget`. Use patch oficial atual em produção.

## Comandos e resultados

Variáveis e PATH: `. scripts/env-local-sdk.sh` (somente para esta sessão).

| Comando / verificação | Resultado |
|---|---|
| `dotnet --info` | SDK 10.0.100, RID linux-x64, runtime 10.0.0 |
| `dotnet restore Silex.Audit.slnx --disable-parallel -m:1` | exit 0; rede autorizada necessária para NuGet |
| `dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false` | exit 0; seis projetos, 0 warnings, 0 errors |
| `dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=tests-linux.trx' --results-directory docs/evidencias` | exit 0; 33 passaram, 0 falharam, 0 ignorados, cerca de 5 s |
| `dotnet run --project src/Silex.Audit.Cli --no-build -- info` | exit 0; dados reais na configuração Debug inicial |
| `dotnet src/Silex.Audit.Cli/bin/Release/net10.0/Silex.Audit.Cli.dll info` | exit 0; snapshot real Release |
| CLI `--help`, `info`, `capabilities`, ambos `--json` | exit 0; JSON parseado, não apenas texto inspecionado |
| CLI comando inválido / falta destino / output em capabilities | exit 2 esperado |
| CLI exportar JSON em diretório temporário próprio | exit 0; conteúdo igual ao stdout JSON da mesma coleta |
| CLI sobrescrever o mesmo destino | exit 1 esperado; destino preservado |
| `dotnet src/Silex.Audit.Desktop/bin/Release/net10.0/Silex.Audit.Desktop.dll` | janela aberta na sessão; screenshot inspecionado; processo encerrado após coleta de evidência |
| `dotnet list Silex.Audit.slnx package --vulnerable --include-transitive --no-restore` | exit 0; feed NuGet não reportou vulneráveis para os seis projetos |
| `dotnet msbuild src/Silex.Audit.Desktop/Silex.Audit.Desktop.csproj -preprocess:/tmp/silex-desktop-expanded.xml -nologo` | exit 0; AvaloniaStats ausente; BuildServices excluído dos assets |
| `dotnet build upstream/ResetExplode/ResetExplode.csproj -m:1 --no-restore` | exit 1; NETSDK1100 esperado no Linux, não regressão |
| `git -C upstream/ResetExplode status --short` | vazio; original intacto |
| `git diff --check` / `git diff --cached --check` | sem erros de whitespace |

O sandbox bloqueou rede na restauração (NU1301/NU1900) e sockets locais em VSTest/formatador.
Comandos necessários foram repetidos com autorização fora do sandbox, sem root.
O build paralelo inicial encerrou sem diagnóstico útil; `-m:1` produziu resultado reproduzível.
`dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet` passou após permitir IPC.
SDK CLI teve seu primeiro uso restrito ao CLI_HOME temporário; não houve mudança do trust store.

## Cobertura e evidências

Fixtures em `tests/Silex.Audit.Tests/Fixtures` são dados de teste explicitamente sintéticos,
nunca usados em runtime. Casos: CPU com ids não contíguos, duplicados numéricos/inválidos;
memória ausente, negativa, duplicada, overflow, locale pt-BR, disponibilidade zero e total impossível;
setores sysfs × 512, overflow; os-release ausente/duplicado/aspas incompletas e conteúdo sem execução;
fontes sem permissão/ausentes, sysfs vazio, nomes de dispositivo inseguros, falha por disco;
leitura com limite de tamanho, cancelamento e timeout real de 5 s; schema e exportação sem overwrite.

- `evidencias/build-linux.txt`: saída Release.
- `evidencias/tests-linux.trx`: relatório VSTest com 33 resultados.
- `evidencias/cli-info.txt`: snapshot factual em texto (RAM disponível varia entre coletas).
- `evidencias/cli-smoke.txt`: cenários da CLI.
- `evidencias/ui-smoke.png` e `ui-smoke.txt`: janela final e escopo do smoke gráfico.
- `evidencias/build-wpf-linux.txt`: bloqueio esperado de Windows targeting.
- `evidencias/nuget-audit.txt`: resumo da consulta de vulnerabilidades.

## Não testado / limites

Cliques Atualizar e Exportar JSON no seletor gráfico: NÃO TESTADOS interativamente.
Coleta inicial, bindings e layout visual observados com hardware real; exporter compartilhado testado.
Wayland nativo, Windows, outras distros e outros fabricantes de hardware: NÃO TESTADOS.
Não há runtime de sensores, GPU, SMART ou benchmarks implementado. Nenhum stress foi executado.
A auditoria original é aprofundada nos módulos centrais e por superfície nos demais (ver inventário);
não foi feita análise integral de segurança de todos os módulos ou binários.
Na entrega da Fase 1, as Fases 2–7 não foram executadas. A continuação CPU está registrada abaixo. Não houve push, PR, release ou commit automático.

## Continuação — Fase 2 CPU (v0.2.0)

O histórico acima descreve a entrega da Fase 1. Nesta continuação a Fase 2 ganhou renderizador
CPU independente em Core e execução/exportação pela CLI. GameSimBench.cs e MemoryBench.cs
foram relidos integralmente e tiveram riscos adicionais documentados; não foram executados/portados.
Fases 3–7 continuam não executadas.

Comandos usados, com `. scripts/env-local-sdk.sh`:

```bash
./scripts/generate-cpu-reference.sh
dotnet restore Silex.Audit.slnx --disable-parallel -m:1
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase2-tests.trx' --results-directory docs/evidencias
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench cpu --threads 1 --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench cpu --threads 4 --json
```

- Restore, format e build: exit 0; build Release final com zero avisos e erros.
- Testes: **57 passaram, 0 falharam, 0 ignorados**, incluindo os 33 anteriores; cerca de 5 s.
- Golden de referência: fonte original compilada apenas em /tmp com Width/Height/SPP/Bucket reduzidos;
  1/4 threads produziram exatamente o mesmo frame. Original no submódulo permaneceu sem alterações.
- Renderizador Silex: todos os 576 pixels iguais à fixture, 4.608 amostras, hash
  `4a2ff589647b89851f86e20f15d093b8d61dff21f75c9fd9e5b11b8a02d8e830` em 1/4 threads.
- Concorrência de chamadas no mesmo runner sem estado compartilhado; pré-cancelamento, cancelamento
  entre buckets, timeout e erro em observer; resultado sem score e workers unidos antes do retorno.
- Validação dos parâmetros completos e da fórmula somente por funções puras; **nenhum teste agenda full**.
- CLI: help/info/capabilities preservados; bench cpu em texto/JSON; exportação idêntica ao JSON da
  mesma coleta; recusa de overwrite, nenhum temporário remanescente. Full sem opt-in e argumentos
  inválidos retornaram 2 antes da execução. Casos em `evidencias/phase2-cli-smoke.txt`.
- Relatório inclui SO/kernel/hardware reais, versões de algoritmo/medição, perfil, threads,
  mínimo solicitado, timeout, passadas/amostras, status e ambiente. Validation retorna score null.

A primeira compilação dos novos testes encontrou uso incorreto da sobrecarga Assert.Throws para
Func<Task> (CS0619/xUnit2014); corrigido para validar os parâmetros sem agendar carga. A execução
intermediária de binário anterior não é a evidência desta entrega; o build final e o TRX de 57 testes são.
Nenhuma dependência NuGet nova foi adicionada. Projetos de produto agora declaram versão 0.2.0;
Core mantém Optimize=true também em Debug. Sem atualização do upstream ou mudança do WPF.

Evidências atuais: `phase2-restore.txt`, `phase2-build.txt`, `phase2-tests.txt`, `phase2-tests.trx`,
`phase2-cli-smoke.txt` e `phase2-cpu-validation.json`. A imagem ui-smoke.png anterior é da Fase 1;
a UI só teve o texto de versão/capacidades ajustado nesta fase, com compilação validada.

**NÃO TESTADOS:** perfil full, duração sustentada, score full, Windows/.NET 8, outras ISAs,
comparabilidade Linux/Windows e integração de benchmark na UI. Não houve stress, carga completa,
benchmark de disco, alterações do sistema, push, PR, release ou commit automático.

## Continuação — corpus original e UI reduzida (0.2.1)

Escopo: somente continuação da Fase 2, sem iniciar sensores/Fase 3. Mesma branch e mesmo SHA upstream;
original continua sem alterações. Versões do algoritmo e medição permanecem /1: matemática e runner
não mudaram; adição de interface para teste não altera o trecho medido.

```bash
. scripts/env-local-sdk.sh
./scripts/generate-cpu-reference.sh original-pixels
./scripts/generate-cpu-reference.sh reduced
dotnet restore Silex.Audit.slnx --disable-parallel -m:1
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase2-ui-tests.trx' --results-directory docs/evidencias
dotnet run --project src/Silex.Audit.Desktop -c Release --no-build
```

Resultados:

- Build Release: exit 0, zero avisos/erros. **66 testes passaram**, zero falharam/ignorados.
- Oracle original: somente 16 buckets 1×1, sem Run; 16.384 amostras totais, sem score.
  Novo renderizador coincidiu nos 16 pixels em 1/4 threads. Hash do corpus:
  `a1b6d7aed6fad8729e62a09a4bf4a3111c9683f65ace706ba0ab2ccc57568622`.
- Oracle reduzido anterior regenerado para validar o novo seletor de modos; bytes/hash preservados.
  O primeiro build do harness original acusou CS0162 por guarda constante do outro modo; corrigido
  usando verificação por método, com TreatWarningsAsErrors=true, sem suprimir o aviso.
- ViewModel: conclusão real reduzida, controles habilitados/impedidos, início duplicado ignorado,
  cancelamento na coleta e no runner, erro de worker, descarte sem notificações tardias,
  callbacks de progresso enfileirados após conclusão ignorados, exportação e recusa de overwrite.
  Coleta e parsing ficam em Task.Run; callbacks/resultado retornam ao contexto UI.
- Tela real em CachyOS/XWayland: aberta sem rodar CPU automaticamente; validação iniciada por teclado,
  prévia BGRA observada, 4.608 amostras e hash esperado. Rolagem e estados concluídos observados.
- O ensaio de automação do seletor não manteve o foco corretamente e foi interrompido; não é evidência
  de sucesso do diálogo. **Seleção/salvamento gráfico e cancelamento nativo por clique NÃO TESTADOS**.
  Não foram enviados novos eventos globais após detectar o problema de foco. O processo de teste encerrou.
- Exportação adicional por CLI no caminho informado `/tmp/silex-native-export-validation/cpu.json`:
  exit 0, JSON parseado e igual ao stdout da mesma coleta, status completed, score null, hash esperado.
  Esse arquivo foi gerado pela CLI, não pelo seletor. Cópia da evidência em phase2-ui-cli-export.json.
- Projeto MSBuild de testes expandido sem AvaloniaStats; BuildServices excluído também no projeto
  de testes. Referência a Desktop reaproveita dependências Avalonia existentes; nenhuma nova versão
  de pacote adicionada. Core/Contracts continuam sem referência a Avalonia.

Evidências: phase2-ui-build.txt, phase2-ui-tests.txt, phase2-ui-tests.trx,
phase2-ui-oracle-original.txt, phase2-ui-oracle-reduced.txt, phase2-ui-before.png,
phase2-ui-validation.png e phase2-ui-cli-export.json. phase2-ui-export-attempt.png mostra a
janela do ensaio e NÃO comprova arquivo salvo. O log da aplicação gráfica ficou sem mensagens.

Persistem NÃO TESTADOS: imagem completa, score/full/sustentado, Windows, outras distros/ISAs,
Wayland nativo. Nenhum stress, benchmark de disco, configuração de sistema, push, PR ou release.
Próximo incremento recomendado: sensores hwmon somente leitura, mediante nova solicitação.

## Continuação 0.3.0 — exportação gráfica e hwmon

Validação em CachyOS x64, SDK 10.0.100, sessão XWayland. Registro UTC do ambiente em 2026-10-21
(mesma sessão local de 20/10). Nenhuma configuração de sistema alterada.

O seletor externo anterior foi substituído por JsonSaveDialog da aplicação. O harness opt-in abriu
DashboardWindow real, acionou eventos dos botões no dispatcher e preencheu TextBox no próprio processo.
Não houve envio de teclas/cliques globais. É validação gráfica automatizada em processo;
interação manual humana e seletor externo antigo não foram validados por esse ensaio.

Comandos executados (após `. scripts/env-local-sdk.sh` quando usando SDK temporário):

```bash
dotnet restore Silex.Audit.slnx --disable-parallel -m:1
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase3-tests.trx' --results-directory docs/evidencias
dotnet restore tools/Silex.Audit.UiSmoke/Silex.Audit.UiSmoke.csproj --disable-parallel -m:1
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-graphical-validation
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- capabilities --json
```

Resultados finais: build Release sem avisos/erros; **104 testes passaram**, zero falhas/ignorados.
O primeiro build dos novos testes detectou xUnit2031; corrigido usando o predicado de Assert.Single.
Fixtures hwmon cobrem unidades/cultura, valores negativos/zero/overflow, nomes e traversal,
metadados opcionais, enable/fault/type, termistor e driver desconhecido, permissão negada,
falhas de leitura, hotplug, timeout próprio/cancelamento externo e limites de inventário.
ViewModel cobre bloqueio de operação duplicada, descarte sem notificações tardias e parada do monitor.
Testes não escrevem /sys e não executam CPU Full.

Harness gráfico: PASS. Hardware exportado e parseado (schema 1.1); sentinela existente preservada.
Botão Cancelar CPU acionado e cancelamento observado; nova execução reduzida concluiu 4.608 amostras,
prévia real e score null. Cancelar diálogo não gerou arquivo; nova abertura exportou cpu.json parseável.
Monitor de sensores iniciou/parou, e exportação gráfica gerou sensors.json. Capturas foram inspecionadas.
Artefatos próprios ficaram em `/tmp/silex-graphical-validation/silex-ui-0e0f271b918848598f67df450e9d9c60`.
O arquivo anterior `/tmp/silex-native-export-validation/cpu.json` não foi sobrescrito.

CLI: JSON de sensors/info/capabilities parseado, exportação de sensores idêntica ao stdout da mesma
coleta, repetição recusada com exit 1 e bytes preservados. Arquivo novo em subpasta temporária própria.
Host real: 13 canais, 12 disponíveis, status partial; BAT1 power1 retornou erro de leitura do kernel.
Temperaturas coretemp/acpitz/nvme, tensão BAT1 e fan1/fan2 foram lidos; RPM zero é o valor da fonte.
Esses resultados não comprovam funcionamento em outros drivers/máquinas. Nenhuma saúde foi inferida.

Evidências: phase3-build.txt, phase3-tests.txt/trx, phase3-cli-smoke.txt, phase3-ui-smoke.txt,
phase3-dashboard.png, phase3-cpu-preview.png, phase3-save-dialog.png e phase3-sensors.json.

Ainda NÃO TESTADOS/indisponíveis: perfil CPU completo/score, Windows, outras distros/ISAs,
Wayland nativo, drivers de temperatura fora da lista conservadora, calibração de placa,
thermal/power_supply completo, GPU/SMART e controle/proteção térmica. O WPF/submódulo segue sem alterações.
Próxima tarefa: thermal/power_supply somente leitura com fixtures, mantendo disponibilidade explícita.

## Continuação 0.4.0 — thermal/energia, DRM/PCI e SMART opcional

Ambiente: branch feat/silex-linux-phase1, base bf5a37a, CachyOS x64/.NET SDK 10.0.100,
sessão XWayland existente. Submódulo upstream continua no SHA ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6.
Original BatteryInfo/GpuInfoCollector, contratos, manifesto de licença e projetos foram revisados.
Nenhum código/asset/binário original foi incorporado ao novo diagnóstico.

Comandos executados após `. scripts/env-local-sdk.sh`:

```bash
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase3-diagnostics-tests.trx' --results-directory docs/evidencias
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- capabilities --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- smart --device nvme0n1
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- smart \
  --device nvme0n1 --allow-device-query --json
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-diagnostics-ui
dotnet list Silex.Audit.slnx package --vulnerable --include-transitive --no-restore
```

Build Release: zero avisos/erros. **177 testes passaram**, zero falhas/ignorados. Resultado final em
phase3-diagnostics-tests.txt/trx. As novas fixtures incluem energia/bateria, conversões diferentes
thermal/d°C, status/presença, capacidades/ciclos, ausência/negação de acesso, hotplug, limites,
identificação PCI e contadores AMD. SMART testa opt-in, nomes de disco, argumentos fixos,
booleano/código contraditórios, saída inválida, limites, cancelamento e timeout. Os testes não
consultam discos ou hardware real. O primeiro teste de limite esperava 64 leituras no total,
mas a fixture fornecia 64 zonas mais 9 atributos de bateria: corrigido para contar zonas.
Nenhum workload/score/golden CPU foi alterado.

CLI real: 29 leituras, duas fontes de energia, status parcial, JSON parseado. thermal e energia
constam nas leituras reais e BAT1/ACAD nos metadados. Exportação igual ao stdout da mesma coleta;
segunda gravação recusada exit 1 com bytes preservados. GPU real card1: 0x8086:0x9a49, PCI class
0x030000; carga e VRAM indisponíveis. Isso comprova identificação PCI Intel, não telemetria vendor.

SMART sem opt-in foi recusado pelo parser (exit CLI 2). Uma consulta explícita somente leitura
NVMe retornou toolExitCode 2, reportedPassed=null, status unavailable (exit CLI 0, diagnóstico
produzido). O processo roda euid=1000, dispositivo modo 0660, os.access(R_OK)=false.
Nenhum sudo, mudança de permissões ou repetição privilegiada. Resposta SMART bem-sucedida real
NÃO TESTADA; casos positivos/negativos são fixtures, não resultados de saúde do disco deste host.
Código 2 isoladamente não diferencia todas as causas; o relatório preserva indisponibilidade.

UI real: harness no processo Avalonia validou identificação GPU apresentada, origem de sensor
na tela, schemas novos, exportações de hardware/CPU/sensores, recusa de sobrescrita, cancelamento
do diálogo e da CPU, prévia real reduzida e monitor start/stop. Sem teclas/cliques globais.
Capturas geradas foram inspecionadas. Interação manual humana NÃO TESTADA nesta continuação.

Auditoria NuGet: nenhuma vulnerabilidade reportada para os projetos e fontes consultadas.
Nenhuma dependência NuGet adicionada. Isso não é garantia de ausência de falhas desconhecidas.
Evidências com prefixo phase3-diagnostics- em docs/evidencias. Novos schemas: hardware 1.2,
sensores agregados 1.1, GPU 1.0 e SMART 1.0. CPU externo 1.0 permanece com hardware interno versionado.

Pendências e condições concretas: [matriz](pendencias-validacao.md). Fase 3 parcial, sem alegar
validação AMD/NVIDIA/Windows ou proteção térmica. Nenhum benchmark Full, stress ou disco pesado.

Capturas finais da UI 0.4.0: phase3-diagnostics-dashboard.png, phase3-diagnostics-cpu-preview.png e phase3-diagnostics-save-dialog.png.

## Critério de avanço solicitado pelo usuário

A revalidação da entrega 0.4.0 e a decisão de aceite por funcionalidade estão em
[aceite Linux](aceite-linux.md). Build sem avisos/erros, 177 testes passaram, CLI factual e
harness Avalonia executados novamente. O aceite continua parcial por falta de validação real
positiva SMART e hardware AMD/NVIDIA; nenhuma nova fase foi iniciada.

## Aviso AMD/NVIDIA e correção para fish

O usuário autorizou dispensar testes reais AMD/NVIDIA por ausência dessas GPUs, condicionando
um aviso na interface. O cartão GPU agora mostra em destaque:
“AMD/NVIDIA · Testes não realizados em hardware real”. Isso não afirma certificação de suporte.
AGENTS e matriz de aceite registram que essa falta de hardware não bloqueia mais o avanço.
SMART continua aguardando o arquivo da consulta real do usuário; não foi executado sudo pelo agente.

Build Release: zero avisos/erros. 177 testes passaram, zero falhas/ignorados.
Harness Avalonia verifica existência, texto e visibilidade do aviso na janela real, junto aos
fluxos de exportação/cancelamento/monitor já existentes. Evidências: gpu-notice-build.txt,
gpu-notice-tests.txt/trx, gpu-notice-ui.txt e gpu-notice-dashboard.png.

O comando Bash anterior era incompatível com o fish do usuário; corrigido para `set` e `$status`.
O script documentado foi validado com `fish --no-config --no-execute` (exit 0): somente parsing,
sem executar a consulta privilegiada. Evidência gpu-notice-fish-syntax.txt.

## Continuação 0.5.0 — SMART real e HTML offline

Arquivo real /tmp/silex-smart.hEos4e.json disponibilizado pelo usuário após ajuste de propriedade.
A validação Python inicial confirmou JSON legível, protocolo NVMe, exit_status 0 e smart_status.passed
true. Em seguida, tools/Silex.Audit.SmartReportValidation chamou o parser C# de produção sobre o
arquivo completo (limite de 65536 caracteres) com código observado 0: status available/passed true.
A ferramenta não consulta dispositivos nem eleva privilégios. Código de saída da ferramenta: 0.
Apenas projeção filtrada e fixture mínima derivada foram incorporadas, sem copiar o JSON bruto.
O usuário executou a consulta privilegiada; isso não certifica execução privilegiada do app Silex.

Após resolver esse bloqueio e manter a exceção AMD/NVIDIA, foi implementado o primeiro recorte
da Fase 4: HTML de hardware offline em Core/CLI/UI. Build Release sem avisos/erros, 190 testes
passaram, zero falhas/ignorados. Nenhuma metodologia/golden de CPU mudou. O teste novo de SMART
usa o relatório real filtrado; demais dados artificiais são explicitamente fixtures de testes.

Comandos usados após `. scripts/env-local-sdk.sh`:

```bash
dotnet run --project tools/Silex.Audit.SmartReportValidation -c Release -- \
  /tmp/silex-smart.hEos4e.json nvme0n1 0 /tmp/silex-smart-hEos4e-validated.json
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase4-html-tests.trx' --results-directory docs/evidencias
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-html-ui
```

O smoke CLI criou diretório próprio /tmp/silex-html-cli-h12uf9nz e executou info --html --output
hardware.html, repetição recusada (exit 1), formatos conflitantes (exit 2), sensors --html inválido
(exit 2) e info --json válido. Exportação HTML igual ao stdout byte a byte, sem BOM e sem temporários
restantes. HTMLParser verificou seis tabelas sem scripts/forms/recursos externos.

UI real em CachyOS/XWayland: HTML cancelado não criou arquivo, extensão .json recusada no diálogo
HTML, sentinela .html preservada, exportação correspondeu ao snapshot JSON já selecionado.
Regressões do JSON, CPU reduzida (4608 amostras/score null) e monitor de sensores passaram.
Chromium Linux automatizado verificou DOM, CSS responsivo, aviso e dados reais. Acesso file://
foi bloqueado pelo harness; foi usada cópia idêntica em servidor estático loopback isolado,
encerrado ao final. Uma requisição GET do documento, zero recursos externos e console sem erros.
Capturas desktop/mobile/Avalonia inspecionadas. Não é validação manual, Firefox ou impressão.

Evidências e alcance completo em [relatórios HTML](relatorios-html.md). Original WPF/submódulo
preservado, nenhum sudo pelo agente, configuração, stress, self-test, push, PR ou release.

## Continuação 0.6.0 — algoritmos de memória e revisão GameSim

Fontes lidas integralmente: MemoryBench.cs e GameSimBench.cs do upstream fixado, integração
MainWindow.xaml.cs (BtnMemBench_Click, BtnSimBench_Click, DrawSim), contratos/Core/CLI/UI
relacionados e docs de metodologia. WPF/submódulo sem alteração. Decisões completas em
[memória/metodologia](memoria-metodologia.md). Não se afirma benchmark completo de RAM.

Oracle independente: scripts/generate-memory-reference.sh gerou fixture dos métodos RAM
upstream com buffers 256 KiB, 1024 warmup, 32768 passos, uma repetição e 1/2/4 workers. Nenhum
método de disco foi incluído ou executado. Métricas de relógio originais são descartadas;
retêm-se somente estado/checksums. Testes do novo Core coincidiram em todos os casos.

Build Release: zero avisos/erros. 209 testes passaram, zero falhas/ignorados. Cobertura nova:
propriedade de ciclo único com saltos de 64 bytes, golden 1/2/4, repetibilidade, limites de
workers/timeout, cancelamento precoce e no percurso, timeout determinístico com TimeProvider
manual (sem dormir/carga), comandos inválidos, exportação/no-overwrite e lifecycle do ViewModel.
O primeiro teste de cancelamento revelou que Cancel() podia sobrescrever o status final por
“cancelando” ao retomar o await sincronamente. Corrigido publicando a solicitação antes de
cancelar o token; o teste de regressão passou. Resultados interrompidos não publicam somas,
checksums ou score completos. Dados faltantes na saída textual são explícitos como indisponível.

Comandos usados após `. scripts/env-local-sdk.sh`:

```bash
./scripts/generate-memory-reference.sh
dotnet format whitespace Silex.Audit.slnx --no-restore --verbosity quiet
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase4-memory-tests.trx' --results-directory docs/evidencias
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --threads 2 --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --threads 4 --json
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-memory-ui
```

CLI real CachyOS: casos 1/2/4 coincidiram com fixture upstream; arquivo JSON igual ao stdout da
mesma coleta. Segunda gravação recusada com exit 1, bytes preservados. Threads 3, perfil full e
allow-cpu-load recusados com exit 2. Saída texto explicita ausência de latência/GB/s/score.
Regressões info JSON/HTML passaram; versão 0.6.0, hardware schema 1.2 e novo relatório de memória
schema 1.0. Score null e performanceStatus not-measured. Timeout/repetição constam nos relatórios.

UI real CachyOS/XWayland: sem autostart de memória, clique/cancelamento, conclusão real em um
worker com somas 32768/32768, 32768 passos e hash esperado, cancelamento de diálogo sem arquivo,
exportação JSON parseada. Regressões de CPU reduzida, preview/exportação, sensores/monitor,
HTML e JSON hardware passaram. Aviso AMD/NVIDIA visível. Sem injeção global de teclado/mouse.
Capturas de dashboard/painéis inspecionadas. Não equivale a teste manual ou Wayland nativo.

Evidências: prefixo phase4-memory- em docs/evidencias (build, tests.txt/trx, oracle, CLI, UI,
JSONs CLI/UI e capturas). Cópia canônica em 1 worker: hash
5d13d4f5eabdb8645983b02dfd86438b77115918b5d2ec8c40ba3355c8a80dc1.
Cadeia: a161778502328abe03b03b6c5a13304938399808c410fe351f9e9af54a53c5a1.
Hashes validam lógica/dados do workload, não saúde ou desempenho físico da RAM.

NÃO IMPLEMENTADOS/EXECUTADOS: latência/banda de RAM, perfil completo, score original de memória
(acoplado a carregamento), GameSim, I/O de loading/disco/rede. Sem stress, sudo, configuração,
instalação, push, PR ou release. Próximo incremento: simulação reduzida por passos fixos com
oracle/snapshots coerentes, sem FPS/score ou warmup por tempo.

## Continuação GameSim 0.7.0 — 2026-10-21

Ambiente: CachyOS Linux x64, SDK 10.0.100/runtime 10.0.0, Avalonia 11.3.21,
backend X11/XWayland da sessão gráfica. SDK/cache locais reaproveitados; nenhuma instalação.
Build Release zero avisos/erros; 232 testes passaram, zero falhas/ignorados.

Comandos executados na raiz em Bash:

```bash
. scripts/env-local-sdk.sh
scripts/generate-gamesim-reference.sh
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase4-gamesim.trx' --results-directory docs/evidencias
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-gamesim-ui
```

CLI executada via DLL Release em 1/2/4 workers, com diretório temporário exclusivo:
`dotnet src/Silex.Audit.Cli/bin/Release/net10.0/Silex.Audit.Cli.dll bench sim --threads 1 --json --output CAMINHO_NOVO.json`.
Comparação automatizada dos três hashes com a fixture, JSON stdout/arquivo idênticos;
segunda exportação recusada exit 1, bytes preservados; perfil full, duração, allow-cpu-load e
threads 3 recusados exit 2. Texto explicita simulação sintética/ausência de FPS/score.

Oracle independente: 12 casos de estados/passo fixo incluindo bordas; igualdade exata do estado
completo em 1/2/4 workers. Testes verificam snapshots não alterados pelo avanço/reset, coleções
somente leitura, invariantes, cancelamento antes/entre/depois dos passos, deadline sem sleep,
ViewModel, descarte, falhas e exportação sem sobrescrita. Não é benchmark de desempenho.

Harness Avalonia real passou sem autostart; cancelar/reiniciar, estado 256 entidades/16 passos,
preview final e JSON com hash oracle, cancelamento de diálogo sem arquivo. Regressões de CPU,
memória, monitor de sensores, hardware JSON/HTML e aviso AMD/NVIDIA passaram. Captura dos painéis
inspecionada: prévia visível e limites claros. Sem eventos globais de teclado/mouse.

Evidências: phase4-gamesim-build.txt, tests.txt/trx, oracle.txt, cli.txt, real-{1,2,4}.json,
ui.txt/json, panels.png/dashboard.png em docs/evidencias. Metodologia em gamesim-metodologia.md.
Submódulo original permanece no SHA fixado, sem alterações. Não houve sudo, stress, alterações
do sistema, push/PR/release. Full/FPS/low1/Windows/Wayland nativo/AMD-NVIDIA reais: NÃO TESTADOS.
Este é aceite técnico do recorte testado; não atribui nova aprovação humana ao usuário.


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

## Ilmenyx 0.10.0 — identidade, atribuição e privacidade (2026-10-21)

Base 3904a47, branch feat/silex-linux-phase1; primeiro commit desta sessão 54fe3f2 apenas ajusta
.gitignore (kits/referência local preservados no disco, fontes/licenças/spec atual/evidências não
ignoradas). Sem remote principal, sem criação de fork/org/URL fictícia. SHA upstream intacto.

IMPLEMENTADO: Ilmenyx.* em solução/projetos/namespaces novos, CLI apphost ilmenyx, UI/título e
assinatura by Bismuth Technology, metadados Product/Company (crédito mantenedor), Sobre com abas
origem/MIT/dependências locais, avisos de privacidade e redação de padrões óbvios em texto limitado.
Sem troca de schemas/IDs/workloads/golden, copyright original ou material histórico. Nenhum asset
visual novo/final; nenhum logo RESET. Props somente em src copia LICENSE/NOTICE/terceiros para
outputs, não afeta WPF. NOTICE distingue adaptação independente/não oficial, sem afiliação/endosso.

Build solução Release e harness: zero erros/avisos. 272 testes passaram, zero falhas/ignorados.
CLI apphost local realmente executado: ajuda/identidade/créditos; hardware JSON/HTML stdout=arquivo;
reduzidos CPU (4608 amostras), RAM e GameSim com hashes esperados/null score. Full sem opt-in
recusado; nenhuma carga full ou SMART real disparada nesta sessão. LICENSE/output notices verificados
byte a byte; todas as fixtures e evidências anteriores preservadas. Busca ativa sem Silex.Audit/SILEX
AUDIT/Silex CPU; IDs lowercase e alias SILEX_DISPLAY_BACKEND mantidos por compatibilidade explícita.

UI real em Wayland nativo (DISPLAY removido, Avalonia.Wayland.WindowImpl) e X11/XWayland
(Avalonia.X11.X11Window): identidade, Sobre/abas/fechar, aviso em diálogo, exportações/cancelamento/
recusa overwrite, CPU/RAM/GameSim reduzidos, sensores e aviso AMD/NVIDIA passaram. Capturas de
dashboard/Sobre/diálogo inspecionadas. Testes usam eventos no processo; não certificam interação
manual/acessibilidade/escala/outros compositores. ILMENYX_DISPLAY_BACKEND tem precedência; alias
legado coberto por teste unitário sem alterar ambiente global.

Testes adicionais: fixtures hostname/username/serial/MAC/IP/UUID/home paths, chaves extras não
exportadas e SMART bruto filtrado; dados legítimos e campos numéricos preservados. Não é
anonimização garantida. Auditoria estática não encontrou cliente remoto de produto; captura de
tráfego/bibliotecas completas NÃO TESTADA. Consulta NuGet não reportou pacotes vulneráveis nas
fontes atuais; não certifica binários/runtime. Logs históricos contêm caminhos de workspace;
revisão humana antes da publicação continua pendente, sem reescrita Git.

Comandos executados (Bash com helper SDK local, telemetria opt-out):

```bash
. scripts/env-local-sdk.sh
dotnet restore Ilmenyx.slnx --disable-parallel -m:1
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=tests.trx' --results-directory docs/evidencias/ilmenyx-identity
dotnet restore tools/Ilmenyx.UiSmoke --disable-parallel -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-identity-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-identity-x11
dotnet list Ilmenyx.slnx package --vulnerable --include-transitive --no-restore
```

Evidências em docs/evidencias/ilmenyx-identity/: build/tests/TRX/CLI/UI/build harness/audit NuGet,
gitignore/integrity/static-audit e capturas. Nenhuma instalação/root/driver/BIOS/OC/disco bruto,
telemetria nova, stress, push/PR/release ou alteração remota. Windows/full/outras distros/hardware
seguem NÃO TESTADOS em Sem_teste.md. PENDENTE DE REVISÃO: direitos de marca/titularidade, canal
privado/retenção, logs históricos, Manual de Marca/empacotamento. BLOQUEADO para redistribuição de
assets/termos desconhecidos e publicação sem aprovação humana. Isso não equivale a parecer legal.

Revisão final do diff: fontes/docs editados sem whitespace errors. O prompt fornecido pelo
mantenedor foi versionado sem alterar seus bytes; contém três hard breaks Markdown intencionais
(espaços no fim das linhas 3–5), que git diff --check aponta. Essa exceção de formatação foi
inspecionada; não se alterou o documento de instruções para ocultá-la.


## Ilmenyx 0.11.0

Workspace movido para /mnt/HDD/Github/Ilmenyx, branch local feat/ilmenyx-linux e descrição Git
Ilmenyx, sem alterar remotos/histórico/submódulo. Solução/CLI continuam Ilmenyx.slnx / ilmenyx.
ValidationHtmlReport no Core exporta três reports reduzidos existentes, sem nova coleta/carga;
CLI --html disponível para CPU validation, memória e GameSim. Mesma escrita atômica/escaping,
sem alterar JSON ou metodologia. Build sem erros/avisos, 276 testes e CLI/UI de regressão passaram.
[Detalhes e comandos](validation-html.md). UI HTML de resultados e browser específico pendentes.

Termos próprios corrigidos conforme esclarecimento: usar programa como ferramenta em serviço
remunerado é permitido; vender programa/acesso ou derivar produto/serviço pago do código próprio
não é autorizado. LICENSE MIT e direitos anteriores/terceiros continuam preservados; política
não é open source OSI e exige revisão jurídica antes de publicação (../LICENSING.md).


## Atualização 0.12.0 — HTML dos resultados na UI

IMPLEMENTADO e testado: exportação dos snapshots CPU/RAM/GameSim pela interface,
sem nova coleta/carga, com cancelamento e recusa de sobrescrita. 279 testes passaram;
Wayland nativo e X11 passaram no CachyOS. Browser Chromium em 390/1280 px passou
para os três relatórios. Isto encerra as pendências locais de botões HTML e layout
nessas condições, sem generalizar para outras plataformas ou acessibilidade.
Detalhes e comandos em [validation-html-ui.md](validation-html-ui.md). Registros anteriores são históricos.


## Atualização 0.13.0 — teclado e foco

IMPLEMENTADO e testado no CachyOS: rótulos do caminho/licenças, ajuda de erro,
ciclo de Tab e restauração de foco após diálogos. 279 testes aprovados; smoke
Wayland nativo e X11 com eventos de teclado no processo passou. GUI-02 continua
NÃO TESTADO para Orca/AT-SPI (Orca ausente), entrada física e avaliação assistiva
completa. Não houve instalação nem certificação de acessibilidade.
[Detalhes e limites](acessibilidade-teclado.md).


## Atualização 0.14.0 — layout mínimo e texto ampliado

IMPLEMENTADO: ações do dashboard quebram em linhas, exportação com rolagem vertical
e título Sobre com quebra. Build sem avisos/erros, 279 testes aprovados e smokes
Wayland nativo/X11 aprovados. Mínimos 640×480, 440×360 e 480×360 DIP, texto normal
e 150% por instrumentação no processo: controles medidos alcançáveis após rolagem.
Fecha esse recorte local de layout, sem fechar GUI-01/GUI-02 para outros displays,
200% de texto, leitores de tela ou entrada física. [Evidências e limites](layout-minimo.md).


## Atualização 0.15.0 — leitura de licenças

IMPLEMENTADO: seletor compacto substitui abas de Sobre, preservando integralmente
os quatro documentos e o leitor somente leitura. No mínimo 480×360, área de leitura
aproximada de 208 DIP normal e 135 DIP a 150%. Build sem avisos/erros, 279 testes
aprovados e smokes Wayland nativo/X11 passaram, incluindo seleção integral, bloqueio
de edição, rolagem e teclado. NÃO TESTADO: clipboard real, Orca/AT-SPI, entrada física,
200%+, outras plataformas e acessibilidade completa; GUI-01/GUI-02 continuam parciais.
[Comandos e evidências](licencas-leitura.md).


## Atualização 0.16.0 — texto a 200%

IMPLEMENTADO: quebra de rótulos/títulos com cortes reproduzidos e cabeçalho Sobre
compacto. Build sem avisos/erros, 279 testes aprovados e nove casos de layout por
backend em Wayland nativo/X11 (100/150/200%) passaram. Leitura a 200% de cerca de
161 DIP/159 DIP, mantendo fonte 28 e critério de 120 DIP. Fecha o recorte local
de 200%; registros anteriores são históricos. Acima de 200%, outras matrizes de
DPI/hardware, Orca/AT-SPI, entrada física e Windows permanecem NÃO TESTADOS.
GUI-01/GUI-02 seguem parciais. [Método, evidências e comandos](texto-200.md).


## Atualização 0.17.0 — pacote local linux-x64

IMPLEMENTADO: bundle CLI/desktop framework-dependent, inventário de avisos e hashes;
gerador/validador sem instalação/restore implícito/publicação. Build sem avisos/erros,
279 testes aprovados, integridade/CLI/ELF/ldd e smoke dos assemblies extraídos em
Wayland nativo/X11 passaram. Pacote alterado foi recusado. PKG-01 continua parcial:
VM limpa, outras distros/glibc, runtime ausente, self-contained e instaladores
NÃO TESTADOS. Revisão individual de licenças antes de redistribuir PENDENTE; material
sem direitos conhecidos BLOQUEADO para redistribuição. [Evidências e comandos](pacote-linux-x64.md).


## Complemento de ambiente isolado — pacote 0.17.0

Sem QEMU/imagem guest pronta; VM limpa continua NÃO TESTADA (PKG-01 parcial).
Contêineres locais, sem root/rede/instalação: Debian 12 sem ICU bloqueou o modo
padrão; diagnósticos CLI passaram somente no teste invariant explícito, sem SDK.
Alpine 3.24.1 é incompatível com o bundle glibc; GUI no contêiner NÃO TESTADA e
fontconfig ausente no Debian. Preflight evita repetir aborts por ICU conhecida
ausente. Requisitos documentados; nada altera o modo padrão do aplicativo.
[Comandos, códigos de saída e limites](validacao-container.md).


## Atualização 0.18.0 — guarda ICU na CLI

IMPLEMENTADO e testado: pré-verificação nativa no adaptador Linux; falta de ICU
retorna código 5 antes da coleta, com mensagem e sem JSON parcial. Não é fallback
invariant nem certificação de versões/ABI. Build sem avisos/erros, 284 testes
aprovados; caso negativo real em Debian e modo normal no CachyOS passaram.
VM limpa, Debian padrão com ICU, GUI sem ICU e matriz adicional seguem NÃO TESTADOS.
Os aborts anteriores de 0.17.0 permanecem históricos. [Detalhes e comandos](icu-cli.md).


## Atualização 0.19.0 — doctor CLI

IMPLEMENTADO e testado: `doctor` textual, bibliotecas carregáveis e variáveis de
display não vazias/ausentes, sem provedores de inventário ou workloads. Build sem
avisos/erros e 296 testes aprovados. Host normal: código 0; Debian sem ICU: relatório
com código 5 sem abort; invariant explícito: código 0, sem certificação do modo padrão.
JSON/exportação do doctor indisponíveis; opções recusadas. VM limpa, Debian com ICU
e matrizes assistivas/de hardware seguem NÃO TESTADAS. [Evidências e limites](doctor-cli.md).


## Atualização 0.20.0 — requisitos na interface

IMPLEMENTADO: consulta explícita em Requisitos → Verificar, sem autostart; DTO em
Contracts, apresentação independente em Core, sondagem no adaptador Linux.
CLI e Desktop compartilham texto/limitações sem depender um do outro. Build sem
avisos, 301 testes aprovados; regressões CLI no host e Debian sem ICU passaram.
NÃO TESTADO: VM limpa, outras distros, Orca/AT-SPI e teclado físico. PENDENTE DE
REVISÃO humana: usabilidade e checklist jurídico/marca; direitos desconhecidos
BLOQUEADOS para redistribuição. [Evidências e resultado gráfico](requisitos-ui.md).


### Roteiro de aceite em outra máquina — pacote 0.20.0

IMPLEMENTADO e testado no CachyOS: integridade antes da execução, doctor com prazo,
smoke CLI existente e resultado estruturado sem afirmar certificação de GUI/VM.
Runtime ausente e pacote alterado recusados; destino existente preservado.
NÃO TESTADO: VM limpa e outra distro com ICU; PKG-01 permanece parcial.
[Comandos, evidências e pendências](aceite-maquina.md).
