# Checklist pré-publicação — sem liberação automática

Data de elaboração: 2026-10-21; não é data de aprovação jurídica. Responsável humano por cada
item: mantenedor [PENDENTE: designar titular/especialista quando necessário]. Evidências técnicas
não equivalem a conformidade legal. Publicação permanece sem autorização nesta sessão.

| Item | Evidência/data | Estado | Bloqueio/ação humana |
|---|---|---|---|
| [~] Busca INPI Ilmenyx e Bismuth Technology (nome antigo da organização); "Ilmenyx Controller" e "Ilmenyx Technology" cobertos pelo radical ILMENYX (nota de 2026-11-10), radicais/fonética/classes (avaliar 9/42 conforme atividade) | `docs/legal/BUSCA-MARCA-2026-11-07.md` (INPI, USPTO, web, domínios); WIPO/TMview/EUIPO exigem consulta manual | PARCIAL: Ilmenyx risco baixo; Bismuth risco médio | Titular/especialista definir abrangência/decisão; sem afirmar marca livre/registrada |
| [ ] Busca internacional, uso comercial, handles/domínios | Não realizada | PENDENTE DE REVISÃO | Domínio/handle livre não comprova direito |
| [ ] Eventual depósito/titular/custeio | Nenhuma ação/registro | PENDENTE DE REVISÃO | Aprovação formal; sem símbolo de registro |
| [~] Autoria/cessões/contribuições/identidade | OWNERSHIP-AND-CONTRIBUTIONS.md, COPYRIGHT-AND-CONTRIBUTIONS.md, CONTRIBUTING.md (sem código externo), 2026-11-07 | Titular: mantenedor pessoa física; Silex = mantenedor | Falta o nome civil completo nos avisos (PENDENCIAS #3); cessão à empresa só por contrato |
| [x] Licença: LICENSE/LICENSES/SPDX/REUSE/Sobre/pacotes | PolyForm Noncommercial 1.0.0 + permissão adicional (PT prevalece); MIT do RESET em LICENSES/MIT.txt; cabeçalho SPDX em src/tests/tools/scripts/packaging; REUSE.toml; metainfo `MIT AND PolyForm-Noncommercial-1.0.0`; 2026-11-07 | IMPLEMENTADO | Revisão breve da permissão adicional por advogado antes de criar a empresa (PENDENCIAS #4) |
| [x] Dependências/assets/fontes/drivers/binários | `THIRD-PARTY-INVENTORY.md` e `THIRD-PARTY-NOTICES.md` revisados em 2026-11-10 (v0.31.1) | FEITO: tudo o que vai nos pacotes tem licença compatível e texto incluído; GPL com fonte anexada à release; nada do RESET além do submódulo | Reavaliar a cada componente novo |
| [x] Dados/privacidade/evidências históricas | PRIVACY.md; varredura de 2026-11-10 (sem senhas, tokens ou chaves no código e no histórico; nomes dos PCs nas evidências trocados por `redacted-host`) | FEITO | E-mail de contato: trocar o pessoal pelo da Ilmenyx Technology quando existir |
| [ ] Segurança, benchmarks, cancelamento e vulnerabilidades | Testes/limites e consulta NuGet nesta sessão | IMPLEMENTADO parcialmente; full NÃO TESTADO | Definir canal privado e validar casos Sem_teste.md sem stress automático |
| [x] Nome/metadata/pacotes/ícone/app ID | `io.github.ilmenyx_technology.IlmenyxController`, .desktop, AppStream, AppImage e Flatpak desde a v0.30/0.31 | FEITO | — |
| [ ] Linux real/distros/hardware/CI/Windows | CachyOS, CLI/UI testáveis; Sem_teste.md | NÃO TESTADO para matriz restante | Provisionar VMs/hardware; CI não criado nesta tarefa |
| [x] Manual de Marca e assets | `docs/branding/kit/` (manual PDF e assets), 2026-11-07 | APROVADO pelo mantenedor (ícone e cores) | — |
| [~] Checksums/assinatura/SBOM rastreável | SHA-256 e assinatura GPG em cada arquivo das releases (`docs/assinatura-gpg.md`); chave com o nome Ilmenyx Technology desde a v0.31.0 | FEITO exceto SBOM | SBOM formal ainda não gerado |
| [x] Org/push/tags/releases | github.com/Ilmenyx-Technology/ilmenyx-controller **público** desde 2026-11-10, com o perfil da organização; pré-releases v0.30.0 a v0.34.2 assinadas | FEITO | Busca de marca internacional (WIPO/TMview/EUIPO) segue pendente, a cargo do mantenedor |

Referências institucionais consultadas em 2026-10-21:
[INPI guia](https://www.gov.br/inpi/pt-br/servicos/marcas/guia-basico),
[busca INPI](https://servicos.busca.inpi.gov.br/marcas),
[GitHub forks](https://docs.github.com/en/pull-requests/reference/forks).
Consulta de guias não é pesquisa de anterioridade, registro ou parecer de disponibilidade.


## Política comercial posterior

IMPLEMENTADO em 2026-11-07: a política comercial está na própria licença (PolyForm Noncommercial 1.0.0 +
permissão adicional) e é explicada em LICENSING.md. LICENSE-ILMENYX.md foi removido. Uso como ferramenta
em trabalho pago, instalação no PC do cliente, vídeos monetizados, redistribuição gratuita e modificação
interna são permitidos; venda, acesso pago e derivados comerciais não. Os trechos MIT continuam MIT.
Não é open source OSI. Não é parecer jurídico.
