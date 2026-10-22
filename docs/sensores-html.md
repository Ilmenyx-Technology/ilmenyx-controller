# Relatório HTML de sensores — Ilmenyx 0.22.0

IMPLEMENTADO no Core e na CLI: `sensors --html [--output ARQUIVO]`.
SensorHtmlReport recebe apenas SensorSnapshot existente e gera documento offline;
não conhece leitores Linux, monitoramento, processos, rede ou benchmarks. A CLI
coleta uma vez e usa o mesmo snapshot para stdout e arquivo. Exportação pura não
repete coleta, ativa monitor ou consulta SMART. JSON/texto anteriores preservados.
O schema JSON de sensores continua 1.1; formato HTML novo 1.0.

O documento preserva schema, estado/motivo agregados, UTC da coleta e de cada
medição, dispositivo/chip/canal/rótulo, valor/unidade, qualidade, fonte, estado e
motivo. Bateria/alimentação mantém Type/Status/Present e sua proveniência. Valores
indisponíveis não mostram números antigos; zero disponível é preservado. Usa
CultureInfo.InvariantCulture para números e datas. Não deduz saúde, calibração,
proteção térmica ou capacidade do equipamento a partir da presença de canais.

Todos os campos variáveis entram como texto HtmlEncode. Sem script, links ativos,
imagens, fonte remota, iframe ou dados em atributos. CSP default-src 'none', estilos
inline locais. Tabelas com rolagem própria em telas estreitas; estilo simples e
provisório, sem identidade visual oficial. Créditos integrais e aviso de revisão
antes do compartilhamento vêm de ProductIdentity. Não garante anonimização.

Escrita reutiliza ReportFile: arquivo exclusivo temporário junto ao destino,
renomeação sem sobrescrita, cancelamento e limpeza em finally. stdout pode ser
usado sem --output. --html/--json são mutuamente exclusivos. Nenhuma nova opção
foi acrescentada ao doctor, SMART ou perfis completos.

## Colaboração e testes

Tarefas divididas com Claude Code por autorização do usuário: auxiliar propôs o
arquivo de testes a partir do contrato planejado; agente principal implementou
Core/CLI, revisou/ajustou a proposta e executou a validação. Chamada CLI isolada,
sem ferramentas ou edição concorrente do workspace. Não se atribui ao auxiliar
execução de testes, revisão humana ou aprovação independente da implementação.

Build Release: zero avisos/erros. **371 testes aprovados**, zero falhas/ignorados,
incluindo 34 casos novos: campos escapados, conteúdo/proveniência/atribuição,
números invariantes/zero/negativos, valores obsoletos ocultos quando indisponíveis,
ausência de classificação de saúde, pré-cancelamento, arquivo igual ao render,
recusa de overwrite, limpeza de temporários e parse CLI. Teste legado que proibia
sensors --html foi atualizado ao novo contrato, preservando recusa de conflito.
Fixtures são testes de código, não leituras de hardware real.

No CachyOS x64/runtime 10.0.0: coleta real de sensores, HTML em stdout idêntico ao
arquivo, JSON anterior válido, flags conflitantes=2, overwrite=1 e destino original
preservado. Pacote local framework-dependent 0.22.0: aceite padrão/invariant,
notices/ldd/JSON/sensores e negativos de protocolo/versão passaram. sensors --html
do pacote publicado também produziu stdout/arquivo idênticos. Artefato somente
local, base Git/árvore dirty registrados no manifesto, sem publicação remota.

Navegador Chromium real via Playwright: 1280×900 e 390×844, largura da página dentro
do viewport, duas tabelas com rolagem própria, créditos e aviso presentes; nenhum
elemento ativo encontrado. Log da página registra somente GET do documento em
127.0.0.1. Para testar, servidor HTTP temporário ligado apenas em loopback, pois
o conector bloqueia file://. Porta/aba exclusivas do teste encerradas depois.
Isso não certifica tráfego de fundo do navegador, leitores de tela ou impressão.
HTML real/JSON e screenshot com leituras permanecem em /tmp, fora do Git.
[Evidências](evidencias/ilmenyx-sensors-html): build/testes/TRX, resultados CLI,
DOM sem leituras, log da página e pacote. Browser usa o HTML da CLI compilada;
o comando do pacote foi validado separadamente por igualdade de arquivo/stdout.

## Compilar e executar

```bash
. scripts/env-local-sdk.sh # apenas para o SDK/cache já preparados nesta sessão
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet run --project src/Ilmenyx.Cli -c Release --no-build --no-restore -- sensors --html --output /tmp/ilmenyx-sensores-novo.html
```

Em checkout novo, seguir restore explícito do README. Destino deve ser novo.
Sem --html, --output continua exportando JSON, conforme comportamento anterior.
Pacote e regressão desta sessão:

```bash
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-0.22.0-local"
bash scripts/test-machine-acceptance.sh "$PWD/.artifacts/ilmenyx-0.22.0-local/Ilmenyx-linux-x64" /tmp/ilmenyx-sensors-html-package-check
```

## Próximo incremento e pendências

PENDENTE DE IMPLEMENTAÇÃO: botão HTML de sensores na Avalonia, exportando snapshot
selecionado sem repetir a coleta, com teste de monitor ativo/troca de snapshot,
cancelamento, erro/overwrite e foco. A UI atual continua exportando sensores em
JSON; este incremento não entrega botão novo nem certifica esse fluxo.

NÃO TESTADO: impressão, Orca/AT-SPI, navegação física, outros navegadores, outras
distros/VM limpa, hardware com bateria e GPUs AMD/NVIDIA. Mantido aviso de testes
reais AMD/NVIDIA ausentes. A suite Avalonia não foi repetida nesta sessão: mudança
funcional é Core/CLI, além da identificação da versão, sem alteração de controles.
PENDENTE DE REVISÃO: usabilidade e checklist jurídico/marca/redistribuição.
Direitos desconhecidos continuam BLOQUEADOS para redistribuição. WPF, MIT, créditos,
histórico e SHA do upstream preservados. Sem root, stress, driver, push ou release.


## Atualização 0.23.0 — HTML de sensores na interface

Substitui a pendência anterior do botão HTML: IMPLEMENTADO e testado em Wayland
nativo/X11. Comando compartilhado JSON/HTML impede reentrada, recusa seleção que
mudou durante o diálogo e suprime escrita/notificações após descarte. Exporta o
snapshot selecionado sem coletar novamente; monitor permanece independente.
Build sem avisos/erros, 392 testes aprovados; monitor real, exportação/cancelamento,
extensão/overwrite, teclado roteado e mínimos até 200% passaram. Interação física,
Orca, VM limpa e revisão humana seguem PENDENTES/NÃO TESTADOS.
[Colaboração Claude, comandos e evidências](sensores-html-ui.md).
