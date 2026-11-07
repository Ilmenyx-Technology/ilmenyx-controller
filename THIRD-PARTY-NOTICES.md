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
| Silk.NET.Vulkan (e Silk.NET.Core, Silk.NET.Maths, Silk.NET.Vulkan.Extensions…) | 2.23.0 | MIT |
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

Upstream MIT: Copyright (c) 2026 Gustavo (GustavoReset). Licença integral em LICENSES/MIT.txt e upstream/ResetExplode/LICENSE.
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
RAM adaptados para validação reduzida no Core. A atribuição permanece no runner e em LICENSES/MIT.txt.
O gerador MemoryReference compila apenas métodos RAM extraídos em temporário com dimensões menores;
nenhum método de I/O/driver é incluído. Fixture memory-reference.json é derivada dessa execução
limitada, não de métricas reais de desempenho. Não há certificação clean-room estrita.


## GameSim

GameSimStateEngine adapta métodos de GameSimBench.cs do RESET EXPLODE, copyright (c) 2026
Gustavo (GustavoReset), MIT, commit ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6. Aviso preservado
no código e em LICENSES/MIT.txt. Fixture gamesim-reference.json deriva da execução reduzida do original;
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


## Componentes embutidos no pacote autocontido (0.28.0+)

| Componente | Versão | Licença | Observação |
|---|---|---|---|
| .NET runtime (Microsoft.NETCore.App) | 10.0.x | MIT | embutido por `--self-contained true`; avisos do runtime acompanham o pacote em `licenses/` |
| Microsoft.ICU.ICU4C.Runtime (ICU 72, app-local) | 72.1.0.3 | Unicode License (ICU) | `libicuuc/libicui18n/libicudata`; o texto da licença vai em `licenses/` e deve ser mantido |
| Silk.NET.Vulkan e dependências | 2.23.0 | MIT | bindings; o loader `libvulkan.so.1` e o driver são do sistema, não embutidos |
| MangoHud | 0.8.4 | MIT | bibliotecas 64 bits oficiais sem alteração em `components/mangohud/`; fonte: github.com/flightlessmango/MangoHud (v0.8.4) |
| OpenRGB | 1.1 | GPL-2.0-only | AppImage oficial sem alteração em `components/openrgb/`, executado como programa separado (sem ligação); fonte correspondente exata (commit 8e16de2) publicada em cada release do Ilmenyx como `OpenRGB-1.1-source.tar.gz` (`scripts/fetch-gpl-sources.sh`), além de codeberg.org/OpenRGB/OpenRGB (release_1.1); oferta escrita de 3 anos em `components/openrgb/SOURCE.txt`; texto da licença em `licenses/components/` |

| Fonte Inter (via Avalonia.Fonts.Inter) | 4.x | SIL OFL 1.1 | Copyright (c) 2016 The Inter Project Authors; texto em `licenses/Inter-font-OFL-1.1.txt`; a fonte não é vendida separadamente |
| Material Design Icons (dados de caminho em `Icons.cs`) | @mdi/js 7.4.47 | Apache-2.0 | Pictogrammers; texto em `licenses/MaterialDesignIcons-Apache-2.0.txt` |
| Runtime .NET (embutido) | 10.0 | MIT + avisos de terceiros | `licenses/dotnet-runtime/LICENSE.TXT` e `THIRD-PARTY-NOTICES.TXT` do pacote de runtime |

Inventário, hashes e avisos coletados não equivalem a liberação jurídica de redistribuição.

## Dados de compatibilidade de jogos (não acompanham o pacote; baixados pelo usuário)

- **ProtonDB** — relatos exportados em github.com/bdefore/protondb-data: Open Database License (ODbL) 1.0; conteúdo individual sob Database
  Contents License (DbCL) 1.0. O Ilmenyx monta um índice derivado só na máquina do usuário e mostra a atribuição na página. O índice não é redistribuído.
- **AreWeAntiCheatYet** — `games.json`, licença MIT (Copyright © 2021 Starz0r, Curve).
- **Steam Deck** — status de compatibilidade da loja Steam (Valve), consultado sob demanda. Steam e Steam Deck são marcas da Valve.
- **SteamDB** — não usado como fonte de dados (o FAQ do site proíbe scraping); apenas link.

## Ícones da interface

- **Material Design Icons** (Pictogrammers, pacote `@mdi/js` 7.4.47): traçados SVG de cerca de 40 ícones copiados para
  `src/Ilmenyx.Desktop/Icons.cs`, sob a Apache License 2.0 (Pictogrammers Free License: ícones Apache-2.0, código MIT).
