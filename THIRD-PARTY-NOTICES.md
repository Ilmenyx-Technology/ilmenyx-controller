# Dependências de terceiros — inventário NuGet

Licenças declaradas nos manifests restaurados para aplicação e testes; não equivale à auditoria integral de binários.
O app Linux não inclui drivers, PawnIO, DLL Corsair ou assets RESET.
Avalonia.BuildServices excluído via ExcludeAssets=all / PrivateAssets=all; projeto MSBuild expandido conferido sem AvaloniaStats.

| Pacote | Versão | Licença declarada |
|---|---|---|
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | LICENSE (arquivo do pacote) |
| Avalonia.BuildServices | 11.3.2 | MIT |
| Avalonia.Desktop | 11.3.21 | MIT |
| Avalonia.FreeDesktop | 11.3.21 | MIT |
| Avalonia.Native | 11.3.21 | MIT |
| Avalonia.Remote.Protocol | 11.3.21 | MIT |
| Avalonia.Skia | 11.3.21 | MIT |
| Avalonia.Themes.Fluent | 11.3.21 | MIT |
| Avalonia.Win32 | 11.3.21 | MIT |
| Avalonia.X11 | 11.3.21 | MIT |
| Avalonia | 11.3.21 | MIT |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.1 | MIT |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.1 | MIT |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.1 | MIT |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.1 | MIT |
| HarfBuzzSharp | 8.3.1.1 | MIT |
| MicroCom.Runtime | 0.11.0 | MIT |
| Microsoft.CodeCoverage | 17.14.1 | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT |
| Newtonsoft.Json | 13.0.3 | MIT |
| SkiaSharp.NativeAssets.Linux | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.macOS | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.WebAssembly | 2.88.9 | MIT |
| SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT |
| SkiaSharp | 2.88.9 | MIT |
| Tmds.DBus.Protocol | 0.21.3 | MIT |
| xunit.abstractions | 2.0.3 | consultar pacote |
| xunit.analyzers | 1.18.0 | Apache-2.0 |
| xunit.assert | 2.9.3 | Apache-2.0 |
| xunit.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.0.2 | Apache-2.0 |
| xunit | 2.9.3 | Apache-2.0 |

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
