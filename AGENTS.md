# AGENTS.md — diretrizes do fork de portabilidade Linux
<!-- REUSE-IgnoreStart -->

## Objetivo
Portar incrementalmente o RESET EXPLODE de WPF/.NET Windows para Linux com Avalonia + .NET, preservando compatibilidade futura com Windows e a autoria/licença do projeto original.

## Antes de alterações
- Leia o README, a LICENSE, os `.csproj`, docs de portabilidade e código relacionado à tarefa.
- Verifique `git status` e a branch; não descarte mudanças de terceiros.
- Diferencie fatos observados, suposições, funcionalidades implementadas e funcionalidades não testadas.
- Prefira pequenas mudanças incrementais e código original reaproveitável a recriações desnecessárias.

## Arquitetura
- `Core`/`Contracts` não podem depender de WPF, Windows Desktop, WMI, Registry, ETW, DirectX ou da Avalonia UI.
- Separe backends por SO; não espalhe condicionais de plataforma nos algoritmos.
- Valide inclusão implícita recursiva de arquivos do SDK-style `.csproj` da raiz antes de criar projetos em `src/` ou `tests/`.
- Use `CancellationToken`, timeouts, validação de unidades e retornos explícitos `indisponível` quando necessário.
- Evite código fictício, telemetria, dependências desnecessárias e serviços com privilégios altos.

## Benchmarks e segurança
- Preserve workload, sementes, algoritmo, pontuação e versão do RESET BENCH quando tecnicamente viável.
- Não afirme que scores Linux e Windows são diretamente comparáveis sem metodologia e validação.
- Nunca execute stress, benchmark de disco pesado, overclock, drivers ou modificações do sistema em CI.
- Benchmark de disco usa somente arquivo temporário em caminho autorizado, com limites, verificação de espaço, cancelamento e limpeza em finally; nunca dispositivos físicos diretamente.
- Sem `sudo` implícito, instalação automática, push, release ou alteração de dados do usuário.
- Leitura de sensores pode estar indisponível: não invente temperatura, consumo ou saúde do disco.

## Qualidade e documentação
- Crie testes unitários com fixtures de `/proc` e `/sys`, tratamento de falhas e casos extremos.
- Execute build/testes disponíveis e reporte resultados exatos. Se não testado, diga `NÃO TESTADO`.
- Atualize os docs de arquitetura, compatibilidade, metodologia e validação quando mudar comportamento.
- Preserve avisos MIT e confira licenças de terceiros/assets/drivers antes de redistribuir.
- Mantenha o app WPF original operável. PRs ao upstream não são o fluxo atual; prepare documentação para o fork e para eventual contato com a equipe.

## Silex Audit — decisões locais da Fase 1
- Marca dos novos executáveis: Silex Audit; código independente, sem assets RESET.
- Original preservado no submódulo upstream/ResetExplode; não alterar ou atualizar seu SHA sem revisão.
- Solução Linux explícita Silex.Audit.slnx; não existe csproj WPF na raiz deste workspace.
- Use .NET 10 e os comandos de README.md; telemetria de build desativada.
- Não alegar clean-room estrito: auditor e implementador tiveram acesso ao código original.

## Continuação da Fase 2
- CpuBenchmarkRunner usa validation por padrão; testes nunca executam o perfil Full.
- Full exige opt-in explícito; não executá-lo automaticamente, mesmo para comparar scores.
- Golden vem do upstream reduzido, via scripts/generate-cpu-reference.sh; não regenerar para ocultar divergência.
- Algoritmo e método de medição têm versões separadas; score Linux/Windows permanece não validado.

## UI CPU e corpus original
- UI só inicia Validation por ação explícita; não adicionar Full à inicialização nem a testes.
- Corpus original é limitado a 16 pixels/1024 spp; o oracle nunca chama Run full.
- Callbacks de progresso devem respeitar dispatcher, geração e descarte da janela.

