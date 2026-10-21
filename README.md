# Silex Audit

Diagnóstico nativo Linux em C#/.NET 10 com Avalonia. Primeiro incremento: inventário factual,
CLI, dashboard e exportação JSON/HTML offline. A continuação da Fase 2 acrescenta renderizador CPU
experimental; nenhuma carga completa é iniciada automaticamente. Sem stress ou alterações do sistema.

O RESET EXPLODE original permanece intacto no submódulo `upstream/ResetExplode`, fixado no SHA
`ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, com histórico e licença MIT do GustavoReset.
Os projetos Silex não compilam os arquivos originais nem incorporam assets RESET;
implementações de algoritmos seguem a fonte MIT auditada, com atribuição. Como houve inspeção das fontes pelo mesmo
implementador, este trabalho **não certifica clean-room estrito**. Veja [arquitetura](docs/arquitetura.md).

## Requisitos

Linux x86_64; SDK [.NET 10 oficial](https://dotnet.microsoft.com/download/dotnet/10.0).
Instale o SDK pelo método oficial escolhido; nenhuma instalação global foi feita nesta tarefa.
No CachyOS/Arch, consulte a disponibilidade de `dotnet-sdk` no gerenciador da distribuição e confirme
`dotnet --list-sdks` antes de instalar. A sessão usou SDK oficial 10.0.100 extraído somente em `/tmp`.
O `global.json` aceita SDKs 10.0 de feature band mais recente.

Interface: servidor X11 ou XWayland e bibliotecas nativas de X11/fontconfig/Skia.
Wayland nativo não foi implementado/validado. A CLI não exige display.
Versão Avalonia fixada: [11.3.21](https://www.nuget.org/packages/Avalonia/11.3.21).

## Compilar e testar

Execute na raiz deste repositório, com SDK disponível no PATH:

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
git submodule update --init --recursive
dotnet restore Silex.Audit.slnx --disable-parallel -m:1
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1
```

Para usar **o SDK temporário desta sessão**, enquanto `/tmp` estiver preservado:

```bash
. scripts/env-local-sdk.sh
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1
```

Ambientes com sandbox precisam permitir IPC local para VSTest. Isso não é necessidade de root do app.
Não inclua o projeto WPF na solução Linux. Para verificar o original, use Windows + SDK .NET 8:
`dotnet build upstream/ResetExplode/ResetExplode.csproj -c Release` (NÃO TESTADO em Windows).

## Executar

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- --help
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- capabilities
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- capabilities --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --output ./silex-snapshot.json
dotnet run --project src/Silex.Audit.Desktop -c Release --no-build
```

`--output` exige arquivo novo e diretório existente. O botão Exportar hardware JSON usa seletor local;
se o destino existir, escolha outro nome. Os valores JSON usam bytes inteiros; a UI mostra também GiB.
Cada leitura tem `source`, `unit`, `status` e `reason`; indisponível tem valor `null`, não zero fictício.
Hardware schema `1.2`, aplicação `0.7.0`, timestamp UTC e runtime registrados.

## Estrutura e limites

- `src/Silex.Audit.Contracts`: DTOs e contrato do coletor.
- `src/Silex.Audit.Core`: relatório e exportação JSON.
- `src/Silex.Audit.Platform.Linux`: /proc, /sys e parsing validado/injetável.
- `src/Silex.Audit.Cli`: diagnóstico sem display, códigos de saída e cancelamento Ctrl+C.
- `src/Silex.Audit.Desktop`: Avalonia, ViewModel, atualizar e salvar snapshot.
- `tests/Silex.Audit.Tests`: fixtures, falhas, limites, unidades, cancelamento e exportação.
- `docs/`: [inventário](docs/portabilidade-inventario.md), [plano](docs/roadmap-linux.md),
  [metodologia](docs/benchmarks-metodologia.md), [validação](docs/validacao.md).

Benchmarks completos de simulação/RAM/disco/GPU, rede, overlays, ajustes, drivers e
backend Windows estão indisponíveis. Renderizador CPU experimental disponível pela CLI; não há
score validado/comparável ao Windows. Inventário de disco mostra dispositivos do kernel, incluindo zram/virtuais;
não mede saúde, espaço livre ou mapeamento de volumes. Recursos de contêiner/cgroup/afinidade
podem diferir dos totais visíveis. Não coleta hostname, serial, UUID, IP ou machine-id.

MIT: [LICENSE](LICENSE). Dependências e termos próprios: [avisos](THIRD-PARTY-NOTICES.md).
Nenhum binário de driver ou SDK proprietário é usado pelo Silex. Não há publicação automática.

## Continuação — Fase 2 CPU

Validação curta do renderizador, sem score e sem carga sustentada:

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench cpu
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench cpu --threads 4 --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench cpu --json --output ./silex-cpu-validation.json
```

