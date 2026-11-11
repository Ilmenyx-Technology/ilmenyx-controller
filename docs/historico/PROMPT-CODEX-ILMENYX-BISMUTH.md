# PROMPT MESTRE PARA CODEX — ILMENYX / BISMUTH TECHNOLOGY

> **Versão:** 1.0 — 21/10/2026  
> **Idioma das entregas:** português brasileiro; documentação voltada à comunidade também pode ter versão em inglês.  
> **Escopo desta execução:** adequar o repositório ao nome **Ilmenyx**, preparar governança, atribuição, privacidade e requisitos de identidade de marca, sem lançar versões públicas. Manter e atualizar o plano técnico de portabilidade Linux.  
> **Nota:** este documento fornece instruções de engenharia e um checklist de conformidade, **não é parecer jurídico nem confirmação de disponibilidade de marca**.

## 0. Missão e informações já decididas

Atue como engenheiro sênior de C#/.NET, Avalonia UI, Linux e manutenção de projetos open source, com atenção a propriedade intelectual, segurança, privacidade, experiência do usuário, Git/GitHub e preparação de marca. Trabalhe **sobre os arquivos e o histórico Git reais do workspace**; nunca substitua a inspeção por suposições. Execute alterações seguras, testáveis e incrementais, não apenas um plano.

**Informações fixadas pelo mantenedor:**

| Item | Definição |
|---|---|
| Marca/identidade da desenvolvedora | **Bismuth Technology** (grafia exata, com `h`) |
| Nome do aplicativo Linux | **Ilmenyx** (sem sufixo Audit, Bench, Monitor ou Diagnostics no nome principal) |
| Nome curto para executável/comando | `ilmenyx` **proposto**, ajustar com base no código e em conflitos reais |
| Descrição funcional | **Open-source hardware diagnostics, monitoring and benchmarking for Linux** |
| Descrição em português | **Diagnóstico, monitoramento e benchmarking de hardware para Linux — código aberto** |
| Origem do port | [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), de Gustavo (GustavoReset) |
| Relação com o upstream | Projeto **independente, não oficial, sem afiliação, patrocínio ou endosso** pela equipe RESET |
| Linguagem principal | C#/.NET; reutilizar a lógica original; Rust só com justificativa e benchmark verificável |
| Interface Linux | Avalonia UI e arquitetura MVVM, conforme viabilidade do repositório |
| Distribuição futura | Código aberto no GitHub, idealmente em fork na organização da Bismuth Technology, após revisão humana |
| Guia de identidade visual | **Ainda NÃO definido**; será elaborado separadamente pelo mantenedor |

**A marca de produto Ilmenyx foi escolhida como nome de trabalho final pela equipe**, mas **não existe aqui autorização para afirmar que ela é registrada ou juridicamente livre**. O mesmo vale para Bismuth Technology. A checagem formal de anterioridade, classes e sinais semelhantes, domínios e handles continua pendente. Não use `®`, nem sugira registro ou exclusividade.

**Origem jurídica observável:** o upstream possui `LICENSE` MIT com o aviso `Copyright (c) 2026 Gustavo (GustavoReset)`. Leia o arquivo efetivamente presente e preserve **integralmente o texto e os avisos do projeto-base**. O README do upstream informa que não recebe PRs; planeje um fork independente. Licença MIT do código não significa automaticamente autorização de uso de marcas, logos, imagens, fontes, drivers, firmware, SDKs ou binários de terceiros.

## 1. Regras inegociáveis

