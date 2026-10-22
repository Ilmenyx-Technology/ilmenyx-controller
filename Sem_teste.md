# Ilmenyx — verificações pendentes

Registro contínuo solicitado pelo usuário em 2026-10-21. Quando SDK, display, hardware e autorização
necessários estiverem disponíveis, executar o teste e atualizar este arquivo com data, ambiente e
link para evidência. Compilação, fixture e emulação não substituem execução no sistema/hardware alvo.
Não marcar como concluído sem evidência. Casos implementados parcialmente são identificados abaixo.

| ID | Caso / estado | Por que ainda NÃO TESTADO | Requisitos e procedimento para fechar |
|---|---|---|---|
| WIN-01 | WPF original: build e execução | Host atual é Linux; Wine não certifica Windows | VM/máquina Windows, SDK .NET 8, `dotnet build upstream/ResetExplode/ResetExplode.csproj -c Release`; abrir app e testar diagnóstico sem carga. Preservar SHA original. |
| WIN-02 | Silex no Windows | Backend de hardware Windows não implementado; UI atual compõe Linux | Implementar adaptador separado, depois build/testes e fluxo gráfico/CLI em Windows. Não confundir com WIN-01. |
| SIM-01 | GameSim completo: 30.000 entidades, warmup 2 s e medição | Runner completo experimental implementado em 0.9.0; testado controle/estatística com fixtures, não carga real. Execução sustentada permanece fora da autorização anterior de desenvolvimento sem stress | Plano revisável em `bench sim-plan`; runner em `bench sim-full --allow-cpu-load`. Ver docs/gamesim-completo.md. Obter autorização explícita para carga curta; testar cancelamento/timeout, FPS/recíproco p99/score e JSON. A preparação não é execução. |
| SIM-02 | Comparação GameSim Linux/Windows | Falta SIM-01 e ambiente Windows equivalente | Mesma revisão/workload/runtime/configuração registrados em ambos; warmup por relógio pode produzir estados diferentes. Não afirmar comparabilidade antecipadamente. |
| CPU-01 | Perfil CPU full e score experimental | Implementado com opt-in, porém carga pesada não executada | Autorização específica e máquina adequada; usar perfil full explícito documentado no README, verificar duração/timeout/cancelamento. Não colocar na suíte automática. |
| GPU-01 | AMD real: identificação, carga e VRAM | Nenhuma GPU AMD disponível | Máquina AMD/Linux com amdgpu e fontes DRM/sysfs; conferir leituras e ausência/falha. Fixtures existentes não certificam hardware. |
| GPU-02 | NVIDIA real: identificação | Nenhuma GPU NVIDIA disponível | Máquina NVIDIA/Linux; verificar IDs DRM/PCI. Telemetria vendor NVIDIA não implementada. |
| GPU-03 | Intel: telemetria e outros modelos | Somente IDs PCI do modelo do host verificados; telemetria não implementada | Adaptador validado e hardware correspondente. Não inferir VRAM compartilhada de iGPU. |
| SMART-01 | Consulta app sem elevação com sucesso, SATA e outros controladores | Neste host a consulta sem privilégio falha. Relatório NVMe obtido pelo usuário foi validado pelo parser | Permissão já existente e disco autorizado; `smart --device NOME --allow-device-query --json`, leitura apenas. Não executar sudo no app. USB/RAID e outros transportes não implementados. |
| SENSOR-01 | Sensores em outras placas/CPUs e bateria física | Cobertura real restrita às fontes presentes no host atual | Hosts com bateria/hwmon/thermal pertinentes; comparar unidades/fontes, desconexão/ausência/permissão. Sem calibração ou proteção térmica presumida. |
| DISTRO-01 | Ubuntu/Debian | Sem VM/dispositivo dessa distro | .NET 10 e dependências gráficas disponíveis; build/teste, CLI info/sensors, UiSmoke X11/Wayland conforme ambiente. |
| DISTRO-02 | Fedora | Sem VM/dispositivo dessa distro | Mesmo roteiro DISTRO-01; registrar versão/compositor/kernel/driver. |
| ARCH-01 | ARM64 e outras arquiteturas | Host/cache/binaries de teste são x64 | SDK/runtime e bibliotecas nativas da arquitetura; executar suíte/oracles e CLI/UI, revisar divergências numéricas. |
| GUI-01 | Escala fracionária, múltiplos monitores e outros compositores | Harness no compositor/monitor atual não cobre toda a matriz | Dispositivos/compositores GNOME/KDE/etc.; testar escala, foco, resize, diálogos e reconexão. Eventos no processo não certificam teclado/mouse físicos. |
| GUI-02 | Interação manual, acessibilidade e tecnologias assistivas | Automação atual aciona controles no processo | Sessão e ferramentas apropriadas; teclado, navegação, leitor de tela e diálogos reais, sem injeção global em outras janelas. |
| IO-01 | Disco/loading/RAM de carga/rede/GPU benchmark | Não implementados; requerem desenho próprio e autorização de carga/tráfego | Especificar limites/cache/temporários/endpoint e metodologia antes de executar. Nunca disco bruto nem endpoints automáticos. |
| PKG-01 | Distribuição/pacotes em máquinas limpas | Builds locais não certificam instalação | Publicação local por RID e teste em VMs, licenças e dependências nativas; sem publicar releases automaticamente. |

