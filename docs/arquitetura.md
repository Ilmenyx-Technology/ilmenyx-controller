# Arquitetura

Ilmenyx: adaptação independente em C# com .NET 10 LTS e Avalonia 12.1.4
(versão estável fixada, disponível no NuGet). Sem Rust: não há medição justificando outra linguagem.
Contracts: DTOs e IHardwareInfoProvider; Core: JSON e apresentação neutra; Platform.Linux:
leitura limitada e parse injetável; Cli e Desktop: composition roots. Nenhum projeto Windows vazio.
Core/Contracts miram net10.0 neste incremento independente; eventual consumo pelo WPF net8 exige
multitarget validado ou migração explícita. Não afirmar compartilhamento binário atual.

Fluxo: filesystem somente leitura → adaptador Linux → snapshot versionado → CLI/Avalonia → JSON local.
Cada valor inclui fonte, unidade, disponibilidade e motivo. Falhas parciais não destroem o snapshot.
/proc/meminfo kB significa 1024 bytes; sys/block/size usa setores fixos de 512 bytes,
não multiplicar por logical_block_size. Discos são dispositivos do kernel, não volumes montados.
Sem coleta dedicada de serial, hostname, machine-id, UUID, IP ou mountpoints; textos livres podem identificar e exigem revisão.
Cancelamento e limites de leitura/quantidade; sem shell/elevação/rede de produto. SMART separado usa smartctl opcional por opt-in.
UI usa ViewModel notificável, coleta em background e exportação pelo diálogo local da aplicação.

## Proveniência e clean-room
O pedido posterior de independência prevalece sobre a sugestão de copiar código. Algoritmos CPU/RAM seguem a fonte MIT e o engine GameSim adapta métodos originais com atribuição.
Nenhum logo, shader ou asset RESET é incorporado nos projetos Ilmenyx. Auditoria da fonte pelo mesmo implementador
impede alegar clean-room estrito com equipes isoladas. Registrar essa limitação; uma certificação
exigiria implementação por equipe sem acesso às fontes, a partir de especificação aprovada.
A cópia upstream e sua MIT ficam isoladas, com histórico próprio e SHA fixado.

## Continuação Fase 2

Contracts adiciona opções/perfis/resultados de CPU; Core/Benchmarks mantém cena, RNG, tonemapping,
runner e fórmula independentes de UI e SO. Runner é reentrante: câmera, pixels, contadores e workers
pertencem a cada chamada; pixels retornados somente por cópia. Exceções de workers são propagadas
após Join; todos os workers encerram antes da publicação do resultado. Não há cancelamento abandonando
threads em background. Timeout/cancelamento suprimem score. Progresso é opcional, emitido pelos workers;
consumidores precisam encaminhar à thread UI e tolerar callbacks concorrentes/fora de ordem.

CLI valida argumentos e opt-in antes de coletar hardware ou agendar trabalho. Perfil padrão validation
é limitado a uma passada, 1–4 threads e 10 s. Full exige AllowCpuLoad explícito; não é iniciado nos testes.
Cronômetro exclui coleta de hardware, hash e JSON. Método de medição versionado separadamente da cena.
JSON reutiliza o mesmo exporter atômico sem overwrite; relatório cancelado pode ser salvo explicitamente.
Na entrega inicial da Fase 2 a UI mostrava apenas inventário. A integração reduzida está descrita abaixo.

Fixture golden foi gerada com a fonte MIT upstream em projeto temporário e atribuída ao original;
nenhum original é compilado por src/ ou tests/. Gerador fica fora da solução e não executa em CI padrão.
A nova implementação passou por inspeção da fonte pelo mesmo agente: sem certificação clean-room.


## UI CPU reduzida e corpus em resolução original (0.2.1)

ICpuBenchmarkRunner permite injetar execuções controladas nos testes do ViewModel; a implementação
produtiva e suas versões numéricas/de medição não mudaram. CpuValidationViewModel opera na thread UI,
coleta hardware em Task.Run, chama o runner sem bloquear o dispatcher e mantém estado do resultado,
exportação e cancelamento. Usa exclusivamente CpuBenchmarkOptions padrão (Validation, 1 thread).
Bloqueia início duplicado e exportação durante execução; cada resultado possui snapshot próprio.
Cancelamento durante coleta não fabrica relatório. Cancelamento/timeout do runner preservam estado
parcial sem score. Descartar o modelo cancela a operação e suprime notificações tardias.

