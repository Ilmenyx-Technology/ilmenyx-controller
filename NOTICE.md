# Ilmenyx — origem, atribuição e avisos

Required Notice: Copyright (c) 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology) — https://github.com/Bismuth-Technology/ilmenyx

## Origem

O Ilmenyx é uma adaptação independente e não oficial para Linux, derivada do RESET EXPLODE, criado por
Gustavo (GustavoReset). É mantido sob a identidade Bismuth Technology e não tem afiliação, patrocínio ou
endosso da equipe RESET.

Ilmenyx is an independent, unofficial Linux adaptation derived from RESET EXPLODE by Gustavo
(GustavoReset). It is maintained under the Bismuth Technology identity and is not affiliated with,
sponsored by, or endorsed by the RESET team.

- Original: https://github.com/GustavoReset/ResetExplode
- Versão preservada: `ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, no submódulo `upstream/ResetExplode`.
- Copyright (c) 2026 Gustavo (GustavoReset). Licença MIT integral em [LICENSES/MIT.txt](LICENSES/MIT.txt).

## Quais partes são MIT

O código próprio do Ilmenyx está sob a PolyForm Noncommercial 1.0.0 com a permissão adicional
([LICENSE](LICENSE), [LICENSING.md](LICENSING.md)). Os arquivos abaixo adaptam código, dados, estrutura
ou layout do RESET EXPLODE. Por isso o cabeçalho deles é
`MIT AND PolyForm-Noncommercial-1.0.0`, com o copyright do Gustavo e o do
mantenedor: **os trechos originais continuam sob a MIT** (texto em `LICENSES/MIT.txt`) e as
modificações e acréscimos do mantenedor estão sob a PolyForm Noncommercial + a permissão adicional.

- `src/Ilmenyx.Contracts/`: `InfoNode.cs`
- `src/Ilmenyx.Core/`: `AppCatalog.cs`, `BatteryEvaluator.cs`, `BottleneckAnalyzer.cs`, `DeviceChanges.cs`, `DiskHealthEvaluator.cs`, `DriverModels.cs`, `GameCatalog.cs`, `GameProfiles.cs`, `GeneralReport.cs`, `GpuCardBuilder.cs`, `GpuLive.cs`, `GpuSpecDb.cs`, `HealthCard.cs`, `HowManyFpsClient.cs`, `NetworkDiagnostics.cs`, `OcSupport.cs`, `OpenRgb.cs`, `OptimizerLogic.cs`, `OverlayCore.cs`, `OverviewSummary.cs`, `RamInfoBuilder.cs`, `SerialDiagnosis.cs`, `StressTester.cs`, `Vdf.cs`
- `src/Ilmenyx.Core/Benchmarks/`: `CpuPathScene.cs`, `CpuScore.cs`, `DiskBenchModels.cs`, `GameSimBenchmarkRunner.cs`, `GameSimStateEngine.cs`, `MemoryBench.cs`, `MemoryValidationRunner.cs`
- `src/Ilmenyx.Desktop/`: `AppsPage.cs`, `BenchPage.cs`, `BottleneckPage.cs`, `DiskHealthPage.cs`, `DriverPage.cs`, `EnergyPage.cs`, `GamesFpsPage.cs`, `GpuInfoPage.cs`, `HealthCardView.cs`, `LauncherPage.cs`, `LightingPage.cs`, `MakerPages.cs`, `MeterView.cs`, `NetworkPage.cs`, `OcPage.cs`, `OptimizePage.cs`, `OverlayPage.cs`, `ShellWindow.cs`, `StartupPage.cs`, `StressPage.cs`, `SystemInfoPage.cs`
- `src/Ilmenyx.Gpu.Vulkan/`: `GpuBurn.cs`, `GpuRtBench.cs`, `GpuScene.cs`
- `src/Ilmenyx.Gpu.Vulkan/Shaders/`: `rt_trace.comp`
- `src/Ilmenyx.Platform.Linux/`: `LinuxAppInstaller.cs`, `LinuxDiskBench.cs`, `LinuxDiskHealthReader.cs`, `LinuxDriverInventory.cs`, `LinuxGames.cs`, `LinuxGpuCollector.cs`, `LinuxOptimizer.cs`, `LinuxPowerReader.cs`, `LinuxStartupManager.cs`, `LinuxSystemInfoCollector.cs`, `MountedDrives.cs`
- `tools/GameSimReference/`: `Program.cs`
- `tools/MemoryReference/`: `Program.cs`

Também são `MIT AND PolyForm-Noncommercial-1.0.0`, conforme [REUSE.toml](REUSE.toml): o SPIR-V
`src/Ilmenyx.Gpu.Vulkan/Shaders/rt_trace.comp.spv` e as referências geradas executando o código
original (`tests/Ilmenyx.Tests/Fixtures/cpu-original-pixels.json`, `cpu-reference.json`,
`cpu-reference.bgra`, `gamesim-reference.json`, `memory-reference.json`).

Os demais arquivos são só do mantenedor (`PolyForm-Noncommercial-1.0.0`). Os scripts
`scripts/generate-*-reference.sh` e `tools/CpuReference` compilam o original numa pasta temporária,
sem copiar o código dele para o produto.

O aviso histórico "Silex Audit contributors" que existia no LICENSE antigo referia-se apenas ao
mantenedor (Silex Audit é o nome anterior do port). O código dessa fase nunca saiu do controle do
mantenedor e foi colocado sob a licença atual.

## Alterações do port

.NET 10/Avalonia, adaptadores Linux, CLI, relatórios locais, validações reduzidas de algoritmos e
runners experimentais. Nenhum logo, shader, DLL/driver ou asset do RESET foi incorporado. Carga completa
e equivalência de pontuações com o Windows não são certificadas.

## Terceiros e marca

Dependências e termos próprios: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e
[docs/legal/THIRD-PARTY-INVENTORY.md](docs/legal/THIRD-PARTY-INVENTORY.md). Nome e identidade visual:
[LICENSES/LicenseRef-Ilmenyx-Marca.txt](LICENSES/LicenseRef-Ilmenyx-Marca.txt). Ilmenyx e Bismuth
Technology não são apresentados aqui como marcas registradas. Este aviso não concede direitos sobre
marcas ou assets do RESET.
