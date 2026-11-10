# Texto a 200% — Ilmenyx 0.16.0

Base: 272dd99, branch feat/ilmenyx-linux. Revisados AboutDialog, DashboardWindow,
SensorPanel, MemoryValidationPanel, JsonSaveDialog, harness, projetos, instruções,
README e licença. Não altera algoritmos, schemas, política comercial ou upstream.

## Reprodução e implementação

O harness passou a cobrir fatores de texto 1, 1,5 e 2 nos mínimos do dashboard
(640×480), exportação (440×360) e Sobre (480×360), em DIP. A reprodução anterior
falhou a 200%: um rótulo de card, dois títulos de painéis e o rótulo do caminho
excederam a largura; a leitura de licenças caiu a 90 DIP, abaixo dos 120 exigidos.
Logs da reprodução em before-wayland.txt nas evidências.

IMPLEMENTADO: TextWrapping.Wrap nos rótulos dos cards, títulos de sensores e RAM
e rótulo do caminho. Cabeçalho Sobre usa fonte local compacta de 14 em vez de 20,
preservando Ilmenyx e by Ilmenyx Technology completos e permitindo quebra de linha.
O nome e a assinatura no dashboard permanecem com seus tamanhos anteriores.
Nenhuma tipografia oficial de marca foi definida. O leitor mantém o critério de
120 DIP; não se reduz fonte do documento para satisfazer o teste.

A matriz gráfica usa o método existente de ScaleText: captura valores efetivos
antes de atribuir o fator, sem multiplicação cumulativa por herança, e restaura
os valores no dashboard. Não altera escala, fonte ou configuração do compositor.
Isso verifica condições instrumentadas no processo, não todas as formas de zoom.

## Verificações

Harness opt-in tools/Ilmenyx.UiSmoke com janelas reais: nove casos de layout por
backend. Confirma tamanho nativo, retângulos alcançáveis com BringIntoView/rolagem,
TextLayout dentro de Bounds e os quatro documentos integrais somente leitura.
Mantém seleção Ctrl+A, recusa de edição, Ctrl+End com offset vertical, seletor de
documentos, F4/Escape, ciclo de Tab e retorno do foco. Executa também as regressões
anteriores de inventário, sensores, JSON/HTML e CPU/RAM/GameSim reduzidos.
Sem clipboard modificado, teclado/mouse global, carga completa ou stress.

Fontes dos documentos no teste são 14×fator, portanto 28 no caso 200%; conteúdo
não é truncado. Acesso ao texto longo exige rolagem; o teste não requer exibição
integral do inventário ou da licença em uma única tela.

[Evidências](evidencias/ilmenyx-text200): build.txt, tests.txt/TRX, ui-build.txt,
wayland-ui.txt, x11-ui.txt, reprodução anterior e capturas dos casos 200%.
Arquivos de erros finais e tamanhos medidos ficam junto aos logs.

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-text200-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-text200-x11
```

## Resultados finais

VERIFICADO POR TESTE no CachyOS x64, kernel 7.2.11-1-cachyos, SDK 10.0.100,
Avalonia 12.1.4: solução e harness compilados sem avisos ou erros; 279 testes
unitários aprovados, zero falhas/ignorados. Ambos os smokes completos encerraram
com código 0, nove casos de layout por backend, erros geométricos finais vazios.
Wayland exigiu DISPLAY removido e reportou Avalonia.Wayland.WindowImpl;
X11/XWayland reportou Avalonia.X11.X11Window. Regressões de teclado/exportação,
seleção/leitura e validações reduzidas também passaram.

No mínimo de Sobre, os quatro documentos mediram 160,77 DIP de leitura a 200%
em Wayland e 159,48 DIP em X11. A fonte do texto foi 28; mínimo exigido continua
120 DIP. Capturas dos três mínimos em 200% foram preservadas e revisadas.
Topo do dashboard e fim dos termos/diálogo após rolagem aparecem nas capturas.
As escalas reportadas são da sessão atual, sem alterar configurações de sistema.
Aceite técnico restrito às condições observadas, não certificação assistiva.

## Limites

NÃO TESTADO: texto acima de 200%, matriz adicional de monitores/DPI/compositores,
Windows, outras distros/dispositivos, Orca/AT-SPI, clipboard real e entrada física.
GUI-01/GUI-02 continuam parcialmente cobertos, sem certificação de acessibilidade.
Os registros anteriores de 200% NÃO TESTADO são históricos, supersedidos apenas
para os casos desta matriz local.

PENDENTE DE REVISÃO: avaliação assistiva humana e checklist jurídico anterior à
publicação. BLOQUEADO: redistribuição de material com direitos desconhecidos,
conforme checklist existente. Sem root, instalação, mudanças de sistema, push,
PR ou release. MIT, demais termos e SHA do upstream permanecem preservados.

Próximo recorte recomendado: gerar e validar um pacote local linux-x64 em diretório
de teste, sem publicar, mantendo a instalação em máquinas limpas como pendência
até haver VM/dispositivo disponível. Não iniciar carga completa nesse processo.
