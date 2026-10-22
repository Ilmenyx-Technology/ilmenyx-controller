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

## Regra deste registro (2026-10-22)

**Toda pendência precisa dizer POR QUE não foi testada** (motivo concreto: hardware ausente, risco de dano, regra do projeto, serviço externo, permissão) e a **condição para testar**.
As listas "NÃO TESTADO: …" das seções históricas abaixo não trazem o motivo; para cada item vigente o motivo está na tabela a seguir, que prevalece.
Um item só sai daqui com evidência real (comando, ambiente, data). Fixture, build e smoke com serviços falsos não contam como teste do caminho real.

## Pendências vigentes — motivo e condição (consolidado em 2026-10-22, versão 0.27.0)

| ID | Item | Por que não foi testado | Condição para testar |
|---|---|---|---|
| GPU-04 | Ficha e telemetria de GPU AMD/NVIDIA (sysfs amdgpu, `nvidia-smi`, PRIME) | O host só tem Intel Iris Xe; `nvidia-smi` não existe e não há amdgpu. O código foi escrito contra o formato documentado e coberto só por fixtures | Máquina com GPU AMD ou NVIDIA e driver; comparar a página GPU com GPU-Z/`nvidia-smi`/`radeontop` |
| GPU-05 | Carga e VRAM de iGPU Intel | O i915/xe só expõe carga com privilégios (perf) ou `intel_gpu_top`; sem root o app mostra só clock | Rodar com permissão (CAP_PERFMON) ou instalar `intel_gpu_top`; decidir se o app deve depender dele |
| SMART-02 | Métricas SMART (temperatura, desgaste, erros) em disco real | `smartctl` sem privilégio falha neste host; o relatório NVMe real do usuário foi gerado só com `--health` e não contém atributos | `sudo smartctl -a -j /dev/nvme0n1 > arq.json` e importar na página Discos; ou dar acesso de leitura ao dispositivo |
| DISK-01 | Temperatura do disco por hwmon (NVMe, drivetemp) | Este kernel não expõe `/sys/block/*/device/hwmon` para o NVMe/SATA do host | Carregar `drivetemp` (root) ou usar outro host/kernel |
| DISK-02 | Tipo/barramento de USB, MMC, SD | Não há disco USB/MMC conectado; só NVMe, SATA e zram reais (VirtIO, óptico e legado vistos nas VMs) | Conectar pendrive/cartão SD |
| ENERGY-01 | Bateria em descarga, ciclos, autonomia, duas baterias | O host tem uma bateria cheia na tomada e reporta `cycle_count=0`; autonomia só existe descarregando | Rodar em bateria até descarregar; outra máquina com ciclos reais |
| ENERGY-02 | PC de mesa sem bateria em hardware real | O host é notebook; o caso foi coberto só por fixture | Executar a página Energia em um desktop |
| DRV-01 | Versão NVIDIA proprietária e consulta online | Sem GPU NVIDIA; a consulta usa a rede e o smoke nunca contata serviços externos | GPU NVIDIA e clique em "Verificar versão mais recente" |
| DRV-02 | Abrir páginas oficiais (`xdg-open`) | O smoke não abre o navegador do usuário | Clique manual nos botões "Abrir página oficial" |
| DRV-03 | fwupd com atualização disponível | O fwupd do host listou 18 dispositivos, nenhum com atualização pendente | Host com firmware desatualizado no LVFS |
| START-01 | Alterar serviços systemd reais e `Hidden=true` real em KDE/GNOME | O smoke só altera diretórios temporários para não mexer na sessão do usuário | Teste manual com uma entrada descartável, ou numa VM com sessão gráfica |
| MAKER-01 | Placa serial USB real (CH340, CP210x, FTDI, Arduino, ESP) | Nenhuma placa serial conectada; o diagnóstico foi validado com fixtures de sysfs | Conectar uma placa e abrir Maker · Portas; testar também sem estar no grupo `dialout`/`uucp` |
| MAKER-02 | brltty e ModemManager reais atrapalhando a porta | Não há porta serial para reproduzir; brltty não está instalado | Máquina com brltty (Ubuntu) e adaptador CH340 |
| GAME-01 | Iniciar um jogo de verdade pelo Launcher e acompanhar o processo | O smoke nunca inicia jogos (decisão de segurança); a biblioteca real do host só tem Lutris | Clique manual com um jogo instalado; conferir perfil de energia revertido ao fechar |
| GAME-02 | Heroic (Epic/GOG) real | Heroic não está instalado; os JSON foram lidos pelo formato conhecido, sem arquivo real | Instalar Heroic e um jogo; abrir a página Jogos |
| GAME-03 | PRIME offload (GPU dedicada) | O host tem uma GPU; o controle fica desativado | Notebook/desktop com iGPU + dGPU |
| GAME-04 | Fechar a Steam real e gravar opções em `localconfig.vdf` real | Fechar a Steam derrubaria jogos do usuário; o smoke usa árvore temporária | Teste manual com Steam aberta e um jogo |
| GAME-05 | Detecção do processo de jogos Proton/Wine | Sem jogo instalado para observar `environ`/`cmdline` reais | Rodar um jogo Proton e abrir a página Jogos |
| STRESS-01 | Carga real de CPU/RAM, comportamento sob pouca RAM/OOM | Regra do projeto: nenhum teste automático gera stress; risco de aquecer/travar o notebook de desenvolvimento | Clique manual com o monitor ativo e limite de temperatura; ver STRESS na lista de validações manuais |
| STRESS-02 | Corte por temperatura em CPU AMD (k10temp/zenpower) | Host Intel (coretemp) | Máquina AMD |
| NET-01 | Teste de rede real (ping, DNS, download, upload, bufferbloat) | Consumiria ~100 MB e contataria serviços de terceiros; por regra só acontece com clique; o smoke usa sonda falsa | Clique manual em TESTAR; testar com Wi-Fi e cabo e com ICMP sem privilégio |
| OPT-01 | Fechar aplicativos reais e alterar perfil de energia/mouse pela UI | O smoke usaria processos e perfis reais do usuário; usa fixtures e serviços falsos | Teste manual com apps descartáveis; mouse requer GNOME |
| OPT-02 | Aceleração do mouse em KDE | A página só implementa GNOME (gsettings); KDE exige edição por dispositivo em `kcminputrc` | Implementar e testar numa sessão Plasma |
| APP-01 | Instalação real de Flatpaks | Baixaria centenas de MB e alteraria o sistema do usuário; o smoke usa executor falso (a leitura real de `flatpak list/remotes` foi verificada) | Clique manual instalando um app pequeno |
| FPS-01 | API HowManyFPS real | Exige chave pedida por e-mail à HowManyFPS e consome cota; o formato de autenticação só vem com a credencial | Obter a chave e clicar em CONSULTAR API |
| PKG-02 | Pacote autocontido: smoke gráfico completo, Wayland, Arch/openSUSE/Alpine, ARM64 | Em 2026-10-22 CLI e janela abriram sem .NET e sem ICU do sistema em Ubuntu/Debian/Fedora (docs/pacote-linux-x64.md); falta o smoke completo com o pacote, outras distros e arquiteturas, que exigem outra imagem/hardware | VMs já preparadas; ARM64/musl exigem outro hardware |
| GUI-03 | Novas páginas em Wayland/GNOME/KDE reais, DPI e escala | Só X11 e Wayland/KWin no host e Xvfb/Weston/GNOME headless nas VMs; sem sessão gráfica real nas VMs | Desktop real; GNOME Boxes com ISO de desktop |
| BENCH-02 | Benchmark CPU Full e GameSim completo pela UI/CLI com carga real | Carga máxima prolongada de CPU: regra do projeto proíbe execução automática e a máquina de desenvolvimento não deve ser aquecida sem o usuário. Os testes e o smoke usam serviços falsos | Clique manual em INICIAR na página Benchmark (ou `bench cpu --profile full --allow-cpu-load`), com sensor de temperatura ativo |
| OC-01 | Verificador OC com carga real e comandos NVIDIA em GPU real | Stress CPU+RAM de 5–60 min não roda automaticamente (regra de carga) e a máquina não tem GPU NVIDIA; testes e smoke usam motor, relógio, log e nvidia-smi falsos. Em 2026-10-22 a leitura real e somente leitura do journal, da placa-mãe (DMI) e do nvidia-smi ausente foi exercida ao abrir a página no smoke, sem erro | Clique manual em INICIAR VERIFICAÇÃO com sensor de temperatura ativo; GPU NVIDIA para validar a faixa de energia/clock e os comandos |
| OC-02 | Detecção de erros de hardware (MCE/EDAC/PCIe/GPU) com ocorrências reais | O host não tem ocorrências desses tipos para ler; o parser foi validado só com texto de fixture. Sem permissão (fora de systemd-journal/adm) o app informa "NÃO VERIFICADO" | Máquina com erros corrigidos no log (ou injeção em VM) e usuário sem acesso ao journal |
| OC-03 | Teste de estabilidade de GPU (GpuBurn/cena) | Depende do módulo Vulkan, ainda não portado; a opção GPU fica desativada na página | Após o port de GPU em Vulkan |
| REP-01 | Relatório geral em outras máquinas (desktop sem bateria, várias GPUs, NVIDIA/AMD, discos USB) e leitura por revisão humana | Só foi exportado nesta máquina (notebook HP, Intel Iris Xe); a minimização de identificadores é por padrões de texto e não pode ser garantida em dados que não existem aqui | Exportar em outras máquinas/VMs e revisar o arquivo antes de compartilhar |
| OVL-01 | Overlay do Ilmenyx sobre jogo em tela cheia, em X11 e Wayland (ordem de janelas, cliques atravessando) | A janela é comum e o gerenciador/compositor decide; não há jogo instalado para observar e o smoke só verifica abrir/fechar e conteúdo. Não há injeção em jogos por decisão de projeto | Abrir um jogo em tela cheia e em janela sem borda (KDE X11 e Wayland, GNOME, Hyprland) com o overlay ligado |
| OVL-02 | FPS real de um jogo pela configuração MangoHud do Ilmenyx (Vulkan/OpenGL/DXVK, Gamescope) | Em 2026-10-22 só o glxgears (OpenGL) rodou sob `mangohud` por 7 s para capturar um CSV real, que virou fixture do parser; nenhum jogo ou DXVK foi usado (a página só gera a configuração e lê o CSV). O 1% low sai de intervalos de 100 ms, não de quadros individuais | Iniciar um jogo Proton pelo Launcher com MangoHud ligado e conferir FPS no Overlay e no Gargalo; comparar com o contador do próprio MangoHud |
| OVL-03 | Opções de inicialização da Steam com MANGOHUD_CONFIGFILE e aplicação do arquivo por "Aplicar na Steam" | O smoke não grava no localconfig.vdf real (decisão de segurança) e a Steam não está instalada com jogos neste host | Aplicar com um jogo de teste e conferir a linha em Propriedades |
| OVL-04 | Consumo da CPU (RAPL) e VRAM de iGPU no overlay | Leitura de RAPL exige privilégios nos kernels atuais e a iGPU não expõe VRAM dedicada; o overlay mostra "—" em vez de inventar valores | Habilitar leitura de RAPL de forma autorizada pelo usuário; GPU discreta para VRAM |
| RGB-01 | Conversa com um servidor OpenRGB real (protocolo 3, versões 0.9 e 1.0) | OpenRGB não está instalado neste host e instalar exigiria sudo; o protocolo foi escrito a partir do original e testado só contra um servidor falso em loopback construído pelo mesmo autor, então um desvio de formato do servidor real não seria detectado | Instalar o OpenRGB (pacote da distribuição), ligar o servidor SDK e clicar em CONECTAR; comparar a lista com a do próprio OpenRGB |
| RGB-02 | Efeitos em dispositivos reais (placa-mãe, memória, ventoinhas, teclado, mouse) e devolução ao modo original | Não há hardware RGB neste host; a devolução usa o modo ativo na conexão, e dispositivos sem modo salvo ficam como estão (limitação do protocolo) | Máquina com RGB; conferir a cor/efeito original antes e depois de DEVOLVER AO ORIGINAL |
| RGB-03 | Corsair iCUE e Razer Synapse diretamente | Os SDKs são de Windows e não se aplicam ao Linux; o suporte a esses fabricantes vem do OpenRGB | Não aplicável; validar pelo OpenRGB com um dispositivo Corsair/Razer |
| CHR-01 | Tema claro em todas as páginas e em outros DPI/fontes | Em 2026-10-22 foram revisadas por captura só 4 páginas (Stress, Discos, Sistema e OC vazias, sem dados variáveis); páginas com resultados (verdicts, gráficos, cartões de alerta) e o Painel clássico não foram inspecionadas visualmente no tema claro | Navegar por todas as páginas no tema claro com dados reais e revisar contraste |
| CHR-02 | Avisos de pendrive/USB com dispositivo real e em Wayland/GNOME | O smoke usa fonte de dispositivos falsa para não depender de plugar hardware; a leitura real de /sys foi exercida só como baseline (sem mudanças). A posição bottom-right depende do compositor (como o overlay) | Conectar/remover um pendrive e um mouse com o app aberto, em X11 e Wayland |
| BENCH-03 | Disco: SSD NVMe e outros sistemas de arquivos (btrfs, xfs, f2fs, NTFS-3G/FUSE) | Só um HDD SATA ext4 foi usado (2026-10-22: 64 MB, fases de 1 s, via CLI: 75 MB/s leitura, 14 MB/s escrita, ~130 IOPS 4K; resultado só prova que o caminho O_DIRECT funciona e limpa o arquivo, não valida o valor). O NVMe do host é o disco do sistema e não foi usado; FUSE/NTFS podem recusar O_DIRECT | Executar `bench disk` em NVMe e em btrfs/xfs; comparar com `fio --direct=1` |
| BENCH-04 | RAM completa com pouca memória e em máquinas de 4 GB | A execução real só foi feita uma vez com 7,2 GiB livres (2026-10-22: latência 101,8 ns, leitura 41,1 GB/s, cópia 38,0 GB/s, 8 threads); o caminho de recusa foi testado só por unidade | Máquina com <3,3 GiB livres: o app deve recusar sem alocar |
| GPU-06 | Benchmarks e stress de GPU | Ainda não portados (dependem de Vulkan); só a iGPU Intel poderia testar | Após o port em Vulkan |

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

