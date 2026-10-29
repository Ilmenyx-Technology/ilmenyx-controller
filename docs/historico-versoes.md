# Histórico de versões

Notas por versão movidas do README. Para o estado atual, veja o [README](../README.md).

## 0.11.0 — HTML de resultados e política comercial

`bench cpu --html`, `bench memory --html` e `bench sim --html` exportam resultado reduzido com
hardware/proveniência/hashes. Sem FPS/score de desempenho; full não aceita esse formato.
[Comandos e evidências](validation-html.md). Build sem avisos/erros, 276 testes aprovados,
CLI real e regressões Wayland/X11. Não há botão novo HTML de resultados na UI ainda.

A política própria permite usar Ilmenyx como ferramenta em serviços pagos, inclusive diagnóstico
e entrega de relatórios. Não autoriza vender o programa, cobrar acesso nem copiar/incorporar o
código próprio para produto/serviço pago. [Termos](../LICENSE-ILMENYX.md) e [mapa](../LICENSING.md);
MIT/terceiros e direitos já concedidos preservados, revisão jurídica antes de publicação.

Pasta/repositório local: `/mnt/HDD/Github/Ilmenyx`; branch `feat/ilmenyx-linux`. O repositório
principal não possui remoto: nenhuma organização ou nome de repositório GitHub foi alterado.

## Exportação dos resultados reduzidos na interface (0.12.0)

Após validar CPU, memória ou GameSim, use o botão HTML do respectivo painel.
O arquivo usa o mesmo resultado/contexto do JSON, sem nova coleta ou execução.
O destino precisa ser novo; cancelamento e recusa de sobrescrita foram testados.
Build sem avisos e 279 testes aprovados; regressões reais em Wayland nativo e X11.
Veja [evidências e limites](validation-html-ui.md).

## Teclado e acessibilidade (0.13.0)

Diálogos têm rótulos/instruções acessíveis e retorno do foco ao botão de origem.
Tab/Shift+Tab, Espaço, Enter, Escape e setas foram testados por eventos internos
em Wayland nativo e X11. Leitor de tela e entrada física NÃO TESTADOS.
Veja [escopo, evidências e comandos](acessibilidade-teclado.md).

## Layout mínimo (0.14.0)

As ações do dashboard quebram em linhas; o diálogo de exportação permite rolagem
e o título de Sobre quebra com texto ampliado.
[Método, evidências e limites](layout-minimo.md).
279 testes aprovados; smokes Wayland nativo e X11 passaram nos tamanhos mínimos
com texto normal e 150% no harness. Outras escalas e tecnologias assistivas pendentes.

## Leitura de licenças (0.15.0)

Sobre agora usa um seletor compacto dos quatro documentos e uma área maior de leitura
somente leitura. Preserva integralmente créditos, MIT, dependências e termos próprios.
[Fluxos de teclado, testes e limites](licencas-leitura.md).
Build sem avisos/erros, 279 testes aprovados e regressões Wayland nativo/X11 passaram.
Área de leitura medida em cerca de 208 DIP normal e 135 DIP a 150%, no mínimo 480×360.

## Texto ampliado (0.16.0)

Rótulos/títulos corrigidos para quebra de linha e cabeçalho Sobre compacto.
[Matriz de texto 100%, 150% e 200%, comandos e limites](texto-200.md).
Build sem avisos/erros, 279 testes aprovados e nove casos de layout por backend
passaram em Wayland nativo/X11. Leitores de tela e outras matrizes continuam pendentes.

## Pacote local linux-x64 (0.17.0)

Bundle CLI/desktop dependente do runtime .NET 10, com hashes e avisos locais.
Scripts de geração e validação não instalam nem publicam; destino deve ser novo.
[Comandos, dependências, evidências e pendências](pacote-linux-x64.md).
279 testes aprovados; CLI extraída e regressões dos assemblies empacotados passaram
em Wayland nativo/X11. Instalação em máquina limpa e revisão de distribuição pendentes.

## Validação complementar em contêiner (pacote 0.17.0)

Debian 12 sem ICU bloqueou os diagnósticos padrão; CLI passou somente com modo
invariant explicitamente usado no teste. Alpine/musl não executa este RID glibc.
Requisitos do bundle: runtime .NET 10, glibc/ICU; GUI também requer bibliotecas como
fontconfig. Isso não certifica VM limpa. [Resultados e roteiro](validacao-container.md).

## Falta de ICU na CLI (0.18.0)