1. **Inspecione primeiro**: `git status`, branch, SHA, remotes, README, LICENSE, AGENTS.md, projeto(s), recursos e CI. Registre a origem de cada informação. Não alegue auditoria completa se não leu os módulos pertinentes.
2. **Preserve integralmente** o histórico Git, copyright, licença e autoria do RESET EXPLODE; não substitua Gustavo/RESET por Bismuth/Ilmenyx em créditos, arquivos históricos, nomes de algoritmos ou outras referências que identifiquem corretamente a origem.
3. **Não faça substituição global cega** de `Silex`, `Reset`, `RESET EXPLODE`, `ResetExplode` ou `RESET BENCH`: classifique cada ocorrência antes de mudar. O código Windows legado, nomes de testes e resultados históricos podem precisar ficar intactos.
4. Não invente CNPJ, e-mail, domínio, URL da organização, nome civil do titular, contrato, registro de marca, screenshot, medição, licença de terceiros ou funcionalidades prontas. Use `[PENDENTE: ...]` e detalhe a ação humana.
5. Não crie `Copyright (c) Bismuth Technology` presumindo titularidade de uma pessoa jurídica ainda não comprovada. Documente autoria individual, titularidade de contribuições e futura formalização separadamente.
6. **Não** execute `git push`, publique releases, crie organização/remoto, abra Issues/PRs, contacte terceiros, compre domínio, registre marca, altere configurações globais, rode `sudo`, baixe scripts não auditados, nem faça operações de impacto sem autorização do mantenedor.
7. Nunca execute benchmarks de estresse, escrita direta em discos, flash de firmware, overclock, alteração de drivers, instalação, elevação silenciosa ou otimização destrutiva durante esta tarefa. Testes leves e seguros são permitidos.
8. Por padrão, sem telemetria, upload, servidor remoto ou coleta em nuvem novos. Auditoria deve constatar se já existe tráfego e documentá-lo. Relatórios podem conter identificadores pessoais: minimizá-los.
9. **Não invente a identidade visual**. Até existir um Manual de Marca aprovado, mantenha UI e metadados com design neutro/coerente, logotipo final como pendência e ativos originais apenas se houver direitos verificados. Não fixe paleta oficial, Pantone, CMYK, fontes ou símbolos definitivos.
10. Faça alterações pequenas, revisáveis e respaldadas por build/testes. Se o ambiente não tiver SDK, rede, display, Linux real ou hardware, registre `NÃO TESTADO`; não simule sucesso. Preserve modificações locais do usuário.
11. Não afirme que benchmarks portados produzem pontuações equivalentes às do upstream sem estudo do workload, algoritmo, ambiente e validação. Recursos sem suporte devem indicar `indisponível`, nunca valores inventados.
12. Ao detectar informações contraditórias entre este prompt e o repositório, priorize as evidências reais para fatos técnicos, preserve políticas de segurança e registre decisões pendentes em vez de descartar código.

## 2. Ordem de execução desta sessão

**Faça nesta sessão**, sem esperar decisões que não impeçam avanços seguros:

- A. Auditoria do estado real, incluindo ocorrências de Silex Audit/Silex, RESET EXPLODE e potenciais identidades da marca;
- B. Renomeação **controlada dos elementos do port Linux** para **Ilmenyx** e assinatura **by Bismuth Technology**;
- C. Ajuste de créditos, licença, documentação e avisos de independência;
- D. Preparação do repositório para a organização da empresa e comunidade open source;
- E. Mapeamento de privacidade, riscos, licenças e checklist pré-lançamento;
- F. Briefing **não definitivo** para o futuro Manual de Identidade Visual da Bismuth Technology;
- G. Build/testes do que foi tocado, revisão de diffs e relatório final.

**Não** reimplemente o programa inteiro nesta sessão. Não inicie fases pesadas de GPU/Vulkan, drivers, overlay e benchmarks de estresse. Se o port Linux ainda não existe no workspace, implemente primeiro os documentos, inventário e **somente o menor incremento seguro** de UI/metadados, sem criar arquivos fictícios que pareçam um produto pronto. Se a portabilidade já estiver avançada, adapte os recursos reais existentes.

## 3. Fase A — Auditoria e mapa das ocorrências

