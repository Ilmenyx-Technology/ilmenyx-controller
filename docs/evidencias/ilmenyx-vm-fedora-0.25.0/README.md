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
