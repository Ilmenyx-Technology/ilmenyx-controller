# Wayland nativo e preparação GameSim — 0.8.0

Em 2026-10-21, CachyOS x64, Linux 7.2.11-1-cachyos, KWin Wayland, .NET SDK 10.0.100/runtime
10.0.0. Aplicação atualizada para 0.8.0; hardware schema permanece 1.2. Nenhum driver ou
configuração do compositor foi alterado. Submódulo original preservado no SHA fixado.

## Investigação e decisão

A versão 11.3.21 anterior usava X11/XWayland. A documentação oficial informa que Wayland
nativo exige Avalonia 12.1 e pacote opt-in; UsePlatformDetect mantém o caminho X11 padrão:
[guia Linux](https://docs.avaloniaui.net/docs/platform-specific-guides/linux),
[anúncio 12.1](https://avaloniaui.net/blog/release-12-1).
Versões consultadas no feed NuGet; Avalonia, Desktop, temas e Wayland fixados em 12.1.4 estável.
Licenças nuspec MIT de Avalonia.Wayland/NWayland verificadas; inventário transitivo atualizado
em THIRD-PARTY-NOTICES.md. Consulta NuGet de vulnerabilidades não reportou pacotes vulneráveis.

DesktopBackend é composition root, sem dependência adicional em Core/Contracts. Variável
SILEX_DISPLAY_BACKEND aceita auto (padrão existente UsePlatformDetect), x11 ou wayland. Typo
retorna exit 2 antes de abrir janela. Wayland explícito chama UseWayland sem fallback para X11;
falha de inicialização não se transforma silenciosamente em sucesso XWayland. Não habilitamos
Wayland automaticamente em outras máquinas sem testar seus drivers/compositores.

UsePlatformDetect também configura renderização Skia/fontes, antes da seleção explícita de
windowing backend. Primeira tentativa só com UseX11 detectou ausência de rendering system;
corrigido mantendo inicialização comum antes do override. Mudanças de API 12 detectadas pelo
build: TextBox.Watermark → PlaceholderText; Bitmap.Save → PngBitmapEncoderOptions no harness.
Sem suprimir avisos/erros para esconder incompatibilidades. BuildServices continua ExcludeAssets=all,
PrivateAssets=all e telemetria opt-out; MSBuild expandido não contém AvaloniaStats.

## Testes executados

Build Release da solução e harness sem erros/avisos. 250 testes passaram, zero falhas/ignorados.
Com SDK temporário desta sessão já disponível, em Bash:

```bash
. scripts/env-local-sdk.sh
dotnet restore Silex.Audit.slnx --disable-parallel -m:1
dotnet build Silex.Audit.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Silex.Audit.slnx -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=phase7-wayland.trx' --results-directory docs/evidencias
dotnet build tools/Silex.Audit.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY SILEX_DISPLAY_BACKEND=wayland \
  dotnet tools/Silex.Audit.UiSmoke/bin/Release/net10.0/Silex.Audit.UiSmoke.dll /tmp/silex-avalonia12-wayland
SILEX_DISPLAY_BACKEND=x11 \
  dotnet tools/Silex.Audit.UiSmoke/bin/Release/net10.0/Silex.Audit.UiSmoke.dll /tmp/silex-avalonia12-x11
dotnet list Silex.Audit.slnx package --vulnerable --include-transitive --no-restore
```

O harness compartilha configuração e DashboardWindow de produção. Verifica tipo concreto da
implementação da janela e exige DISPLAY removido para o teste Wayland. Logs registraram
**Avalonia.Wayland.WindowImpl** no nativo, **Avalonia.X11.X11Window** no X11/XWayland.
TryGetPlatformHandle retorna null no backend Wayland atual, por isso o tipo real e ausência de
DISPLAY são as evidências; variável WAYLAND_DISPLAY sozinha não seria prova.

Ambos passaram: inventário real; diálogos/exportação hardware JSON/HTML, recusa de overwrite e
cancelamento; CPU reduzida/cancelamento/prévia/exportação; memória reduzida; GameSim reduzido
com hash oracle; monitor sensores início/parada/exportação; aviso AMD/NVIDIA visível. Nenhum full
ou stress. Capturas nativas de dashboard e painéis inspecionadas; prévia e dados reais presentes.
Eventos em controles do processo não certificam teclado/mouse físicos, acessibilidade, escala
fracionária ou múltiplos monitores. Outros compositores/distros continuam em Sem_teste.md.

Evidências: docs/evidencias/phase7-wayland-{build,harness-build,tests,ui,nuget-audit}.txt,
phase7-wayland.trx, phase7-x11-ui.txt, phase7-wayland-dashboard.png e phase7-wayland-panels.png.
Aceite técnico somente do fluxo exercitado neste host. Não implica aprovação humana adicional.

## Preparação revisável da simulação completa

A auditoria de GameSimBench.cs original foi consultada novamente: 30.000 entidades, mundo 1000,
grade 64, dt float 1/60, RNG seed 12345; warmup por relógio 2 s antes da medição; Score=AvgFps,
low1=recíproco do p99, sem média do pior 1%. Metodologia reduzida permanece inalterada e seu
hash original continua coincidente após atualização da UI.

Novo comando **bench sim-plan** prepara contrato revisável/exportável, sem instanciar engine,
chamar Step, coletar hardware ou medir desempenho. Não é o runner completo. JSON informa
kind=gamesim-full-preflight, executionStatus=not-executed, executionAvailable=false, score=null.
Propõe workers 1–64, duração 1–10 s, warmup 2 s, timeout que cubra warmup+duração+1 s e máximo
15 s, teto de 200.000 frame times. Estes são limites propostos; não proteções de um runner já
implementado. Futuro limite atingido deve suprimir métricas, sem truncar amostras silenciosamente.

```bash
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- \
  bench sim-plan --threads 2 --duration-seconds 3 --timeout-seconds 8 --json --output /tmp/silex-full-plan.json
```

Destino deve ser novo. CLI testada de ponta a ponta: stdout=arquivo, limites, recusa de sobrescrita
com bytes preservados e flags de execução rejeitadas. Texto diz NÃO EXECUTADO. Testes unitários
cobrem opções, metadados, score null, cancelamento e escrita atômica. Evidências phase7-preflight-cli.txt
e phase7-full-plan.json. FPS/score/30.000 entidades em execução ainda não implementados/testados;
SIM-01/SIM-02 em Sem_teste.md registram requisitos, inclusive autorização específica de carga e Windows.

## Registro e continuidade

Sem_teste.md concentra testes não executáveis neste ambiente e condições para fechar cada caso.
Quando houver todos os requisitos, executar e registrar evidência; ausência de hardware/VM não
bloqueia os recortes independentes testáveis autorizados pelo usuário. Commits são locais, por
incremento; sem push/PR/release. O baseline 0.7.0 foi registrado antes desta atualização.
Próximo passo: implementar runner completo a partir desse contrato, mantendo validação reduzida
como padrão e separando execução de carga explicitamente autorizada. Não executar stress nem
alterar o sistema automaticamente.
