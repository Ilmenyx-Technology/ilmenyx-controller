# Licença do Ilmenyx, explicada

> Esta página explica a licença em linguagem simples. O que vale juridicamente são os textos em
> [LICENSE](LICENSE) e na pasta [LICENSES/](LICENSES/). Em caso de divergência entre o português e o
> inglês da permissão adicional e desta explicação, **prevalece o português**. O texto da PolyForm é o
> texto oficial em inglês.

**Titular:** Flávio (mantido como Bismuth Technology). Bismuth Technology é o nome sob o qual o projeto
é mantido; não é pessoa jurídica.

**Required Notice:** Copyright (c) 2026 Flávio (mantido como Bismuth Technology) — https://github.com/Bismuth-Technology/ilmenyx

## Em uma frase

O Ilmenyx é gratuito. Você pode usá-lo à vontade, inclusive como ferramenta no seu trabalho pago, mas
não pode vendê-lo, cobrar pelo acesso a ele nem usar o código dele em um produto ou serviço pago.

## Qual licença vale

| Material | Licença | Onde está o texto |
|---|---|---|
| Código e documentação do Ilmenyx escritos pelo mantenedor, desde o primeiro commit (inclusive a fase "Silex Audit") | **PolyForm Noncommercial 1.0.0** + **permissão adicional** | [LICENSE](LICENSE), [LICENSES/PolyForm-Noncommercial-1.0.0.txt](LICENSES/PolyForm-Noncommercial-1.0.0.txt), [LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt](LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt) |
| Trechos que vêm do RESET EXPLODE, de Gustavo (GustavoReset) | **MIT** (continua MIT) | [LICENSES/MIT.txt](LICENSES/MIT.txt) |
| Nome, logo, símbolo, ícones e manual de marca | Termos de marca (todos os direitos reservados, salvo o que o texto permite) | [LICENSES/LicenseRef-Ilmenyx-Marca.txt](LICENSES/LicenseRef-Ilmenyx-Marca.txt) |
| Bibliotecas, fontes, ícones, dados e programas de terceiros | Licença de cada titular | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) |
| O original em `upstream/ResetExplode` (submódulo) | MIT, do Gustavo, sem alteração | `upstream/ResetExplode/LICENSE` |