## Verificado anteriormente no CachyOS x64

Inventário factual CLI/UI/JSON/HTML; CPU reduzida e corpus de pixels; RAM reduzida; GameSim
reduzido com oracle em 1/2/4 workers; sensores presentes, monitor e exportações; SMART NVMe no
parser a partir do relatório real fornecido pelo usuário. Detalhes em docs/validacao.md.
AMD/NVIDIA permanecem com aviso visível “Testes não realizados em hardware real”, conforme
exceção autorizada. Não ampliar suporte a partir das fixtures.

## Fechado neste incremento — Wayland nativo (2026-10-21)

CachyOS x64, KWin Wayland, Linux 7.2.11-1-cachyos, Avalonia 12.1.4. Janela real nativa
`Avalonia.Wayland.WindowImpl`, com DISPLAY removido e UseWayland sem fallback. Hardware,
JSON/HTML, diálogos, CPU/RAM/GameSim reduzidos e sensores passaram; X11/XWayland também passou
com `Avalonia.X11.X11Window`. Suíte 250 testes aprovados. Evidências e limites em
[docs/wayland-validacao.md](docs/wayland-validacao.md). GUI-01/GUI-02 continuam pendentes para a
matriz adicional e interação manual. Não generalizar o resultado para outros dispositivos.

Preparação SIM-01 também testada: `bench sim-plan` e exportação atômica. Não fecha SIM-01:
o runner experimental existe desde 0.9.0, mas não há FPS ou score medidos em carga real; plano e docs deixam isso explícito.


## Atualização 0.9.0

261 testes passaram, incluindo controle e estatística do runner completo com engine/relógio de
teste e entrada pública pré-cancelada sem alocar física de 30.000 entidades. Não fecha SIM-01.
Sem execução sustentada, Windows ou comparação de desempenho. Regressões reduzidas Linux
CLI/UI continuam documentadas em docs/gamesim-completo.md.


## Identidade 0.10.0

IMPLEMENTADO e testado no CachyOS: nomes/metadados Ilmenyx, assinatura Bismuth Technology,
créditos/Sobre/MIT/dependências, aviso de compartilhamento e redação de padrões óbvios em fixtures.
272 testes aprovados; regressões CLI/Wayland/X11. Nomes Silex nas etapas anteriores são históricos.

NÃO TESTADO: pacote instalado/VM Windows/outros dispositivos para nova identidade; interação física,
acessibilidade e matriz gráfica continuam nos casos existentes. Privacidade em rótulos arbitrários,
IPv6 e formatos não previstos exige revisão adicional; não há anonimização garantida nem captura
completa de tráfego de dependências. PENDENTE DE REVISÃO: canal privado, retenção/anexos e logs
históricos antes de publicação; BLOQUEADO para redistribuição de assets/termos desconhecidos.
Checklist humano em docs/legal/PRE-RELEASE-CHECKLIST.md. Nada fecha SIM-01 ou AMD/NVIDIA reais.


