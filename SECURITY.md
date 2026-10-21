# Segurança — Ilmenyx

Canal privado de vulnerabilidades: [PENDENTE: mantenedor definir e testar antes de publicação].
Não existe aqui e-mail, formulário ou domínio de suporte confirmado. Não publique exploits,
credenciais, relatórios identificáveis, dumps ou raw SMART em issues públicas. Enquanto não houver
canal privado, mantenha detalhes localmente e solicite ao mantenedor um meio privado verificado.
Uma issue pública, se habilitada futuramente, deve limitar-se a pedir canal seguro, sem detalhes sensíveis.

Versão local experimental; política de versões suportadas e prazo de resposta: PENDENTE DE REVISÃO.
Não há promessa de SLA. Documente versão/commit e passos mínimos sem dados pessoais quando um
canal apropriado existir. Não ataque terceiros nem execute stress para preparar relato.

O app funciona sem root, com leitura limitada de proc/sys, validação de entradas, cancelamento e
timeouts cooperativos. SMART é leitura explícita por ArgumentList sem shell/elevação. Exportação
local não sobrescreve destino e HTML escapa texto sem recursos remotos. Não há atualização automática,
telemetria de produto ou upload implementados. Sem proteção térmica ou garantia de saúde.

Build/testes com SDK e ferramentas do sistema são operações separadas: respeitar telemetria opt-out,
não executar workloads full automaticamente. Revisão de dependências não cobre todo binário/runtime.
Limites e pendências: Sem_teste.md, docs/security/PRIVACY-SECURITY-AUDIT.md e PRIVACY.md.