Fase 1 — GPU: página no layout do original (chips, cartão de destaque, 8 seções estilo GPU-Z, sensores ao vivo), banco de especificações portado, coletor sysfs + nvidia-smi.
573 testes; smoke X11 no host real (Intel Iris Xe: clock ao vivo via i915; chip/arquitetura "—" pois a GPU não está no banco, como no original para GPUs não listadas).
NÃO TESTADO: qualquer NVIDIA ou AMD real (aviso permanece visível por autorização), nvidia-smi com campos de versões novas/antigas do driver, contadores de carga de iGPU Intel (exigem privilégios), múltiplas GPUs.

Fase 1 — Gargalo: página no layout do original (equilíbrio CPU×GPU, veredito ao vivo, leituras, análise de sessão 1/3/5 min), regras/textos e tabelas (GameCatalog) portados; 585 testes; smoke no host.
NÃO TESTADO: sessão real durante um jogo, GPU AMD/NVIDIA (carga/VRAM/temperatura), FPS (aguarda o módulo Overlay/MangoHud), tabelas de índice para GPUs Intel.

Fase 1 — Inicialização: página no layout do original (resumo, interruptores, REMOVER, RESTAURAR REMOVIDOS com confirmação), autostart XDG + systemd --user.
592 testes; smoke com diretórios temporários (override do sistema, recusa/confirmação, restauração) e leitura da sessão real (17 itens). Vantagem sobre o original: entradas do sistema são desativadas sem root.
NÃO TESTADO: alteração real de serviços systemd do usuário (o smoke não clica em serviços reais), KDE/GNOME respeitando Hidden=true em todas as versões, ~/.config em disco de rede, entradas OnlyShowIn/NotShowIn.