## Ilmenyx 0.11.0

276 testes passaram; novo HTML de resultados reduzidos verificado por CLI, parser e fixtures,
com hardware/proveniência/hash/status e sem score. Exportação desses resultados por botões na
UI ainda não implementada; layout específico da nova seção no browser NÃO TESTADO. Regressões
UI anteriores e aba de termos passaram em Wayland/X11. Revisão jurídica dos termos próprios
(uso instrumental profissional permitido, venda/acesso/derivação pagos restritos) pendente antes
de publicar; partes MIT/terceiros preservadas. Pasta atual /mnt/HDD/Github/Ilmenyx; ambientes
anteriores nos logs são históricos. Windows, hardware ausente e cargas full seguem pendentes.


## Atualização 0.12.0 — HTML dos resultados na UI

IMPLEMENTADO e testado: exportação dos snapshots CPU/RAM/GameSim pela interface,
sem nova coleta/carga, com cancelamento e recusa de sobrescrita. 279 testes passaram;
Wayland nativo e X11 passaram no CachyOS. Browser Chromium em 390/1280 px passou
para os três relatórios. Isto encerra as pendências locais de botões HTML e layout
nessas condições, sem generalizar para outras plataformas ou acessibilidade.
Detalhes e comandos em [validation-html-ui.md](docs/validation-html-ui.md). Registros anteriores são históricos.


## Atualização 0.13.0 — teclado e foco

IMPLEMENTADO e testado no CachyOS: rótulos do caminho/licenças, ajuda de erro,
ciclo de Tab e restauração de foco após diálogos. 279 testes aprovados; smoke
Wayland nativo e X11 com eventos de teclado no processo passou. GUI-02 continua
NÃO TESTADO para Orca/AT-SPI (Orca ausente), entrada física e avaliação assistiva
completa. Não houve instalação nem certificação de acessibilidade.
[Detalhes e limites](docs/acessibilidade-teclado.md).


## Atualização 0.14.0 — layout mínimo e texto ampliado

IMPLEMENTADO: ações do dashboard quebram em linhas, exportação com rolagem vertical
e título Sobre com quebra. Build sem avisos/erros, 279 testes aprovados e smokes
Wayland nativo/X11 aprovados. Mínimos 640×480, 440×360 e 480×360 DIP, texto normal
e 150% por instrumentação no processo: controles medidos alcançáveis após rolagem.
Fecha esse recorte local de layout, sem fechar GUI-01/GUI-02 para outros displays,
200% de texto, leitores de tela ou entrada física. [Evidências e limites](docs/layout-minimo.md).


## Atualização 0.15.0 — leitura de licenças

IMPLEMENTADO: seletor compacto substitui abas de Sobre, preservando integralmente
os quatro documentos e o leitor somente leitura. No mínimo 480×360, área de leitura
aproximada de 208 DIP normal e 135 DIP a 150%. Build sem avisos/erros, 279 testes
aprovados e smokes Wayland nativo/X11 passaram, incluindo seleção integral, bloqueio
de edição, rolagem e teclado. NÃO TESTADO: clipboard real, Orca/AT-SPI, entrada física,
200%+, outras plataformas e acessibilidade completa; GUI-01/GUI-02 continuam parciais.
[Comandos e evidências](docs/licencas-leitura.md).


## Atualização 0.16.0 — texto a 200%

