# Segurança / Security

**Português** · [English](#english)

## Como relatar uma vulnerabilidade
Escreva para **ilmenyx.technology@gmail.com** com o assunto `[Ilmenyx segurança]`. Não abra issue pública para falhas de segurança.
Inclua: versão do Ilmenyx (canto do menu lateral ou `ilmenyx --help`), distro, como reproduzir e o impacto que você observou.

- Resposta inicial em até 7 dias. Correções saem numa nova release; quem relatou recebe crédito, se quiser.
- Pontos sensíveis do app: as correções com um clique e a aba Driver NVIDIA rodam scripts fixos como root via polkit (pkexec), e o
  Flatpak usa `flatpak-spawn --host`. Relatos sobre esses caminhos têm prioridade.
- As releases são assinadas com GPG (impressão digital `3D97 1657 2A39 8DCD D0CF  F0EE 733D 2EC7 8436 DCE8`); veja
  [docs/assinatura-gpg.md](docs/assinatura-gpg.md).

## Versões cobertas
Somente a release mais recente.

---

## English

### Reporting a vulnerability
Email **ilmenyx.technology@gmail.com** with the subject `[Ilmenyx security]`. Please don't open a public issue for security problems.
Include: Ilmenyx version (bottom of the sidebar or `ilmenyx --help`), distro, steps to reproduce and the impact you saw.

- First reply within 7 days. Fixes ship in a new release; reporters are credited if they want.
- Sensitive parts of the app: one-click fixes and the NVIDIA Driver tab run fixed scripts as root through polkit (pkexec), and the
  Flatpak uses `flatpak-spawn --host`. Reports about these paths get priority.
- Releases are GPG-signed (fingerprint `3D97 1657 2A39 8DCD D0CF  F0EE 733D 2EC7 8436 DCE8`); see
  [docs/assinatura-gpg.md](docs/assinatura-gpg.md).

### Supported versions
Only the latest release.