1. Registre SHA, branch, remote upstream e estado do working tree. Localize e leia, quando presentes: `README.md`, `LICENSE`, `NOTICE*`, `ATTRIBUTION*`, `AGENTS.md`, arquivos `*.sln*`, `*.csproj`, `.github/workflows`, `src`, `tests`, `docs`, `Assets`, `Drivers`, recursos XAML, arquivos `.desktop`, Flatpak/AppStream, scripts de build e instaladores.
2. Faça busca **somente de leitura** pelas expressões: `Silex`, `Silex Audit`, `Ilmenyx`, `Bismuth`, `ResetExplode`, `RESET EXPLODE`, `GustavoReset`, `RESET BENCH`. Inclua `rg --hidden` com exclusão consciente de `.git`/binários/cache. Não substitua resultados automaticamente.
3. Crie `docs/branding/RENAME-INVENTORY.md` com tabela: **caminho, ocorrência, contexto, decisão (renomear/manter), motivo, risco, estado de migração, teste de validação**.
4. Diferencie áreas: (i) marca nova e interface Linux, (ii) nomes internos de projetos novos, (iii) código legado Windows e benchmarks originais, (iv) licença/créditos, (v) histórico e relatórios de versões anteriores, (vi) configurações/dados de usuários.
5. Audite as principais dependências Windows WPF/DirectX/WMI/ETW/Registry e o que já foi extraído para Core/Linux/Avalonia; faça link para docs de portabilidade existentes. Mantenha o plano técnico atualizado, sem duplicação conflitante.
6. No inventário de propriedade intelectual, cubra **código, assets, ícones, screenshots, fontes, bibliotecas NuGet transitivas, SDKs, drivers e binários**. Registre licença e fonte de cada item, sem assumir MIT para todo o conteúdo.

**Aceite A:** todas as mudanças de nomes feitas posteriormente são justificáveis pelo inventário; pontos de possível quebra identificados.

## 4. Fase B — Renomeação segura para Ilmenyx

Implemente onde houver port Linux real e o risco for controlado:

### 4.1 Textos visíveis e metadados

- Título da janela e `About/Sobre`: `Ilmenyx` e `by Bismuth Technology` em espaço adequado, sem atribuir o upstream à Bismuth.
- README principal e descrições de projeto: nome Ilmenyx, Linux, código aberto, origem, status exato das funções, instruções testadas, limitações, capturas **reais** apenas.
- CLI/ajuda: nome de exibição Ilmenyx e comando `ilmenyx`, se já houver CLI; manter aliases de versões anteriores apenas se necessário e documentar depreciação.
- Assembly/package metadata do **novo port** (`Product`, `Description`, título, nome do pacote, nomes de release) quando verificáveis. **`Company` como crédito de mantenedor não deve ser usado como prova de titularidade autoral/jurídica.**
- Ícones e imagens: NÃO reutilizar símbolo/logo do RESET sem verificar licença própria. Temporariamente usar rótulo tipográfico neutro ou ícone genérico de licença comprovada, claramente provisório.
- Remover `Silex Audit` dos **metadados ativos do port**, mas preservá-lo se necessário em changelog, notas de migração, antigos identificadores de preferências ou histórico.

### 4.2 Código e compatibilidade

- **Não renomeie automaticamente classes, namespaces e caminhos de projetos WPF originais**. Nos projetos C# novos, use `Ilmenyx.Core`, `Ilmenyx.Contracts`, `Ilmenyx.Platform.Linux`, `Ilmenyx.Desktop`, `Ilmenyx.Cli` **somente quando isso for viável**, sem quebrar referências ou acoplamentos. Faça por mudanças atômicas.
- Atente à inclusão recursiva de `*.cs` no `ResetExplode.csproj` legado; novos `src/` e `tests/` dentro do mesmo diretório podem ser compilados indevidamente pelo WPF. Corrija/exclua globs após inspeção e valide Windows quando possível.
- Mapeie namespace, package ID, app ID, executable, nome de `.desktop`, cache/estado, diretórios XDG, atalhos, URLs internas, nomes no instalador, metadados AppStream, Flatpak e CI. **Não invente domínio próprio** para um app ID reverso; se não houver decisão válida, use valor parametrizado e informe que precisa ser definido antes de empacotar.
- Migre preferências locais antigas somente se houver formato antigo real; mantenha compatibilidade e rollback, teste caminhos existentes e não apague configurações automaticamente.
- Se não houver binários publicados, documente que a compatibilidade de nomes antigos é opcional; não crie migração especulativa.
- Gere `docs/branding/RENAME-DECISIONS.md` com identificadores aceitos/propostos, escolhas adiadas e impacto em Linux/Windows.

