# Relatório HTML offline — 0.5.0

HardwareHtmlReport, em Core, transforma o snapshot já coletado em um arquivo HTML único.
O relatório inclui SO/kernel, CPU, RAM, dispositivos/capacidades, GPU DRM/PCI, sensores, bateria,
capacidades e limitações. Cada leitura preserva valor/unidade, fonte, estado, motivo e timestamp;
sensores também preservam qualidade. Valores indisponíveis não se tornam zero.

O formato HTML é 1.0, enquanto o contrato de hardware permanece schema 1.2 e sensores 1.1.
O relatório não contém resultados CPU/SMART/memória separados, não inicia benchmark nem consulta SMART.
A exportação UI preserva o snapshot selecionado antes de abrir o diálogo, mesmo se houver
atualização posterior do dashboard. Não há upload ou abertura automática de navegador.

## Executar

Com SDK .NET 10 e telemetria desativada, na raiz do repositório:

```bash
dotnet build Silex.Audit.slnx -c Release
dotnet test Silex.Audit.slnx -c Release --no-build
dotnet run --project src/Silex.Audit.Cli -c Release --no-build -- info --html --output ./silex-hardware.html
```

O stdout contém o mesmo HTML salvo; sem --output, somente stdout. --json e --html são exclusivos,
e --html aplica-se somente a info. O arquivo precisa ser novo: uma segunda exportação ao mesmo
destino é recusada. CLI retorna 1 para falha de escrita/overwrite e 2 para argumentos inválidos.

Na interface Avalonia, clique Exportar hardware HTML e informe caminho absoluto novo com extensão
.html. Cancelar não cria arquivo. JSON continua disponível pelo botão anterior. O app padrão
não recebe privilégios ou acesso SMART como consequência da exportação.

## Conteúdo e gravação

Todo dado variável é escapado por WebUtility.HtmlEncode e entra como texto, inclusive fontes,
versões, rótulos, motivos, unidades e metadados. Nenhum valor de hardware entra em CSS ou atributos.
Não há scripts, links, imagens, fontes remotas, iframes ou forms. CSP restringe recursos e permite
somente o CSS inline constante. O aviso AMD/NVIDIA de testes reais ausentes aparece no HTML e UI.
Sem coleta adicional de hostname/serial/UUID/IP/machine-id; os rótulos brutos do kernel são preservados.

A escrita UTF-8 sem BOM usa ReportFile: temporário no diretório escolhido, fechamento e rename sem
overwrite. Falhas/cancelamento removem o temporário. JSON reutiliza esse mecanismo, preservando sua
serialização anterior. Não há garantia de durabilidade diante de queda de energia. Não há nova
biblioteca, asset ou dependência de navegador no produto.

## Aceite Linux executado

- Build Release: zero avisos/erros; 190 testes passaram, zero falhas/ignorados.
- Parser C# SMART de produção aceitou o relatório NVMe real do usuário: available, exit 0, passed=true.
  O agente não consultou novamente o dispositivo nem executou sudo. Não representa garantia de saúde.
- Testes HTML cobrem conteúdo malicioso em dados/metadados/fontes/unidades, cultura pt-BR,
  indisponibilidade versus zero real, cancelamento, falha de escrita, symlinks, limpeza e overwrite.
- CLI real CachyOS: stdout idêntico ao arquivo; nova versão 0.5.0 em JSON; arquivos anteriores preservados.
- Avalonia real: botão HTML, cancelamento, extensão incorreta, recusa de overwrite e igualdade exata
  com o snapshot JSON selecionado. Regressão CPU reduzida/preview/cancelamento e sensores passou.
- Chromium Linux via Playwright: título, seis tabelas e 62 linhas, colunas consistentes, aviso
  AMD/NVIDIA e dados reais de CPU/thermal/bateria. Nenhum script, handler ou recurso externo.
  Console sem erros/avisos; rede da página registrou somente GET do documento em loopback.
- Viewports 1280×900 e 390×844: sem overflow horizontal da página; tabelas rolam em seus contêineres.
  Capturas desktop/mobile/UI foram inspecionadas.

O navegador do harness bloqueou file://. O mesmo arquivo foi servido, sem alterações, por um
servidor temporário somente em 127.0.0.1, expondo apenas uma cópia do relatório. O servidor foi
encerrado após o ensaio. Não alegamos abertura file://, Firefox, impressão/PDF ou interação manual
validados; o conteúdo renderizado foi validado no Chromium Linux automatizado. Não há servidor no app.

Evidências em docs/evidencias com prefixo phase4-html-. SMART em smart-real-validation.txt e
smart-real-filtered.json; fixture mínima em tests/Silex.Audit.Tests/Fixtures/smart-nvme-cachyos.json,
derivada do relatório real com somente smartctl.exit_status e smart_status, sem IDs persistentes.

Fase 4 iniciou somente pelos relatórios. RAM/disco/rede e novos workloads não foram implementados
ou executados neste incremento. Windows, Wayland nativo e AMD/NVIDIA reais continuam não testados.