Cada arquivo de código diz a sua licença no cabeçalho, no padrão [SPDX/REUSE](https://reuse.software):

- `PolyForm-Noncommercial-1.0.0`: arquivo só do mantenedor.
- `MIT AND PolyForm-Noncommercial-1.0.0`: arquivo que adapta código do RESET
  EXPLODE. Ele traz dois `SPDX-FileCopyrightText`: o do Gustavo e o do mantenedor.
- Arquivos que não aceitam cabeçalho (imagens, JSON, PDF, capturas de tela) estão descritos em
  [REUSE.toml](REUSE.toml).

A permissão adicional vale para **todo** material sob PolyForm deste repositório, mesmo quando o
cabeçalho cita apenas `PolyForm-Noncommercial-1.0.0`.

## O que você pode fazer

- **Usar de graça**, em casa, na escola, em ONG ou órgão público, sem limite.
- **Usar como ferramenta em trabalho pago.** Um técnico pode diagnosticar o PC de um cliente com o
  Ilmenyx e cobrar pelo diagnóstico. Uma empresa pode usá-lo nos próprios computadores. Os relatórios
  gerados podem ser entregues ao cliente.
- **Instalar no PC do cliente** e cobrar pela visita, desde que não cobre pelo app e deixe a licença
  e os avisos junto.
- **Fazer vídeos, tutoriais, cursos e artigos** sobre o Ilmenyx, inclusive monetizados.
- **Redistribuir de graça** os pacotes sem modificação (AppImage, Flatpak, tar.gz), em qualquer site,
  desde que baixar não exija cadastro, pagamento ou anúncio e que a licença e os avisos acompanhem.
- **Modificar para uso interno** da sua empresa, sem vender nem oferecer a versão modificada a ninguém
  de fora.
- **Estudar, modificar e compartilhar** para fins não comerciais (o que a própria PolyForm já permite),
  mantendo a licença e o Required Notice.

## O que você não pode fazer

- **Vender** o Ilmenyx, cópias dele ou versões modificadas, nem alugar ou cobrar ativação.
- **Cobrar pelo acesso**: paywall, assinatura, download pago ou desbloqueio pago.
- **Oferecer as funções do Ilmenyx como serviço pago** (hospedado, por API, "wrapper").
- **Usar o código do Ilmenyx num produto ou serviço pago**, copiado ou adaptado.
- **Usar o nome ou o logo** do Ilmenyx em outro produto ou numa versão modificada distribuída.

Se você quiser algum desses usos, peça uma licença por escrito ao titular.

## Doações

O mantenedor pode aceitar doações e patrocínios voluntários. Doar é opcional: não é condição para usar
o Ilmenyx, não libera recursos pagos (não existem) e não dá direitos além da licença. Receber doações
também não muda os termos não comerciais para ninguém: a licença limita o uso por outras pessoas, não o
titular.

## Por que as partes MIT continuam livres

O Ilmenyx começou como uma adaptação do RESET EXPLODE, que o Gustavo publicou sob a licença MIT. A MIT
é uma autorização permanente dada a qualquer pessoa, inclusive para vender. Ninguém pode retirá-la.

Por isso:

- Os trechos que vêm do RESET EXPLODE continuam sob a MIT. Quem quiser só esses trechos pode pegá-los
  no [repositório original](https://github.com/GustavoReset/ResetExplode) e usá-los conforme a MIT.
- A MIT permite combinar o código com outro e licenciar o resultado sob outros termos, desde que o aviso
  de copyright e o texto da MIT acompanhem. É o que o Ilmenyx faz: **as modificações e acréscimos do
  mantenedor** nesses arquivos estão sob a PolyForm Noncommercial + a permissão adicional.
- O copyright do Gustavo e o texto integral da MIT estão em [LICENSES/MIT.txt](LICENSES/MIT.txt),
  em [NOTICE.md](NOTICE.md), na tela Sobre e em cada pacote.
- O histórico Git mostra o que foi escrito pelo mantenedor.

Os arquivos que adaptam código do RESET EXPLODE estão listados em [NOTICE.md](NOTICE.md).

## Componentes de terceiros

Avalonia, .NET, SkiaSharp, HarfBuzzSharp, Silk.NET, a fonte Inter (SIL OFL 1.1), os ícones Material
Design (Apache-2.0), o MangoHud (MIT), o OpenRGB (GPL-2.0) e os dados de ProtonDB/AreWeAntiCheatYet
seguem as licenças dos seus titulares, que esta licença não altera. Veja
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). O código-fonte do OpenRGB é publicado junto com cada
release (`scripts/fetch-gpl-sources.sh`).

## O que acompanha cada cópia

Ao redistribuir, mantenha: `LICENSE`, a pasta `LICENSES/`, `NOTICE.md`, `LICENSING.md`,
`THIRD-PARTY-NOTICES.md` e a pasta `licenses/` dos pacotes. Os pacotes oficiais já trazem tudo isso.

## Contribuições

Por enquanto o projeto não aceita código de outras pessoas; issues são bem-vindas. Veja
[CONTRIBUTING.md](CONTRIBUTING.md).

## Sem garantia

O Ilmenyx é fornecido "no estado em que se encontra", sem garantia, na extensão máxima permitida pela
lei (seção "No Liability" da PolyForm). Ações que mudam o sistema (driver, overclock, iluminação,
estresse) sempre pedem confirmação na tela.

---

## English summary

*Translation for convenience; the Portuguese text above prevails. The legal texts are in [LICENSE](LICENSE)
and [LICENSES/](LICENSES/).*

**Holder:** Flávio (maintained as Bismuth Technology). **Required Notice:** Copyright (c) 2026 Flávio
(mantido como Bismuth Technology) — https://github.com/Bismuth-Technology/ilmenyx

- **License:** the maintainer's own code and documentation (since the first commit, including the earlier
  "Silex Audit" phase) are under the **PolyForm Noncommercial License 1.0.0** plus a short **additional
  permission** (`LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt`). Each source file carries an SPDX
  header; files without one are covered by `REUSE.toml`.
- **You may:** use it free of charge; use it as a tool in paid work (e.g. diagnosing a client's PC and
  delivering the report); install an unmodified copy on a client's PC and charge for the visit, not for
  the app; make monetized videos, tutorials and articles; redistribute unmodified packages free of
  charge, without requiring registration or ads to download, keeping the license and notices; modify it
  for a company's internal use only.
- **You may not:** sell it or modified versions; charge for access; offer its functions as a paid
  service; use its code in a paid product or service; use the Ilmenyx name or logo for other products or
  modified versions.
- **MIT parts:** files marked `MIT AND PolyForm-Noncommercial-1.0.0` adapt code from RESET EXPLODE by
  Gustavo (GustavoReset). The original portions remain under MIT for everyone (also available upstream);
  the maintainer's modifications are under PolyForm Noncommercial + the additional permission. The MIT
  text and copyright are in `LICENSES/MIT.txt`, `NOTICE.md` and the About screen.
- **Third parties:** see `THIRD-PARTY-NOTICES.md`; their licenses are unchanged.
- **Donations:** the maintainer may accept voluntary donations/sponsorship. Donating is optional, gives no
  paid features and no extra rights, and does not change the noncommercial terms for anyone.
- **Contributions:** external code contributions are not accepted for now; issues are welcome.
