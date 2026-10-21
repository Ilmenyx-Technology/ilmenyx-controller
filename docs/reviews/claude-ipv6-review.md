# Revisão do sanitizador IPv6 (`ReportTextPrivacy`)

Analisei só o texto colado. Não tenho o código de `LinuxParsers.Clean`, então os pontos que dependem dele estão marcados como "a confirmar".

## Bloqueante

### 1. `:` logo após o endereço faz o IPv6 vazar, com ou sem escopo

Esse é o formato típico de mensagens de erro, como `connect to <addr>: ...` ou `<addr>%<if>: ...`.

- **Sem escopo:** a classe `[0-9A-Fa-f:.]+` é gulosa e consome o `:` final. O candidato `2001:db8::1:` é inválido e volta intacto.
  - `connect to 2001:db8::1: Connection refused` → vaza
  - `fe80::1:` → vaza
- **Com escopo:** a zona para em `eth0`, mas o lookahead proíbe `:`. O backtracking não acha alternativa e não há match.
  - `ping fe80::1%eth0: Network unreachable` → vaza
- Isso contraria o objetivo declarado ("comuns ... com escopo"). A regra de IPv4 que roda depois também não cobre esses casos.

**Correção pequena:**
- Em `RedactIpv6`, se a validação falhar e o candidato (já sem `.` finais) terminar em `:`, tente de novo sem um único `:` e preserve-o no sufixo. Isso também resolve `2001:db8:::` → `2001:db8::` + `:`. Não use `TrimEnd(':')`, porque quebraria `2001:db8::`.
- No lookahead depois da zona, aceite um `:` que não seja seguido por caractere de endereço, por exemplo:
  ```
  (?![\p{L}\p{N}_.%]|:[\p{L}\p{N}_:.%])
  ```
  Assim `fe80::1%eth0:1` (alias) continua sem match, como limite aceito.

## A confirmar (pode ser bloqueante, depende de `Clean`)

### 2. Limite de 512 caracteres e truncamento

- **Expansão:** `::` tem 2 caracteres e `[IP redigido]` tem 13. Se o limite for aplicado antes da redação, 512 caracteres de `:: :: ...` viram cerca de 2,3 mil. O teste com `'x'` não exercita isso.
- **Corte no meio do endereço:** se o limite cortar `2001:db8:1234:5678:9abc:...` em `2001:db8:1234:5678:9a`, o candidato é inválido e o prefixo /64, que é a parte mais identificadora, vaza.
- **Sugestão:** redigir e só então truncar; ou, ao truncar antes, descartar o último token se ele parecer candidato. Faltam testes com `"2001:db8::1 "` repetido até ultrapassar 512 e com endereço cortado na borda.

## Recomendado (não bloqueante, mas é escape real)

### 3. Rótulo colado com `:`

- O lookbehind proíbe `:` imediatamente antes do endereço: `IPv6:2001:db8::1`, `src:fe80::1`, `addr:2001:db8::1` → vazam. Só `ip:` é coberto por `Labeled`.
- Um formato como `inet6 addr: fe80::1/64` (com espaço) funciona.
- **Opção:** acrescentar ao lookbehind uma alternativa que aceite `palavra:` iniciada por letra (o .NET aceita lookbehind de tamanho variável):
  ```
  (?:(?<![\p{L}\p{N}_:.%])|(?<=(?:^|[^\p{L}\p{N}_])\p{L}[\p{L}\p{N}_-]*:))
  ```
- Verifiquei mentalmente os casos de teste existentes:
  - `dead:beef` → `beef` não tem `:` e fica intacto.
  - `Time:12:34:56` → inválido, intacto.
  - `namespace::Class` → sem candidato válido.
  - `dead:2001:db8::1` → já é válido como um todo.

### 4. Ordem `HomePath` depois do IPv6 corrompe a saída

- `/home/x/2001:db8::1` → IPv6 vira `/home/x/[IP redigido]` → `HomePath` consome até o espaço → `[caminho redigido] redigido]`.
- **Sugestão:** rodar `HomePath` antes de `Ipv6Candidate`. Isso não viola a exigência de IPv6 antes de MAC/IPv4.

## Falsos positivos

- **`::` isolado:** `Categoria :: Modelo` → `Categoria [IP redigido] Modelo`. O endereço `::` não tem valor de privacidade.
  - **Sugestão:** não redigir `::`, e talvez nem `::1`. O teste atual exige o contrário, então é uma decisão de vocês.
- **Palavras só com letras hex e `::`:** `cafe::babe`, `add::`, `fe::ed`. Aceitável.
- **Dumps de 8 bytes com `:`:** `00:11:22:33:44:55:66:77` (EUI-64, GUID) é IPv6 válido e é redigido. Como isso também é identificador, aceitável.

Conferi que não geram falso positivo: PCI `0000:00:02.0`/`0000:00:1f.3`, `12:34:56.789`, USB `8087:0024`, `[8086:1234]`, `2026-10-21T12:34:56` e MAC de 6 grupos (que segue para `Identifier`).

## Limites aceitáveis (documentar)

| Entrada | Resultado |
|---|---|
| `2001:db8::1suffix`, `addr_2001:db8::1`, `x2001:db8::1` | vaza (token embutido) |
| `endereço 2001:db8::1.Próximo` (ponto sem espaço) | vaza |
| `fe800000000000000000000000000001` (`/proc/net/if_inet6`, `ipv6_route`) | vaza; só é problema se esses arquivos forem coletados |
| `fe80::1%eth0:1` (alias como zona) | vaza |
| `fe80::1%` (`%` sem zona) | vaza |
| `fe80::1%wg+0` | redige `fe80::1%wg` e sobra `+0`; vaza só o resto do nome da interface |

Backtracking: como todos os caracteres da classe estão no lookbehind e no lookahead, só a primeira posição de cada token é tentada, então o custo fica linear. Com entrada limitada, não vejo risco de ReDoS.

## Testes: lacunas e melhorias pequenas

- **Adicionar:**
  - `"connect to 2001:db8::1: refused"`
  - `"fe80::1%eth0: down"`
  - `"IPv6:2001:db8::1"`
  - `"/home/x/2001:db8::1"`
  - `"64:ff9b::192.0.2.1"`
  - `"[fe80::1%eth0]:22"`
  - `"2001:db8::1,fe80::2"`
  - `"0000:00:1f.3"`
  - `"12:34:56.789"`
  - `"A :: B"`, conforme a decisão sobre `::`
- **`LegacyRedactions...`:** `DoesNotContain("192.0.2")` não prova que o IPv6 rodou primeiro, porque `::ffff:[IP redigido]` também passaria. Acrescente `Assert.DoesNotContain("ffff", clean)`.
- **Estilo:** no lambda de `Ipv4`, `System.Net.IPAddress` está totalmente qualificado embora já exista `using System.Net;`. Use só `IPAddress`, como em `RedactIpv6`.

## Resumo

- **Bloqueante:** item 1 (`:` depois do endereço, com e sem escopo).
- **A confirmar antes do merge:** item 2 (limite de 512 vs. expansão e truncamento).
- **Recomendados:** itens 3 e 4 e o ajuste no teste de ordem.
- **Escolha de política:** o falso positivo de `::`.
