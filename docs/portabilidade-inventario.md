# Auditoria Fase 0 — Silex Audit

Base local: `bf5a37a`, branch inicial `main`; trabalho: `feat/silex-linux-phase1`.
O workspace inicial NÃO era um fork com fontes: continha apenas especificações e referência visual.
Upstream obtido separadamente: https://github.com/GustavoReset/ResetExplode,
SHA `ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6` (v0.16).
Fontes preservadas em `upstream/ResetExplode`; caminhos abaixo relativos a essa pasta.
Inventário de arquivos e hashes: `fontes-auditadas.tsv`. Leitura integral concentrada nos quatro
benchmarks CPU/RAM/disco, coletores principais, relatório, csproj, README, licença e workflow;
demais módulos inspecionados por imports, assinaturas, chamadas nativas e pontos de integração.
Isso não constitui revisão integral de cada implementação ou auditoria de segurança de binários.
Não há suíte de testes original identificada.

| Funcionalidade / arquivos | APIs e acoplamento | Reuso técnico / Linux | Windows / risco / teste | Prioridade |
|---|---|---|---|---|
| Sistema: SystemInfoCollector.cs, InfoNode.cs | WMI, árvore de apresentação | reimplementar coleta /proc e /sys; DTO neutro | preservar WMI; dados pessoais; fixtures | P1 |
| Discos: DriveMap.cs, DiskHealth.cs | WMI Storage, LHM, geometria WPF | separar inventário de saúde; sys/block; SMART indisponível | preservar; não confundir ausência com saudável; hotplug/negação | P1/P3 |
| Sensores: SensorMonitor.cs, SensorRow.cs | LibreHardwareMonitor 0.9.6, PawnIO, ObservableCollection | backend hwmon futuro; indisponível agora | manter; concorrência Update/Close e polling exigem revisão; fixtures | P3 |
| GPU: GpuInfoCollector.cs, GpuSpecDb.cs, Nvml.cs, Dxgi.cs | WMI, Registry, nvml.dll, DXGI, D3D12 | refatorar identificação DRM/PCI futura; não inferir specs | manter; NVML opcional precisa resolver .so, ABI e ciclo de vida; hardware real | P3 |
| RESET BENCH: BenchRenderer.cs | Numerics, Stopwatch, Thread, buffers BGRA | algoritmo diretamente extraível, sem WPF; NÃO portado | mesma cena/seed; acesso concorrente a Pixels/Active; golden reduzido | P2 |
| Simulação: GameSimBench.cs | Parallel, Stopwatch, SoA | reuso técnico direto; separar snapshot de UI | seed 12345 e double buffer; determinismo a provar; hash reduzido | P2 |
| RAM: MemoryBench.cs | arrays, Parallel, NativeMemory, File.OpenHandle | RAM refatorar; loading reimplementar I/O sem cache | cancelamento divide por steps total; buffers grandes; testes pequenos | P4 |
| Disco: DiskBench.cs | FILE_FLAG_NO_BUFFERING, WRITE_THROUGH, alinhamento 4096 | política temporária e backend próprios; não traduzir flags numericamente | QD real 1/8 diverge do README QD32; validar leitura parcial e limites | P4 |
| Rede: NetworkTest.cs | Ping, sockets, HttpClient, Cloudflare | grande parte portátil; extrair DTO de DiskHealthCard | sempre opt-in, tráfego/timeout; teste local | P4 |
| Relatório: ReportBuilder.cs | JSON/HTML, InfoNode, marca/logo, hostname | reimplementar schema neutro; sem hostname/serial | manter exporter legado; escape HTML existente; privacidade | P1 |
| Energia: BatteryInfo.cs | WMI Battery/PowerPlan, LHM, DiskHealthCard | reimplementar power_supply futuro | ausência não implica desktop/tomada; fixtures | P3 |
| GPU carga: GpuGameBench.cs, GpuRtBench.cs, GpuBurn.cs | D3D11/12/DXC, shaders, user32 | backend Vulkan independente futuro; indisponível | preservar workload; score sem equivalência proibido; teste opt-in | P5 |
| FPS: FrameMonitor.cs, OverlayMetrics.cs, OverlayWindow.xaml*, MainWindow.Overlay.cs | ETW TraceEvent, DXGI Present, user32, WPF | reimplementar, limitações X11/Wayland | sem injeção; ETW/polling e encerramento; sessão real | P5 |
| Drivers: DriverManager.cs, MakerDrivers.cs, Drivers/ | WMI, pnputil, PowerShell, download/assinatura | indisponível; diagnóstico somente leitura futuro | manter; drivers têm licença própria; nunca executar na auditoria | P6 |
| Otimização: WindowsTweaks.cs, Optimizer.cs, StartupManager.cs, UsbCleaner.cs, OcCheck.cs | Registry, ntdll, planos energia, SetupAPI, EventLog | desenho Linux separado; indisponível | efeitos sistêmicos/backup; não executar | P6 |
| Apps: AppInstaller.cs, CorsairSdkInstaller.cs | winget, instaladores, download DLL | indisponível | assinatura não equivale permissão de redistribuição | P6 |
| RGB: LightBackends.cs, CorsairSdk.cs, MainWindow.Integ.cs | TCP OpenRGB, REST Razer, iCUE DLL | protocolo OpenRGB reaproveitável em tese; sem backend agora | opt-in, licença SDK e timeouts; servidor simulado | P6 |
| Jogos: GameLibrary.cs, GameLauncher.cs, Vdf.cs, GameCatalog.cs, HowManyFpsClient.cs | Registry, paths Windows, DPAPI, VDF, API HTTP | parser VDF tecnicamente portátil; lançamento refatorar | backups e dados sensíveis; sem estimativas apresentadas como medições | P6 |
| UI: App.xaml*, MainWindow.xaml*, MainWindow.*.cs, Theme.xaml, ThemeManager.cs, ToastWindow.xaml* | WPF, DispatcherTimer, Win32 devicechange, assets | nova Avalonia MVVM sem assets RESET | original intocado; UI e algoritmos separados; smoke gráfico | P1 |
| StressTester.cs, BottleneckAnalyzer.cs, AppSettings.cs, HardwareErrors.cs | WMI/LHM, persistência local, políticas | sem execução/migração nesta etapa | política térmica não garante segurança; opt-in futuro | fora P1 |

