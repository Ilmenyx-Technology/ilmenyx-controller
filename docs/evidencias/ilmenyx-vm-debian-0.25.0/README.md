# Pacote 0.25.0 em VM Debian 13 (trixie) — QEMU/KVM, modo usuário

Imagem oficial debian-13-genericcloud-amd64.qcow2 (SHA512 conferido contra SHA512SUMS), kernel
6.12.113+deb13-cloud. Mesmo procedimento de ../ilmenyx-vm-ubuntu-2404-0.25.0/README.md.

- **Como entregue (result-asdelivered)**: sem ICU, o próprio runtime .NET aborta (FailFast "Couldn't
  find a valid ICU package") antes de o Ilmenyx iniciar; a validação fica BLOQUEADA (doctor 134). Resultado
  negativo esperado e documentado: requer libicu, sem habilitar invariant automaticamente.
- **Com dependências (result-deps)**: libicu76, libfontconfig1, libx11-6, xvfb, x11-utils, fonts-dejavu-core.
  validate-linux-machine PASS, globalização padrão; desktop abre sob Xvfb (janela "Ilmenyx" 1060x780).
- **Do código-fonte na VM**: 0 avisos/erros, 468 testes aprovados; UiSmoke X11 sob Xvfb (25 PASS, sem FAIL).
NÃO TESTADO: Wayland/compositor real, GPU, interação humana/Orca, SMART real, outras fontes/escalas.
Xvfb não certifica renderização em sessão real; a VM recebeu ferramentas de teste.
