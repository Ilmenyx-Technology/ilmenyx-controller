# Ilmenyx em Flatpak

Pacote de arquivo único (`Ilmenyx-<versão>-x86_64.flatpak`) feito a partir do pacote autocontido linux-x64 (o .NET 10 e o ICU vão dentro).
O runtime `org.freedesktop.Platform//25.08` só fornece a parte gráfica (X11/Wayland, fontconfig, Vulkan/Mesa); quando o PC tem o driver
NVIDIA, o Flatpak instala sozinho a extensão `org.freedesktop.Platform.GL.nvidia-<versão>` que corresponde ao driver do sistema.

```bash
# Gerar (ferramentas no nível do usuário, sem sudo):
flatpak install --user flathub org.flatpak.Builder org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08
bash scripts/package-flatpak.sh /caminho/absoluto/destino-novo
# Instalar e abrir (só este usuário):
flatpak install --user Ilmenyx-Controller-0.32.1-x86_64.flatpak
flatpak run io.github.ilmenyx_technology.IlmenyxController          # interface
flatpak run --command=ilmenyx io.github.ilmenyx_technology.IlmenyxController info   # linha de comando
```

## Por que o Flatpak precisa de uma ponte para o sistema

O Ilmenyx é uma ferramenta de diagnóstico: ele lê o hardware e usa programas do sistema (nvidia-smi, lspci, dpkg-query, pkexec, smartctl,
steam, powerprofilesctl...). Dentro do sandbox esses programas não existem e o `/proc` só mostra o próprio app. Por isso, quando roda em
Flatpak (existe `/.flatpak-info`), o app:

| O quê | Como | Código |
|---|---|---|
| Programas do sistema | rodam no sistema com `flatpak-spawn --host` (variáveis de ambiente e pasta passadas explicitamente) | `HostSpawn` |
| Arquivos do sistema (`/etc/os-release`, `pci.ids`, `/lib/modules`, `/etc/group`) | lidos em `/run/host/...` | `HostSpawn.HostPath` |
| Lista de processos, "Steam aberta", jogo em execução, apps para fechar | `cat /proc/[0-9]*/stat` e `ps` no sistema | `HostProcesses`, `GameProcesses.FindAsync` |
| `/sys/module` (versão do driver carregado) e `/sys/firmware` (UEFI/Secure Boot) | lidos no sistema, com cache de 30 s | `HostSysfs` |
| MangoHud e OpenRGB que vêm no pacote | os scripts que o sistema executa apontam para o caminho real de `/app` no sistema (`app-path` em `/.flatpak-info`) | `HostSpawn.ForHost` |
| Grupos do usuário (portas seriais) | `id -G` no sistema (no sandbox os grupos aparecem como 65534) | `LinuxSerialPorts.Identity` |

Fora do Flatpak nada disso é usado: o comportamento nativo é idêntico (testes 1047/1047, smoke de interface 56/56).

## Permissões pedidas e por quê

| Permissão | Para quê |
|---|---|
| `--socket=x11`, `--socket=wayland`, `--share=ipc` | janela (X11 quando existe, como no app nativo; Wayland nativo sem X11) |
| `--device=all` | GPUs (`/dev/dri`, `/dev/nvidia*`), i2c/USB do OpenRGB, portas seriais (Maker), discos para SMART |
| `--share=network` | ProtonDB/Steam (Compatibilidade) e a versão mais recente do driver NVIDIA, sempre só depois de um clique |
| `--filesystem=host-os:ro`, `--filesystem=host-etc:ro` | achar os programas do sistema e ler os arquivos do sistema (só leitura) |
| `--talk-name=org.freedesktop.Flatpak` | a ponte `flatpak-spawn --host`. **Isto dá ao app o mesmo alcance de um programa comum do usuário fora do sandbox**: o Flatpak aqui serve para instalar e atualizar, não como isolamento de segurança. É o mesmo nível do AppImage e do pacote .tar.gz. |
| `--filesystem=home` | bibliotecas da Steam/Heroic/Lutris, logs do MangoHud, relatórios exportados |

Por essa ponte, este Flatpak **não seria aceito no Flathub como está** (o Flathub recusa `org.freedesktop.Flatpak` sem justificativa
especial). Ele é distribuído como arquivo `.flatpak` na release.

## Validado de verdade (Dell Inspiron 7460, Mint 22.3, Flatpak 1.14.6, 2026-11-07)

Instalado com `flatpak install --user --bundle` e aberto num servidor X virtual (Xephyr; a tela do PC estava bloqueada). Capturas em
`docs/evidencias/flatpak-2026-11-07/`.
- Visão geral: "Linux Mint 22.3", CPU 54 °C, GPU 940MX 47 °C, **273 processos do sistema**, consumo da CPU pelo RAPL (2,0 W).
- GPU: 940MX com telemetria ao vivo pelo nvidia-smi do sistema (46 °C, 135/405 MHz, VRAM, P8) e "Driver nvidia 580.178.04".
- Sensores: 51 canais (coretemp, dell_smm, thermal, bateria), como no nativo.
- Processos: tabela com os processos do sistema e % de CPU ao vivo.
- Driver NVIDIA: "Funcionando · driver 580.178.04", Secure Boot desligado, PRIME on-demand, plano "Voltar ao nouveau" simulado (apt-get -s) — igual ao nativo.
- Drivers, Otimiza (perfil de energia lido no sistema: "Equilibrado"), Jogos (sem jogos instalados; sem travar), Bloqueados: iguais ao nativo.
- CLI: `info`, `sensors`, `nvidia-driver status` corretos dentro do Flatpak.
- Achados e corrigidos neste teste: estado do driver errado ("reinicie") porque `/sys/module` não existe no sandbox; travamento da
  interface ao abrir Jogos (espera síncrona na thread da UI); grupos do usuário como 65534.

## NÃO TESTADO / limitações conhecidas

- **Senha (pkexec) pela ponte**: as correções com um clique e a aba Driver NVIDIA chamam `pkexec` no sistema; a janela de senha não foi
  exercitada no Flatpak (a tela estava bloqueada). Deve funcionar porque o processo roda na sessão do usuário, mas está sem prova.
- **Jogos de verdade**: iniciar pela Steam/executável, MangoHud do pacote, PRIME e GameMode pela ponte — sem jogos instalados aqui.
  Encerrar programas auxiliares: o Flatpak manda SIGTERM ao `flatpak-spawn`, que repassa ao programa; se o app for morto à força, o
  programa no sistema pode continuar aberto.
- **OpenRGB do pacote** pela ponte: não testado.
- **Benchmarks de GPU (Vulkan)** no Flatpak: as ICDs Mesa e NVIDIA estão presentes no sandbox, mas nenhum benchmark foi rodado nele.
- `/boot` não é visível: a lista de kernels instalados vem de `/lib/modules` e dos pacotes.
- Wayland nativo dentro do Flatpak: não testado (só X11).
- Configurações ficam em `~/.var/app/io.github.ilmenyx_technology.IlmenyxController/` — separadas das do app nativo/AppImage.
- Outras distros e Flatpak de sistema (`--system`): não testados.