A validação faz 32×18 pixels × 8 spp = 4.608 amostras, uma passada, até 4 threads.
O teste compara todos os pixels com uma fixture gerada do upstream em ambiente isolado:
[especificação e proveniência](docs/cpu-workload-spec.md). O hash validado no CachyOS x64 está
registrado na fixture. Essa comparação reduzida não comprova equivalência do perfil completo.

Perfil completo implementado **experimentalmente e NÃO EXECUTADO nesta tarefa**:
`bench cpu --profile full --allow-cpu-load --threads N [--min-seconds N] [--timeout-seconds N]`.
O opt-in gera carga alta; não há monitor térmico implementado. Padrão: uma passada, timeout 120 s.
`--min-seconds 600 --timeout-seconds 900` solicita duração mínima de 10 min, com limite de 15 min.
Não rode esse perfil para validar build/testes. `Ctrl+C` cancela; CLI retorna 130 para cancelamento,
124 para timeout e 2 para argumentos inválidos. Validation não permite repetição por tempo.
A UI oferece diagnóstico e validação CPU reduzida, com prévia e exportação JSON. O perfil full continua somente na CLI.

Relatório de benchmark agrega snapshot real de hardware, perfil/dimensões/SPP, versão de algoritmo,
versão da medição, status, amostras concluídas, passadas, hash BGRA, runtime/arquitetura/configuração.
`experimentalScore` é null para validation/cancelled/timedOut; apenas full concluído usa a fórmula
original amostras/segundos/10.000. Comparabilidade sempre marcada como não validada.
Nenhum código original é compilado nos projetos Silex; o oracle de testes usa a fonte MIT original
somente em temporário. O binário golden é uma fixture derivada, com atribuição, não um asset de produto.


## Interface — validação CPU

Inicie `dotnet run --project src/Silex.Audit.Desktop -c Release --no-build` e role até
**Validação do renderizador CPU**. O botão **Validar CPU (reduzido)** inicia somente 32×18 pixels
com 8 spp e uma passada. **Cancelar CPU** solicita cancelamento enquanto há operação ativa;
a interface aguarda o retorno do runner. Não há opção Full na janela.

A prévia é construída a partir dos pixels calculados, inclusive se parciais; o resultado mostra
status, amostras e hash, sempre sem score. **Exportar CPU JSON** salva o relatório da validação
com seu contexto de hardware, separado de **Exportar hardware JSON**. Ambos exigem destino novo.
Os botões abrem um diálogo da aplicação para informar o caminho completo de um novo arquivo `.json`.
Exportação gráfica, recusa de sobrescrita, cancelamento do diálogo e botão Cancelar CPU foram
validados em janela real por harness no próprio processo, sem eventos globais de teclado/mouse.

O corpus numérico agora inclui 16 pixels fixos em **960×540 e 1024 spp**, comparados com fonte upstream
inalterada, além da imagem reduzida. Isso não executa nem valida uma passada completa ou seu score.
Para regenerar só esse oracle de teste, após revisar divergências: `./scripts/generate-cpu-reference.sh original-pixels`.

## Sensores somente leitura — 0.3.0

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- sensors --output ./silex-sensors.json
```

Backend hwmon lê temperatura, tensão, corrente, potência e RPM quando o kernel disponibiliza.
A UI permite atualizar, exportar e ativar monitor opcional a cada 2 s, sem histórico. O monitor
inicia desligado. Dados brutos não representam saúde, calibração ou proteção térmica.
Temperatura aceita somente drivers coretemp/k10temp/acpitz/nvme/amdgpu; outros e termistores aparecem
indisponíveis. Falhas não viram zero. Na continuação 0.4.0, hardware JSON usa schema 1.2; sensores agregados usam 1.1.
Detalhes e evidências em [validação](docs/validacao.md).

Teste gráfico opt-in, exige sessão gráfica e cria apenas uma subpasta própria no destino:

```bash
dotnet run --project tools/Silex.Audit.UiSmoke -c Release -- /tmp/silex-ui-validation
```

O harness aciona controles no processo Avalonia, testa diálogos e exportações e salva capturas.
Não envia teclas ou cliques globais. Não executa perfil CPU full.


## Diagnóstico de temperatura, energia e GPU — 0.4.0

`info` e `sensors` agora incluem `/sys/class/thermal` e `/sys/class/power_supply`.
A UI apresenta zonas térmicas, alimentação AC e bateria junto aos sensores, com origem e data.
Thermal pode repetir hwmon: fontes são preservadas, sem somar/deduplicar por rótulo.
Temperatura de bateria usa décimos de °C; thermal usa milésimos; energia/carga usam Wh/Ah,
sem confundir carga atual com desgaste. `present=0` impede publicar valores antigos da bateria.
Atributos opcionais ausentes são omitidos; falhas retornam null/indisponível.

O novo cartão GPU e `info` mostram IDs PCI das placas DRM. No host Intel foram observados
vendor `0x8086` e device `0x9a49`; não inferimos nome comercial ou VRAM compartilhada.
Contadores AMD de carga e VRAM possuem implementação e testes com fixtures;
**AMD/NVIDIA reais NÃO TESTADOS**. Intel/NVIDIA têm identificação PCI, sem telemetria vendor.

SMART é separado do diagnóstico padrão e só consulta um dispositivo por ação explícita:

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- smart \
  --device nvme0n1 --allow-device-query --json
```

