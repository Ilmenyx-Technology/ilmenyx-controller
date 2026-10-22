# HTML de sensores na interface — Ilmenyx 0.23.0

IMPLEMENTADO: painel de sensores oferece **Exportar sensores HTML**, ao lado de
JSON. Usa o mesmo SensorHtmlReport do Core e o diálogo local com extensão .html,
aviso de compartilhamento e exigência de arquivo novo. Não inicia leitura,
monitoramento, SMART ou benchmark. JSON e HTML compartilham o novo comando de
exportação do SensorMonitorViewModel; UI continua sem provider novo.

## Seleção, concorrência e cancelamento

O comando captura a referência do snapshot antes do diálogo e marca IsExporting,
desabilitando os dois botões de exportação até concluir/cancelar. Reentrada não
abre outro diálogo. O monitor continua independente: se trocar o snapshot ou
iniciar leitura enquanto o destino é escolhido, a gravação é recusada com orientação
para parar o monitor e selecionar novamente. Nenhum arquivo é criado nesse caso.
A guarda também passa a valer para JSON, que antes exportava o snapshot capturado
sem verificar a troca durante o diálogo.

Após a guarda, a escrita usa apenas o snapshot selecionado, fora da thread gráfica,
com os writers existentes e sem nova coleta. Atualizações posteriores do monitor
não substituem os dados dessa escrita. Token do chamador é ligado ao descarte do
viewmodel; pré-cancelamento não abre diálogo, descarte impede escrita ao retornar
do seletor e suprime notificações tardias. O callback do seletor não é interrompido
à força: a verificação ocorre quando ele retorna. Falhas I/O/argumento/destino
preservam o snapshot e reabilitam os botões; retorno de foco ao botão correspondente.

## Colaboração e verificação

Claude Code propôs os testes do viewmodel em chamada CLI separada, sem ferramentas
ou edição concorrente. Agente principal implementou UI/viewmodel/harness e revisou
a proposta antes de executar. Foi corrigida uma asserção proposta que esperava
sobrescrita, contrariando o requisito de preservar arquivos; adicionados casos
JSON e igualdade integral com o render esperado. Não é revisão humana nem
aprovação independente do código final pelo auxiliar.

Build Release sem avisos/erros; **392 testes aprovados**, zero falhas/ignorados.
21 novos casos: HTML/JSON sem coleta extra, extensão e estado durante seletor,
ausência de snapshot, leitura em andamento, cancelamento antes/depois do seletor,
seleção alterada, reentrada, erros reais em temporários, overwrite recusado,
descarte enquanto seletor aguarda e supressão de notificações. Fixtures provam
controle de código; não são certificação de placa/sensor.

CachyOS x64/KWin, runtime 10.0.0, Avalonia 12.1.4:

- Wayland nativo com DISPLAY removido e Avalonia.Wayland.WindowImpl passou.
- X11 com Avalonia.X11.X11Window passou sequencialmente.
- Monitor real atualizou snapshot durante o diálogo HTML; gravação recusada sem
  arquivo. Após parar, exportação HTML igual ao render do JSON selecionado.
- Cancelar diálogo, extensão errada, destino existente, exportação real, foco,
  Space/Tab/Shift+Tab e ordem JSON/HTML passaram via eventos no processo.
- Botões alcançáveis e texto sem corte nos mínimos com fontes 100/150/200%;
  leituras, licenças, requisitos e validações reduzidas anteriores regredidos.

Pacote local framework-dependent 0.23.0 passou aceite CLI padrão/invariant e
negativos de protocolo/versão. O desktop publicado foi copiado para temporário,
acrescentando SOMENTE Ilmenyx.UiSmoke.dll; runtimeconfig/deps do pacote e
ILMENYX_EXPECT_DESKTOP_DIR conferem Assembly.Location. Regressões completas em
Wayland/X11 desses assemblies estão registradas na conclusão abaixo. Harness não
entra no bundle. Metadados registram SHA base e árvore dirty ao empacotar, sem
prometer reproduzir o novo código somente pelo SHA base.

[Evidências](evidencias/ilmenyx-sensors-html-ui): build/tests/TRX, smokes nativos,
pacote e identidade dos assemblies. Inventário, HTML/JSON reais e screenshots com
leituras permanecem em temporários fora do Git. Render HTML/browser continuam os
do Core testado em 0.22.0; não houve nova validação em browser nesta sessão.

## Comandos

Com SDK/cache da sessão preparados (checkout novo segue README):

```bash
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet run --project src/Ilmenyx.Desktop -c Release --no-build --no-restore
# Wayland explícito:
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project src/Ilmenyx.Desktop -c Release --no-build --no-restore
```

No painel, pare o monitor para manter a seleção estável e escolha Exportar sensores
HTML. O destino deve ser novo. Reproduzir o harness (somente validações reduzidas):

```bash
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-sensors-html-ui-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-sensors-html-ui-x11
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-0.23.0-local"
bash scripts/test-machine-acceptance.sh "$PWD/.artifacts/ilmenyx-0.23.0-local/Ilmenyx-linux-x64" /tmp/ilmenyx-sensors-html-ui-package-check
```

Destinos novos; build/publish sequenciais. Para smoke de assemblies extraídos,
seguir protocolo de docs/pacote-linux-x64.md e logs package-wayland/package-x11.

## Pendências

NÃO TESTADO: teclado físico/Orca/AT-SPI, outras distros/VM limpa, impressão, hardware
com bateria ou AMD/NVIDIA adicionais, encerramento por sinais externos do SO com
exportação em andamento. Teste de Dispose é fixture de estado, não certificação de
fechamento externo pelo compositor. Não há novos scores ou benchmark de carga.

PENDENTE DE REVISÃO: usabilidade humana e checklist jurídico/marca/licenças;
direitos desconhecidos continuam BLOQUEADOS para redistribuição. WPF, upstream,
MIT, créditos e histórico preservados. Sem root, drivers, stress, push ou release.
Próxima validação recomendada: interação humana do fluxo, sobretudo a orientação
de parar o monitor, além do aceite pendente em VM Linux glibc com ICU.


## Conclusão do pacote

Wayland nativo e X11 passaram também com o desktop publicado, incluindo monitor
real/troca de seleção, HTML igual ao JSON selecionado, cancelamento/overwrite,
teclado e mínimos até 200%. Comparação SHA256 de todos os arquivos da cópia de
desktop contra o bundle passou; apenas Ilmenyx.UiSmoke.dll acrescentado para teste.
Não houve modificação do bundle ou inclusão do harness no archive.