**Aceite B:** busca final não encontra uso indevido de Silex nos pontos ativos do port; ainda existem menções necessárias a RESET EXPLODE nos créditos. Builds afetados não pioram.

## 5. Fase C — Origem, licença MIT e créditos

1. **Mantenha o arquivo `LICENSE` original completo**, incluindo `Copyright (c) 2026 Gustavo (GustavoReset)` e o texto da MIT. Nunca substitua o titular original pelo nome da empresa.
2. Para código novo, assuma **MIT como intenção de projeto**, sujeita à revisão de contribuições e dependências. Registre se há arquivos com licenças diferentes e condições próprias. Não invente cessões de direito autoral.
3. Crie ou atualize `NOTICE.md`/`ATTRIBUTION.md` (PT-BR e/ou EN) descrevendo origem, port Linux independente, mantenedor, alterações relevantes e não afiliação. Avisos informativos **não substituem a licença**.
4. Implemente/planeje `Sobre → Licenças e créditos` na interface: link upstream, autor original, MIT, lista de dependências e links de licenças quando verificadas. Se UI não estiver implementada, modele texto/recursos de localização sem criar um botão não funcional.
5. Incluir nos releases futuros licenças e atribuições necessárias também **junto aos binários**; documentar mecanismo de geração/cópia para os empacotadores, sem publicar binários automaticamente.
6. Textos oficiais sugeridos, adaptando apenas dados factuais:

   **PT-BR:** `Ilmenyx é uma adaptação independente e não oficial para Linux, derivada do RESET EXPLODE, criado por Gustavo (GustavoReset). O projeto é mantido sob a identidade Bismuth Technology e não possui afiliação, patrocínio ou endosso da equipe RESET. Os avisos de direitos autorais e licenças aplicáveis estão preservados.`

   **EN:** `Ilmenyx is an independent, unofficial Linux adaptation derived from RESET EXPLODE by Gustavo (GustavoReset). It is maintained under the Bismuth Technology identity and is not affiliated with, sponsored by, or endorsed by the RESET team. Applicable copyright notices and licenses are preserved.`

7. Gere `docs/legal/UPSTREAM-PROVENANCE.md` com upstream, licença, SHA de origem, caminhos reaproveitados/modificados e evidências. Gere `docs/legal/THIRD-PARTY-INVENTORY.md` listando pacote/asset, versão, licença, fonte, obrigação, estado e bloqueios. Para licenças desconhecidas, marque **BLOQUEIA REDISTRIBUIÇÃO ATÉ REVISÃO**.
8. Se houver materiais visuais do RESET, não presuma que estão cobertos pela mesma licença do código. Consulte a origem; se não for possível demonstrar permissão, remova/substitua do pacote futuro ou adie o empacotamento.

**Aceite C:** README, About/aviso (se implementado) e documentação são consistentes; LICENSE inalterado; nenhuma afirmação de afiliação ou copyright exclusivo.

## 6. Fase D — Comunidade, organização GitHub e governança