`smartctl` é opcional, detectado nos caminhos do sistema; não é instalado nem redistribuído.
Aceita somente nomes de discos SATA (`sda`) ou NVMe (`nvme0n1`), sem partições/caminhos arbitrários.
Não habilita SMART, inicia self-tests ou pede root. Sem permissão, retorna status indisponível.
Consulta pode ter efeitos de leitura/power-state dependentes do controlador: solicita evitar standby
nos transportes compatíveis, sem garantia universal. USB/SCSI/RAID não têm fallback automático.
O JSON exporta somente status booleano relatado e código de saída; não exporta serial/modelo.
`reportedPassed=true` não representa garantia de saúde. A consulta sem privilégios deste host foi recusada
por permissões (exit smartctl 2). Posteriormente, o usuário executou uma consulta NVMe com privilégio
pontual: código 0 e passed=true, aceitos pelo parser C# de produção. Isso não habilita acesso
privilegiado automático no app nem garante saúde do disco.

O avanço entre incrementos exige funcionamento Linux testado e aceite registrado:
[critério e estado atual](docs/aceite-linux.md). Funcionalidades parciais e hardware não testado
não são apresentados como aprovados.

### Consulta SMART pelo terminal fish

Para produzir um relatório real com acesso administrativo pontual, execute pessoalmente:

```fish
set smart_report (mktemp /tmp/silex-smart.XXXXXX.json)
sudo /usr/bin/smartctl --json --health --quietmode=noserial \
    --nocheck=standby,3,5 --device=nvme /dev/nvme0n1 > "$smart_report"
set smart_status $status
printf 'Relatório: %s\nCódigo: %s\n' "$smart_report" "$smart_status"
```

Essa consulta não altera permissões, habilita SMART ou inicia self-test. Informe o caminho gerado
para validar a resposta. O script `scripts/env-local-sdk.sh` é para Bash, não para `source` no fish.
O aviso AMD/NVIDIA na interface indica testes reais não realizados; ausência desses fabricantes
foi aceita pelo usuário como exceção para continuar o trabalho, sem alegar suporte validado.


## Relatório HTML offline — 0.5.0

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --html --output ./silex-hardware.html
```

Na interface, use **Exportar hardware HTML** e escolha um arquivo novo `.html`. A exportação
preserva o snapshot selecionado, com unidades, fontes, datas, estados e limitações. Não consulta
SMART nem inicia benchmarks. **Cancelar** não cria arquivo e arquivos existentes são preservados.
O HTML é um arquivo único, sem scripts ou recursos remotos, com valores escapados e política CSP.
O aviso AMD/NVIDIA de testes não realizados é mantido. Conteúdo validado no Chromium Linux e
exportação testada na janela real Avalonia; detalhes em [relatórios HTML](docs/relatorios-html.md).
`--html` e `--json` são mutuamente exclusivos; `--html` aplica-se somente a `info`.


## Validação de algoritmos de memória — 0.6.0

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --json
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- bench memory --threads 4 --json --output ./silex-memory.json
```

Na interface, role até **Validação de algoritmos de memória** e clique **Validar memória
(reduzido)**. Há cancelamento e exportação JSON próprios; nenhuma execução automática.
Cadeia de ponteiros, soma e cópia usam buffers de 256 KiB, uma passada e 1 worker na UI
(1/2/4 na CLI). Arrays principais somam 784 KiB, além do overhead/hash/runtime.
**Não mede latência, GB/s, estabilidade ou saúde da RAM. Não há score nem perfil completo.**

A correção foi comparada com métodos upstream reduzidos, sem executar seus buffers grandes
ou I/O de disco. [Metodologia e referência](docs/memoria-metodologia.md). GameSim foi reanalisado,
mas ainda não foi implementado ou executado. O resultado de memória não integra o HTML de hardware.


## Validação reduzida GameSim — 0.7.0

Core/CLI/Avalonia executam 256 entidades por 16 passos fixos, com snapshot final e exportação JSON.
Sem FPS, low1, score ou warmup por tempo. Não representa desempenho de jogos/GPU.
Testado no CachyOS: oracle original em 1/2/4 workers, 232 testes e janela Avalonia real.
[Metodologia, diferenças e evidências](docs/gamesim-metodologia.md).

```bash
dotnet run --project src/Silex.Audit.Cli -c Release -- bench sim --threads 1 --json --output /tmp/silex-sim.json
dotnet run --project src/Silex.Audit.Desktop -c Release
```

Escolha arquivo novo: exportações recusam sobrescrita. Na interface, use “Validar simulação
(reduzida)” e “Exportar simulação JSON”. Nenhuma simulação inicia automaticamente.
Os trechos 0.2–0.6 abaixo/acima registram entregas anteriores; o estado atual do GameSim é este.
