# Portagem completa do RESET EXPLODE para Linux — matriz e ordem

Diretriz do usuário (2026-10-22): portar **todas** as funções do original para a versão Linux, com apenas as adaptações necessárias.
Código não precisa ser idêntico; o comportamento deve ser equivalente. Base lida: `upstream/ResetExplode` @ `ce1d9b3` (somente leitura).
Cada linha vira IMPLEMENTADO / PARCIAL / PENDENTE no Sem_teste.md conforme evidência real. Nada aqui é promessa de teste em hardware ausente
(AMD/NVIDIA, bateria, SATA, iluminação RGB, placas Maker): o código é portado e testado por fixtures/Linux disponível e o restante fica NÃO TESTADO.

## Salvaguardas mantidas (valem para tudo abaixo)
Nenhuma carga, stress, benchmark pesado, tráfego de rede ou alteração do sistema roda em testes automáticos, na abertura do app ou sem clique
explícito com confirmação visível; toda ação tem limite, timeout, cancelamento e limpeza (finally). Alterações são reversíveis (backup + "restaurar").
Sem sudo implícito: ações que exigem root mostram o comando para o usuário executar. Sem telemetria; serviços externos só por ação explícita com
URL/credencial visíveis. Escrita em disco só em arquivo temporário autorizado. Sem copiar assets/logo/marca; código adaptado mantém atribuição MIT.

