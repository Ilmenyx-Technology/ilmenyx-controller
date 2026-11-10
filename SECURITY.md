# Segurança

## Como relatar uma vulnerabilidade
Escreva para **meirafhenrique@gmail.com** com o assunto `[Ilmenyx segurança]`. Não abra issue pública para falhas de segurança.
Inclua: versão do Ilmenyx (menu lateral ou `--cli --version`), distro, como reproduzir e o impacto que você observou.

- Resposta inicial em até 7 dias. Correções saem numa nova release; quem relatou recebe crédito, se quiser.
- Pontos sensíveis do app: as correções com um clique e a aba Driver NVIDIA rodam scripts fixos como root via polkit (pkexec), e o
  Flatpak usa `flatpak-spawn --host`. Relatos sobre esses caminhos têm prioridade.

## Versões cobertas
Somente a release mais recente.

> Pendente: trocar este contato por um e-mail da Ilmenyx Technology quando ele existir (ver `docs/governance/PENDENCIAS.md`).
