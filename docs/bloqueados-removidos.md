<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0 -->
<!-- SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology) -->
# Bloqueados: itens removidos e por quê

A página **Bloqueados (!)** lista só o que ainda pode ser portado ou validado. Em 2026-11-07 saíram da lista os itens
que não existem no Linux (o selo "NÃO SE APLICA") e o WinRAR, que ganhou um substituto. Eles ficam registrados aqui.

| Item removido | Por quê | O que o Ilmenyx faz no lugar |
|---|---|---|
| Ajustes do Windows (Game DVR, HAGS, janela do Windows) | São opções do registro e do Windows; o Linux não tem nada igual para ligar ou desligar | A página **Otimiza** traz os equivalentes: perfil de energia, governador da CPU, `vm.max_map_count`, aceleração do mouse |
| Limpeza de dispositivos USB fantasmas | É uma limpeza do registro do Windows; o Linux não guarda histórico de dispositivos que precise ser apagado | Nada a fazer: o kernel esquece o dispositivo quando ele é desconectado |
| WinRAR no catálogo de apps | Programa proprietário, sem versão Linux | O catálogo **Instalar apps** passou a ter o **PeaZip** (`io.github.peazip.PeaZip` no Flathub, LGPL-3.0, motor do 7-Zip), que abre e extrai `.rar`, `.zip` e `.7z` em qualquer distribuição. O AppStream do Flathub declara os tipos `application/x-rar` e `application/x-rar-compressed` e cita RAR entre os formatos abertos |

Alternativas avaliadas para o WinRAR (dados do AppStream do Flathub de 2026-11-07): **File Roller** (`org.gnome.FileRoller`,
GPL-2.0+, declara `application/vnd.rar`) e **Ark** (`org.kde.ark`, GPL-2.0+, cita rar). As duas são boas, mas cada uma é a
ferramenta de um ambiente (GNOME, KDE) e abrem `.rar` por meio de programas auxiliares (`unrar`, `7z`, libarchive) cuja presença
dentro do Flatpak não foi conferida. O PeaZip é independente de ambiente gráfico e leva o motor do 7-Zip junto, por isso foi o
escolhido. Instalação real pelo app e abertura de um `.rar` pelo PeaZip **não foram testadas** (ver `Sem_teste.md`).

O detalhe de cada item que **continua** bloqueado está no próprio cartão da página e em `Sem_teste.md`.
