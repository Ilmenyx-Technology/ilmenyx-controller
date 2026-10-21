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
