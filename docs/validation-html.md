# HTML de validações reduzidas — Ilmenyx 0.11.0

IMPLEMENTADO na CLI e Core: CPU validation, memória reduzida e GameSim reduzido podem exportar
um HTML offline com hardware/contexto e resultado já calculado. A renderização não coleta hardware
nem executa o algoritmo: recebe um report tipado. A CLI primeiro executa a validação solicitada,
depois exporta o mesmo snapshot para arquivo/stdout. Não há novo botão HTML nos painéis da UI.

```bash
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- bench cpu --html --output /tmp/ilmenyx-cpu.html
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- bench memory --html --output /tmp/ilmenyx-memory.html
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- bench sim --html --output /tmp/ilmenyx-sim.html
```

Destino deve ser novo. HTML e JSON são mutuamente exclusivos. Full CPU/GameSim não aceitam HTML;
argumentos são recusados antes da coleta/carga. Nenhuma execução sustentada foi realizada.

Reutiliza HardwareHtmlReport (CSP, CSS provisório, escaping, atribuição e aviso de compartilhamento)
e acrescenta seção validation-result com schema, estado, algoritmo/workload/versão, SHA upstream,
workers, parâmetros/contagens e hashes. Memória não declara latência/GB/s/saúde; GameSim não declara
FPS; CPU reduzida não tem score. Interrupções preservam status e indisponibilidades, sem inventar
métrica. Arrays internos não são expostos. JSON/schema/golden permanecem inalterados.

Writer atômico comum sem overwrite, cancelamento via API e limpeza do seu temporário em finally.
A CLI pode exportar explicitamente um resultado interrompido sem reaplicar o token já cancelado,
como no JSON anterior. Valor dinâmico só entra como texto escapado, sem URLs/HTML arbitrários,
recursos remotos, scripts ou imagem inventada. Fontes/rótulos continuam sujeitos à revisão de privacidade.

Testes no CachyOS x64/.NET SDK 10.0.100: build sem erros/avisos, 276 testes aprovados. Casos novos:
três tipos reais reduzidos; hashes/proveniência; payload de script escapado; ausência de recursos
ativos; resultado cancelado/hash indisponível; token de renderização; arquivo igual ao Render;
overwrite recusado e limpeza; argumentos full/formato conflitante rejeitados. CLI executada nos três
modos, stdout byte a byte igual ao arquivo e hash de RAM/GameSim esperado. Parser HTML confirmou
uma seção de resultado e nenhum script/img/iframe. Browser/layout específico desta nova seção:
NÃO TESTADO; estilos de hardware foram mantidos, sem alegar aceite visual novo de browser.

Regressão Avalonia real em Wayland nativo e X11/XWayland passou (fluxos anteriores), incluindo a
aba de termos próprios e a MIT separada. Sem automatizar teclado/mouse global. Termos refletem
uso instrumental profissional permitido, venda/acesso pago/derivação comercial de código restritos,
sem retirar direitos MIT/terceiros. Revisão jurídica/ownership pendente antes de publicar.

Workspace agora /mnt/HDD/Github/Ilmenyx; descrição Git Ilmenyx, branch feat/ilmenyx-linux. Solução
Ilmenyx.slnx e CLI ilmenyx já existiam. Nenhum remoto principal; não há nome GitHub remoto alterado.
Pasta movida sem mesclar/apagar conteúdo, histórico e submódulo intactos. Reabrir o workspace no
novo caminho; não foi criado alias/symlink com o nome antigo. Evidências antigas não reescritas.

Evidências desta etapa: docs/evidencias/ilmenyx-html (build/tests/TRX/CLI/UI e capturas). Sem stress,
sudo, drivers, configuração do sistema, push/PR/release. Próximo incremento possível: oferecer
exportação HTML desses snapshots na UI com os mesmos limites; não iniciar perfis full.


## Atualização 0.12.0 — HTML dos resultados na UI

IMPLEMENTADO e testado: exportação dos snapshots CPU/RAM/GameSim pela interface,
sem nova coleta/carga, com cancelamento e recusa de sobrescrita. 279 testes passaram;
Wayland nativo e X11 passaram no CachyOS. Browser Chromium em 390/1280 px passou
para os três relatórios. Isto encerra as pendências locais de botões HTML e layout
nessas condições, sem generalizar para outras plataformas ou acessibilidade.
Detalhes e comandos em [validation-html-ui.md](validation-html-ui.md). Registros anteriores são históricos.