IMPLEMENTADO: quebra de rótulos/títulos com cortes reproduzidos e cabeçalho Sobre
compacto. Build sem avisos/erros, 279 testes aprovados e nove casos de layout por
backend em Wayland nativo/X11 (100/150/200%) passaram. Leitura a 200% de cerca de
161 DIP/159 DIP, mantendo fonte 28 e critério de 120 DIP. Fecha o recorte local
de 200%; registros anteriores são históricos. Acima de 200%, outras matrizes de
DPI/hardware, Orca/AT-SPI, entrada física e Windows permanecem NÃO TESTADOS.
GUI-01/GUI-02 seguem parciais. [Método, evidências e comandos](docs/texto-200.md).


## Atualização 0.17.0 — pacote local linux-x64

IMPLEMENTADO: bundle CLI/desktop framework-dependent, inventário de avisos e hashes;
gerador/validador sem instalação/restore implícito/publicação. Build sem avisos/erros,
279 testes aprovados, integridade/CLI/ELF/ldd e smoke dos assemblies extraídos em
Wayland nativo/X11 passaram. Pacote alterado foi recusado. PKG-01 continua parcial:
VM limpa, outras distros/glibc, runtime ausente, self-contained e instaladores
NÃO TESTADOS. Revisão individual de licenças antes de redistribuir PENDENTE; material
sem direitos conhecidos BLOQUEADO para redistribuição. [Evidências e comandos](docs/pacote-linux-x64.md).


## Complemento de ambiente isolado — pacote 0.17.0

Sem QEMU/imagem guest pronta; VM limpa continua NÃO TESTADA (PKG-01 parcial).
Contêineres locais, sem root/rede/instalação: Debian 12 sem ICU bloqueou o modo
padrão; diagnósticos CLI passaram somente no teste invariant explícito, sem SDK.
Alpine 3.24.1 é incompatível com o bundle glibc; GUI no contêiner NÃO TESTADA e
fontconfig ausente no Debian. Preflight evita repetir aborts por ICU conhecida
ausente. Requisitos documentados; nada altera o modo padrão do aplicativo.
[Comandos, códigos de saída e limites](docs/validacao-container.md).


## Atualização 0.18.0 — guarda ICU na CLI

IMPLEMENTADO e testado: pré-verificação nativa no adaptador Linux; falta de ICU
retorna código 5 antes da coleta, com mensagem e sem JSON parcial. Não é fallback
invariant nem certificação de versões/ABI. Build sem avisos/erros, 284 testes
aprovados; caso negativo real em Debian e modo normal no CachyOS passaram.
VM limpa, Debian padrão com ICU, GUI sem ICU e matriz adicional seguem NÃO TESTADOS.
Os aborts anteriores de 0.17.0 permanecem históricos. [Detalhes e comandos](docs/icu-cli.md).


## Atualização 0.19.0 — doctor CLI

IMPLEMENTADO e testado: `doctor` textual, bibliotecas carregáveis e variáveis de
display não vazias/ausentes, sem provedores de inventário ou workloads. Build sem
avisos/erros e 296 testes aprovados. Host normal: código 0; Debian sem ICU: relatório
com código 5 sem abort; invariant explícito: código 0, sem certificação do modo padrão.
JSON/exportação do doctor indisponíveis; opções recusadas. VM limpa, Debian com ICU
e matrizes assistivas/de hardware seguem NÃO TESTADAS. [Evidências e limites](docs/doctor-cli.md).


## Atualização 0.20.0 — requisitos na interface

IMPLEMENTADO: consulta explícita em Requisitos → Verificar, sem autostart; DTO em
Contracts, apresentação independente em Core, sondagem no adaptador Linux.
CLI e Desktop compartilham texto/limitações sem depender um do outro. Build sem
avisos, 301 testes aprovados; regressões CLI no host e Debian sem ICU passaram.
NÃO TESTADO: VM limpa, outras distros, Orca/AT-SPI e teclado físico. PENDENTE DE
REVISÃO humana: usabilidade e checklist jurídico/marca; direitos desconhecidos
BLOQUEADOS para redistribuição. [Evidências e resultado gráfico](docs/requisitos-ui.md).


### Roteiro de aceite em outra máquina — pacote 0.20.0