Fase 1 — Maker (Portas e Drivers seriais): páginas no layout do original; diagnóstico Linux (módulo ausente/não carregado, brltty, permissão/grupo, ModemManager) sem abrir a porta. 604 testes.
Bugs reais achados por teste: SysfsText removia tabulações (quebraria /proc/self/status). NÃO TESTADO: qualquer placa serial USB real (host sem ttyUSB/ttyACM), regras udev, brltty/ModemManager reais, outras distros (grupo uucp x dialout).
Não portado por não existir no Linux: instalação/remoção de pacotes de driver do Windows, dispositivos fantasma do registro (UsbCleaner), driver FTDI zerando PID.

Fase 1 — Jogos (Launcher): página no layout do original, VDF portado, bibliotecas Steam/Lutris/Heroic/manuais, perfis persistidos em ~/.config/ilmenyx/settings.json, opções da Steam com backup,
auxiliares, JOGAR com perfil de energia reversível. 627 testes; smoke com árvore Steam temporária e lançador falso; leitura real do Lutris do host.
NÃO TESTADO: iniciar um jogo de verdade (por desenho o smoke nunca inicia), Heroic real (formatos lidos de forma defensiva), GameMode/MangoHud (não instalados), PRIME offload em máquina com 2 GPUs,
fechamento da Steam real, detecção do processo de jogos Proton/Wine fora do Steam, Flatpak Steam.
Não portado por não existir no Linux: prioridade de processo elevada (exige privilégio; use GameMode), otimizações de tela cheia do Windows, preferência de GPU do Windows.

