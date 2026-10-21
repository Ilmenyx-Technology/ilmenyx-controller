# Layout mínimo e texto ampliado — Ilmenyx 0.14.0

Base: ba09538, branch feat/ilmenyx-linux. Inspecionados DashboardWindow,
JsonSaveDialog, AboutDialog, painéis, harness gráfico, projetos, README,
licença e instruções aplicáveis. Sem alteração dos algoritmos, schemas, upstream
ou termos de licença. Não foi definida identidade visual oficial.

## Problemas reproduzidos e correções

- Dashboard 640×480: a linha horizontal cortava Licenças e créditos; com fonte
  ampliada também cortava Exportar hardware HTML. Agora WrapPanel distribui
  as ações em linhas conforme o espaço, preservando a ordem de teclado.
- Diálogo 440×360 com texto a 150%: campo, erro e ações ficavam fora da janela.
  Agora há rolagem vertical, sem stop adicional de Tab; o foco mantém o controle
  alcançável. O caminho permanece numa linha editável com rolagem própria.
- Sobre 480×360 com texto ampliado: o título era cortado horizontalmente.
  Agora quebra em linhas. Abas, texto somente leitura e Fechar continuam
  acessíveis; no mínimo ampliado a área de leitura é pequena e exige rolagem.

IMPLEMENTADO: três mudanças pontuais de layout, sem configuração nova do sistema
ou preferência persistida no produto. A ampliação é uma condição de teste,
não um novo ajuste de fonte no aplicativo.

## Método automatizado

Harness opt-in tools/Ilmenyx.UiSmoke, com janela real e eventos no processo.
Ao concluir os fluxos anteriores de inventário/exportação/CPU/RAM/GameSim reduzidos
e sensores, mede os três mínimos com fatores de texto 1 e 1,5. Valores efetivos
são capturados antes de atribuir fontes para evitar multiplicação por herança.
As fontes são restauradas no dashboard; diálogos de cada caso são descartados.

Verifica controles nomeados da aplicação, campo/erro/ações do diálogo, quatro
abas, texto de leitura, título e Fechar. BringIntoView e espera de geometria
permitem rolagem; o retângulo de cada controle deve caber no viewport com tolerância
de 1 DIP. Elementos internos PART_* de templates não são ações do produto e
não entram na lista de botões. Mede também TextLayout contra Bounds dos TextBlocks
visíveis (tolerância de 2 DIP). Textos longos podem demandar rolagem vertical;
o teste não exige exibir todo o inventário em uma única tela.

Resize tem confirmação por Bounds, tolerância de 1 DIP por arredondamento de pixels.
A execução X11 inicial identificou resize assíncrono, deixando altura intermediária;
o harness passou a aguardar os tamanhos e abrir o dashboard no mínimo desde o início.
Rolagem também aguarda até 2 s antes de reprovar, sem ampliar o limite geométrico.
Não se alteram configuração do compositor, outras janelas ou escala do sistema.
Uma tentativa de usar Resize interno do backend foi rejeitada pelo compilador e
removida; a implementação final usa propriedades públicas da janela.

Evidências finais em [evidencias/ilmenyx-layout](evidencias/ilmenyx-layout):
build/testes/TRX, build do harness, logs dos backends e capturas reais. A reprodução
antes da correção está em before-wayland.txt; inclui também detecção preliminar
de controles PART_* com tamanho zero, excluídos posteriormente do teste de ações.

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-layout-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-layout-x11
```

## Resultados finais

VERIFICADO POR TESTE no CachyOS x64, kernel 7.2.11-1-cachyos, SDK 10.0.100,
Avalonia 12.1.4: build da solução/harness sem avisos ou erros; 279 testes unitários
aprovados, zero falhas/ignorados. Os dois smokes finais encerraram com código 0.
Wayland nativo com DISPLAY removido reportou Avalonia.Wayland.WindowImpl;
X11/XWayland reportou Avalonia.X11.X11Window. Cada backend passou os seis casos,
além da regressão de teclado, exportações reais e validações reduzidas anteriores.

Wayland mediu 640×480 / 440×360 / 480×360 DIP; X11 mediu
640,258×480 / 440,516×360 / 480×360 DIP. Logs registram RenderScaling=1,3 no
dashboard Wayland e aproximadamente 1,2917 em X11; os diálogos reportaram 1.
São os valores observados nesta sessão, sem normalização ou mudança do ambiente.
Arquivos layout-errors.txt finais vazios. Capturas dos dois backends em 150%
foram preservadas; título completo, ações quebradas e controles do diálogo
alcançáveis após rolagem. Aceite técnico restrito a estes casos, não aprovação
humana de acessibilidade completa.

## Limites e próximos passos

NÃO TESTADO: texto a 200% ou superior, matriz de DPI/monitores/compositores/distros,
leitor de tela, entrada física, acessibilidade completa e Windows. As escalas
reportadas em cada log são observações da sessão atual, não uma matriz certificada.
Fontes de controles e templates são ampliadas no harness; isso não reproduz todas
as formas de ampliação usadas por tecnologias assistivas. Capturas usam
RenderTargetBitmap; confirme foco/contraste/usabilidade em avaliação humana.

PENDENTE DE REVISÃO: avaliação assistiva humana e checklist jurídico anterior
à publicação. BLOQUEADO: redistribuição de material com direitos desconhecidos,
conforme checklist existente. Sem stress, sudo, mudança de sistema, push/PR/release.

Próximo recorte recomendado: ergonomia da leitura de licenças em janela pequena,
com mais área de texto e preservação da navegação por teclado; depois ampliar
progressivamente a matriz de texto e DPI. Nenhum novo perfil de carga é necessário.