Progress<T> captura o SynchronizationContext da UI; geração/estado filtram callbacks atrasados,
e progresso nunca regride por ordem de entrega concorrente. O painel converte cópia de pixels para
BGRA little-endian e respeita RowBytes do framebuffer; WriteableBitmap anterior e final são descartados.
A prévia não usa o golden de teste como imagem. JSON reutiliza exportação atômica; diálogo de destino pertence à aplicação Avalonia. Não há novo serviço privilegiado, persistência automática ou dependência NuGet nova.

Os testes referenciam Desktop para exercitar o ViewModel sem inicializar plataforma gráfica;
Avalonia.BuildServices tem assets excluídos também no projeto de testes. Core expõe internos apenas
a Ilmenyx.Tests via InternalsVisibleTo para comparar pixels selecionados, sem API pública para
executar workloads arbitrários e sem dependência Core → UI.


## Sensores e exportação gráfica (0.3.0)

ISensorProvider e SensorSnapshot pertencem a Contracts; Core formata/exporta sem depender de UI/SO.
LinuxPlatform compõe hardware e hwmon. IHwmonFiles permite fixtures, falhas e hotplug sem tocar /sys.
Descoberta é refeita por coleta; não guarda valores de dispositivos removidos. Limites: 64 chips,
128 entradas/chip, 512 leituras, 4096 caracteres/atributo, timeout cooperativo de 2 s (máximo 5 s).
Flags enable/fault/type são respeitadas; permissão negada nesses flags impede interpretação do canal.
Ausência opcional difere de falha de acesso. Cada leitura inclui fonte, data, unidade e qualidade kernel-raw.
IDs hwmon não são identidade persistente; sensores não são classificados por saúde.

