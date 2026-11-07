# Leitor de licenças compacto — Ilmenyx 0.15.0

Base: 21212f7, branch feat/ilmenyx-linux. Inspecionados AboutDialog,
DashboardWindow, harness gráfico, projetos, README, LICENSE, instruções aplicáveis
e documentação de layout/acessibilidade. O incremento altera a apresentação
local de documentos já existentes, sem alterar licenças, atribuições ou política comercial.

## IMPLEMENTADO

Sobre usa agora um ComboBox padrão Avalonia com rótulo Documento, seguido por uma
única área de leitura expansível. Os documentos continuam sendo Origem, MIT,
Dependências e Termos Ilmenyx. Origem mantém ProductIdentity.Attribution + NOTICE;
MIT é LICENSE integral; dependências usam THIRD-PARTY-NOTICES; termos combinam
LICENSING e LICENSE-ILMENYX, como anteriormente. Não há busca, download ou upload.
(Atualização 2026-11-07: "MIT (partes do RESET EXPLODE)" lê LICENSES/MIT.txt e "Licença Ilmenyx"
combina LICENSING.md, a permissão adicional e a PolyForm Noncommercial em LICENSE.)
Falha de leitura mantém mensagem explícita de indisponibilidade já existente.

O seletor recebe nome acessível e LabeledBy. O leitor tem nome de controle estável
LicenseTextContent e nome acessível atualizado para o documento selecionado.
Texto permanece somente leitura, selecionável, com quebra de linha e rolagem própria.
Ao trocar documento, conteúdo e caret/seleção são atualizados. O foco inicial é no
seletor; Fechar e retorno ao botão de origem continuam funcionando.

As abas antigas e seus nomes internos de TextBox foram substituídos; não eram
identificadores persistidos nem schema de relatório. Históricos anteriores sobre
abas descrevem versões anteriores. Não há mudança no WPF original ou em benchmarks.
Não foi adicionada preferência de fonte; texto ampliado continua condição do harness.

## Verificação

O harness real tools/Ilmenyx.UiSmoke foi atualizado para o seletor e ganhou:

- Verificação integral de cada documento contra os arquivos locais do pacote,
  incluindo os prefixos/separadores de Origem e Termos; sem truncar ou omitir créditos.
- Ctrl+A seleciona integralmente cada texto. TextInput não altera o documento;
  IsReadOnly é verificado. Não se escreve no clipboard do usuário.
- Ctrl+End move a leitura em documentos longos, com offset vertical observado.
- Seta para baixo seleciona MIT; Tab percorre seletor → leitor → Fechar → seletor.
  F4 abre a lista; Escape fecha a lista preservando a janela/documento, e o segundo
  Escape fecha a janela com foco restaurado. Peers expõem os nomes de documento.
- Quatro documentos no mínimo de 480×360 DIP, com texto normal e 150% no processo.
  Leitor deve ter pelo menos 120 DIP de altura e controles/título devem ser alcançáveis.
  Geometria usa confirmação nativa e tolerâncias documentadas em layout-minimo.md.
- Regressão de exportações JSON/HTML, teclado, sensores e CPU/RAM/GameSim reduzidos.

Na preparação, a troca do Name de um controle já estilizado foi rejeitada pela
Avalonia. Corrigido usando nome estável e atualizando apenas conteúdo e nome
acessível; execução final ocorre após essa correção. Nenhum Name é alterado em runtime.

Evidências e resultados finais: [evidencias/ilmenyx-license-reader](evidencias/ilmenyx-license-reader).
Capturas do layout mínimo ficam no fim dos termos após Ctrl+End; about.png mostra
Origem na janela padrão. A revisão das capturas não certifica leitor de tela.

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-license-reader-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-license-reader-x11
```

## Resultados finais

VERIFICADO POR TESTE no CachyOS x64, kernel 7.2.11-1-cachyos, SDK 10.0.100 e
Avalonia 12.1.4: build da solução/harness sem erros ou avisos; 279 testes unitários
aprovados, zero falhas/ignorados. Os dois smokes completos encerraram com código 0.
Wayland nativo com DISPLAY removido reportou Avalonia.Wayland.WindowImpl;
X11/XWayland reportou Avalonia.X11.X11Window.

No mínimo 480×360 DIP, Wayland mediu 208,46 DIP para a leitura normal e 135,38 DIP
com texto a 150%. X11 mediu aproximadamente 207,48 e 136,26 DIP, respectivamente.
Os quatro documentos passaram nas duas condições por backend; arquivos de erros
geométricos finais vazios. Logs registram a escala real da sessão, sem modificar
configurações. Capturas normais e mínimas foram preservadas, incluindo Origem e
fim dos termos. Aceite técnico restrito a esses casos, sem aprovação humana de
acessibilidade completa ou suporte generalizado.

## Pendências

NÃO TESTADO: clipboard real (não substituído pelo harness), entrada física,
Orca/AT-SPI (Orca ausente), texto 200%+, matriz de DPI/monitores/distros/compositores,
Windows e acessibilidade completa. Eventos roteados verificam handlers no processo,
não entrada física via compositor. GUI-01/GUI-02 de Sem_teste.md continuam parciais.

PENDENTE DE REVISÃO: usabilidade assistiva humana, titularidade/termos/marca e checklist
pré-publicação anteriores. BLOQUEADO: redistribuição de material com direitos
não esclarecidos, conforme checklist. Nenhuma publicação ou conclusão jurídica.
Sem stress, sudo, alteração de sistema, push, PR ou release.

Próximo recorte seguro: testar progressivamente texto a 200% no mínimo, corrigindo
apenas cortes comprovados e mantendo os casos sem ambiente no Sem_teste.md.
