# Contribuir com Ilmenyx

Port independente e não oficial de RESET EXPLODE, de Gustavo (GustavoReset), mantido sob a
identidade Bismuth Technology. Leia AGENTS.md, NOTICE.md e LICENSE. O upstream não recebe PRs
conforme README local auditado; colaboração futura aqui é para o port, não publicação em nome do RESET.
Repositório remoto da organização: [PENDENTE: decisão humana]. Nenhum envio remoto autorizado agora.

Não há CLA, DCO, cessão automática ou nova condição comercial imposta. A política própria permite uso instrumental em trabalho remunerado e restringe venda/acesso
pago do programa e derivação comercial de código; leia
LICENSING.md e confirme autoria/permissões/compatibilidade antes de contribuir. Partes MIT
e direitos anteriormente concedidos permanecem com seus termos. Não acrescente logo,
fonte, driver ou asset sem proveniência/licença própria. GitHub owner e marca não provam titularidade.

Crie alterações pequenas e explicáveis, preserve o submódulo fixado e a metodologia dos benchmarks.
Core/Contracts sem UI/Windows; adaptadores por SO; dados reais ou indisponíveis. Não executar stress,
sudo, drivers, escrita em disco bruto ou perfis full em testes. Não coletar/anexar identificadores.

Com SDK .NET 10 disponível, telemetria opt-out e dependências já instaladas:

```bash
dotnet restore Ilmenyx.slnx --disable-parallel -m:1
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
```

Em Bash, helper local opcional `. scripts/env-local-sdk.sh` reutiliza apenas o SDK temporário da
sessão anterior, se ainda existir. Não executar esse source no fish. Sem instalação automática.

Informe o comportamento, evidência e limites; registre NÃO TESTADO em Sem_teste.md quando faltar
ambiente. Leia PRIVACY.md antes de anexar relatórios. Vulnerabilidades: SECURITY.md. Commits locais
por incremento; push, tags, releases e publicação exigem decisão explícita do mantenedor.