## Sensores e validação gráfica
- hwmon é somente leitura; falhas são null/indisponível, zero só se informado pela fonte.
- Não inferir calibração, saúde ou proteção térmica a partir de sensores brutos.
- Monitor é opt-in, sem histórico; preserve limites, cancelamento e descoberta por coleta.
- Teste gráfico opt-in usa tools/Silex.Audit.UiSmoke e eventos no processo Avalonia.
- Não usar injeção global de teclado/mouse: há risco de atingir outras janelas.

## Diagnósticos de energia/GPU/SMART
- thermal e hwmon podem repetir fontes: não deduplicar nem somar por rótulo.
- power_supply usa unidades próprias; temp em décimos °C, energia em µWh, carga em µAh.
- present inválido/negado/zero bloqueia valores numéricos potencialmente antigos.
- GPU disponível no inventário não significa contadores disponíveis; não inferir VRAM de iGPU.
- smartctl apenas pela CLI com disco/opt-in explícitos; nunca em info, polling ou testes de hardware automáticos.
- Não acrescentar opções SMART de escrita, self-test, sudo ou detecção automática de transporte.

## Critério de aceite definido pelo usuário
- Toda implementação deve funcionar no Linux e passar por testes antes de avançar para a tarefa seguinte.
- Registre o ambiente, os comandos, as evidências e a decisão de aceite por funcionalidade.
- Fixture aprova comportamento de código; não certifica suporte ao hardware não disponível.
- Não declare aprovação completa de funcionalidade cujo caminho real de sucesso permanece NÃO TESTADO.
- Se faltarem hardware/permissões/ambiente, registre o bloqueio e não avance silenciosamente de fase.
- Testes passando não equivalem a aprovação humana; não atribua ao usuário aprovação que ele não deu.

## Exceção de hardware autorizada pelo usuário
- A máquina disponível não possui AMD/NVIDIA. Não bloquear o avanço por esses testes reais ausentes.
- Manter aviso visível na interface: AMD/NVIDIA — Testes não realizados em hardware real.
- A exceção não transforma fixtures em certificação de hardware e não dispensa testes no Linux disponível.

## Relatórios HTML
- Exportar snapshots existentes sem disparar nova coleta, SMART ou benchmarks.
- Todo dado variável entra somente como texto escapado; sem scripts, URLs/recursos remotos ou HTML arbitrário.
- Preservar proveniência, unidades, datas, qualidade e indisponibilidades; não atribuir saúde/score.
- JSON e HTML compartilham escrita atômica sem overwrite, com cancelamento e limpeza de temporários.

## Validação de memória 0.6.0
- bench memory e UI são somente correção reduzida, com 256 KiB por buffer e workers 1/2/4 (UI 1).
- Não reportar ns, GB/s, FPS ou score; não introduzir perfil completo/carga/duração automaticamente.
- Golden independente vem dos métodos RAM upstream reduzidos via scripts/generate-memory-reference.sh.
- Não chamar métodos de disco/MeasureLoading do upstream. Preserve atribuição MIT e guardas do oracle.
- GameSim requer oracle de passos fixos e snapshots coerentes antes de portar workload/medição.


## Validação GameSim 0.7.0
- Somente 256 entidades/16 passos fixos, workers 1/2/4 (UI 1), sem warmup temporizado/FPS/score.
- Engine adapta métodos MIT upstream com atribuição; não alegar clean-room estrito.
- Oracle independente via scripts/generate-gamesim-reference.sh remove Run antes de compilar.
- Estado publicado somente em cópias após Step completo; cancelamento/timeout não publica State/hash.
- Nenhum teste pode iniciar GameSim completo, disco pesado ou simulação sustentada.

## Registro contínuo de testes
- Por instrução do usuário: executar testes de funcionalidades sempre que houver ambiente, hardware e autorização necessários.
- Registrar casos que não possam ser executados em Sem_teste.md, com motivo e condição para retestar em dispositivo/VM.
- Atualizar o registro ao fechar um caso; não confundir fixtures, preparação ou build com execução real.