IMPLEMENTADO e testado no CachyOS: integridade antes da execução, doctor com prazo,
smoke CLI existente e resultado estruturado sem afirmar certificação de GUI/VM.
Runtime ausente e pacote alterado recusados; destino existente preservado.
NÃO TESTADO: VM limpa e outra distro com ICU; PKG-01 permanece parcial.
[Comandos, evidências e pendências](docs/aceite-maquina.md).


### Intermitência de redimensionamento Wayland

Dois incrementos observaram timeout do harness ao reduzir Sobre ao mínimo.
Nova solicitação limitada preserva exigência de geometria e passou na repetição;
causa raiz permanece PENDENTE DE REVISÃO. Logs originais preservados; não atribuir
aprovação irrestrita a toda combinação de escala/compositor com esse resultado.
Ver docs/aceite-maquina.md e evidências ilmenyx-aceite-maquina.


### Aceite CLI — distinção de globalização

IMPLEMENTADO e testado: resultado do roteiro schema 1.1 distingue padrão/invariant,
registra runtime/versão/códigos e recusa formato desconhecido/ambíguo ou versão
divergente. Casos positivos usam o pacote real; protocolo/metadados negativos usam
cópias de teste. Produto permanece 0.20.0. VM limpa e outra distro com ICU continuam
NÃO TESTADAS; revisões humanas/jurídicas permanecem PENDENTES.
[Comandos e evidências](docs/aceite-globalizacao.md).


## Atualização 0.21.0 — IPv6 em texto livre

IMPLEMENTADO: ocultação de IPv6 comuns antes de MAC/IPv4 no sanitizador Linux,
sem DNS/sockets ou resolução de interfaces. Build sem avisos/erros, 337 testes
aprovados; fixture cobre saída texto/JSON/HTML e preservação de PCI/horários/modelos.
Casos arbitrários/ofuscados e limites de truncamento continuam PENDENTES DE REVISÃO;
não há anonimização garantida. IPv6 em rótulos reais de outros dispositivos, VM limpa
e matrizes adicionais seguem NÃO TESTADOS. [Evidências e limites](docs/privacidade-ipv6.md).


Revisão auxiliar Claude Code recebida para o código anterior à correção IPv6;
achados de delimitador/caminho/truncamento corrigidos e cobertos por testes.
Rótulos arbitrários colados (`src:fe80::1`), alias de zona e ofuscação permanecem
PENDENTES DE REVISÃO; não declarar anonimização. Ver docs/privacidade-ipv6.md.


## Atualização 0.22.0 — HTML de sensores na CLI

IMPLEMENTADO: SensorHtmlReport no Core, snapshot existente, HTML passivo/escapado,
escrita atômica sem overwrite. CLI sensors --html com coleta única, 371 testes
aprovados; coleta real/arquivo/stdout e browser desktop/estreito passaram.
Interface HTML de sensores PENDENTE DE IMPLEMENTAÇÃO; impressão, outros browsers,
VM limpa/bateria/outras placas NÃO TESTADOS. Checklist jurídico/humano PENDENTE.
[Comandos, colaboração Claude e evidências](docs/sensores-html.md).


## Atualização 0.23.0 — HTML de sensores na interface

Substitui a pendência anterior do botão HTML: IMPLEMENTADO e testado em Wayland
nativo/X11. Comando compartilhado JSON/HTML impede reentrada, recusa seleção que
mudou durante o diálogo e suprime escrita/notificações após descarte. Exporta o
snapshot selecionado sem coletar novamente; monitor permanece independente.
Build sem avisos/erros, 392 testes aprovados; monitor real, exportação/cancelamento,
extensão/overwrite, teclado roteado e mínimos até 200% passaram. Interação física,
Orca, VM limpa e revisão humana seguem PENDENTES/NÃO TESTADOS.
[Colaboração Claude, comandos e evidências](docs/sensores-html-ui.md).


## Atualização 0.24.0 — SMART importado offline

