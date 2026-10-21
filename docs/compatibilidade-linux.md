# Compatibilidade Linux — incrementos das Fases 1 a 4

| Recurso | Implementado | Evidência / limite |
|---|---|---|
| SO/kernel | sim | /etc/os-release e /proc/sys/kernel/osrelease no CachyOS |
| CPU nome/lógicos | sim | /proc/cpuinfo; 8 lógicos visíveis, não inferência de cores físicos |
| Memória | sim | MemTotal e MemAvailable, kB × 1024; sem estimativa quando ausente |
| Discos | sim | /sys/block/NAME/size × 512, inclui virtuais; sem serial/modelo/saúde |
| JSON | sim | hardware schema 1.2, fontes/unidades/status/motivos, criação sem sobrescrita |
| HTML offline | sim | CLI/UI, arquivo único com escaping, fontes/limitações e criação sem overwrite; Chromium Linux validado |
| CLI | sim | info/capabilities/help; JSON; inválidos exit 2, falha I/O 1, plataforma 3, cancelamento 130 |
| Avalonia | sim | compilação e janela real; backend X11 em sessão com DISPLAY e WAYLAND_DISPLAY |
| Exportação pelo diálogo da aplicação | validada | hardware/CPU/sensores, cancelamento e recusa de overwrite via harness Avalonia no processo |
| Wayland nativo | sim, opt-in | Avalonia 12.1.4, WindowImpl nativo com DISPLAY removido; fluxo gráfico testado no CachyOS/KWin |
| Windows WPF | preservado | fonte sem alterações; build/execução Windows NÃO TESTADOS |
| Outras distros/arquiteturas | não validado | somente host CachyOS x64 disponível |
| Renderizador CPU | experimental | Core + CLI/UI reduzida; imagem reduzida e 16 pixels em resolução/SPP originais, 1/4 threads |
| CPU full/sustentado | implementado, NÃO EXECUTADO | opt-in explícito, timeout, sem proteção térmica |
| Validação CPU na UI | sim, reduzida | início por clique, prévia/status/hash observados; cancelamento/exportação testados no ViewModel |
| Sensores hwmon/thermal/energia | parcial | somente leitura; unidades validadas, falhas explícitas; monitor opcional 2 s |
| GPU DRM/PCI | parcial | IDs reais Intel; AMD carga/VRAM cobertos por fixtures; AMD/NVIDIA reais NÃO TESTADOS |
| SMART | parcial, opt-in CLI | sem acesso automático; consulta real NVMe do usuário (exit 0, passed=true) aceita pelo parser; sem certificação de execução privilegiada do app |
| Algoritmos de memória | validação reduzida | CLI/UI/JSON, oracle upstream 1/2/4 workers; sem latência/GB/s/score ou buffers grandes |
| GameSim full CLI | experimental, carga NÃO TESTADA | opt-in, warmup/duração/timeout/teto; controle/estatística testados com fixtures; SIM-01 aberto |
| GameSim reduzido | sim | 256 entidades/16 passos, oracle 1/2/4 workers, CLI/UI/JSON; sem FPS/score |
| Outros benchmarks | não | Fases 4–5 pendentes |

Diagnóstico padrão sem ferramentas externas obrigatórias, root, rede ou acesso direto a /dev. SMART separado usa smartctl opcional e permissões já existentes.
Timeout de coleta 5 s cooperativo; leitura limitada a 4 Mi caracteres por fonte e 256 dispositivos.
Chamadas de filesystem bloqueadas pelo kernel não têm garantia de interrupção imediata pelo .NET.
Exportação grava temporário no diretório escolhido, renomeia sem overwrite e remove seu temporário
em finally; falha de remoção também é reportada. Não fornece garantia de durabilidade após queda de energia.
Assets originais e referência JPG não foram incorporados como recursos da interface.


A UI CPU não inicia render na abertura e não oferece perfil full. Fecha/cancela solicitando interrupção;
Core aguarda os workers antes de devolver resultado, e ViewModel ignora atualizações após Dispose.
A prévia é 32×18 ampliada por nearest-neighbor, não imagem de referência nem gráfico de desempenho.

Hardware schema 1.2 inclui sensores e GPU; sensores agregados 1.1 incluem energia. Canal bruto não indica função elétrica da placa ou saúde. Temperatura restringe drivers conhecidos e rejeita termistor type=4. Nenhuma proteção térmica automática implementada.


## Atualização 0.8.0 — Wayland e registro de testes

Wayland nativo agora validado no CachyOS/KWin: Avalonia 12.1.4 opt-in, DISPLAY removido e
implementação real Avalonia.Wayland.WindowImpl, sem fallback X11. Regressão X11/XWayland também
passou; 250 testes, build sem avisos/erros. Isso substitui o estado anterior de nativo não
implementado. Outras distros/compositores, Windows, AMD/NVIDIA reais e perfis completos seguem
no registro contínuo Sem_teste.md, conforme nova orientação do usuário. Preparação de GameSim
completo disponível em bench sim-plan, sem carga/medição; não equivale a runner/FPS/score testado.
[Comandos, decisões e evidências](wayland-validacao.md). Commits locais, sem publicação automática.


