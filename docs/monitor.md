# Monitor ao vivo — Ilmenyx 0.26.0

IMPLEMENTADO: janela inicial **Monitor** (referência visual: monitor de sistema com tabela de processos à esquerda e
cartões com gráficos à direita). O painel de diagnósticos anterior continua no botão "Diagnósticos de hardware".

## Dados e fontes (somente leitura)
CPU % (/proc/stat), frequência média (/proc/cpuinfo "cpu MHz"), temperatura (hwmon coretemp/k10temp/zenpower,
rótulo Package/Tctl/Tdie), memória usada (MemTotal − MemAvailable), disco (/proc/diskstats, só dispositivos inteiros
sd*/nvme*/vd*/xvd*/mmcblk*, setores de 512 B), rede (/proc/net/dev, ignora lo/veth/docker/br-/virbr), processos
(/proc/PID/stat: nome comm, CPU, RSS). Taxas exigem duas amostras; a primeira mostra "—". Contadores que voltam atrás
não geram taxa negativa. CPU por processo é normalizada pela capacidade total da máquina; USER_HZ assumido 100.
Threads do kernel (RSS 0) são contadas mas ocultadas da tabela. Máximo 4096 processos lidos e 40 exibidos; sem linha
de comando, ambiente ou E/S de outros usuários.

## Decisões e limites
- Atualização de 1 s **inicia ao abrir a janela** por decisão explícita do usuário (2026-10-22); botão Pausar/Retomar.
  Isto substitui a regra anterior de monitor opt-in somente para esta janela; o monitor de sensores do dashboard
  continua opt-in.
- Histórico apenas em memória: 60 pontos para os gráficos, nunca gravado nem exportado. Nomes de processo não saem
  da janela (sem exportação).
- Disco e rede somam os dispositivos/interfaces válidos; a interface exibida é a de maior contador acumulado.
- GPU: só inventário DRM/PCI e o aviso "AMD/NVIDIA — Testes não realizados em hardware real"; sem contadores de uso/VRAM.
- Não implementado: agrupamento "Aplicações", colunas GPU/VRAM/Disco por processo, ordenação por outras colunas, ícones,
  detalhes por recurso, tema do desktop. Layout fixo escuro; cores provisórias, sem marca oficial.

## Evidências
`UsageViewModelTests`/`LinuxUsageProviderTests` (fixtures de /proc e /sys) e cenário `monitor` do UiSmoke (X11 e Wayland):
gráficos, processos, ordenação, pausa sem amostragem, diagnósticos e Sobre. Captura em `docs/evidencias/ilmenyx-monitor-0.26.0/`.
NÃO TESTADO: múltiplos discos/interfaces reais, CPUs AMD (k10temp), contêineres com /proc restrito, escala fracionária,
operação prolongada/vazamento de memória, Orca.
