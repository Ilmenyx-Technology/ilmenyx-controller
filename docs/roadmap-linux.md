# Plano de migração

0. Auditoria: registrar base, APIs, riscos e limites da inspeção (este documento e inventário).
1. Vertical slice: Contracts/Core/Linux/CLI/Desktop, fixtures, JSON local; aceite build e testes,
CLI factual e UI compilada; validação visual condicionada a sessão acessível.
2. Benchmark CPU: primeiro resolver política clean-room versus extração MIT; preservar seed,
cena, resolução, SPP, pontuação e versões; testes pequenos de hash/cancelamento, sem carga automática.
3. hwmon/thermal/power_supply e DRM/PCI; ausência explícita; fixtures/hotplug e hardware autorizado.
4. RAM/disco/rede opt-in; I/O temporário limitado, espaço e finally; comparar cache/sem cache;
relatório HTML seguro. Não reutilizar scores após alterações de workload.
5. Vulkan raster/RT e overlay: prova de equivalência e matriz de hardware antes de afirmar suporte.
6. Jogos/VDF/RGB; serviços sensíveis com autorização granular, reversão e diagnóstico primeiro.
7. QA distros/GPU/X11/Wayland, CI sem stress, empacotamento e licenças; sem publicação automática.

Fase 2 CPU implementada nesta continuação com validação reduzida; carga completa NÃO EXECUTADA.
Simulação/RAM apenas reanalisadas. Fase 3 iniciada somente por hwmon; restante das Fases 3–7 pendente. Riscos: acesso a sensores, equivalência numérica/JIT, licenças binárias,
Wayland, distribuição Arch rolling e falta de Windows para validação WPF.


Entregue na continuação 0.2.1: corpus de 16 pixels em resolução/SPP originais e fluxo de validação
reduzida na UI com prévia, estados, cancelamento e exportação. Não há score full validado.
Continuação 0.3.0: exportação/cancelamento gráficos validados por harness Avalonia no processo;
hwmon somente leitura com CLI, UI, JSON, fixtures e polling opt-in. Sem GPU/SMART ou controle térmico.
Próximo incremento: thermal/power_supply somente leitura com fixtures e fontes explícitas;
validar mapeamento sem duplicar sensores. Windows, outras ISAs/distros e Wayland nativo pendentes.


Continuação 0.4.0: thermal/power_supply e identificação DRM/PCI implementados com fixtures,
CLI/UI/JSON; SMART somente leitura por opt-in na CLI, sem privilégio, com falha real de permissão
validada. Fase 3 funcional parcial: faltam validação SMART bem-sucedida, AMD/NVIDIA reais e
cobertura de drivers adicionais. Calibração e proteção térmica não devem ser presumidas.
Próxima tarefa de implementação: iniciar Fase 4 pelo relatório HTML offline com escaping e
proveniência, sem começar benchmarks de RAM/disco/rede automaticamente. Esses workloads ainda
exigem revisão própria da metodologia e limites. QA Windows/outras distros segue dependendo
de ambientes disponíveis; não substituir testes reais por alegação de compatibilidade.

Entregue 0.5.0: relatório SMART real do usuário aceito pelo parser C#; HTML offline implementado
e validado em CLI, Avalonia e Chromium Linux (desktop/viewport estreito). Exceção AMD/NVIDIA
com aviso na interface/relatório, conforme autorização. Fase 4 limitada aos relatórios;
próxima tarefa: revisar MemoryBench/GameSim e definir um incremento controlado, sem stress
ou execução automática de RAM/disco/rede. HTML de resultados CPU/SMART separados ainda não implementado.

Entregue 0.6.0: validação de algoritmos RAM em buffers pequenos, Core/CLI/Avalonia/JSON, referência
independente upstream e aceite CachyOS. Não é benchmark de DRAM e não há métricas/score de RAM.
GameSim e integração WPF foram reanalisados; próximo recorte: simulação reduzida por passos fixos,
oracle e snapshot coerente, antes de FPS/score/duração. Perfis completos/disco/rede seguem pendentes.