## Continuação 0.9.0 — runner GameSim experimental

Runner full CLI implementado com opt-in e limites, reutilizando engine matemático parametrizado;
validação padrão/UI continua 256 entidades/16 passos. Build sem erros/avisos e 261 testes passaram,
com relógio/engine controlados para controle/estatística. Carga real de 30.000 entidades NÃO
EXECUTADA; Sem_teste.md SIM-01 permanece aberto. Não declarar aceite pleno de FPS/score nem
comparabilidade Windows. [Metodologia, comandos e evidências](gamesim-completo.md).


## Identidade Ilmenyx 0.10.0

Renomeação controlada de projetos novos/CLI/UI/metadados, preservando formatos e origem MIT.
Diretrizes permanentes acrescentadas ao AGENTS sem apagar anteriores. 272 testes e fluxos Linux
CLI/Wayland/X11 passaram; Sobre/avisos de compartilhamento implementados. Não é liberação legal,
privacidade garantida ou empacotamento final. Marca/manual/organização/titularidade/licenças nativas
permanecem em revisão humana; veja branding/RENAME-DECISIONS.md, ../Sem_teste.md e
legal/PRE-RELEASE-CHECKLIST.md. Exemplos Silex de incrementos anteriores são históricos.


## Ilmenyx 0.11.0

Workspace movido para /mnt/HDD/Github/Ilmenyx, branch local feat/ilmenyx-linux e descrição Git
Ilmenyx, sem alterar remotos/histórico/submódulo. Solução/CLI continuam Ilmenyx.slnx / ilmenyx.
ValidationHtmlReport no Core exporta três reports reduzidos existentes, sem nova coleta/carga;
CLI --html disponível para CPU validation, memória e GameSim. Mesma escrita atômica/escaping,
sem alterar JSON ou metodologia. Build sem erros/avisos, 276 testes e CLI/UI de regressão passaram.
[Detalhes e comandos](validation-html.md). UI HTML de resultados e browser específico pendentes.

Termos próprios corrigidos conforme esclarecimento: usar programa como ferramenta em serviço
remunerado é permitido; vender programa/acesso ou derivar produto/serviço pago do código próprio
não é autorizado. LICENSE MIT e direitos anteriores/terceiros continuam preservados; política
não é open source OSI e exige revisão jurídica antes de publicação (../LICENSING.md).


## Atualização 0.12.0 — HTML dos resultados na UI

IMPLEMENTADO e testado: exportação dos snapshots CPU/RAM/GameSim pela interface,
sem nova coleta/carga, com cancelamento e recusa de sobrescrita. 279 testes passaram;
Wayland nativo e X11 passaram no CachyOS. Browser Chromium em 390/1280 px passou
para os três relatórios. Isto encerra as pendências locais de botões HTML e layout
nessas condições, sem generalizar para outras plataformas ou acessibilidade.
Detalhes e comandos em [validation-html-ui.md](validation-html-ui.md). Registros anteriores são históricos.


## Atualização 0.13.0 — teclado e foco

IMPLEMENTADO e testado no CachyOS: rótulos do caminho/licenças, ajuda de erro,
ciclo de Tab e restauração de foco após diálogos. 279 testes aprovados; smoke
Wayland nativo e X11 com eventos de teclado no processo passou. GUI-02 continua
NÃO TESTADO para Orca/AT-SPI (Orca ausente), entrada física e avaliação assistiva
completa. Não houve instalação nem certificação de acessibilidade.
[Detalhes e limites](acessibilidade-teclado.md).


## Atualização 0.14.0 — layout mínimo e texto ampliado

IMPLEMENTADO: ações do dashboard quebram em linhas, exportação com rolagem vertical
e título Sobre com quebra. Build sem avisos/erros, 279 testes aprovados e smokes
Wayland nativo/X11 aprovados. Mínimos 640×480, 440×360 e 480×360 DIP, texto normal
e 150% por instrumentação no processo: controles medidos alcançáveis após rolagem.
Fecha esse recorte local de layout, sem fechar GUI-01/GUI-02 para outros displays,
200% de texto, leitores de tela ou entrada física. [Evidências e limites](layout-minimo.md).


## Atualização 0.15.0 — leitura de licenças

IMPLEMENTADO: seletor compacto substitui abas de Sobre, preservando integralmente
os quatro documentos e o leitor somente leitura. No mínimo 480×360, área de leitura
aproximada de 208 DIP normal e 135 DIP a 150%. Build sem avisos/erros, 279 testes
aprovados e smokes Wayland nativo/X11 passaram, incluindo seleção integral, bloqueio
de edição, rolagem e teclado. NÃO TESTADO: clipboard real, Orca/AT-SPI, entrada física,
200%+, outras plataformas e acessibilidade completa; GUI-01/GUI-02 continuam parciais.
[Comandos e evidências](licencas-leitura.md).