1. Preparar `.github/ISSUE_TEMPLATE` para bugs e sugestões, `CONTRIBUTING.md`, `SECURITY.md` e eventual template de PR **para o fork da Bismuth**, sem gerar mecanismos de contato inexistentes. Não impor CLA, cessão de direitos, DCO ou regras comerciais novas automaticamente.
2. README com seção explícita de créditos upstream, limitações técnicas, licença, comandos de instalação/compilação **efetivamente verificados**, roadmap identificado como planejado, segurança dos benchmarks e opção de sanitizar relatórios antes de anexá-los a Issues.
3. Não declarar que a organização GitHub existe ou que há empresa com CNPJ. Elaborar `docs/governance/GITHUB-ORG-CHECKLIST.md`: criar organização quando decidido, 2FA, poucos owners, permissões mínimas, branch protection, revisão de secrets, planejamento de fork/rename, releases independentes e URLs a preencher depois.
4. Preserve o vínculo upstream quando for fork real. Se o checkout já for clone independente, **não falsifique vínculo GitHub**; mantenha upstream remoto e provenance, documente caminhos possíveis para fork real sem ações remotas.
5. Criar `docs/governance/OWNERSHIP-AND-CONTRIBUTIONS.md` com distinção entre pessoa física, possível pessoa jurídica, mantenedores GitHub e titulares de direitos. Não presumir transferência automática à empresa.
6. Explicitar que produtos/jogos futuros da Bismuth podem ter licenças diferentes, mas a licença MIT do código derivado deve ser respeitada e redistribuições devem seguir as condições aplicáveis.
7. Se repositório publicar Windows + Linux juntos, separar release notes, build workflows e distinção clara de versão oficial upstream vs versão independente Ilmenyx. Nunca publicar o `.exe` WPF upstream como produto novo sem revisão.

## 7. Fase E — Privacidade, segurança e benchmarking responsável

1. Auditar **dados realmente coletados/processados**: hostname, usuário, IP/MAC, serial, UUID, paths, processos, temperatura, logs, crash dumps, relatórios. Criar inventário dos campos com origem, necessidade e modo de exposição.
2. Por padrão, não enviar dados a servidores nem baixar componentes sem opt-in. Se o software existente já realiza conexões, registrar e não apresentar falsa promessa de funcionamento totalmente offline.
3. Exportar relatório local com campos potencialmente identificadores **desmarcados/redigidos por padrão quando possível**; oferecer prévia ou aviso antes de compartilhar. Testar pelo menos casos com hostname, username, MAC, IP, serial e paths, se presentes. Nunca declarar anonimização irreversível sem validação.
4. Criar `PRIVACY.md` compatível com o comportamento real; separar dados locais dos dados que o mantenedor pode receber via issue/anexo. Contatos, prazos de retenção e operações desconhecidas ficam `[PENDENTE]`.
5. `SECURITY.md` deve apresentar canal real apenas se já fornecido e alternativa operacional segura; não pedir que usuários publiquem dados sensíveis em issue pública.
6. Controle de execução: Linux sem root por padrão, leitura de `/proc`/`/sys` com validação e timeouts, comandos externos sem shell injection, sensores ausentes como `indisponível`, benchmarks só sob ação explícita e limites seguros; sem alterações de dispositivos de bloco, firmware ou drivers.
7. Para benchmarks preservados do RESET, documentar algoritmo, versão, workload, dados de validação e por que pontuação Linux não é necessariamente comparável ao Windows. Distinguir implementado/experimental/não suportado.
8. Atualizar checklist de LGPD/ANPD somente conforme operações verificadas; não emitir parecer jurídico automático.

## 8. Fase F — Preparação do futuro Manual de Identidade Visual

O mantenedor desenvolverá **depois, em um trabalho separado**, o Manual de Identidade Visual / Brand Guidelines da **Bismuth Technology**. **Não inventar nem fixar agora** logotipo, tipografia, paleta, Pantone, CMYK ou normas de aplicação.

Prepare apenas `docs/branding/BRAND-BRIEF.md` e, se útil, `docs/branding/BRAND-INTEGRATION-TODO.md`, com:

### Marca-mãe: Bismuth Technology
- Nome escrito sempre **Bismuth Technology**.
- Universo conceitual: minerais, cristais, gemas, elementos químicos, estrutura cristalina, precisão, tecnologia e inovação.
- Arquitetura: Bismuth Technology (empresa), Ilmenyx (produto), futuros softwares e jogos com nomes próprios ligados a minerais/gemas.
- Público-alvo: entusiastas de hardware, usuários Linux, técnicos, desenvolvedores e público de software de qualidade; marca da empresa pode abranger softwares, jogos e serviços pagos ou open source.
- Valores/posicionamento **propostos**, nunca apresentados como já aprovados: precisão, transparência, robustez, curiosidade técnica e responsabilidade.

