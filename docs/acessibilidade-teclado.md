# Teclado e metadados acessíveis — Ilmenyx 0.13.0

IMPLEMENTADO e VERIFICADO POR TESTE no CachyOS x64, Avalonia 12.1.4,
.NET SDK 10.0.100: navegação interna da aplicação com sessão gráfica real
KWin Wayland e X11/XWayland. Base do incremento: 5ecc8ab, branch feat/ilmenyx-linux.

O campo do diálogo de exportação agora tem rótulo visível, nome acessível,
LabeledBy e instrução HelpText. Em erro, a mensagem atualiza HelpText e devolve
foco ao campo; o bloco do erro tem LiveSetting=Polite. Isso configura metadados,
mas não prova anúncio por um leitor de tela Linux. A janela ganhou espaço para
o rótulo e mínimo vertical, sem mudança de identidade visual oficial.

Diálogos usam ciclo de Tab local. Os textos das quatro abas de licenças têm nomes
para leitura/cópia, sem alterar seu conteúdo ou direitos. A barra de progresso CPU
e as prévias recebem descrições; resumos textuais continuam contendo contagens e
hashes. O desenho GameSim personalizado não foi certificado por tecnologia assistiva.

O teste encontrou perda real de foco ao cancelar a exportação de hardware:
o botão de origem era desabilitado durante a operação. Agora o foco lógico retorna
ao botão depois de reabilitado. O mesmo retorno foi explicitado em exportações dos
painéis de validação/sensores e no fechamento de Sobre, respeitando janela visível,
controles habilitados e token de vida quando aplicável. Não se ativa outra janela.

## Testes realizados

279 testes unitários passaram (zero falhas/ignorados), build Release sem avisos/erros.
O harness opt-in tools/Ilmenyx.UiSmoke foi ampliado, sem novas dependências:

- Espaço no botão abre exportação; Enter salva arquivo JSON factual, validado por parser.
- Foco inicial no caminho, Tab passa por Salvar/Cancelar e retorna ao caminho;
  Shift+Tab faz o inverso. Caminho relativo é recusado, erro mantém diálogo e foco.
- Escape cancela; foco retorna ao botão tanto após cancelar quanto após salvar.
- Setas trocam a aba Sobre; peer do texto MIT fornece o nome; Escape fecha com retorno.
- Peers dos botões nomeados do dashboard fornecem nomes não vazios.
- Com resultados reduzidos presentes, Tab percorre CPU/RAM/GameSim e botões JSON/HTML,
  pulando cancelamentos desabilitados; Shift+Tab funciona na direção inversa.
- Os três fluxos HTML verificam também retorno do foco ao botão de exportação.
- Regressões de inventário, termos/créditos/MIT, sensores e validações reduzidas passaram.

Wayland exigiu DISPLAY removido e reportou Avalonia.Wayland.WindowImpl; X11 reportou
Avalonia.X11.X11Window. Eventos roteados KeyDown/KeyUp são enviados somente a controles
do processo: testam os handlers Avalonia, não teclado físico nem protocolo de entrada
compositor→aplicação. Não houve injeção global, carga completa, root ou mudanças de sistema.

Durante a preparação, o harness foi corrigido para esperar o snapshot inicial e não
esperar novamente o texto antigo depois de exportar; erros nullable do harness também
foram corrigidos. A regressão final foi executada após essas correções e as de foco.

[Evidências](evidencias/ilmenyx-keyboard): build.txt, tests.txt, tests.trx,
ui-build.txt, wayland-ui.txt, x11-ui.txt e keyboard-error.png. Captura do diálogo
revisada na resolução padrão; não certifica todas as escalas/tamanhos/contrastes.

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-keyboard-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-keyboard-x11
```

## Pendências e próxima tarefa

NÃO TESTADO: Orca/AT-SPI/leitura por voz (Orca ausente, nenhuma instalação automática),
teclado/mouse físicos, contraste mensurado, texto ampliado/escala fracionária, ordem
completa anunciada por tecnologia assistiva, outros compositores/distros e Windows.
GUI-02 de Sem_teste.md fica parcialmente coberto, não encerrado.

PENDENTE DE REVISÃO: avaliação humana assistiva e checklist jurídico anterior à
publicação. BLOQUEADO para redistribuir material com direitos desconhecidos, conforme
checklist existente; esta etapa não alterou licenças nem o upstream.

Próximo recorte seguro: layout no tamanho mínimo e ampliação do texto, com medições
de geometria na janela real e correções pontuais; sem iniciar benchmarks completos.
