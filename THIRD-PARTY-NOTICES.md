# Dependências de terceiros — inventário NuGet

Licenças declaradas nos manifests restaurados para aplicação e testes; não equivale à auditoria integral de binários.
O app Linux não inclui drivers, PawnIO, DLL Corsair ou assets RESET.
Avalonia.BuildServices excluído via ExcludeAssets=all / PrivateAssets=all; projeto MSBuild expandido conferido sem AvaloniaStats.

| Pacote | Versão | Licença declarada |
|---|---|---|
| Avalonia | 12.1.4 | MIT |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | LICENSE (arquivo do pacote) |
| Avalonia.BuildServices | 11.3.2 | MIT |
| Avalonia.Desktop | 12.1.4 | MIT |
| Avalonia.FreeDesktop | 12.1.4 | MIT |
| Avalonia.FreeDesktop.AtSpi | 12.1.4 | MIT |
| Avalonia.HarfBuzz | 12.1.4 | MIT |
| Avalonia.Native | 12.1.4 | MIT |
| Avalonia.Remote.Protocol | 12.1.4 | MIT |
| Avalonia.Skia | 12.1.4 | MIT |
| Avalonia.Themes.Fluent | 12.1.4 | MIT |
| Avalonia.Wayland | 12.1.4 | MIT |
| Avalonia.Win32 | 12.1.4 | MIT |
| Avalonia.X11 | 12.1.4 | MIT |
| HarfBuzzSharp | 8.3.1.3 | MIT |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT |
| MicroCom.Runtime | 0.11.6 | MIT |
| Microsoft.CodeCoverage | 17.14.1 | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| NWayland | 0.11.0 | MIT |
| SkiaSharp | 3.119.4 | MIT |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT |
| Tmds.DBus.Protocol | 0.94.1 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | consultar https://raw.githubusercontent.com/xunit/xunit/master/license.txt |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| xunit.assert | 2.9.3 | Apache-2.0 |
| xunit.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.0.2 | Apache-2.0 |

Manifest de licença em arquivo (ANGLE) deve acompanhar eventual redistribuição;
Skia/HarfBuzz incluem componentes nativos e avisos internos a revisar no empacotamento.
Pacotes de teste não devem compor distribuição do aplicativo.
Consulta NuGet --vulnerable --include-transitive nesta sessão não reportou pacotes vulneráveis;
isso não certifica ausência de falhas nem cobre o runtime/OS. Use patches oficiais atuais em produção.

Upstream MIT: Copyright (c) 2026 Gustavo (GustavoReset). Licença integral em upstream/ResetExplode/LICENSE.
Drivers têm termos próprios; não autorizados pela MIT do aplicativo.


Fixture CPU: cpu-reference.bgra foi gerada de BenchRenderer.cs MIT do GustavoReset em resolução reduzida.
Metadados e atribuição acompanham a fixture. Original compilado somente pelo gerador opt-in em /tmp,
nunca pelos projetos Silex ou nos binários distribuíveis. Nenhuma nova dependência NuGet nesta fase.

Fixture complementar cpu-original-pixels.json: 16 pixels gerados da fonte MIT original inalterada,
com resolução/SPP originais e atribuição no JSON; não compõe recursos visuais do produto.


## smartmontools opcional

Silex pode invocar o executável smartctl já instalado, por opt-in somente leitura. Nenhum binário
ou código smartmontools é incorporado/redistribuído. O projeto possui licença GPL própria; veja
[fonte e licença](https://github.com/smartmontools/smartmontools). Instalação/redistribuição futura
exige revisão dos termos do pacote; MIT do Silex não relicencia a ferramenta externa.

## Algoritmos de RAM e fixture

MemoryBench.cs de RESET EXPLODE (GustavoReset, MIT, commit fixado) é a referência dos algoritmos
RAM adaptados para validação reduzida no Core. A atribuição permanece no runner e na LICENSE.
O gerador MemoryReference compila apenas métodos RAM extraídos em temporário com dimensões menores;
nenhum método de I/O/driver é incluído. Fixture memory-reference.json é derivada dessa execução
limitada, não de métricas reais de desempenho. Não há certificação clean-room estrita.


## GameSim

GameSimStateEngine adapta métodos de GameSimBench.cs do RESET EXPLODE, copyright (c) 2026
Gustavo (GustavoReset), MIT, commit ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6. Aviso preservado
no código e LICENSE. Fixture gamesim-reference.json deriva da execução reduzida do original;
oracle em temporário não compõe distribuição. Sem assets originais, nova dependência ou
certificação clean-room estrita. Ver docs/gamesim-metodologia.md.


## Wayland nativo — 0.8.0

Avalonia e temas atualizados para 12.1.4, necessário para o backend nativo opt-in. Novo pacote
Avalonia.Wayland 12.1.4: MIT, copyright 2013–2026 The AvaloniaUI Project; repositório AvaloniaUI/Avalonia
commit 2d79ba55fae1b1e71c69f93a668080c9029e6a31. Dependência NWayland 0.11.0: MIT,
repositório kekekeks/NWayland commit 2c37c6f84c45e2e0428221b24ff486187e8ae873.
Manifests nuspec restaurados conferidos. SkiaSharp passa a 3.119.4 e HarfBuzzSharp a 8.3.1.3;
licenças declaradas MIT. Revisar avisos nativos completos no empacotamento, sem distribuição nesta tarefa.
Avalonia.BuildServices continua excluído dos assets/privado, com telemetria desativada.


Runner GameSim 0.9.0: medição/warmup adaptados de GameSimBench.cs MIT GustavoReset, com
aviso preservado no novo GameSimBenchmarkRunner.cs. Sem nova dependência; carga completa ainda
não executada. Metodologia e diferenças versionadas em docs/gamesim-completo.md.