Diagnósticos protegidos agora retornam código 5 e mensagem explícita se ICU não for
encontrada, antes da coleta, sem JSON parcial. Não se ativa invariant automaticamente.
284 testes aprovados e pacote testado no CachyOS; caso negativo reproduzido em Debian
sem ICU. [Evidências, comandos e limites](icu-cli.md).

## Diagnóstico de requisitos (0.19.0)

`ilmenyx doctor` informa bibliotecas nativas e presença das variáveis de display,
sem inventário ou benchmarks. Funciona também para explicar a falta de ICU.
Somente texto, sem --json/--output; 0 indica requisitos básicos CLI encontrados,
5 indica ausência. Isso não certifica funcionamento gráfico.
[Comandos, testes e limites](doctor-cli.md).

## Requisitos na interface (0.20.0)

No dashboard, abra **Requisitos** e pressione **Verificar**. O relatório local usa a
mesma apresentação do `ilmenyx doctor`; abrir a janela não inicia a sondagem.
Não coleta hardware, conecta ao display ou executa benchmarks. Bibliotecas
carregáveis não certificam fontes, ABI, drivers ou funcionamento gráfico.
[Implementação, testes e limites](requisitos-ui.md).


## Roteiro de aceite em outra máquina — pacote 0.20.0

IMPLEMENTADO e testado no CachyOS: integridade antes da execução, doctor com prazo,
smoke CLI existente e resultado estruturado sem afirmar certificação de GUI/VM.
Runtime ausente e pacote alterado recusados; destino existente preservado.
NÃO TESTADO: VM limpa e outra distro com ICU; PKG-01 permanece parcial.
[Comandos, evidências e pendências](aceite-maquina.md).


## Aceite CLI — distinção de globalização

IMPLEMENTADO e testado: resultado do roteiro schema 1.1 distingue padrão/invariant,
registra runtime/versão/códigos e recusa formato desconhecido/ambíguo ou versão
divergente. Casos positivos usam o pacote real; protocolo/metadados negativos usam
cópias de teste. Produto permanece 0.20.0. VM limpa e outra distro com ICU continuam
NÃO TESTADAS; revisões humanas/jurídicas permanecem PENDENTES.
[Comandos e evidências](aceite-globalizacao.md).


## Privacidade em rótulos — 0.21.0

A limpeza de texto Linux agora oculta IPv6 comuns, inclusive escopo de interface,
além dos padrões anteriores. Não é anonimização garantida: revise antes de
compartilhar. [Testes, comandos e limites](privacidade-ipv6.md).


## HTML de sensores pela CLI — 0.22.0

`sensors --html --output /caminho/novo.html` gera relatório offline com fontes,
unidades, horários, qualidade, indisponibilidades e créditos. Não ativa monitor,
SMART ou benchmark; JSON anterior preservado. [Testes e comandos](sensores-html.md).
Exportação HTML de sensores pela interface foi acrescentada em 0.23.0; veja abaixo.


## HTML de sensores na interface — 0.23.0

No painel de sensores, escolha **Exportar sensores HTML**. Se o monitor atualizar
a seleção durante o diálogo, a gravação é recusada; pare o monitor e selecione
novamente. O mesmo controle protege JSON. [Resultados e comandos](sensores-html-ui.md).


## SMART offline — 0.24.0

`smart-import --device nvme0n1 --html --output /caminho/novo.html < relatorio.json`
converte relatório smartctl existente sem consultar o disco. Entrada limitada,
saída filtrada e data da análise distinta da coleta original não confirmada.
[Uso, testes e limites](smart-import.md). Não é garantia de saúde atual.

## Monitor ao vivo — 0.26.0

A janela inicial segue a estrutura do RESET EXPLODE: lateral por grupos, Visão geral com gráfico ao vivo, tabela de componentes e
páginas Sistema, Sensores, GPU, Discos (SMART offline), Energia, Rede, Processos e Validações reduzidas (somente leitura de /proc e
hwmon, histórico só em memória). O painel antigo continua em "Painel clássico de diagnósticos".
[Dados, limites e o que falta](monitor.md). Aplicações agrupadas e GPU por processo ainda não existem.

## SMART importado na interface — 0.25.0

O dashboard tem o painel "SMART · importação offline": informe o disco e o caminho
completo de um JSON smartctl existente, importe e exporte JSON/HTML. Só arquivo regular
(sem link, /dev, /proc, /sys), sem smartctl nem consulta ao disco. Pacote 0.25.0 validado
por CLI em VM Ubuntu 24.04; GUI real, Wayland e Orca na VM seguem NÃO TESTADOS
([evidências](evidencias/ilmenyx-vm-ubuntu-2404-0.25.0/README.md), [limites](smart-import.md)).
