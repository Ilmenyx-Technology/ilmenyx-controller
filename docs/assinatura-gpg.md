# Assinatura dos arquivos de release (GPG)

Cada arquivo de release tem um `.asc` ao lado: a assinatura feita com a chave da Bismuth Technology. O `.sha256` só detecta
arquivo corrompido; a assinatura prova que o arquivo saiu do mantenedor e não foi trocado depois.

- Chave: `Bismuth Technology (Ilmenyx releases) <meirafhenrique@gmail.com>`. É o nome antigo da organização (hoje Ilmenyx Technology):
  para mudar, acrescente um UID novo com a chave principal (`gpg --quick-add-uid`) e publique a chave pública de novo.
- Impressão digital (confira por um segundo canal, como o README ou o perfil da organização):
  **`3D97 1657 2A39 8DCD D0CF  F0EE 733D 2EC7 8436 DCE8`**
- Chave pública: `packaging/ilmenyx-release-key.asc` e o arquivo `ilmenyx-release-key.asc` de cada release.

## Como conferir
```
gpg --import ilmenyx-release-key.asc
gpg --verify Ilmenyx-0.30.0-x86_64.AppImage.asc Ilmenyx-0.30.0-x86_64.AppImage
```
Deve aparecer `Assinatura correta de "Bismuth Technology (Ilmenyx releases)"`. O aviso de que a chave "não é certificada com uma
assinatura confiável" é normal: o que vale é a impressão digital bater com a de cima.

## Como a chave é guardada (mantenedor)
- A **chave principal** (certifica, renova e revoga) fica só fora do computador, no pendrive pessoal do mantenedor, com o
  certificado de revogação. Validade: até 2031-11-06 (renovável).
- O computador de build tem só a **subchave de assinatura** (`2EEDA90024DAA1F9`, validade 2029-11-06). Se ele for comprometido,
  a subchave é revogada com a chave principal e outra é criada; a identidade (impressão digital) continua a mesma.
- Assinar: `bash scripts/sign-release.sh arquivos...` (gera `<arquivo>.asc` e confere cada um).
