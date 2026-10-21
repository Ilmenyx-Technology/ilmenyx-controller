# Pendências após o incremento 0.4.0

| Item solicitado | Resultado verificável | Condição para concluir |
|---|---|---|
| thermal e power_supply | implementados: fixtures, dados reais CachyOS, CLI/UI/JSON | ampliar matriz de máquinas/drivers |
| GPU | DRM/PCI real Intel; AMD carga/VRAM e temperatura com fixtures | máquinas AMD/NVIDIA para validação real; adaptadores de telemetria Intel/NVIDIA ainda não implementados |
| SMART | consulta real NVMe do usuário validada pelo parser: exit 0, passed=true; app sem acesso automático | validar outros transportes/hardwares; não há elevação automática |
| Calibração | valores brutos e limites documentados; não se aplica correção adivinhada | especificações da placa/driver e comparação com referência; não prometer calibração universal |
| Proteção térmica | NÃO IMPLEMENTADA; leituras de sensores não são mecanismo de proteção | política de fontes/limites/falhas e testes específicos, antes de usar em carga sustentada |
| CPU Full/score | NÃO EXECUTADO; somente testes numéricos limitados | sessão específica autorizada para carga e metodologia controlada; restrição atual sem stress preservada |
| Windows WPF/backend futuro | submódulo original intacto; build/execução Windows NÃO TESTADOS | ambiente Windows e implementação/validação do adaptador; não atualizar SHA/TFM original automaticamente |
| Outras distros/ISAs/Wayland nativo | NÃO TESTADOS | ambientes e hardware reais; compilação isolada não equivale a suporte |

Estas pendências não impedem diagnóstico factual no host validado. Ausência de dados não é saúde,
zero, equivalência de score ou compatibilidade confirmada. Não houve instalação, mudança de permissões,
configuração, execução privilegiada, stress, self-test de disco, push, PR ou release.

Próximo incremento de software: relatório HTML offline com conteúdo escapado, schema/proveniência e
limitações, reutilizando snapshots existentes. Depois: revisão separada de RAM/disco/rede e suas
metodologias, sem carga/endpoint automático. Essa implementação de relatórios ainda não está pronta.

## Condição de avanço atualizada

O usuário condicionou a próxima tarefa ao funcionamento Linux testado e aprovado. A entrega
0.4.0 permanece com aceite parcial: [verificações e bloqueios](aceite-linux.md). O relatório
HTML não foi iniciado. Não considerar fixtures ou resposta indisponível como prova de um
caminho real de sucesso SMART/AMD/NVIDIA.

Exceção posterior do usuário: AMD/NVIDIA podem permanecer sem testes reais, com aviso explícito
na interface. Não bloqueiam mais o avanço por falta de hardware. SMART ainda aguarda a resposta
real da consulta; o comando fornecido em Bash foi corrigido para fish.


## Estado após 0.5.0

O relatório SMART real foi disponibilizado e validado pelo parser C# de produção. A exceção
AMD/NVIDIA continua com aviso visível. Esses bloqueios específicos de avanço foram resolvidos;
a execução privilegiada do Silex não foi testada nem habilitada. Relatório HTML offline foi
implementado e validado em CLI, UI Avalonia e Chromium Linux, sem benchmarks adicionais.
Próximo incremento: revisão da metodologia de RAM/GameSim e recorte limitado para testes,
antes de implementar ou executar benchmarks de carga. Não há autorização automática de stress.

## Estado após 0.6.0

Revisão RAM/GameSim concluída e validação reduzida de cadeia/soma/cópia implementada e testada
em Core/CLI/UI, com oracle independente. Não mede latência, GB/s, estabilidade ou saúde. Benchmark
completo de RAM, score ligado ao carregamento, GameSim e disco/rede seguem pendentes. Próximo
incremento recomendado é simulação por passos fixos com oracle, sem temporização/FPS/carga sustentada.


## Estado 0.7.0 — GameSim reduzido

Substitui o estado anterior de GameSim não implementado: Core/CLI/Avalonia/JSON com 256 entidades,
16 passos fixos, sem FPS/score/warmup por relógio. Referência independente upstream em 1/2/4
workers, 232 testes e fluxo gráfico Linux passaram. Aceite técnico restrito ao perfil reduzido;
não implica aprovação humana adicional ou certificação de hardware. AMD/NVIDIA seguem com aviso
visível de testes reais não realizados, conforme exceção autorizada. GameSim completo, RAM de
carga, disco/rede, Windows e Wayland nativo continuam pendentes/NÃO TESTADOS. Próxima tarefa:
especificar I/O temporário seguro antes de implementação/carga. Sem executar fases seguintes
automaticamente. Ver [metodologia e evidências](gamesim-metodologia.md).
