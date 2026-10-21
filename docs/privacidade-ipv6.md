# Minimização de IPv6 em texto livre — Ilmenyx 0.21.0

IMPLEMENTADO: LinuxParsers.Clean oculta candidatos IPv6 válidos com
`[IP redigido]` antes de aplicar os filtros existentes MAC/UUID/IPv4. A sequência
impede ocultar apenas parte de IPv6 mapeado de IPv4 ou confundi-lo com um MAC.
O texto continua limitado a 512 caracteres, sem controle, e números/unidades,
scores, algoritmos e schemas JSON não foram alterados.

Regex identifica candidatos inteiros; IPAddress.TryParse confirma somente a parte
IPv6 e sua família. Escopo `%eth0`/`%42` é removido do candidato antes do parse,
sem resolver nomes de interface, consultar DNS, enumerar rede ou abrir sockets.
O marcador substitui também o escopo. Prefixos CIDR e portas fora de colchetes
permanecem como delimitadores; pontuação final comum é preservada.

A mudança atua nas fontes que já passam por Clean: modelo de CPU, descrição do SO,
texto do kernel e rótulos dos leitores Linux. Não é filtro genérico de qualquer
DTO fornecido ao Core, nem inspeção de arquivos antigos ou do histórico. Core e
Contracts não ganham dependências de rede, Linux ou Avalonia.

## Testes e escopo

Build Release sem avisos/erros, 337 testes aprovados (36 novos casos de privacidade):
IPv6 completo/comprimido, maiúsculas, loopback/indefinido, IPv4 mapeado, escopo
alfanumérico/numérico, colchetes/porta/CIDR/pontuação; preservação de PCI, horários,
modelos, kernel e tokens inválidos. Idempotência, limite e filtros anteriores
passaram. Fixture proc/os-release confirma ausência dos identificadores em texto,
JSON e HTML gerados pelo provedor real. Fixtures testam sanitização, não são
medições ou prova de existência desse hardware.

Pacote local framework-dependent 0.21.0 gerado (base 65e22f9 e árvore dirty
registradas no manifesto, pois o incremento ainda não estava commitado); regressão do aceite passou nos modos
padrão e invariant explícito, com versão/runtime/códigos registrados, notices,
JSON/sensores/ldd/recusa de overwrite e negativos de protocolo/versão. Inventário
real permanece somente nos temporários locais. Nenhum benchmark completo, SMART,
root, instalação, mudança do sistema, push ou publicação.

[Evidências](evidencias/ilmenyx-ipv6): build/tests/TRX, harness, logs UI e pacote.
Resultado gráfico e revisão auxiliar são registrados na conclusão da sessão abaixo.
As bibliotecas gráficas são as do build do harness; não se confunde esta execução
com os smokes do pacote publicado realizados no incremento anterior.

## Reproduzir

SDK/cache já disponíveis nesta sessão; diretórios de saída devem ser novos.

```bash
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-ipv6-reviewed-wayland
ILMENYX_DISPLAY_BACKEND=x11 dotnet tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll /tmp/ilmenyx-ipv6-reviewed-x11
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-0.21.0-reviewed"
bash scripts/test-machine-acceptance.sh "$PWD/.artifacts/ilmenyx-0.21.0-reviewed/Ilmenyx-linux-x64" /tmp/ilmenyx-ipv6-reviewed-package-check
# Aplicativo:
ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project src/Ilmenyx.Desktop -c Release --no-build --no-restore
```

## Limites e pendências

Não é anonimização. Formatos arbitrários/ofuscados, endereços partidos pelo limite
de texto/controles, identificadores sem rótulo, caminhos com espaços e outros dados
livres podem escapar; strings sintaticamente IPv6 podem ser rótulos legítimos
(falso positivo). Revisar relatórios antes de compartilhar. A fixture não prova
remoção de IPv6 de hardware real: não houve endereço desse tipo constatado nos
rótulos do host, nem alteração de sysfs para criar um.

NÃO TESTADO: outra distro/VM limpa, outros dispositivos, matriz de ABI/ICU e
tecnologias assistivas. AMD/NVIDIA continuam com aviso de testes reais ausentes.
PENDENTE DE REVISÃO: usabilidade e checklist jurídico/marca/privacidade; direitos
não verificados continuam BLOQUEADOS para redistribuição. Licenças e WPF preservados.


## Conclusão gráfica

Wayland nativo com DISPLAY removido e Avalonia.Wayland.WindowImpl, seguido de X11
com Avalonia.X11.X11Window: fluxos completos passaram. Exportações reais JSON/HTML,
licenças, requisitos, controles de cancelamento, validações reduzidas e mínimos
com texto 100/150/200% passaram. Nenhum erro geométrico registrado. Não certifica
teclado físico, leitor de tela ou a matriz de outros compositores/dispositivos.


## Revisão auxiliar recebida

Com autorização do usuário, Claude Code instalado foi consultado em chamada
separada, somente com o texto do sanitizador e testes, sem ferramentas/edições,
sem retomar ou alterar o terminal interativo existente. Maestri não estava
acessível nesta sessão; a CLI `claude -p` forneceu o parecer.
[Revisão original](reviews/claude-ipv6-review.md) corresponde à versão ANTERIOR
às correções abaixo e contém sugestões não adotadas; não é teste nem aprovação
independente da versão final. [Tarefa delimitada](reviews/claude-ipv6-task.md).

Corrigidos e testados após o parecer: delimitador `:` depois do endereço/com
escopo; filtro home antes de IPv6 para não quebrar marcadores; chaves IPv4/IPv6
coladas; escopo com `+`; NAT64; preservação de horários/PCI adicionais. A limpeza
agora lê até 513 caracteres e, ao cortar, descarta conservadoramente o último
token com `:` antes de sanitizar, evitando publicar prefixo de endereço inválido
por truncamento. Isso pode descartar outro token legítimo com `:` na borda.
Expansão dos marcadores continua limitada a 512 após redação; testes cobrem os
dois casos. Mantida política conservadora de ocultar `::`/`::1` válidos.

Limites conhecidos ainda PENDENTES DE REVISÃO: rótulos arbitrários colados como
`src:fe80::1`, interface/alias `%eth0:1`, tokens ofuscados/embutidos e caminhos
com espaços. Não se coletam /proc/net/if_inet6 ou dumps hexadecimais de rede.
As sugestões do parecer sobre lookbehind variável/complexidade não foram
incorporadas como fatos ou provas; a implementação mantém entrada limitada.
Build e 337 testes, pacote e regressões gráficas foram repetidos após as correções.


Um build do harness simultâneo ao publish falhou por PDBs removidos pelo publish
DebugType=None. Log preservado em harness-build-concurrent-failure.txt. Não foi
falha do sanitizador: reexecução sequencial com --no-incremental passou. Não rodar
builds/publish concorrentes sobre os mesmos diretórios bin/obj.
