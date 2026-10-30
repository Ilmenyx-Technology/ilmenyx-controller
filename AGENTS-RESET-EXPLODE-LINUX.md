# AGENTS.md — diretrizes do fork de portabilidade Linux

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
