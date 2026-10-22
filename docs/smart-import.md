# Importação SMART offline — Ilmenyx 0.24.0

IMPLEMENTADO: CLI **smart-import**, adaptador de entrada limitada e relatório HTML
passivo. Converte JSON smartctl já obtido pelo usuário em JSON filtrado/HTML/texto.
Não abre arquivo/dispositivo de entrada, não consulta sysfs, não executa smartctl,
SMART, self-test, sudo ou outro processo. Recebe somente a entrada padrão; o shell
faz o redirecionamento explícito. A rota online `smart` continua exigindo disco e
--allow-device-query; não ganhou HTML nem foi executada nesta sessão.

## Contrato e proveniência

SmartImportReport em Contracts tem schema 1.0 e reportKind `smart-import`, separado
do SmartDiagnostic original. Registra ImportedAt (análise UTC), Diagnostic filtrado
e limitações. Diagnostic.CollectedAt nesse wrapper também é horário da análise;
**nenhum dos dois comprova a data original da coleta**. local_time bruto não é
usado. Não autentica entrada nem confirma saúde atual a partir de firmware passado.
Código CLI 0 significa importação processada, não disco saudável: diagnóstico pode
ser unavailable por erro/contradição, e reportedPassed=false continua relato.

O adaptador Linux reutiliza SmartctlDiagnostic.Parse e suas regras de bits/status.
Exige nome SATA/NVMe já aceito pelo port, device.name exatamente /dev/NOME e código
inteiro smartctl.exit_status 0..255. Rejeita JSON inválido, profundidade >32,
duplicatas em root/smartctl/device/smart_status e metadados incoerentes. Valida
nome declarado, sem verificar existência física: validação offline não certifica
que o relatório veio desse dispositivo ou não foi adulterado.

Entrada limitada a 65.536 caracteres; espera máxima de 3 s para ler/analisar.
Console.In pode bloquear leituras síncronas, por isso a espera é limitada também
com WaitAsync. Um leitor não cooperativo pode continuar até stdin/processo terminar;
não se promete interromper o kernel nem usar este helper como serviço duradouro.
Pré-cancelamento/cancelamento externo propagados; timeout CLI retorna 124 sem
saída parcial. Sem redirecionamento (PTY real) retorna 2. JSON/metadados/tamanho
inválidos retornam 1; argumentos/flags conflitantes retornam 2. Erros não imprimem
o JSON bruto. Saída só é criada após a análise, por ReportFile sem overwrite.

Serial, modelo, mensagens, JSON bruto e horário de origem não são copiados à saída.
Core recebe apenas o wrapper, sem dependência Linux/UI. SmartImportHtmlReport
escapa cada string como texto; CSP default-src 'none', sem script, imagens, links
ativos ou recursos remotos. Exibe fontes declaradas, unidades, estado/motivo,
reportedPassed disponível, análise UTC, limitações e créditos/aviso do produto.
Leitura unavailable não mostra Value antigo. Estilo simples provisório, sem marca
visual oficial. Arquivos/relatórios antigos e histórico não são alterados.

## Uso Linux (Bash e fish: redirecionamento por <)

```bash
. scripts/env-local-sdk.sh # somente em Bash, SDK/cache já preparados nesta sessão
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
# Use relatório existente e destino novo. Não executa consulta no disco:
dotnet run --project src/Ilmenyx.Cli -c Release --no-build --no-restore -- smart-import --device nvme0n1 --html --output /tmp/ilmenyx-smart-importado-novo.html < /tmp/silex-smart.hEos4e.json
# Saída estruturada filtrada:
dotnet run --project src/Ilmenyx.Cli -c Release --no-build --no-restore -- smart-import --device nvme0n1 --json < /tmp/silex-smart.hEos4e.json
```

No fish use SDK no PATH ou o executável do pacote; não dê source no helper Bash.
smart-import rejeita --allow-device-query, --input, flags de carga e --html/--json
juntos. Não aceita snapshot JSON do Ilmenyx como se fosse JSON bruto smartctl.
Nomes/aliases Windows, USB/SCSI/RAID e /dev/disk/by-id não são suportados pelo
confronto de nomes atual. Não há conversão SMART na Avalonia neste incremento.

## Testes executados e colaboração

Build Release sem avisos/erros; **457 testes aprovados**, zero falhas/ignorados.
65 casos novos cobrem metadados/nomes/duplicatas/profundidade/tamanho, estado do
firmware/contradições, dados privados excluídos, cancelamento, HTML escapado,
indisponibilidades, origem histórica, escrita atômica/overwrite e argumentos CLI.
Claude Code propôs testes de parser/privacidade em chamada separada sem ferramentas
ou edições; agente principal implementou, revisou a proposta e complementou testes
CLI. Ajustada uma asserção que rejeitava marcação estática válida. Não representa
aprovação humana nem revisão independente final do código.

Com o relatório NVMe histórico fornecido pelo usuário (arquivo existente, sem nova
consulta): importação real, JSON filtrado e HTML stdout igual ao arquivo passaram.
Mismatch de dispositivo, JSON inválido, excesso de tamanho, conflito de flags,
--allow-device-query e overwrite foram recusados sem stdout parcial; original
preservado. Processo real com stdin aberto sem dados retornou 124 em cerca de 3 s.
Teste em PTY sem redirecionamento retornou 2. São testes offline no Linux, não
consulta SMART nova ou certificação de saúde do NVMe.

Chromium via Playwright, documento real em 1280×900 e 390×844: página dentro da
largura, avisos históricos/sem consulta/sem garantia, créditos, análise UTC e CSP
presentes; nenhum elemento ativo. Log da página só registra GET loopback do HTML.
Servidor temporário só em 127.0.0.1 e aba do teste encerrados. Não certifica tráfego
de fundo do browser, impressão ou leitor de tela. HTML/JSON reais, raw e screenshot
permanecem em /tmp. [Evidências](evidencias/ilmenyx-smart-import) não incluem o raw.

Pacote local framework-dependent 0.24.0: ver conclusão abaixo. SHA base e árvore
dirty registrados no manifesto porque o incremento ainda não estava commitado.
Não houve nova execução dos smokes Avalonia: mudou Core/CLI e apenas a versão na
UI, cujos controles seguem os de 0.23.0. Sem publicação, push ou release remoto.

## Pendências

NÃO TESTADO: outras distros/VM limpa, ARM64, entrada com BOM/encodings alternativos,
SATA real/USB/RAID, consultas ao vivo com sucesso sem elevação, impressão/Orca e
mudanças manuais de saúde. Arquivo histórico não fecha SMART-01. Nenhuma permissão
foi alterada para contornar falta de acesso. AMD/NVIDIA continuam com aviso real
na interface. Não certifica anonimização: revise a saída antes de compartilhar.

PENDENTE DE REVISÃO: interação humana, integração futura offline à Avalonia e
checklist jurídico/marca/redistribuição. Direitos desconhecidos continuam BLOQUEADOS
para redistribuição. WPF, MIT, créditos, histórico e SHA upstream preservados.