- Nova orientação do usuário permite continuar recortes testáveis enquanto casos sem ambiente ficam registrados em Sem_teste.md; não ocultar pendências.
- Fazer commits locais por incremento validado, preservando arquivos de apoio do usuário; sem push/PR/release automático.

## Desktop 0.8.0
- Avalonia 12.1.4 com Wayland opt-in, via SILEX_DISPLAY_BACKEND=wayland; padrão auto preserva X11.
- Smoke nativo exige DISPLAY removido e implementação Avalonia.Wayland.WindowImpl; regressão X11 também necessária ao alterar UI/backend.
- bench sim-plan é somente preparação revisável, sem runner completo/FPS/score; não tratá-lo como teste de carga.


## GameSim completo experimental 0.9.0
- bench sim-full exige --allow-cpu-load; UI e bench sim permanecem reduzidos.
- Não executar o runner de carga em testes automáticos; testes usam engine/relógio controlados ou pré-cancelamento sem alocar engine completo.
- Limites: 30.000 entidades, warmup 2 s, medição 1–10 s, timeout até 15 s, 200.000 amostras.
- Status interrompido/limite/tempo inválido não publica FPS/low1/médias/score. Registrar carga real NÃO TESTADA em Sem_teste.md até autorização/validação.

## Adendo permanente — Ilmenyx / Ilmenyx Technology (2026-10-21)

As decisões de nome deste adendo prevalecem sobre as referências históricas a Silex acima;
as regras de arquitetura Linux, benchmarks, testes, licenças e segurança anteriores permanecem.

- Produto Linux: Ilmenyx; marca mantenedora: Ilmenyx Technology (grafia exata), sem sufixo Audit no nome principal.
- Origem: RESET EXPLODE de Gustavo (GustavoReset), adaptação independente e não oficial, sem afiliação, endosso ou patrocínio.
- Preservar a MIT integral do RESET EXPLODE em LICENSES/MIT.txt (Copyright (c) 2026 Gustavo (GustavoReset)), referência upstream e direitos de dependências/assets verificados individualmente. (Atualizado 2026-11-07: o LICENSE da raiz agora é a PolyForm Noncommercial; ver "Licença 2026-11-07".)
- Ilmenyx não implica marca registrada/disponível: não usar símbolo de registro, exclusividade ou alegação de liberação no INPI.
- Organização GitHub não prova pessoa jurídica; não atribuir direitos à empresa sem definição formal.
- Renomear somente identificadores ativos seguros do port; não substituir cegamente nomes WPF, benchmarks, histórico, créditos ou formatos.
- Manual de Marca ainda será criado. Não inventar logo final, paleta, tipografia, CMYK, Pantone, monograma ou ícones oficiais; registrar integrações pendentes.
- UI/metadados: Ilmenyx e quando apropriado by Ilmenyx Technology. Créditos RESET/licenças em README/NOTICE/Sobre, nunca omitidos.
- Sem divulgação remota, release, publicação ou registro de marca sem aprovação; sem root, drivers, BIOS, overclock, escrita direta em discos, telemetria ou stress automático.
- Registrar IMPLEMENTADO, NÃO TESTADO, PENDENTE DE REVISÃO e BLOQUEADO com evidências reais.
- Solução atual Ilmenyx.slnx, src/Ilmenyx.*, tests/Ilmenyx.Tests e tools/Ilmenyx.UiSmoke. Formatos silex-* e evidências anteriores permanecem históricos/compatíveis.
- ILMENYX_DISPLAY_BACKEND é atual; SILEX_DISPLAY_BACKEND continua alias legado, com precedência do nome atual.
- Atualização 2026-10-29: o mantenedor aprovou a identidade visual do Ilmenyx (docs/branding/MANUAL-ILMENYX.md). A regra de não inventar
  logo/paleta/ícones passa a significar: usar os ativos e tokens aprovados (Palette.cs, IlmenyxTheme.cs, AppIcon.cs/Assets, packaging/linux);
  mudanças de identidade exigem nova aprovação. Continua sem presunção de registro de marca.