## Dependências e licenças
MIT original preservada em `upstream/ResetExplode/LICENSE`. README atribui MPL-2.0 a LHM,
MIT a TraceEvent/Vortice. System.Management e ProtectedData 10.0.2 são Windows para os usos observados.
Vortice D3DCompiler/D3D11/D3D12/Dxc 3.8.3 e TraceEvent 3.2.8 não entram no núcleo Linux.
Drivers/LEIA-ME.md exclui explicitamente drivers da licença MIT. Corsair DLL não versionada;
assets sem proveniência individual confirmada: nenhum usado no novo app.
Licenças/transitivas devem ser conferidas nos pacotes restaurados; não há aprovação de redistribuição dos drivers.
Workflow publicar.yml publica automaticamente em main/tags: preservado apenas dentro do upstream,
sem workflow de publicação no Silex. Nenhum instalador, benchmark ou serviço original foi executado.

## Globbing e histórico
Não existe csproj na raiz deste workspace. O original está em upstream/ResetExplode e seus globs
não alcançam src/ e tests/ irmãos. Não é necessário alterar Compile Remove no WPF.
Original deve ser mantido como submódulo fixado no SHA auditado; não copiar drivers para os outputs Linux.


## Atualização Fase 2

BenchRenderer.cs foi relido integralmente para especificação da cena/medição e oracle reduzido;
GameSimBench.cs e MemoryBench.cs também foram relidos integralmente. Novos riscos e decisões
em benchmarks-metodologia.md. O renderer Silex não compila a fonte original; a fixture de teste
foi gerada dela num projeto temporário isolado. A matriz acima registra a auditoria inicial,
não uma alegação de que GPU/sensores/outros benchmarks já foram migrados.


### Continuação 0.3.0 — hwmon

Sensores novos usam exclusivamente `/sys/class/hwmon`, leitura sem privilégios, sem reutilizar
bibliotecas Windows de sensores ou seus drivers. Contracts/Core permanecem livres de APIs gráficas
e Windows. CLI `sensors`, snapshot integrado a `info` e painel Avalonia apresentam valores brutos,
fonte/unidade/data e indisponibilidade por canal. Não implementa thermal/power_supply completo,
SMART, telemetria GPU ou controle térmico. BAT1 eventualmente listado pelo hwmon é somente o canal
exposto, não implementação do inventário de bateria. Backend Windows futuro deve implementar os
mesmos contratos sem condicionar algoritmos por SO.


### Continuação 0.4.0

BatteryInfo.cs original foi revisado novamente: depende de WMI e classifica desgaste/saúde.
O novo adaptador power_supply usa dados brutos, sem portar essas classificações. GpuInfoCollector.cs
original depende de DXGI/WMI/NVML; substituição Linux usa DRM/PCI e contadores sysfs opcionais AMD.
Não carrega drivers/binários upstream. Novos diagnósticos não alteram metodologia dos benchmarks.
SMART usa ferramenta externa opcional, com parâmetros somente leitura, limitação de saída e opt-in;
nenhuma reutilização de DiskHealth/DriveMap Windows nem classificação de disco saudável.

### Continuação 0.5.0 — relatórios

ReportBuilder.cs foi revisto: lógica de escaping é portável, mas seus DTOs usam estruturas
Windows/WPF e campos/diagnósticos que não pertencem ao snapshot Linux. HardwareHtmlReport usa
os DTOs neutros existentes, sem copiar template/assets ou inferir saúde. Princípios preservados:
arquivo único offline e escaping; diferenças: sem MachineName, sem histórico de scores,
sem diagnóstico de disco/bateria nem logos. JSON permanece compatível com hardware schema 1.2.
