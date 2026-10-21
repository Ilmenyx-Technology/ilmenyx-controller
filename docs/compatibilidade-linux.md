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