Fase 2 — Stress CPU/RAM: página no layout do original (componentes, limites, INICIAR/PARAR, leituras, gráfico, registro), motor no Core, guarda de abortamento pura e planejamento de RAM com reserva de 1 GiB.
647 testes sem gerar carga; smoke com motor falso (limites inválidos, confirmação recusada, aborto por temperatura, término por tempo, aviso sem sensor).
NÃO TESTADO (por regra): carga real de CPU/RAM, comportamento sob pouca RAM/OOM, sensor de temperatura em AMD (k10temp), stress de GPU (não portado).

Fase 2 — Rede: página no layout do original (TESTAR, progresso, cartão RUIM/ATENÇÃO/ESTÁVEL/SEM REDE) + gráfico de tráfego ao vivo; fluxo, limiares e textos portados; 655 testes e smoke com sonda falsa.
NÃO TESTADO (por regra): ping/download/upload reais, ICMP sem privilégio (depende de ping_group_range ou do binário ping), Wi-Fi vs cabo reais, rede atrás de proxy/captive portal.

Fase 3 — Otimiza: página no layout do original (RAM + OTIMIZA, programas para fechar, ajustes para jogos, RESTAURAR ORIGINAIS); 667 testes (SIGTERM e gsettings injetados) e smoke com processos de fixture e serviços falsos.
Bug real achado por teste: caminhos entre aspas com espaço no Exec (.desktop) quebravam o nome do executável (corrigido).
NÃO TESTADO: fechamento real de aplicativos, perfil de energia e mouse reais pela UI, KDE (mouse), Flatpak/Snap (nomes de processo), drop_caches (por regra só comando).
Não portado por não existir no Linux: limpeza de standby list/working set do Windows, Game DVR, HAGS, otimização de janelas DX.