## Matriz (página do original → Linux)
| Original | Função | Adaptação Linux | Fase | Estado |
|---|---|---|---|---|
| Visão geral (Dash) | gráfico, desempenho, tabela de componentes | monitor /proc+hwmon; GPU/consumo dependem de GPU/RAPL | 0 | PARCIAL (sem GPU/consumo/score) |
| Sistema (SystemInfoCollector) | placa-mãe, BIOS, CPU, RAM, SO | /sys/class/dmi/id, /proc, /sys; módulos de RAM via importação offline do `dmidecode` (exige root) | 1 | IMPLEMENTADO (RAM por importação; sem serial) |
| Sensores (SensorMonitor/LibreHardwareMonitor) | temperaturas, ventoinhas, tensões, potência | hwmon, thermal, power_supply, nvidia-smi/NVML, amdgpu sysfs | 0 | PARCIAL (sem GPU vendor) |
| GPU (GpuInfoCollector, GpuSpecDb, Nvml, Dxgi) | ficha estilo GPU-Z | sysfs PCI/amdgpu/i915 + nvidia-smi CSV (sem P/Invoke); banco de especificações portado como dados | 1 | IMPLEMENTADO (AMD/NVIDIA só por fixtures; iGPU Intel real) |
| Discos (DiskHealth, DriveMap) | veredito de saúde, unidades e ocupação | smartctl/hwmon/sysfs + statfs/mounts; vereditos com limiares do original | 1 | IMPLEMENTADO (SMART só por clique/importação; sem privilégio o veredito fica SEM DADOS) |
| Energia (BatteryInfo) | bateria, plano de energia, consumo | power_supply, power-profiles-daemon (powerprofilesctl) | 1 | IMPLEMENTADO (testado em bateria real do host; desktop sem bateria só por fixture) |
| Rede (NetworkTest) | latência, jitter, perda, DNS, velocidade, bufferbloat | ping/ICMP do SO, DNS, HTTPS (speed.cloudflare.com); endpoints e volume listados na confirmação, só por clique | 2 | IMPLEMENTADO (rede real NÃO TESTADA; sonda falsa) |
| Driver (DriverManager) | inventário de drivers, vídeo, suporte da placa | sysfs PCI/USB + módulos (taint), versão NVIDIA instalada/mais recente (rede só por clique), fwupd (listagem), suporte do fabricante por DMI; sem instalar | 1 | IMPLEMENTADO (instalação = comando sugerido, não executado) |
| Gargalo (BottleneckAnalyzer) | regras CPU/GPU/VRAM/térmico | regras e textos portados; amostras do monitor + telemetria de GPU; FPS depende do módulo Overlay (MangoHud) | 1 | IMPLEMENTADO (sem FPS; GPU AMD/NVIDIA só por fixtures) |
| Inicialização (StartupManager) | itens que abrem com o sistema | XDG autostart (usuário e /etc/xdg via override Hidden=true, sem root) + serviços systemd --user; remover com backup e restaurar | 1 | IMPLEMENTADO (alterações só testadas em diretórios temporários; lista real só lida) |
| Maker portas/drivers (MakerDrivers, UsbCleaner) | placas/conversores seriais, drivers | sysfs ttyUSB*/ttyACM*, módulos ch341/cp210x/ftdi_sio/pl2303/cdc_acm, permissões do nó, brltty/ModemManager; sem abrir a porta; UsbCleaner (dispositivos fantasma do registro) não se aplica ao Linux | 1 | IMPLEMENTADO (sem placa serial no host: só fixtures) |
| Jogos/Launcher (GameLibrary, GameLauncher, Vdf) | biblioteca, perfis, iniciar com ajustes | Steam (libraryfolders/appmanifest, localconfig.vdf com backup), Lutris (lutris --json), Heroic (Epic/GOG), manuais; PRIME offload, GameMode, MangoHud, perfil de energia reversível, auxiliares sem shell; prioridade/FSO do Windows não se aplicam | 1 | IMPLEMENTADO (Steam/Lutris reais lidos; Heroic só fixtures; disparo real de jogo NÃO TESTADO) |
| FPS estimado (HowManyFpsClient) | API externa | só com credencial do usuário, por clique; estimativa local do catálogo | 2 | PENDENTE |
| Benchmark CPU (BenchRenderer) | path tracer, RESET BENCH | existe; perfil Full opt-in já portado | 0 | PARCIAL (UI só validação) |
| Benchmark GameSim/Memória/Disco (GameSimBench, MemoryBench, DiskBench) | CPU de jogo, RAM latência/banda, disco | existe reduzido; adicionar perfis completos opt-in; disco só arquivo temporário (O_DIRECT) | 2 | PARCIAL |
| Benchmark GPU (GpuGameBench, GpuRtBench) | cena de jogo e ray tracing em D3D12 | reescrita em Vulkan (compute/RT), testável só na iGPU Intel local | 4 | PENDENTE |
| Stress (StressTester, GpuBurn) | CPU/RAM; GPU estilo FurMark | CPU/RAM portados (motor no Core, reserva de 1 GiB de RAM, confirmação, corte por temperatura/erro/tempo); GPU via Vulkan fica para a fase 4 | 2/4 | CPU/RAM IMPLEMENTADO (carga real NÃO TESTADA); GPU PENDENTE |
| OC (OcCheck, página OC) | erros WHEA, resets de GPU, ajustes NVML, guia BIOS | kernel log (MCE/EDAC/amdgpu/NVRM Xid) somente leitura; ajustes NVML exigem root (comando) | 2 | PENDENTE |
| Otimiza (Optimizer, WindowsTweaks) | fechar programas, liberar memória, ajustes | fechar apps do usuário com confirmação; ajustes de usuário (perfil de energia); itens root só como comando | 3 | PENDENTE |
| Apps (AppInstaller) | catálogo pós-formatação via winget | catálogo Flatpak/pacman; instalação só por clique, sem root implícito | 3 | PENDENTE |
| Overlay (OverlayMetrics, OverlayWindow, FrameMonitor) | métricas sobre o jogo, FPS por ETW | janela sempre-no-topo (X11; Wayland depende do compositor); FPS via MangoHud/gamescope se presente | 3 | PENDENTE |
| Integração (LightBackends, CorsairSdk) | iluminação: Corsair, OpenRGB, Razer | OpenRGB SDK (TCP 6742); Corsair via OpenRGB; Razer via OpenRazer; sem baixar SDK | 3 | PENDENTE |
| Relatório (ReportBuilder) | HTML/JSON de tudo | estender relatórios existentes às novas páginas | 1–3 | PARCIAL |
| Chrome (tema claro/escuro, toasts) | tema, notificações | tema alternável e notificação in-app/freedesktop | 3 | PENDENTE |

## Ordem de execução
Fase 1 (somente leitura, sem carga): Sistema, Discos (veredito + mapa), Energia, Driver, Gargalo, Inicialização, Maker, Jogos/Launcher, GPU.
Fase 2 (carga ou rede só por clique): Stress CPU/RAM, benchmarks completos de memória/disco/GameSim, Rede, OC (verificador), FPS externo.
Fase 3: Otimiza, Apps, Overlay, Integração/iluminação, tema/toasts. Fase 4: benchmarks e stress de GPU em Vulkan.
