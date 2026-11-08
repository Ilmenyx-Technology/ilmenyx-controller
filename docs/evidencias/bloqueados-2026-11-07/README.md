<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0 -->
<!-- SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology) -->
# Bloqueados: os dois "Teste disponível nesta máquina" — Dell Inspiron 7460, 2026-11-07

i7-7500U, Intel HD 620 + GeForce 940MX (driver 580.178.04, PRIME on-demand), Linux Mint 22.3, kernel 6.14, X11.
Antes desta mudança a página Bloqueados marcava dois itens como testáveis aqui: **GPU NVIDIA** e **RGB**.

## 1. GPU NVIDIA (`nvidia-940mx/`) — validado; item restringido ao que falta

Feito com o CLI desta branch, sem root, sem rede. Regra de calor: só começa com a CPU < 70 °C e aborta a carga em 90 °C.
| Arquivo | Resultado |
|---|---|
| `ambiente.txt` | GeForce 940MX, 4096 MiB, `power.limit` e `fan.speed` **[N/A]** (a placa não tem limite ajustável nem ventoinha própria) |
| `cli-gpu-list.txt` | Vulkan lista HD 620 (integrada), 940MX (dedicada) e llvmpipe; a 940MX não tem ray query |
| `cli-sensors.json` × `nvidia-smi-referencia.csv` | Iguais: 44 °C, 0 %, 135 MHz núcleo, 405 MHz memória, 5 / 4096 MiB; hotspot, memória, consumo e ventoinha "indisponível" (o driver não informa) |
| `gpu-burn.txt` | Stress Vulkan de 6 s na 940MX: 180 quadros, **29,4 FPS**, exit 0 |
| `gpu-scene.txt` | Cena 1080p de 6 s: **17,3 FPS** (1 % low 8,7), nota 172, exit 0 — igual à rodada de 8 s da manhã (172,4) |
| `termico-*.csv`, `termico.log` | GPU 100 %, 1189 MHz, máx. **58 °C**; CPU de 48 a no máximo **76 °C** (sem aborto) |
| `kernel-xid.txt` | 0 linhas Xid/NVRM no log do kernel durante o teste |

Conclusão: a NVIDIA de notebook (Maxwell, driver proprietário) está validada (somando a evidência de
`docs/evidencias/gpu-nvidia-2026-11-07/`). O cartão passou a ser "GPU NVIDIA de desktop ou RTX" (limite de energia,
ventoinha, NVENC, driver open), que só fica "Teste disponível" numa NVIDIA com ray tracing (RTX).

## 2. RGB (`rgb/usb.txt`) — falso positivo; não há o que testar nesta máquina

O único dispositivo USB de um fabricante da lista RGB é um **pendrive Kingston DataTraveler** (`0951:1666`, interface de
classe 08 = armazenamento). A sonda olhava só o fabricante (a Kingston também faz a linha HyperX com RGB). Corrigido: agora
o dispositivo precisa ter uma interface HID (classe 03), como teclados, mouses e controladoras de LED. O item RGB continua
bloqueado: falta um periférico RGB real para o teste (o dono precisa conectar um, se tiver).

Depois das correções esta máquina não tem nenhum item "p/ testar" (as capturas `blocked-*.png` do smoke mostram a página
com a leitura real deste PC).