Fase 2 — Estimativa de FPS: página no layout do original (busca, resolução/gráficos/upscaling, fonte de dados com chave, cartões em grade); catálogo e estimativa portados; cliente HowManyFPS com cache de 7 dias.
677 testes (manipulador HTTP falso). Diferença deliberada do original: a API não é consultada automaticamente a cada mudança de opção, só em CONSULTAR API após confirmação.
Marcador de equilíbrio do Gargalo agora usa a escala logarítmica exata do original.
NÃO TESTADO: serviço HowManyFPS real e seu formato de autenticação, chave inválida/cota reais.

Fase 3 — Instalar apps: página no layout do original (INSTALAR SELECIONADOS, progresso, lista, marcar todos/desmarcar), catálogo Flatpak com equivalentes Linux, Flathub só no usuário, confirmações. 690 testes (executor flatpak falso).
Achado nas VMs: formatação de percentual dependia da cultura ("25 %" na cultura invariante de Debian/Ubuntu sem LANG); padronizada.
NÃO TESTADO: instalação real de Flatpaks, Flathub real, rede/espaço em disco, flatpak system-wide, Snap/AppImage.
Validação 0.27.0 nas VMs (pacote do commit "bump 0.27.0"): validate-linux-machine PASS e smoke X11 (38 PASS) nas três distros; suíte 677/677 no Fedora e 676/677 em Debian/Ubuntu (teste de cultura, corrigido depois).
