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
