# Pacote 0.25.0 em VM Fedora 44 Cloud — QEMU/KVM, modo usuário

Imagem oficial Fedora-Cloud-Base-Generic-44-1.7.x86_64.qcow2 (SHA256 conferido contra o CHECKSUM oficial; assinatura GPG NÃO verificada), SELinux Enforcing, kernel
6.19.10-300.fc44. Mesmo procedimento de ../ilmenyx-vm-ubuntu-2404-0.25.0/README.md.

- **Como entregue (result-asdelivered)**: sem ICU, o próprio runtime .NET aborta (FailFast "Couldn't
  find a valid ICU package") antes de o Ilmenyx iniciar; a validação fica BLOQUEADA (doctor 134). Resultado
  negativo esperado e documentado: requer libicu, sem habilitar invariant automaticamente.
- **Com dependências (result-deps)**: libicu 77.1, fontconfig, libX11, Xvfb, xwininfo, xauth, dejavu-sans-fonts.
  validate-linux-machine PASS, globalização padrão; desktop abre sob Xvfb (janela "Ilmenyx" 1060x780).
- **Do código-fonte na VM**: 0 avisos/erros, 468 testes aprovados; UiSmoke X11 sob Xvfb (25 PASS, sem FAIL).
NÃO TESTADO: Wayland/compositor real, GPU, interação humana/Orca, SMART real, outras fontes/escalas.
Xvfb não certifica renderização em sessão real; a VM recebeu ferramentas de teste.

## Adendo: Wayland nativo contra Weston headless

Fedora 44, Weston 15.0.1 (`--backend=headless --renderer=pixman`, socket próprio, sem GPU), DISPLAY removido,
ILMENYX_DISPLAY_BACKEND=wayland. UiSmoke: 25 PASS, sem FAIL, janela `Avalonia.Wayland.WindowImpl`
(`ui-smoke-wayland-weston.txt`). Limites: Weston headless não é GNOME/KDE, sem dispositivos de entrada
reais, renderização por software; não certifica compositores de produção, portais, DPI/escala fracionária
nem Orca.

## Adendo: Wayland nativo contra GNOME Shell 50.6 headless

Fedora 44, `gnome-shell --headless --wayland --no-x11 --virtual-monitor 1600x1200` dentro de `dbus-run-session`
(mutter real, renderização por software, sem GDM/login nem portais completos), DISPLAY removido. UiSmoke: 25 PASS,
sem FAIL, janela `Avalonia.Wayland.WindowImpl` (`ui-smoke-wayland-gnome-shell.txt`). Avisos do shell sobre GDM,
geolocalização e câmera são do ambiente headless. Limites: sem sessão de usuário real, sem dispositivos de
entrada físicos, GPU ou escala fracionária, sem Orca/AT-SPI; não certifica a experiência no desktop GNOME.