Unidades seguem [ABI hwmon](https://www.kernel.org/doc/Documentation/ABI/testing/sysfs-class-hwmon).
Divisores: temperatura/tensão/corrente 1000, potência 1.000.000, RPM 1. Conversões e atribuições de
placa podem exigir calibração externa; não usamos sensors.conf. Conforme [interface hwmon](https://docs.kernel.org/hwmon/sysfs-interface.html),
termistores podem precisar de conversão própria. Por isso temperatura limita drivers a coretemp,
k10temp, acpitz e nvme e rejeita type=4. Valores inválidos/sentinelas tornam leitura indisponível.
Zero válido informado pelo kernel é preservado; ausência nunca produz zero.

SensorMonitorViewModel guarda somente último snapshot, sem sobrepor coletas. Polling opcional de 2 s
usa CancellationToken e encerra com a janela; nenhuma escrita de controle ou serviço privilegiado.
JsonSaveDialog substitui o seletor externo: valida caminho completo/novo e extensão JSON, mas a
proteção definitiva contra corrida/sobrescrita permanece no exporter atômico de Core.
UiSmoke é ferramenta opt-in fora da solução: usa eventos dos controles no próprio dispatcher e
captura a janela, sem automação global ou dependência de ferramentas externas.


## Continuação 0.4.0 — thermal, power_supply, DRM e SMART

DiagnosticSysfsFiles limita roots, nomes e atributos: só leitura, 4096 caracteres por arquivo.
Não abre mode, emul_temp, PWM, PCI config/resource/ROM ou atributos de controle. Testes de caminhos
rejeitam traversal, conectores DRM, números seriais e nomes de partição para consulta SMART.
KernelDiagnosticSensors coleta até 64 zonas térmicas e 64 fontes de energia; timeout próprio de
2 s. Cada fonte tem tipo/status/present com origem. Flags ausentes diferem de acesso negado;
present inválido/negado ou zero impede valores numéricos de bateria potencialmente antigos.
São até 13 atributos opcionais por fonte: valores ausentes são omitidos, erros viram indisponíveis.
Não calcula autonomia, desgaste, totais da máquina ou equivalências energia/carga.

Unidades seguem [ABI thermal](https://www.kernel.org/doc/Documentation/ABI/testing/sysfs-class-thermal)
e [ABI power_supply](https://www.kernel.org/doc/Documentation/ABI/testing/sysfs-class-power).
Thermal é m°C; bateria usa d°C, µV/µA/µW/µWh/µAh. Corrente/potência com sinal são preservadas;
capacidade fica em 0–100%, online/present em 0/1, ciclos em inteiro não negativo.
LinuxSensorProvider agrega hwmon e diagnóstico em paralelo sem esconder fontes repetidas.
Limites agregados: até 1408 leituras e 64 metadados de fontes, sem histórico. Monitor UI
reaproveita o contrato; cancelamento da janela se propaga aos dois adaptadores.

DrmGpuInfoProvider enumera somente cardN, até 16 placas, timeout 2 s. Lê IDs hexadecimais
vendor/device/class via links DRM para [PCI sysfs](https://docs.kernel.org/PCI/sysfs-pci.html).
Não depende de pci.ids nem infere modelo comercial. Contadores opcionais são lidos apenas para
vendor AMD 0x1002: [VRAM em bytes](https://docs.kernel.org/gpu/amdgpu/driver-misc.html) e
[carga percentual](https://docs.kernel.org/gpu/amdgpu/thermal.html). Ausência não representa zero.
VRAM usada maior que total é rejeitada, pois as leituras não são atômicas. IDs válidos disponíveis
não garantem telemetria disponível: campos conservam status próprios. Nenhuma API vendor é carregada.

SmartctlDiagnostic é somente CLI, separado de snapshots/polling. O parser exige opt-in e disco
explícito; SmartctlQuery permite uma lista fixa de argumentos via ProcessStartInfo.ArgumentList,
sem shell/PATH livre, sudo, instalação, auto-probe de transporte ou opções de escrita/self-test.
Invoca --json --health --nocheck=standby,3,5 --device=ata|nvme /dev/NOME.
Há verificação de /sys/block, timeout 3 s, stdout 64 Ki caracteres e stderr 8 Ki; o processo próprio
é encerrado em cancelamento/falha. Código e JSON são validados juntos conforme o
[manual original smartctl](https://github.com/smartmontools/smartmontools/blob/main/src/smartctl.8.in).
Bits de erro 0–2 tornam resultado indisponível; bit 3 é conferido contra o booleano passed.
Demais bits permanecem no código, sem classificações de saúde; JSON bruto/identificadores não
são publicados. Relato positivo do firmware não significa garantia de integridade de dados.

Hardware schema 1.2 adiciona gpus, preservando sensores; agregado de sensores 1.1 adiciona
powerSupplies. Relatório CPU externo continua 1.0 com snapshot de hardware versionado interno;
algoritmo, golden, medição e pontuação CPU não mudaram. Nenhuma dependência NuGet nova.

A temperatura hwmon de amdgpu foi acrescentada à lista de conversões suportadas em 0.4.0,
baseada na ABI documentada em m°C e testada com fixture; hardware AMD real permanece NÃO TESTADO.

## Relatório HTML offline (0.5.0)

HardwareHtmlReport no Core transforma um HardwareSnapshot existente em arquivo único. Dados
variáveis passam por WebUtility.HtmlEncode e entram somente em nós de texto; atributos/CSS são
constantes. Sem scripts, fontes, imagens, forms ou recursos externos; CSP restringe carregamentos.
Formato HTML 1.0 independente do hardware schema 1.2. Versões numéricas e medição CPU não mudam.

ReportFile centraliza a criação temporária ao lado do destino e rename sem overwrite. JSON
reutiliza o mesmo mecanismo; testes verificam cancelamento após escrita, falhas, symlinks,
recusa de overwrite e limpeza. Render HTML verifica CancellationToken durante construção;
UI faz o trabalho em Task.Run e captura o snapshot antes do diálogo, mesmo que o dashboard atualize.
CLI info --html é exclusivo de --json. JsonSaveDialog foi generalizado para .json/.html com
extensão explícita, título correspondente e caminho absoluto. Nenhum navegador é aberto pelo app.

ReportBuilder.cs original foi revisado novamente como referência funcional; o novo template usa
contratos Silex sem incorporar MachineName, logotipos ou classificações de saúde do original.
Nenhuma nova dependência NuGet ou asset foi adicionada. Aviso AMD/NVIDIA compartilhado entre UI/HTML.

## Validação de algoritmos de memória (0.6.0)

MemoryValidationOptions/Result/Report e IMemoryValidationRunner ficam em Contracts. Core executa
cadeia/soma/cópia com tamanho fixo, cancelamento por blocos, workers limitados e timeout por TimeProvider.
Não mede relógio de performance. Estado cancelled/timedOut não recebe hash/soma/score completos.
System.Random(42) e hashes little-endian mantêm a referência auditável; nenhuma API Windows/UI no Core.

MemoryValidationViewModel captura hardware fora do trabalho de memória, impede início duplicado,
exporta JSON com ReportFile e cancela ao fechar. O painel inicia somente por clique e usa 1 worker.
CliArguments aceita bench memory com threads 1/2/4 e rejeita full/allow-cpu-load. Não há bloco de memória
no HTML de hardware nem acoplamento aos backends de disco. O perfil completo exige desenho separado.


## GameSim 0.7.0

Contracts define opções/resultados/snapshot; Core mantém engine de dono único e runner com
passos fixos. Não depende de SO/UI. UI só recebe cópias imutáveis após física, lógica e sort;
nenhum buffer interno atravessa a fronteira. Prévia final top-down sem timer/medição.
CLI e ViewModel usam o runner; JSON usa writer atômico comum. Detalhes e diferenças da fonte MIT
em [metodologia GameSim](gamesim-metodologia.md). Não há comparação de FPS com Windows.


## Desktop 0.8.0 e preflight

DesktopBackend compõe UsePlatformDetect (Skia/fontes), depois override X11/Wayland opcional
por SILEX_DISPLAY_BACKEND. Core/Contracts continuam sem Avalonia. Padrão mantém X11, nativo
opt-in sem fallback. Harness confirma implementação real da janela, não apenas variáveis da sessão.
GameSimFullPlanner só prepara metadados/limites revisáveis; não executa engine ou mede score.
Detalhes e testes em [Wayland/preflight](wayland-validacao.md). Sem_teste.md é registro contínuo.


## Continuação 0.9.0 — runner GameSim experimental

Runner full CLI implementado com opt-in e limites, reutilizando engine matemático parametrizado;
validação padrão/UI continua 256 entidades/16 passos. Build sem erros/avisos e 261 testes passaram,
com relógio/engine controlados para controle/estatística. Carga real de 30.000 entidades NÃO
EXECUTADA; Sem_teste.md SIM-01 permanece aberto. Não declarar aceite pleno de FPS/score nem
comparabilidade Windows. [Metodologia, comandos e evidências](gamesim-completo.md).


## Identidade 0.10.0

Solução/namespaces Ilmenyx.*; CLI assembly/apphost ilmenyx. Company=Bismuth Technology é apenas
crédito mantenedor, sem presunção jurídica. ProductIdentity fornece textos neutros de produto,
origem e compartilhamento; Sobre lê LICENSE/NOTICE/THIRD-PARTY-NOTICES locais copiados pelo props
em src (não herdado pelo WPF). ILMENYX_DISPLAY_BACKEND tem precedência, com alias legado.
Schemas/workloads/IDs/hash não mudam pela marca. LinuxParsers limpa texto limitado e redige padrões
óbvios, sem anonimização garantida. Sem persistência XDG ou migração fictícia. Inventário em
branding/RENAME-INVENTORY.md, decisões em branding/RENAME-DECISIONS.md e privacidade em ../PRIVACY.md.


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


## Atualização 0.21.0 — IPv6 em texto livre

IMPLEMENTADO: ocultação de IPv6 comuns antes de MAC/IPv4 no sanitizador Linux,
sem DNS/sockets ou resolução de interfaces. Build sem avisos/erros, 337 testes
aprovados; fixture cobre saída texto/JSON/HTML e preservação de PCI/horários/modelos.
Casos arbitrários/ofuscados e limites de truncamento continuam PENDENTES DE REVISÃO;
não há anonimização garantida. IPv6 em rótulos reais de outros dispositivos, VM limpa
e matrizes adicionais seguem NÃO TESTADOS. [Evidências e limites](privacidade-ipv6.md).


## Atualização 0.22.0 — HTML de sensores na CLI

IMPLEMENTADO: SensorHtmlReport no Core, snapshot existente, HTML passivo/escapado,
escrita atômica sem overwrite. CLI sensors --html com coleta única, 371 testes
aprovados; coleta real/arquivo/stdout e browser desktop/estreito passaram.
Interface HTML de sensores PENDENTE DE IMPLEMENTAÇÃO; impressão, outros browsers,
VM limpa/bateria/outras placas NÃO TESTADOS. Checklist jurídico/humano PENDENTE.
[Comandos, colaboração Claude e evidências](sensores-html.md).


## Atualização 0.23.0 — HTML de sensores na interface

Substitui a pendência anterior do botão HTML: IMPLEMENTADO e testado em Wayland
nativo/X11. Comando compartilhado JSON/HTML impede reentrada, recusa seleção que
mudou durante o diálogo e suprime escrita/notificações após descarte. Exporta o
snapshot selecionado sem coletar novamente; monitor permanece independente.
Build sem avisos/erros, 392 testes aprovados; monitor real, exportação/cancelamento,
extensão/overwrite, teclado roteado e mínimos até 200% passaram. Interação física,
Orca, VM limpa e revisão humana seguem PENDENTES/NÃO TESTADOS.
[Colaboração Claude, comandos e evidências](sensores-html-ui.md).


## Atualização 0.24.0 — SMART importado offline

IMPLEMENTADO: smart-import recebe JSON smartctl em stdin, verifica metadados/nome,
filtra com parser existente e exporta wrapper histórico JSON/HTML. Sem dispositivo,
processo smartctl ou nova consulta. Build sem avisos, 457 testes aprovados;
relatório NVMe histórico real aceito e erros/timeout/browser validados.
SMART-01 permanece NÃO TESTADO para consulta ao vivo com sucesso sem elevação;
VM/SATA/encodings/tecnologias assistivas e integração UI continuam pendentes.
[Colaboração, comandos e evidências](smart-import.md).

## Atualização 0.25.0–0.29.0 — shell, port completo e módulo Vulkan

Estrutura atual (dependências só descem; `Core`/`Contracts` não conhecem Avalonia, Vulkan nem APIs de SO):

```
Ilmenyx.Contracts   dados e esquemas (JSON versionado)
Ilmenyx.Core        lógica pura: avaliadores de saúde, catálogos, benchmarks de CPU/RAM/GameSim,
                    StressTester, OcCheckRunner, OpenRgbClient, MangoHudConfig, GeneralReport, ReportFile
Ilmenyx.Platform.Linux  leitores de /proc, /sys, journalctl, nvidia-smi, flatpak, systemd --user, O_DIRECT (disco)
Ilmenyx.Gpu.Vulkan  Silk.NET + shaders SPIR-V embutidos: lista de adaptadores, GpuBurn, GpuScene (offscreen, sem janela)
Ilmenyx.Desktop    Avalonia (UI só em código): ShellWindow + páginas; serviços injetáveis (BenchServices, OcServices,
                    OverlayServices, LightingServices) para que o smoke use motores/relógios/servidores falsos
Ilmenyx.Cli        comandos de linha; benchmarks pesados em HeavyBenchCommand com --allow-*-load
```

Princípios que as páginas novas seguem: (1) nada conecta, mede ou muda estado ao abrir; (2) carga, rede e mudanças só por clique com
confirmação visível que cita limites; (3) cada serviço externo é injetável (o smoke nunca inicia carga real); (4) valores ausentes são
`null`/indisponível com motivo, nunca zero; (5) resultados vão para um histórico local (`~/.config/ilmenyx/settings.json`, 0600, nunca enviado).

Decisões de portabilidade: Corsair/Razer SDK (Windows) → OpenRGB em loopback; D3D → Vulkan 1.3 com renderização dinâmica; ETW de FPS → log CSV do MangoHud
gerado por configuração própria; WHEA → linhas do log do kernel (MCE/EDAC/AER/GPU reset) via `journalctl -k`; NVML elevado → comandos `sudo` mostrados, nunca executados.
