# Flatpak — teste real, 2026-11-07 (Dell Inspiron 7460, Mint 22.3, Flatpak 1.14.6)

Bundle `Ilmenyx-0.30.0-x86_64.flatpak` (runtime org.freedesktop.Platform 25.08) instalado com `flatpak install --user --bundle` e aberto
com `flatpak run` num X virtual (Xephyr :5 — a tela física estava bloqueada). Detalhes e limitações: `docs/flatpak.md`.

| Captura | O que prova |
|---|---|
| 01-overview.png | SO do sistema (Mint 22.3), CPU/GPU ao vivo, 273 processos do sistema (ponte de processos) |
| 02-consumo.png | consumo da CPU pelo RAPL dentro do Flatpak (2,0 W) |
| 03-gpu.png | nvidia-smi do sistema pela ponte; versão do driver lida de /sys/module no sistema |
| 04-sensores.png | hwmon/thermal/bateria (51 canais) |
| 05-processos.png | processos do sistema com % de CPU |
| 06-jogos.png | página Jogos sem travar (corrigido: espera síncrona na thread da UI) |
| 07-driver-nvidia.png | "Funcionando · driver 580.178.04", igual ao nativo (corrigido: antes dizia "reinicie") |
| 08-drivers.png, 09-otimiza.png | inventário de drivers; perfil de energia lido no sistema |
