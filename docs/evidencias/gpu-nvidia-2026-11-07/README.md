# Validação em notebook híbrido Intel + NVIDIA — Dell Inspiron 7460, 2026-11-07

i7-7500U, 16 GB, **Intel HD 620** (i915) + **GeForce 940MX** (GM108, Maxwell, 2 GB), Linux Mint 22.3, kernel 6.14, X11 (Cinnamon).
A NVIDIA está com o driver livre **nouveau** (o proprietário não foi instalado: exige a senha do dono; ver Sem_teste.md).

| Arquivo | Conteúdo |
|---|---|
| `ambiente.txt` | Reconhecimento: GPUs, módulos, hwmon, powercap (RAPL só root), Vulkan (940MX não aparece no nouveau), GL, drivers disponíveis (nvidia-driver-580) |
| `cli-gpu-list-nouveau.txt`, `cli-sensors-nouveau.json` | O que o app lê com o nouveau: nenhuma telemetria da NVIDIA; HD 620 só clock |
| `pacote-teste.txt` | Pacote autocontido: PASS |
| `fase1/`, `fase2/` | validate-gpu-remote: HD 620 stress 16,1 FPS, cena 1080p 10,9 FPS (nota 109), quadro conferido; ray tracing recusado com o motivo (sem as extensões) |
| `fase2/termico.tsv` | CPU até **89 °C** durante 8 s de carga na iGPU (chip compartilhado): o corte padrão de 90 °C fica muito perto |
| `prime/` | Cabeçalho do CSV do MangoHud embutido com glxgears: sem variável → "Mesa Intel HD Graphics 620"; com `DRI_PRIME=1` → **"NV118" (GeForce 940MX)** |

## Com o driver proprietário (nvidia-driver-580 580.178.04 via DKMS, kernel 6.14) — `proprietario/`

Instalação: o `ubuntu-drivers install nvidia:580` deixou o driver pela metade (puxou um kernel 7.0 com cabeçalhos incompletos); corrigido
removendo os pacotes 7.0 e instalando `nvidia-dkms-580` para o kernel em uso. PRIME em on-demand.
- `nvidia-smi.txt`: referência. O app lia **nada** da NVIDIA: o driver 580 recusa `clocks.max.video` e `clocks_throttle_reasons.active` e a
  consulta inteira falhava (exit 2). Corrigido: campos recusados são removidos um a um; leituras batem com o nvidia-smi (`cli-sensors.json`).
- Limites de energia: com `power.limit` N/A o driver devolve 200/0/200 W de preenchimento; ignorados na página GPU e na OC (`app-oc-940mx.png`).
- Vulkan na 940MX (`cli-gpu-list.txt`): stress 29,4 FPS, cena 1080p 17,2 FPS (nota 172; HD 620: 109), quadro `cena-940mx-960x540.png`; ray tracing recusado (Maxwell).
  Carga: GPU 100 %, 1189 MHz, máx. 66 °C; CPU até 90 °C (`termico-940mx.csv`).
- PRIME proprietário (`mangohud-*.csv`): OpenGL sem variáveis → Intel; com `__NV_PRIME_RENDER_OFFLOAD`… → "NVIDIA GeForce 940MX".
- App (`app-gpu-940mx.png`, `app-visao-geral.png`, `app-oc-940mx.png`): ficha GM108/Maxwell, sensores ao vivo; smoke X11 54/54.
- Versão "mais recente" online: responde 595.x, que **não suporta Maxwell** (ramo 580 é o último): correção em andamento na aba Driver NVIDIA.