## Estado 0.7.0 — GameSim reduzido

Substitui o estado anterior de GameSim não implementado: Core/CLI/Avalonia/JSON com 256 entidades,
16 passos fixos, sem FPS/score/warmup por relógio. Referência independente upstream em 1/2/4
workers, 232 testes e fluxo gráfico Linux passaram. Aceite técnico restrito ao perfil reduzido;
não implica aprovação humana adicional ou certificação de hardware. AMD/NVIDIA seguem com aviso
visível de testes reais não realizados, conforme exceção autorizada. GameSim completo, RAM de
carga, disco/rede, Windows e Wayland nativo continuam pendentes/NÃO TESTADOS. Próxima tarefa:
especificar I/O temporário seguro antes de implementação/carga. Sem executar fases seguintes
automaticamente. Ver [metodologia e evidências](gamesim-metodologia.md).


## Atualização 0.8.0 — Wayland e registro de testes

Wayland nativo agora validado no CachyOS/KWin: Avalonia 12.1.4 opt-in, DISPLAY removido e
implementação real Avalonia.Wayland.WindowImpl, sem fallback X11. Regressão X11/XWayland também
passou; 250 testes, build sem avisos/erros. Isso substitui o estado anterior de nativo não
implementado. Outras distros/compositores, Windows, AMD/NVIDIA reais e perfis completos seguem
no registro contínuo Sem_teste.md, conforme nova orientação do usuário. Preparação de GameSim
completo disponível em bench sim-plan, sem carga/medição; não equivale a runner/FPS/score testado.
[Comandos, decisões e evidências](wayland-validacao.md). Commits locais, sem publicação automática.


## Continuação 0.9.0 — runner GameSim experimental

Runner full CLI implementado com opt-in e limites, reutilizando engine matemático parametrizado;
validação padrão/UI continua 256 entidades/16 passos. Build sem erros/avisos e 261 testes passaram,
com relógio/engine controlados para controle/estatística. Carga real de 30.000 entidades NÃO
EXECUTADA; Sem_teste.md SIM-01 permanece aberto. Não declarar aceite pleno de FPS/score nem
comparabilidade Windows. [Metodologia, comandos e evidências](gamesim-completo.md).


## Identidade Ilmenyx 0.10.0

Renomeação controlada de projetos novos/CLI/UI/metadados, preservando formatos e origem MIT.
Diretrizes permanentes acrescentadas ao AGENTS sem apagar anteriores. 272 testes e fluxos Linux
CLI/Wayland/X11 passaram; Sobre/avisos de compartilhamento implementados. Não é liberação legal,
privacidade garantida ou empacotamento final. Marca/manual/organização/titularidade/licenças nativas
permanecem em revisão humana; veja branding/RENAME-DECISIONS.md, ../Sem_teste.md e
legal/PRE-RELEASE-CHECKLIST.md. Exemplos Silex de incrementos anteriores são históricos.


## Ilmenyx 0.11.0

Workspace movido para /mnt/HDD/Github/Ilmenyx, branch local feat/ilmenyx-linux e descrição Git
Ilmenyx, sem alterar remotos/histórico/submódulo. Solução/CLI continuam Ilmenyx.slnx / ilmenyx.
ValidationHtmlReport no Core exporta três reports reduzidos existentes, sem nova coleta/carga;
CLI --html disponível para CPU validation, memória e GameSim. Mesma escrita atômica/escaping,
sem alterar JSON ou metodologia. Build sem erros/avisos, 276 testes e CLI/UI de regressão passaram.
[Detalhes e comandos](validation-html.md). UI HTML de resultados e browser específico pendentes.

Termos próprios corrigidos conforme esclarecimento: usar programa como ferramenta em serviço
remunerado é permitido; vender programa/acesso ou derivar produto/serviço pago do código próprio
não é autorizado. LICENSE MIT e direitos anteriores/terceiros continuam preservados; política
não é open source OSI e exige revisão jurídica antes de publicação (../LICENSING.md).