IMPLEMENTADO: smart-import recebe JSON smartctl em stdin, verifica metadados/nome,
filtra com parser existente e exporta wrapper histórico JSON/HTML. Sem dispositivo,
processo smartctl ou nova consulta. Build sem avisos, 457 testes aprovados;
relatório NVMe histórico real aceito e erros/timeout/browser validados.
SMART-01 permanece NÃO TESTADO para consulta ao vivo com sucesso sem elevação;
VM/SATA/encodings/tecnologias assistivas e integração UI continuam pendentes.
[Colaboração, comandos e evidências](docs/smart-import.md).


## Atualização 0.25.0 — SMART importado na interface

IMPLEMENTADO: painel "SMART · importação offline" na Avalonia. O usuário informa disco
e caminho completo de um JSON smartctl existente; ReadFileAsync aceita só arquivo regular
(sem link simbólico, /dev, /proc, /sys; abertura limitada a 3 s; máx. 256 KiB em disco e
64 Ki caracteres lidos). Exporta JSON/HTML do relatório selecionado, sem smartctl, sem
consulta ao disco. Build sem avisos, 468 testes aprovados; smoke X11 e Wayland nativo
passaram (recusa por nome divergente, importação, exportação JSON/HTML).
NÃO TESTADO: FIFO/arquivo em FS de rede (abertura bloqueante só é limitada por timeout; a
thread pode permanecer até o kernel liberar), teclado físico/Orca, VM limpa, SMART-01 ao vivo.
Importação continua sendo relatório histórico, sem prova de origem ou saúde atual.

Pacote local 0.25.0 (linux-x64, framework-dependent): gerado e aprovado por
scripts/test-linux-package.sh (hashes, licenças, info/sensores, overwrite recusado,
dependências nativas). O teste detectou applicationVersion ainda 0.24.0 em
LinuxHardwareInfoProvider; corrigido para 0.25.0. Pacote gerado antes do commit desta
correção (sourceWorkingTree=dirty). VM limpa, outras distros e redistribuição seguem
NÃO TESTADOS/PENDENTES DE REVISÃO.

Pacote 0.25.0 em VM Ubuntu 24.04 (QEMU/KVM, imagem cloud verificada): validate-linux-machine PASS,
smart-import CLI OK, desktop abre sob Xvfb. Evidências em docs/evidencias/ilmenyx-vm-ubuntu-2404-0.25.0.
Continuam NÃO TESTADOS: sessão gráfica real/Wayland/interação na VM, GNOME Boxes, outras distros, Orca.

DISTRO-01 (Ubuntu 24.04, parcial): build, 468 testes e UiSmoke X11 (Xvfb) aprovados na VM.
Achado e corrigido: títulos sem quebra cortavam a 200 % com DejaVu. Seguem NÃO TESTADOS:
Wayland/compositor real no Ubuntu, Debian, interação humana/Orca, outras fontes/escalas.

DISTRO-01 (Debian 13) e DISTRO-02 (Fedora 44, SELinux Enforcing), 2026-10-21, VMs QEMU/KVM de imagens
cloud oficiais verificadas: pacote 0.25.0 sem ICU é BLOQUEADO (o runtime aborta antes do Ilmenyx;
resultado negativo registrado); com libicu/fontconfig/X11 instalados: validate-linux-machine PASS,
desktop abre sob Xvfb, build do código-fonte sem avisos, 468 testes e UiSmoke X11 (25 PASS) aprovados.
Evidências: docs/evidencias/ilmenyx-vm-debian-0.25.0 e ilmenyx-vm-fedora-0.25.0.
Continuam NÃO TESTADOS: Wayland/compositor real (GNOME/KDE) nessas distros, hardware, interação
humana/Orca, ARM64, musl/Alpine, assinatura GPG do checksum Fedora. Dependência de ICU deve constar
nos requisitos de instalação por distro (ver docs/pacote-linux-x64.md).

Wayland nativo fora do host (parcial): Fedora 44 VM + Weston 15 headless, UiSmoke 25 PASS com
Avalonia.Wayland.WindowImpl (docs/evidencias/ilmenyx-vm-fedora-0.25.0). Continuam NÃO TESTADOS: GNOME/KDE reais,
GPU/aceleração, escala fracionária, múltiplos monitores, entrada física, Orca.