### Produto: Ilmenyx
- Tom: técnico, claro, confiável e acessível, sem promessas não comprovadas de precisão/desempenho.
- Nome de produto `Ilmenyx` e assinatura textual opcional `by Bismuth Technology`.
- Descritivo em vez de sufixo oficial: `Hardware diagnostics & benchmarking for Linux`.
- Ilmenyx pode ter identidade subsidiária, subordinada às regras da marca-mãe **quando forem aprovadas**.
- Não reutilizar a marca RESET como elemento gráfico da identidade Ilmenyx; a menção ao upstream permanece em créditos e seção Sobre.

### O futuro manual deverá definir (itens pendentes)
1. Logotipo da Bismuth: versões horizontal, vertical, reduzida, monograma, negativo, monocromático, fundos claros/escuros.
2. Área de proteção, tamanhos mínimos, grid, relação símbolo/logotipo, usos incorretos e contraste/acessibilidade.
3. Paleta primária/secundária: valores oficiais HEX/RGB e versões aprovadas para impressão; **CMYK e Pantone só após validação de impressão e referência adequada**, sem prometer equivalência matemática perfeita.
4. Tipografia primária/secundária, hierarquia e regras de uso; validação da licença das fontes para logo, distribuição de aplicativos, web e material comercial.
5. Iconografia, grafismos inspirados em cristais, imagens/fotografia, padrões gráficos, tom de voz, usos em redes, site, GitHub, jogos e UI.
6. Relação entre a marca institucional e as marcas de produto; aplicação da assinatura `by Bismuth Technology` e do aviso legal RESET em locais apropriados (crédito legal é distinto de endorsement/assinatura de marca).
7. Tokens de UI, dark/light theme e recursos (`SVG`/PNG) **a implementar somente depois da aprovação do manual**.

**Aceite F:** briefing e lista de decisões humanas prontos para futura etapa; nenhum logo/ativo gráfico é declarado final e nenhuma fonte comercial é copiada para o repositório sem licença.

## 9. Fase G — Checklist jurídico/técnico pré-publicação

Criar `docs/legal/PRE-RELEASE-CHECKLIST.md` com checkboxes, **responsável, evidência, data, status e bloqueio**. Incluir, no mínimo:

- [ ] Busca de anterioridade no INPI para **ILMENYX** e **BISMUTH TECHNOLOGY**, incluindo radicais, semelhanças fonéticas e classes relevantes (avaliar 9/42 e outras conforme atividade real); decisão documentada do titular ou especialista.
- [ ] Busca internacional conforme países de distribuição/atividade e conferência de uso comercial, handles, GitHub org e domínio (disponibilidade de domínio não comprova direito de marca).
- [ ] Decisão sobre eventual depósito/registro de marca, titular atual e custeio. Sem símbolo `®` antes de registro aplicável.
- [ ] Titularidade do código novo, da identidade visual, contribuições de terceiros, contratos/autorizações e eventual formalização da empresa.
- [ ] Histórico, arquivo LICENSE e avisos de copyright upstream preservados; credits/NOTICE/Sobre corretos; ausência de falsa afiliação.
- [ ] Licenças de dependências transitivas, assets, fontes, drivers, SDKs, imagens e binários verificadas; arquivos sem direito comprovado excluídos da distribuição.
- [ ] Auditoria de dados, política de privacidade factual, tratamento dos relatórios e revisão de obrigações legais se aplicável.
- [ ] Segurança, limite de benchmarks, cancelamento, sem `sudo` silencioso, dependências e vulnerabilidades checadas.
- [ ] Nomes e metadados consistentes para CLI, executável, `.desktop`, AppStream/Flatpak/deb/rpm, atalhos, configs e README; ícone final licenciado.
- [ ] Testes Linux reais em distros/hardware representativos, CI em execução e diferenciação entre executáveis oficiais RESET e independente Ilmenyx.
- [ ] Manual de Identidade Visual da Bismuth e assets Ilmenyx aprovados, quando existirem; contraste, tamanhos e créditos de fontes verificados.
- [ ] Artefatos assinados/checados apenas se houver infraestrutura; checksums reais e rastreabilidade de versão/build.
- [ ] Aprovação humana explícita para criar organização, realizar push público, tags e releases.

