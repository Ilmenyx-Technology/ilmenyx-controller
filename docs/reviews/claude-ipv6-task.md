# Revisão auxiliar — sanitizador IPv6

Usuário autorizou Claude Code como auxiliar. Tarefa inicial SOMENTE LEITURA,
sem editar arquivos ou fazer commits, para evitar concorrência no workspace.

Ler AGENTS.md, docs/privacidade-ipv6.md, src/Ilmenyx.Platform.Linux/ReportTextPrivacy.cs,
src/Ilmenyx.Platform.Linux/LinuxParsers.cs e tests/Ilmenyx.Tests/Ipv6PrivacyTests.cs.
Revisar limites, IPv6 mapeado/escopo/delimitadores e falsos positivos em PCI/modelos.
Distinguir fixture, regressão real e anonimização não garantida. Informar somente
achados concretos com entrada/reprodução e gravidade. Não executar stress, root,
SMART, mudanças de rede/sistema, publicações ou outro benchmark completo.

Retornar achados ao responsável por esta sessão antes de qualquer edição. Uma
revisão não recebida não conta como aprovação. Se for solicitada implementação
posterior, combinar arquivos exclusivos e aguardar os builds correntes terminarem.


## Execução desta sessão

Parecer textual recebido via CLI isolada (sem ferramentas/edições), com código e
testes fornecidos em stdin; a sessão interativa existente não foi retomada.
Resultado em claude-ipv6-review.md; tratamento dos achados e testes finais em
../privacidade-ipv6.md. Não houve segunda revisão do código final pelo auxiliar.
