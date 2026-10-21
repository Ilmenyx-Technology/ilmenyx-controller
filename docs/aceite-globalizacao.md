# Aceite CLI: modo de globalização e coerência de versão

IMPLEMENTADO nas ferramentas; aplicativo continua 0.20.0, sem mudança em C#,
workloads ou interface. Base local fb0de61, branch feat/ilmenyx-linux. Inspecionados
README, LICENSE, instruções, projetos, apresentação do doctor e scripts de aceite.

O result.json do roteiro passa de schema 1.0 para **1.1**, com campos adicionais:

| Campo | Origem / limite |
|---|---|
| globalizationMode | `default`, `invariant` ou `unknown`, conforme linhas exatas do doctor |
| defaultGlobalizationRequested | verdadeiro somente quando o doctor informou modo padrão; não certifica culturas/ABI |
| bundleVersion | versão dos metadados do pacote, após integridade aprovada |
| observedRuntime | string runtime do inventário real após smoke aprovado; `unknown` se não concluído |
| doctorExitCode / smokeExitCode | códigos efetivos; null quando não executado |

Não deduz o modo a partir apenas do ambiente do shell: runtimeconfig também pode
solicitar invariant. O doctor da própria aplicação informa o modo. Se o formato
não for reconhecido, ou informar ambos os modos, o roteiro retorna 3 e não inicia
inventário. Alterações futuras no texto do doctor exigem atualizar este protocolo.
A consulta continua textual, sem acrescentar exportação ao comando doctor.

Invariant explícito pode passar no smoke e retornar 0, mas registra modo próprio,
flag padrão falsa e aviso textual. Não é aprovação do modo padrão com ICU. GUI,
VM limpa e certificação de hardware continuam com flags falsas em ambos os modos.
O script não liga/desliga invariant no uso normal; só a regressão alterna a variável
em subprocessos explicitamente criados para esses casos de teste.

O smoke do pacote exige applicationVersion do inventário igual à version dos
metadados, além de runtime não vazio. Divergência impede aprovação, mesmo se os
hashes internos forem coerentes. Hashes não provam autenticidade ou licença.

## Testes executados

CachyOS x64, pacote real framework-dependent 0.20.0, runtime .NET 10.0.0 disponível:

| Caso | Resultado |
|---|---|
| Modo padrão real, variável explicitamente 0 só no teste | 0, default, flag padrão verdadeira, CLI/JSON/sensores/ldd/notices/overwrite passaram |
| Invariant real, variável explicitamente 1 só no teste | 0, invariant, flag padrão falsa, aviso e mesmo smoke passaram |
| Doctor sem linha de modo (fixture textual derivada da saída real) | 3, unknown, sem diretório de inventário |
| Doctor com linhas dos dois modos (fixture textual) | 3, unknown, sem inventário |
| Metadados alterados em cópia, hashes recalculados; executável real preservado | 1, smoke falha pela divergência de versão após info real, antes de sensores |
| Apphost real com DOTNET_ROOT/DOTNET_ROOT_X64 em diretório vazio | 3, doctor não inicia, smoke null, modo unknown |
| Bash -n nos três scripts e git diff --check | Passaram |

Fixtures são somente de protocolo/metadados, não inventam hardware. Os positivos
e o caso de versão usam a aplicação real; JSON de inventário/sensores fica em /tmp,
sem commit. Não houve stress, SMART, instalação, root, rede ou alteração do sistema.
Nenhum arquivo original do bundle foi modificado; cópias negativas são temporárias.
[Evidências](evidencias/ilmenyx-aceite-globalizacao): resultados estruturados,
saídas doctor reais e log da regressão. Scripts não mudam o esquema hardware 1.2.

## Reproduzir

Requer ambiente padrão com ICU e runtime já disponíveis; não instala dependências.
A regressão usa Bash, ferramentas do roteiro, find/xargs e GNU cp com reflink=auto,
com destino absoluto novo. Copia o pacote para casos negativos e preserva saídas.
É teste CLI, sem GUI, benchmarks ou SMART.

```bash
. scripts/env-local-sdk.sh # somente para reutilizar SDK/runtime isolado desta sessão
bash scripts/test-machine-acceptance.sh "$PWD/.artifacts/ilmenyx-0.20.0-local/Ilmenyx-linux-x64" /tmp/ilmenyx-acceptance-regression-final
```

O helper não é necessário com runtime normalmente instalado. Em uso normal:

```bash
bash scripts/validate-linux-machine.sh /caminho/Ilmenyx-linux-x64 /caminho/resultado-novo
```

## Pendências

NÃO TESTADO: VM limpa e outra distro com ICU em modo padrão, matriz de versões/ABI,
ARM64, Orca e teclado físico. Caso negativo Debian sem ICU anterior permanece
válido para o aplicativo inalterado; este novo roteiro não foi executado lá nesta sessão. Não o contar como nova aprovação.
301 testes C# e smokes UI anteriores são históricos deste mesmo produto, não
foram repetidos nesta sessão, pois apenas scripts/documentação foram alterados.

PENDENTE DE REVISÃO: aceite humano e checklist jurídico/marca. Material sem direitos
conhecidos continua BLOQUEADO para redistribuição. Licenças, créditos, WPF e SHA do
upstream preservados. Sem push/publicação. PKG-01 continua parcial.