Wayland nativo no GNOME (parcial): Fedora 44 + GNOME Shell 50.6 headless (mutter), UiSmoke 25 PASS com
Avalonia.Wayland.WindowImpl. NÃO TESTADOS: sessão GNOME real/login, KDE Plasma, GPU, escala fracionária,
múltiplos monitores, entrada física, Orca.

Auditoria de paridade DiskHealth/BatteryInfo (2026-10-22): documento docs/auditoria-paridade-saude-energia.md;
leitura de código apenas, sem execução comparativa (WIN-01 segue NÃO TESTADO). Implementações sugeridas A/B/C aguardam decisão.

## Atualização 0.26.0 — Monitor ao vivo
IMPLEMENTADO: janela principal estilo RESET EXPLODE (lateral, Visão geral, 10 páginas) com monitor ao vivo. 481 testes e smoke X11/Wayland no
CachyOS. NÃO TESTADO: AMD k10temp, múltiplos discos/interfaces, /proc restrito, execução prolongada, Orca, outras distros
(smoke nas VMs não repetido). Ver docs/monitor.md.

Itens A/B da auditoria (0.26.0): métricas SMART por fixtures sintéticas e tipo/barramento validado no host (NVMe, SATA HDD, zram).
NÃO TESTADO: métricas SMART em disco real (SMART-01), USB/MMC/VirtIO reais, outros fabricantes ATA. Desgaste de bateria (C) não implementado.

## Portagem completa — Fase 1: Sistema
IMPLEMENTADO: página Sistema no layout do original (categorias, anéis de unidades montadas, árvore de detalhes), coletor /proc+/sys+/etc, nomes de GPU
por pci.ids, inventário de RAM por importação do `dmidecode -t memory` (estimativas EXPO/XMP, canais, ECC; sem serial). 522 testes; smoke X11 do CachyOS.
NÃO TESTADO: módulos de RAM em hardware real com dmidecode, nome de GPU AMD/NVIDIA, outras distros (VMs não repetidas para esta página).

Fase 1 — Discos: página no layout do original (cartões com veredito SAUDÁVEL/ATENÇÃO/CRÍTICO/SEM DADOS, limiares do original, métricas, SMART por clique ou importação).
539 testes; smoke X11 do CachyOS. NÃO TESTADO: SMART ao vivo com sucesso (SMART-01), temperatura hwmon de NVMe/drivetemp em hardware real (este host não expõe),
USB/MMC, HDD físico com métricas reais, outras distros para esta página.

Fase 1 — Energia: página no layout do original (cartões de bateria com veredito DESGASTADA/ATENÇÃO/SAUDÁVEL, cartão de energia, perfil via powerprofilesctl).
Validado em bateria física do host (HP, Li-ion, desgaste 27,9%). Bug real corrigido: sysfs reporta tamanho 4096 e uma guarda por FileInfo.Length descartava todos os
valores (afetava também modelo/firmware dos discos); agora SysfsText limita por caracteres lidos, com teste de regressão contra o /sys real.
NÃO TESTADO: bateria dupla, ciclos reais (este host reporta 0), descarga/autonomia, PC de mesa sem bateria em hardware real, power-profiles-daemon ausente.
SENSOR-01 (bateria física) passa a PARCIAL: uma bateria, em tomada.

Fase 1 — Drivers: página no layout do original (GPU instalada/mais recente, placa-mãe + suporte, firmware fwupd, backup do inventário, lista de drivers).
561 testes; smoke X11 do CachyOS com dados reais (Iris Xe/i915, HP, 18 itens fwupd). Bug real corrigido: GPU com nouveau herdava a versão do módulo proprietário.
NÃO TESTADO: NVIDIA proprietária real (versão e consulta de rede), AMD, instalação (por desenho não executada), abertura de páginas no navegador (xdg-open), fwupd com atualização disponível, outras distros.
