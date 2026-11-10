# Contribuir com o Ilmenyx
<!-- REUSE-IgnoreStart -->

## Por enquanto: issues sim, código não

**O Ilmenyx não aceita contribuições de código de outras pessoas por enquanto** (pull requests,
patches, trechos enviados por e-mail ou issue). Pull requests de terceiros serão fechados sem
incorporar o código. Assim todo o código continua de um único titular, o que mantém a licença simples
e deixa aberta a porta para uma futura empresa.

**Issues são muito bem-vindas:**

- relato de bug (o que fez, o que esperava, o que aconteceu, distro e hardware);
- sugestão de recurso;
- resultado de teste em hardware ou distro que o projeto ainda não validou (veja `Sem_teste.md`).

Antes de anexar relatórios, leia [PRIVACY.md](PRIVACY.md): eles podem identificar a sua máquina.
Vulnerabilidades: não abra issue pública, siga [SECURITY.md](SECURITY.md).

Ao abrir uma issue, não envie código para ser copiado. Uma ideia descrita em palavras é suficiente.

## Licença

O Ilmenyx está sob a PolyForm Noncommercial 1.0.0 com uma permissão adicional; os trechos que vêm do
RESET EXPLODE continuam também sob a MIT. Explicação: [LICENSING.md](LICENSING.md). O Ilmenyx é uma
adaptação independente e não oficial do RESET EXPLODE, de Gustavo (GustavoReset); não envie nada ao
projeto original em nome do Ilmenyx.

## Para o mantenedor e os agentes que trabalham no repositório

- Todo arquivo novo leva o cabeçalho SPDX (ver [AGENTS.md](AGENTS.md) e [LICENSING.md](LICENSING.md)):
  `SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0` e
  `SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)`. Arquivo que adapta código do
  RESET EXPLODE: `MIT AND PolyForm-Noncommercial-1.0.0` com os dois copyrights.
- Não acrescente logo, fonte, driver, asset ou código de terceiros sem licença conhecida e registro em
  `THIRD-PARTY-NOTICES.md`.
- Alterações pequenas e explicáveis; preserve o submódulo fixado e a metodologia dos benchmarks.
  Core/Contracts sem UI/Windows; adaptadores por SO; dados reais ou indisponíveis. Não executar stress,
  sudo, drivers, escrita em disco bruto ou perfis full em testes.

Com o SDK .NET 10 disponível:

```bash
dotnet restore Ilmenyx.slnx --disable-parallel -m:1
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
```

Em Bash, o helper opcional `. scripts/env-local-sdk.sh` reutiliza o SDK temporário em `/tmp/silex-dotnet`
(`scripts/get-local-sdk.sh` o baixa). Registre NÃO TESTADO em `Sem_teste.md` quando faltar ambiente.
Push, tags, releases e publicação exigem decisão explícita do mantenedor.

---

**English:** external code contributions are not accepted for now; pull requests from third parties will
be closed without merging. Issues (bug reports, feature ideas, test results on new hardware/distros) are
welcome. Security issues: see `SECURITY.md`. License: PolyForm Noncommercial 1.0.0 + additional
permission, MIT for the RESET EXPLODE portions; see `LICENSING.md`. The Portuguese text prevails.
<!-- REUSE-IgnoreEnd -->