**Nunca marque automaticamente itens como conformidade jurídica confirmada.** Separe `IMPLEMENTADO`, `VERIFICADO POR TESTE`, `PENDENTE DE REVISÃO` e `BLOQUEADO`.

## 10. Continuidade da portabilidade técnica (roadmap, NÃO executar todo agora)

Respeitar o plano de migração, já existente ou a ser documentado:

- **Etapa 0 — inventário técnico:** dependências WPF, WMI, ETW, DirectX, algoritmos, licenças, módulos reutilizáveis. Referências úteis no upstream, se existirem: `BenchRenderer.cs`, `GameSimBench.cs`, `MemoryBench.cs`, `DiskBench.cs`, `SensorMonitor.cs`, `SystemInfoCollector.cs`, `GpuInfoCollector.cs`, `ReportBuilder.cs`, `GpuGameBench.cs`, `GpuRtBench.cs`, `FrameMonitor.cs`.
- **Etapa 1 — slice Linux:** Core/Contracts + adaptador `/proc`/`/sys`, CLI informativo JSON, UI Avalonia, dados reais/indisponíveis, testes automatizados. Evitar acoplamento Windows e inclusão indevida de `src/**` no `.csproj` WPF original.
- **Etapa 2 — CPU:** extrair o `BenchRenderer` original, preservar cargas, sementes, metodologia, cancelamento e testes de determinismo. Não reivindicar equivalência de scores sem validação.
- **Etapa 3 — sensores:** hwmon/thermal/DRM/NVML conforme recursos reais, com unidades, permissões e ausência de dados explícitas.
- **Etapa 4 — RAM/disco/rede:** limites, opt-in, arquivos temporários, metodologia de cache, sem escrita em bloco/benchmark destrutivo, relatórios versionados e com privacidade.
- **Etapa 5 — GPU:** estudo de equivalência antes de Vulkan/ray tracing; overlay Linux com limitações X11/Wayland, sem injeção insegura em jogos.
- **Etapa 6 — ferramentas/integrações:** preferir leitura; drivers/overclock/otimizações apenas após segurança, permissão explícita e reversibilidade.
- **Etapa 7 — distribuição:** testes Ubuntu/Debian, Fedora e distros relevantes, AMD/Intel/NVIDIA quando hardware disponível, Wayland/X11, pacotes Linux e CI seguros.

Avaliar SDK .NET LTS disponível e compatibilidade do legado Windows; **não migrar runtime/versões às cegas**. Separar Core de platform-specific e UI, com adapters. Use Rust apenas para subcomponentes se medições justificarem. Não reescrever todo projeto em outra linguagem.

## 11. Arquivos esperados ao final (ajuste a caminhos existentes)

Arquivos a criar/atualizar **somente quando não houver equivalente adequado**:

```text
README.md
LICENSE                     # upstream original preservado
NOTICE.md                    # ou ATTRIBUTION.md
docs/branding/
  RENAME-INVENTORY.md
  RENAME-DECISIONS.md
  BRAND-BRIEF.md
  BRAND-INTEGRATION-TODO.md
docs/legal/
  UPSTREAM-PROVENANCE.md
  THIRD-PARTY-INVENTORY.md
  PRE-RELEASE-CHECKLIST.md
  COPYRIGHT-AND-CONTRIBUTIONS.md
docs/governance/
  GITHUB-ORG-CHECKLIST.md
  OWNERSHIP-AND-CONTRIBUTIONS.md
PRIVACY.md
SECURITY.md
CONTRIBUTING.md
.github/ISSUE_TEMPLATE/        # quando viável
```