- Atualização 2026-11-09: o produto passa a se chamar **Ilmenyx Controller** e a organização **Ilmenyx Technology** (GitHub
  `Ilmenyx-Technology`, antes `Bismuth-Technology`). App ID `io.github.ilmenyx_technology.IlmenyxController`. O nome completo vai em
  títulos de janela, menu do sistema, Sobre, relatórios, metadados e pacotes; em frases corridas, "o Ilmenyx" é a forma curta aceita.
  Projetos, namespaces, binários (`ilmenyx`, `Ilmenyx.Desktop`), variáveis `ILMENYX_*` e pastas de dados (`~/.local/share/ilmenyx`)
  continuam como estão. Registros históricos (evidências, busca de marca, relatório de licença) mantêm o nome Bismuth.


## Licença 2026-11-07 (prevalece sobre as regras de licença anteriores deste arquivo)
- Todo o código e a documentação do mantenedor, desde o primeiro commit (inclusive a fase Silex), estão sob PolyForm Noncommercial 1.0.0 + permissão adicional (LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt, PT prevalece). Explicação: LICENSING.md. LICENSE-ILMENYX.md não existe mais.
- Permitido: uso como ferramenta em trabalho pago, instalar no PC do cliente sem cobrar pelo app, vídeos monetizados, redistribuição gratuita sem cadastro/anúncio, modificação para uso interno. Proibido: vender, cobrar acesso, serviço pago, derivado comercial. Doações ao mantenedor são aceitas e não mudam os termos.
- Titular: "Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)" até o nome civil completo ser informado (docs/governance/PENDENCIAS.md). Ilmenyx Technology não é pessoa jurídica.
- **Todo arquivo novo** em src/, tests/, tools/, scripts/, packaging/ leva no topo (após `#!`/`#version`/`<?xml`):
  `SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0` e `SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)`.
- Arquivo que adapta código, dados, estrutura ou layout do RESET EXPLODE: `SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0`, com `SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)` e a linha do mantenedor; acrescentar o arquivo à lista de NOTICE.md.
- Arquivo sem comentário possível (imagem, JSON, binário): cobrir em REUSE.toml. Terceiros mantêm seu identificador (ex.: Icons.cs `PolyForm-Noncommercial-1.0.0 AND Apache-2.0`); registrar em THIRD-PARTY-NOTICES.md.
- Pacotes levam LICENSE, LICENSES/, NOTICE.md, LICENSING.md e THIRD-PARTY-NOTICES.md. Metainfo: `MIT AND PolyForm-Noncommercial-1.0.0`.
- Não aceitar código externo (CONTRIBUTING.md). Não anunciar como open source OSI. Não remover nem alterar o copyright/texto MIT do Gustavo.
- Workspace/repositório local histórico: /mnt/HDD/Github/Ilmenyx; preservar histórico e submódulo.


## Exportação dos resultados pela UI 0.12.0
- Botões HTML exportam o snapshot selecionado; não repetir coleta/workload ao exportar.
- Preservar guarda de troca do resultado durante o diálogo e testes JSON/HTML em ambos os backends.

## Teclado e acessibilidade 0.13.0
- Manter retorno do foco ao botão após reabilitá-lo, ciclo local dos diálogos e rótulos do campo de destino.
- Teste com eventos roteados valida handlers no processo; não equivale a teclado físico ou Orca/AT-SPI. Registrar separadamente.

## Layout mínimo 0.14.0
- Preservar quebra de ações no dashboard, rolagem do diálogo sem stop extra de Tab e título Sobre com quebra.
- Geometria deve usar tamanho real confirmado pelo backend; ampliação de fonte no harness não certifica toda escala/DPI ou tecnologia assistiva.

## Leitor de licenças 0.15.0
- Sobre usa seletor de documento e leitor expansível, com nome de controle estável. Não alterar Name de controle já estilizado.
- Preservar os quatro conteúdos integrais, somente leitura, nome acessível atualizado e navegação da lista/diálogo; não escrever no clipboard em testes automáticos.

