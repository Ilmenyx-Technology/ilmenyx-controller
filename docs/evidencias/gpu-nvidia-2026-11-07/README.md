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
