<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0 -->
<!-- SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology) -->
# Quanto o próprio Ilmenyx gasta

Medido no PC do mantenedor (HP 250 G8, i5-1135G7, Iris Xe, CachyOS, KDE Plasma), 2026-11-10. CPU em % de **um** núcleo (o i5 tem 8),
incluindo os programas que o app chama (smartctl, nvidia-smi…). "Lixo" = memória alocada e descartada por segundo (vira trabalho do
coletor de lixo).

| | Antes (v0.31.0) | Depois (v0.31.1) |
|---|---|---|
| CPU parado, média das páginas | 4,3 % | **2,7 %** |
| Lixo parado, média das páginas | 11,3 MB/s | **1,4 MB/s** |
| Minimizado | 5,8 % · 10,2 MB/s | 4,4 % · 1,3 MB/s |
| Memória (RSS) depois de abrir todas as páginas | 402 MB | 362 MB |
| Abrir a Estimativa de FPS | 32,8 MB/s | 5,0 MB/s |

## O que pesava e o que mudou
- **Monitor de uso (1 s, em todas as páginas):** lia ~300 arquivos de `/proc` por segundo com leitura assíncrona, ~32 KB de lixo por
  arquivo, e quebrava cada linha de processo em ~50 textos. Agora: leitura enxuta (`LinuxFiles.ReadSmall`) e campos lidos no lugar.
  Por amostra: 9,7 MB e 20 ms de CPU → 0,9 MB e 4 ms.
- **`pci.ids`:** 14 pontos do código liam e interpretavam o arquivo inteiro (~18 MB e 17 ms por vez). Agora fica em memória e só é relido
  se o arquivo mudar.
- **Telas fora de vista:** a tabela de Processos, os gráficos da Visão geral e da Rede, os cartões de componentes e a RAM do Otimiza se
  redesenhavam a cada segundo mesmo com outra página aberta. Agora só atualizam quando estão na tela (`UiKit.OnScreen`) e, ao voltar,
  mostram o valor atual. A tabela de Processos e os blocos ao vivo da página GPU reaproveitam as linhas e só trocam os números.
- **Overlay:** a prévia continuava lendo a GPU a cada 2 s depois de sair da página; agora para, a menos que o overlay esteja ligado.

## O que continua e por quê
- **Abertura (~50 % por 5 s)** e **abrir Jogos (~50 % + programas externos)**: inventário de hardware e busca de jogos, uma vez só.
- **Memória (~250–360 MB)**: .NET + Avalonia + Skia; as páginas abertas ficam guardadas para voltar instantaneamente.
- **Monitor de 1 s**: necessário para gráficos, overlay e barra lateral; custa ~0,4 % de um núcleo.

## Como medir de novo
```
source scripts/env-local-sdk.sh
dotnet build tools/Ilmenyx.UiSmoke -c Release
ILMENYX_PERF_AUDIT=1 ILMENYX_PERF_SECONDS=10 dotnet run --project tools/Ilmenyx.UiSmoke -c Release --no-build -- /tmp/medida
```
Abre a janela real (dados reais do PC), fica parado, minimiza e passa por todas as páginas; grava `perf.tsv` (CPU, RSS, pico, threads,
memória alocada, coletas do GC e CPU dos programas filhos por fase).