Pontos opcionais de implementação real: título/sobre/strings localizadas, metadados dos projetos Linux, `.desktop`, CLI, testes de strings de créditos, sanitização de relatórios, scripts de check de atribuição e lint de metadados. Adicione apenas se houver código/estrutura compatível.

## 12. Critérios de qualidade e execução

1. Antes de editar, apresente plano de ação resumido e inventário Git. Prossiga com alterações seguras; não interrompa por decisões que podem ser explicitamente marcadas como pendentes.
2. Faça alterações em commits pequenos **se o usuário já autorizar commit no ambiente**; caso contrário, apenas prepare diffs e sugira mensagens de commit. Não `git push`.
3. Execute os builds e testes disponíveis (`dotnet build`, `dotnet test`, validação `.desktop`, lint/links quando presentes) sem testes de carga; inclua comandos e resultados. Falta de ambiente deve aparecer como `NÃO TESTADO` e não `APROVADO`.
4. Faça busca final por `Silex` e demais nomes, inspeção do diff, teste de rotas/configs, link original e copyright no LICENSE. Garanta que o app Windows original permaneça separado; se host Linux não puder buildar WPF, registre essa limitação.
5. Não insira imagens genéricas da internet, logotipos falsos, números de benchmark fictícios, funcionalidades que não existem nem contatos inventados.
6. Se houver bloqueio de distribuição por licença ou marca, **não tente contornar o bloqueio**: reporte-o e deixe pronto o processo de correção.

## 13. Formato obrigatório do relatório final do Codex

Responda em **português brasileiro**, com:

1. **Estado do repositório:** branch, SHA, status, arquivos relevantes realmente analisados.
2. **Mudanças implementadas:** nome Ilmenyx e Bismuth nos pontos corretos, créditos, documentos, UI/metadados e testes; distinguir de tarefas meramente planejadas.
3. **Inventário de renomeação:** ocorrências de Silex removidas, legadas mantidas e razões para referências ao RESET EXPLODE.
4. **Licenças e proveniência:** o que foi confirmado; dependências/assets bloqueados; copyright original preservado.
5. **Segurança/privacidade:** comportamento observado, melhorias feitas, riscos pendentes.
6. **Marca:** Ilmenyx escolhido mas ainda sem comprovação de registro/disponibilidade legal; checklist humano, sem alegações conclusivas.
7. **Brand Guidelines:** briefing entregue, decisões pendentes para criação futura, sem afirmar existência de identidade visual aprovada.
8. **Builds/testes:** comandos, saídas, falhas, ambiente não disponível, regressões conhecidas.
9. **Próximas ações:** ordem de execução segura, com prioridade/critério de aceite para cada pendência.
10. **Resumo de diff**, sem push, PR, releases ou mudanças remotas.

**COMECE AGORA:** inspeção do repositório real; execute o inventário, a renomeação controlada e os documentos de conformidade do Ilmenyx; rode as verificações disponíveis; reporte o que depende do mantenedor. Não entregue somente planejamento, não declare verificação legal concluída e não crie identidade visual definitiva nesta tarefa.

## 14. Fontes de referência (verifique atualidade na execução)

- Projeto base: https://github.com/GustavoReset/ResetExplode
- Licença base: https://github.com/GustavoReset/ResetExplode/blob/main/LICENSE
- Documentação de forks: https://docs.github.com/en/pull-requests/reference/forks
- INPI, guia de marcas: https://www.gov.br/inpi/pt-br/servicos/marcas/guia-basico
- INPI, consulta: https://servicos.busca.inpi.gov.br/marcas
- Avalonia, deploy Linux: https://docs.avaloniaui.net/docs/deployment/linux
- Avalonia, empacotamento: https://docs.avaloniaui.net/tools/parcel/packaging-for-linux
