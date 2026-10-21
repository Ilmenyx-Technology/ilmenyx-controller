# Requisitos na interface — 0.20.0

IMPLEMENTADO: dashboard → Requisitos → Verificar. Janela inicialmente inativa,
relatório somente leitura, consultas por ação explícita, ciclo local de Tab,
Escape para fechar e retorno do foco ao botão de origem. A consulta roda fora da
thread gráfica; fechar impede atualização tardia. O carregamento nativo é finito,
sem processo externo, mas não é interrompido no meio ao fechar a janela.
Não há inventário, SMART, sensores, rede ou benchmark nessa ação.

O DTO imutável RuntimeRequirements pertence a Contracts. O adaptador Linux captura
bibliotecas/flags; Core escreve a apresentação fixa, sem dependência de Linux,
Avalonia ou APIs culturais. CLI e Desktop usam a mesma apresentação; Desktop não
referencia CLI. O texto ASCII do doctor permanece igual, inclusive o código 0/5
relativo aos requisitos básicos CLI e as ressalvas sobre GUI. As variáveis de
ambiente aparecem somente como presença não vazia, sem mostrar seus valores.

A interface já depende de ICU/fontes para iniciar: esta janela não resolve falhas
anteriores à abertura do aplicativo. Para esses casos, use `ilmenyx doctor`.
Não habilita recursos pela presença de bibliotecas e não certifica ABI, culturas,
fontes ou driver. A faixa ICU e demais limites de [doctor](doctor-cli.md) continuam.

## Verificação desta sessão

- Build Release: zero avisos/erros; 301 testes aprovados, zero ignorados/falhas.
- Harness: abertura inativa, relatório igual ao probe nativo compartilhado,
  somente leitura, Space/Tab/Escape e foco restaurado; provider de teste bloqueado
  confirma ausência de consulta ao abrir e descarte do resultado após fechar.
- X11: fluxo completo e mínimo 480×360 do novo diálogo com texto 100/150/200%.
- Wayland nativo com DISPLAY removido: repetição isolada passou fluxo completo e
  mínimos com texto 100/150/200%. O primeiro teste teve timeout no redimensionamento
  de Sobre a 150%, preservado em wayland-first-timeout.txt. Não houve ajuste do
  produto ou relaxamento geométrico; causa não confirmada. Harnesses Wayland/X11
  tiveram execução sobreposta inicialmente; repetição sequencial passou.
- Pacote local framework-dependent 0.20.0: hashes, avisos idênticos, CLI factual,
  JSON, sensores, recusa de overwrite e ldd passaram. Sem publicação.
- Debian 12 sem ICU: doctor do pacote retorna 5 sem abort; info/capacidades/sensores
  retornam 5 antes da coleta, sem JSON parcial. Contêiner local já disponível,
  sem rede/root/devices/display/SDK, entradas readonly, kernel compartilhado.
  Não equivale a VM limpa. Host CachyOS: doctor=0 no modo padrão.

[Evidências](evidencias/ilmenyx-requisitos-ui): build/tests/TRX, logs de harness,
prints dos novos layouts, pacote/doctor e identidade/estado Debian. Smokes UI usam
os assemblies do harness; o smoke do pacote nesta sessão é CLI. Nenhum inventário
JSON de hardware foi adicionado às evidências versionadas.

## Comandos

SDK/cache local já preparado; destinos de testes/pacote devem ser novos.

```bash
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-requisitos-wayland-repeat
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-requisitos-x11
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-0.20.0-local"
bash scripts/test-linux-package.sh "$PWD/.artifacts/ilmenyx-0.20.0-local/Ilmenyx-linux-x64" /tmp/ilmenyx-requisitos-package-check
ILMENYX_CONTAINER_TEST_DOCTOR=1 ILMENYX_CONTAINER_TEST_ICU_GUARD=1 bash scripts/test-container-linux-package.sh 145325b7e39f "$PWD/.artifacts/ilmenyx-0.20.0-local/Ilmenyx-linux-x64" "$(cat /tmp/ilmenyx-container-location)/runtime" /tmp/ilmenyx-requisitos-debian
```

Runtime-only e imagem são locais, sem instalação pelos scripts. Executar:

```bash
. scripts/env-local-sdk.sh
ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project src/Ilmenyx.Desktop -c Release --no-build --no-restore
dotnet run --project src/Ilmenyx.Cli -c Release --no-build --no-restore -- doctor
```

## Pendências humanas

NÃO TESTADO: Orca/AT-SPI, teclado físico, outras distros/VM limpa e matriz DPI real;
GUI sem ICU não pode ser certificada por este diálogo. AMD/NVIDIA continuam com
aviso de testes não realizados em hardware real. PENDENTE DE REVISÃO: usabilidade
humana e revisão jurídica/marca/licenças antes de publicar. Direitos desconhecidos
continuam BLOQUEADOS para redistribuição. MIT, créditos, upstream e WPF intactos.

Próxima tarefa recomendada: validação externa do pacote em VM Linux glibc com ICU,
fontconfig e .NET 10, quando ambiente disponível; manter PKG-01 parcial até lá.