## Texto ampliado 0.16.0
- Harness cobre 100/150/200% nos mínimos; manter quebra dos rótulos/títulos e leitor de licenças com ao menos 120 DIP nos casos testados.
- Não reduzir fonte do documento nem relaxar os limites geométricos para ocultar cortes; ampliação no processo não certifica matriz de DPI ou leitores de tela.

## Pacote local linux-x64 0.17.0
- Empacotador usa restore explícito, destino novo e framework-dependent; não incluir SDK/runtime/testes/driver silenciosamente.
- Hashes/inventário não liberam redistribuição. Smoke deve conferir Assembly.Location do desktop extraído; startup do apphost é separado de interação.
- Preservar artefatos em .artifacts ignorado e registrar VM/distro limpa NÃO TESTADA até execução real.

## Validação isolada do pacote 0.17.0
- Contêiner não equivale a VM limpa; registrar imagens/digests, modo de globalização e kernel compartilhado.
- Testes somente em imagem já local, usuário não-root, sem rede/capabilities/host devices/display, binds de entrada readonly e saída nova.
- ICU é requisito do modo padrão; invariant explícito no teste não aprova globalização padrão. RID linux-x64 glibc não certifica Alpine/musl.
- Evitar repetir aborts conhecidos por dependências ausentes; manter preflight e resultados negativos explícitos.

## Guarda ICU na CLI 0.18.0
- LinuxRuntimeDependencies fica no adaptador Linux; evitar APIs culturais antes da verificação.
- Falta de ICU nas rotas protegidas retorna 5 sem coleta/JSON parcial; não habilitar invariant silenciosamente.
- Par de bibliotecas encontrado não certifica ABI/culturas; documentar sonames e faixa testados. Teste negativo aprovado não aprova inventário sem ICU.

## Doctor CLI 0.19.0
- Doctor é textual, sem opções de exportação/carga, sem provedores de inventário e sem conexão gráfica.
- Bibliotecas carregáveis e variáveis não vazias não provam GUI/driver/fontes/ABI. Não habilitar recursos com base apenas nisso.
- Deve explicar ICU ausente sem APIs culturais/JSON; não ativar invariant automaticamente. Saída 0/5 reflete requisitos básicos CLI.


## Requisitos na UI 0.20.0
- Manter sondagem somente por Verificar; abrir aplicativo/diálogo não inicia esta consulta.
- Contracts contém snapshot neutro; Core escreve texto fixo sem APIs culturais. Desktop não referencia CLI.
- Não confundir requisitos básicos CLI com aprovação gráfica; não exibir valores de env.
- Preservar readonly, ciclo Tab/Escape/foco e supressão de resultados depois de fechar.


## Aceite em outra máquina — ferramentas do pacote 0.20.0
- validate-linux-machine.sh é coleta CLI em diretório novo; não certifica VM limpa/GUI/hardware pelo sucesso da CLI.
- Verificar hashes antes de executar o pacote; doctor bloqueante e smoke com prazos, sem instalação/elevação.
- Inventário/sensores exportados são locais e exigem revisão antes de compartilhar.
- Harness pode repetir solicitações de resize de forma limitada, mantendo tolerância e mínimo real; preservar falhas e não alegar causa raiz sem evidência.


## Resultado do aceite CLI 1.1
- Registrar modo informado pelo doctor, não inferir só do shell. Invariant não aprova globalização padrão.
- Formato ausente/ambíguo bloqueia inventário; mudança do texto exige revisão do protocolo.
- Smoke exige versão do inventário igual ao pacote e runtime informado não vazio.
- test-machine-acceptance.sh usa pacote real nos positivos; fixtures negativas são só protocolo/metadados e cópias temporárias.


