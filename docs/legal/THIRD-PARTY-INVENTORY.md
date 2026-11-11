# Inventário de terceiros e propriedade intelectual

Revisado em 2026-11-10 (v0.31.1), antes de tornar o repositório público. Substitui o inventário inicial de 2026-10-21.
Lista completa, com versões e onde fica cada texto de licença no pacote: [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md).
Isto é uma verificação técnica de licenças, não um parecer jurídico.

## O que está no repositório

| Material | Licença | Estado |
|---|---|---|
| Código do Ilmenyx (src, tests, tools, scripts, packaging) | PolyForm Noncommercial 1.0.0 + permissão adicional (`LICENSING.md`); arquivos adaptados do RESET EXPLODE: MIT AND PolyForm | SPDX em cada arquivo; o resto coberto por `REUSE.toml` (`scripts/check-spdx.py`) |
| RESET EXPLODE original | MIT, Gustavo (GustavoReset) | Só um **submódulo** (`upstream/ResetExplode`) apontando para o repositório público do autor; nenhum arquivo dele (assets, drivers, DLLs) é guardado neste repositório nem vai nos pacotes |
| Ícone e marca do Ilmenyx (`packaging/linux/hicolor`, `src/Ilmenyx.Desktop/Assets`, `docs/branding`) | Próprios (`LicenseRef-Ilmenyx-Marca`) | Feitos para o projeto; o kit de marca foi aprovado pelo mantenedor |
| Ícones da interface (`Icons.cs`) | Apache-2.0 (Material Design Icons 7.4.47) | Texto da licença vai no pacote |
| Evidências de teste (`docs/evidencias`) | Próprias | Revisadas para publicação: nomes dos computadores trocados por `redacted-host`; sem número de série, IP ou nome de usuário real |

## O que vai dentro dos pacotes (AppImage, Flatpak, .tar.gz)

| Componente | Licença | Obrigação e como é cumprida |
|---|---|---|
| .NET 10 runtime, Avalonia 12.1.4, SkiaSharp, HarfBuzzSharp, Silk.NET, Tmds.DBus, MicroCom, NWayland | MIT | Avisos e licenças de cada pacote em `licenses/` |
| ICU 72 (app-local) | Unicode License | Texto em `licenses/` |
| Fonte Inter | SIL OFL 1.1 | Texto em `licenses/`; a fonte não é vendida separadamente |
| MangoHud 0.8.4 | MIT | Binários oficiais sem alteração; licença em `licenses/components/` |
| OpenRGB 1.1 | GPL-2.0-only | AppImage oficial sem alteração, executado como programa separado; fonte exata anexada a cada release |
| umu-launcher 1.4.4 (com python-xlib 0.33, truststore, urllib3, six) | GPL-3.0 / LGPL-2.1-or-later / MIT | zipapp oficial sem alteração, executado como programa separado; fontes do umu-launcher e do python-xlib anexadas a cada release |

## O que não vai nos pacotes

| Item | Por quê |
|---|---|
| xunit, Microsoft.NET.Test.Sdk e demais pacotes de teste | Usados só para rodar os testes; nunca são redistribuídos |
| smartctl, nvidia-smi, pkexec, flatpak e outros programas do sistema | O app usa os do sistema do usuário; nada é embutido |
| Soda 11.0-10 (Wine) e GE-Proton9-27 | Baixados a pedido do usuário, do lançamento oficial, com SHA-256 fixado; não redistribuídos |
| Dados do ProtonDB (ODbL), AreWeAntiCheatYet (MIT), Steam | Baixados ou consultados pelo usuário; não acompanham o pacote |
| Medições do Notebookcheck usadas para ajustar os índices de GPU | Só os índices ajustados estão no código; os dados brutos não são republicados |

## Pendências conhecidas
- `reuse lint` oficial e SBOM formal ainda não gerados (o `scripts/check-spdx.py` cobre os cabeçalhos por ora).
- Opcional: confirmar se as 21 páginas marcadas "layout segue o original" copiaram algo do RESET EXPLODE; enquanto isso ficam
  `MIT AND PolyForm`, o que é o caminho mais cauteloso.
- Nenhum documento autoriza uso da marca RESET; o Ilmenyx se apresenta como adaptação independente e não oficial.