## Atualização 0.16.0 — texto a 200%

IMPLEMENTADO: quebra de rótulos/títulos com cortes reproduzidos e cabeçalho Sobre
compacto. Build sem avisos/erros, 279 testes aprovados e nove casos de layout por
backend em Wayland nativo/X11 (100/150/200%) passaram. Leitura a 200% de cerca de
161 DIP/159 DIP, mantendo fonte 28 e critério de 120 DIP. Fecha o recorte local
de 200%; registros anteriores são históricos. Acima de 200%, outras matrizes de
DPI/hardware, Orca/AT-SPI, entrada física e Windows permanecem NÃO TESTADOS.
GUI-01/GUI-02 seguem parciais. [Método, evidências e comandos](texto-200.md).


## Atualização 0.17.0 — pacote local linux-x64

IMPLEMENTADO: bundle CLI/desktop framework-dependent, inventário de avisos e hashes;
gerador/validador sem instalação/restore implícito/publicação. Build sem avisos/erros,
279 testes aprovados, integridade/CLI/ELF/ldd e smoke dos assemblies extraídos em
Wayland nativo/X11 passaram. Pacote alterado foi recusado. PKG-01 continua parcial:
VM limpa, outras distros/glibc, runtime ausente, self-contained e instaladores
NÃO TESTADOS. Revisão individual de licenças antes de redistribuir PENDENTE; material
sem direitos conhecidos BLOQUEADO para redistribuição. [Evidências e comandos](pacote-linux-x64.md).


## Complemento de ambiente isolado — pacote 0.17.0

Sem QEMU/imagem guest pronta; VM limpa continua NÃO TESTADA (PKG-01 parcial).
Contêineres locais, sem root/rede/instalação: Debian 12 sem ICU bloqueou o modo
padrão; diagnósticos CLI passaram somente no teste invariant explícito, sem SDK.
Alpine 3.24.1 é incompatível com o bundle glibc; GUI no contêiner NÃO TESTADA e
fontconfig ausente no Debian. Preflight evita repetir aborts por ICU conhecida
ausente. Requisitos documentados; nada altera o modo padrão do aplicativo.
[Comandos, códigos de saída e limites](validacao-container.md).


## Atualização 0.18.0 — guarda ICU na CLI

IMPLEMENTADO e testado: pré-verificação nativa no adaptador Linux; falta de ICU
retorna código 5 antes da coleta, com mensagem e sem JSON parcial. Não é fallback
invariant nem certificação de versões/ABI. Build sem avisos/erros, 284 testes
aprovados; caso negativo real em Debian e modo normal no CachyOS passaram.
VM limpa, Debian padrão com ICU, GUI sem ICU e matriz adicional seguem NÃO TESTADOS.
Os aborts anteriores de 0.17.0 permanecem históricos. [Detalhes e comandos](icu-cli.md).


## Atualização 0.19.0 — doctor CLI

IMPLEMENTADO e testado: `doctor` textual, bibliotecas carregáveis e variáveis de
display não vazias/ausentes, sem provedores de inventário ou workloads. Build sem
avisos/erros e 296 testes aprovados. Host normal: código 0; Debian sem ICU: relatório
com código 5 sem abort; invariant explícito: código 0, sem certificação do modo padrão.
JSON/exportação do doctor indisponíveis; opções recusadas. VM limpa, Debian com ICU
e matrizes assistivas/de hardware seguem NÃO TESTADAS. [Evidências e limites](doctor-cli.md).


## Atualização 0.20.0 — requisitos na interface

IMPLEMENTADO: consulta explícita em Requisitos → Verificar, sem autostart; DTO em
Contracts, apresentação independente em Core, sondagem no adaptador Linux.
CLI e Desktop compartilham texto/limitações sem depender um do outro. Build sem
avisos, 301 testes aprovados; regressões CLI no host e Debian sem ICU passaram.
NÃO TESTADO: VM limpa, outras distros, Orca/AT-SPI e teclado físico. PENDENTE DE
REVISÃO humana: usabilidade e checklist jurídico/marca; direitos desconhecidos
BLOQUEADOS para redistribuição. [Evidências e resultado gráfico](requisitos-ui.md).


### Roteiro de aceite em outra máquina — pacote 0.20.0

IMPLEMENTADO e testado no CachyOS: integridade antes da execução, doctor com prazo,
smoke CLI existente e resultado estruturado sem afirmar certificação de GUI/VM.
Runtime ausente e pacote alterado recusados; destino existente preservado.
NÃO TESTADO: VM limpa e outra distro com ICU; PKG-01 permanece parcial.
[Comandos, evidências e pendências](aceite-maquina.md).