## Privacidade IPv6 0.21.0
- Sanitizar candidatos IPv6 inteiros antes de MAC/IPv4; validar família e retirar escopo sem resolver interfaces.
- Preservar fixtures de PCI/horários/modelos e limites de texto. Não alegar anonimização ou cobertura de todo texto arbitrário.
- Schema de hardware permanece 1.2; não alterar workload/score para mudanças de privacidade.
- Usuário autorizou Claude Code como auxiliar. Separar tarefas/revisões para evitar edições concorrentes; só atribuir revisão recebida de fato.

- Em truncamento, evitar publicar token com dois-pontos cortado; manter limite de 512 também após expansão dos marcadores.
- Ocultar caminhos home antes de substituir IPv6 por marcador que contém espaço.
- Executar build/publish sequencialmente quando compartilham bin/obj, inclusive entre auxiliares.


## Sensores HTML 0.22.0
- SensorHtmlReport recebe snapshot e não coleta; CLI sensors --html usa uma coleta para stdout/arquivo.
- Preservar Channel/Quality/timestamps/fontes/unidades e não mostrar Value antigo se Status indisponível.
- Todo dado variável somente em texto escapado; reutilizar ReportFile para escrita e cancelamento.
- UI sensores continua JSON até incremento próprio com guardas de troca de snapshot/monitor.


## Sensores HTML UI 0.23.0
- UI oferece JSON/HTML; ExportAsync do viewmodel captura seleção antes do picker e bloqueia reentrada.
- Monitor segue independente; recusar gravação se snapshot mudou ou há refresh ao voltar do diálogo.
- Token de exportação ligado ao descarte; não escrever nem notificar após retorno de picker descartado.
- Preservar extensão, foco, arquivo novo e testes de monitor real/seleção nos dois backends.


## SMART offline 0.24.0
- smart-import lê somente stdin limitado, sem query/sysfs/dispositivos; não aceitar --allow-device-query nessa rota.
- Confrontar device.name e duplicatas/código; reutilizar parser existente para firmware/status.
- Wrapper smart-import distingue análise atual de data original não confirmada; não apresentar importação como saúde atual.
- Raw/serial/model/mensagens/horário bruto ficam fora da saída. Core só recebe DTO filtrado e texto escapado.
- Timeout limita a espera; não prometer interrupção de leitor não cooperativo ou do kernel.


## SMART importado na UI 0.25.0
- Importação só por ação explícita do usuário, com arquivo regular e disco declarado; sem smartctl/sysfs/dispositivo.
- Preservar recusa de links, /dev, /proc, /sys, limites de tamanho/tempo e bloqueio de reentrada durante exportação.
- Exportar o relatório selecionado, nunca reimportar ao exportar; relatório histórico não indica saúde atual.

## Monitor ao vivo 0.26.0
- Janela inicial Monitor amostra a cada 1 s ao abrir, por decisão explícita do usuário (2026-10-22); botão Pausar/Retomar obrigatório.
- Histórico só em memória (até 900 pontos); nunca gravar/exportar amostras ou nomes de processo. Somente leitura de /proc e hwmon.
- Preservar aviso AMD/NVIDIA na janela e o acesso a Diagnósticos e Licenças/créditos; não inferir saúde dos gráficos.
- Layout da janela principal segue a estrutura do RESET EXPLODE (autorizado pelo usuário em 2026-10-22); cores da imagem de referência são provisórias. Não copiar assets/logo/textos de marca do original.

- smartctl ao vivo passa a usar --health --attributes (somente leitura); métricas SMART são valores brutos sem veredito/limiar; ler só campos listados em docs/smart-import.md, nunca serial/modelo.
- DiskInfo.Kind/Bus são classificações best-effort do sysfs; transporte desconhecido fica indisponível.

## Portagem completa do original (decisão do usuário, 2026-10-22)
- Meta: portar todas as funções do RESET EXPLODE para Linux, com apenas as adaptações necessárias; ver docs/portagem-completa.md (matriz e ordem).
- Isto autoriza, substituindo restrições anteriores sobre essas funções: vereditos de saúde (disco/bateria) com heurística adaptada do original, jogos/launcher,
  overlay, Otimiza, Inicialização, Apps, Driver (inventário), OC (verificador), Maker, iluminação, stress e benchmarks completos, teste de rede.
