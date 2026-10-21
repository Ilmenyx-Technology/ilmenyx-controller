# Exportação HTML pela interface — Ilmenyx 0.12.0

IMPLEMENTADO: botões HTML nos painéis CPU, memória e GameSim reduzidos. Usam
ValidationHtmlReport do Core e escrita atômica comum, sem sobrescrever. Antes de
haver resultado os botões ficam desabilitados. O diálogo valida extensão e destino;
cancelar não escreve arquivo. Se o resultado mudar durante a escolha, é necessário
escolhê-lo novamente. Exportar não coleta hardware nem executa workloads.

Cada viewmodel mantém compatibilidade do método JSON e oferece ExportHtmlAsync.
Cancelamento e falhas restauram o estado de exportação. Algoritmos, schemas,
hashes, formatos históricos silex-* e submódulo original não foram alterados.
Não foram adicionados controles de carga completa nem métricas de desempenho.

## Evidências no CachyOS x64

- Build Release da solução: zero avisos/erros; 279 testes aprovados, zero falhas/ignorados.
- Novas teorias dos três viewmodels: ausência de resultado, exportação exata do
  snapshot, cancelamento, sobrescrita recusada, limpeza e nenhuma coleta adicional.
- Smoke Avalonia em Wayland nativo (DISPLAY removido) e X11/XWayland: aprovado.
  Os três HTMLs são byte a byte iguais à renderização dos respectivos JSONs
  exportados anteriormente na mesma sessão. Diálogo cancelado, extensão errada
  e destino existente foram exercitados para os três painéis.
- Chromium/Playwright: três relatórios reais produzidos pela UI abertos em larguras
  390 e 1280 px, altura 900 px. Uma seção validation-result por documento,
  scrollWidth igual à largura do viewport, nenhum script/img/iframe e nenhuma
  entrada de recurso externo. Isso não certifica outros navegadores/acessibilidade.
- Servidor temporário somente loopback, servindo três documentos em memória,
  encerrado após o teste. Nenhum servidor foi adicionado ao produto.

Logs build.txt, tests.txt, tests.trx, ui-build.txt, wayland-ui.txt e x11-ui.txt em
[evidencias/ilmenyx-ui-html](evidencias/ilmenyx-ui-html). Capturas do navegador
restritas pela raiz antiga da ferramenta foram salvas em /tmp e copiadas para
este diretório; a captura preservada é GameSim em 1280 px.

```bash
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-ui-html-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-ui-html-x11
```

NÃO TESTADO: Windows, outras distros/dispositivos/navegadores, interação física e
acessibilidade completa; carga full e AMD/NVIDIA reais continuam em Sem_teste.md.
PENDENTE DE REVISÃO: termos/titularidade e checklist humano antes de publicar.
BLOQUEADO: redistribuição de material de direitos desconhecidos, conforme checklist.
Não houve stress, sudo, mudanças de sistema, push, PR ou release.

Próximo recorte recomendado: validação de acessibilidade dos painéis e diálogos,
com testes automatizados onde disponíveis e matriz manual explícita.