- Salvaguardas mantidas: nada de carga, stress, benchmark pesado, rede ou alteração do sistema em testes automáticos, na abertura ou sem clique explícito
  com confirmação; limites, timeout, cancelamento e limpeza obrigatórios; alterações reversíveis com backup; sem sudo implícito (root = comando exibido);
  sem telemetria; serviços externos só por ação explícita; disco só em arquivo temporário autorizado; sem assets/marca do original; atribuição MIT.


## Pacote autocontido 0.28.0
- Por decisão do usuário (2026-10-22) o pacote embute runtime .NET e ICU app-local; isso substitui a regra de 0.17.0 de não incluir runtime. SDK, testes e drivers continuam fora.
- ICU embutido só com -p:IlmenyxBundleIcu=true e restore explícito; não ativar Invariant para contornar. Programas externos opcionais (GameMode, MangoHud, Flatpak, smartctl) não são embutidos: detectar e informar.
- Avisos do runtime (MIT) e do ICU (Unicode) acompanham em licenses/; sem liberação jurídica de redistribuição.


## GPU em Vulkan (módulo Ilmenyx.Gpu.Vulkan)
- Implementação independente em Silk.NET (MIT); não copia os shaders D3D do original. Shaders GLSL em Shaders/, SPIR-V gerado por scripts/build-gpu-shaders.sh e embutido.
- Carga de GPU só com clique + confirmação (UI) ou --allow-gpu-load (CLI), com duração máxima, cancelamento, corte por temperatura quando houver sensor (CPU se a GPU divide o chip), timeout de fence (5 s) e liberação do dispositivo em finally.
- Testes nunca criam dispositivo nem renderizam; o teste real é opt-in (ILMENYX_RUN_GPU_TESTS=1) e um "passou" sem a variável não prova nada sobre GPU.
- Sem janela/swapchain: render offscreen. Ray tracing (bench gpu-rt) usa VK_KHR_acceleration_structure + VK_KHR_ray_query (equivalente ao RayQuery do DXR 1.1); sem o hardware, registrar BLOQUEADO.
- Nunca escolher o adaptador CPU (llvmpipe, que também anuncia ray tracing) sem pedido explícito; o quadro de teste recusa configurações com quadro acima de 2 s.
- Scores de GPU são experimentais e não comparáveis ao Windows.


## Componentes integrados (0.30.0)
- Por decisão do usuário (2026-10-22) o usuário não deve instalar peças extras: MangoHud (MIT) e OpenRGB (GPL-2.0, programa separado) acompanham o pacote em `components/` (scripts/fetch-components.sh, versões e SHA-256 fixados); GameMode tem reserva embutida no app (ionice/renice do próprio usuário).
- Programa do sistema tem prioridade (`BoundedProcess.Find`); o embutido é reserva. Wrappers são gerados na pasta de dados do Ilmenyx, nunca em locais do sistema, sem root.
- O servidor OpenRGB só inicia por clique (ou `ilmenyx components --check-openrgb`), em 127.0.0.1, e é encerrado ao sair. Regras udev/grupo i2c exigem root: comando exibido, nunca executado.
- Flatpak não é embutível (serviço do sistema): o app mostra o comando da distribuição.

## Correções de sensores com um clique (decisão do mantenedor, 2026-11-06)
- Os usuários não devem precisar digitar comandos. Para os sensores da CPU (módulo k10temp/coretemp e leitura do RAPL), o app oferece um botão
  que roda um script **fixo**, montado só com valores de lista permitida, via polkit (`pkexec`): o sistema pede a senha na janela dele e o
  Ilmenyx nunca a vê. Sempre por clique com confirmação, com Desfazer, e com o comando visível para técnicos. Isso substitui, só para esses
  casos, a regra "root = comando exibido". Nenhum outro script, texto livre ou valor vindo de relatórios/rede pode chegar ao `pkexec`.
<!-- REUSE-IgnoreEnd -->
