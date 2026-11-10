# Idiomas (pt-BR, inglês e espanhol)

## O que o usuário vê

- O botão de idioma (ícone de tradução) fica no topo, ao lado do tema. O menu lista **Português (Brasil)**, **English** e
  **Español**, cada um escrito no próprio idioma.
- A troca é gravada em `settings.json` (`Language`) e vale quando o app abre de novo. A confirmação aparece já no idioma
  escolhido, com **Reiniciar agora** ou **Depois**.
- Sem escolha gravada, o app segue o idioma do sistema (`LC_ALL`, `LC_MESSAGES`, `LANG`): `pt*` dá pt-BR, `es*` dá espanhol e
  qualquer outro dá inglês. `ILMENYX_LANG=en|es|pt-BR` força um idioma (útil para prints e testes).
- Números e datas seguem o idioma: pt-BR, en-US ou es-419 (espanhol latino-americano neutro).
- A CLI (`ilmenyx`) segue a mesma escolha: `ILMENYX_LANG`, depois o idioma salvo pelo app, depois o do sistema. Comandos e
  opções (`--output`, `zram status`) ficam iguais em todos os idiomas; só mudam as mensagens e os nomes em maiúsculas da
  ajuda (`ARQUIVO` vira `FILE`). Sem ICU (modo invariant), as mensagens mudam e números e datas ficam no formato invariant.

## Como funciona

- `src/Ilmenyx.Core/Localization.cs`: `L.T("texto")` devolve o texto no idioma em uso. O texto em pt-BR escrito no código é a
  chave (como no gettext). Em `L.T($"{n} jogos")` a chave é o modelo `"{0} jogos"` e os valores entram depois, então a tradução
  pode mudar a ordem deles (`"{0} games"`).
- `src/Ilmenyx.Core/Locales/en.json` e `es.json`: chave pt-BR → tradução. Texto que falta no catálogo aparece em pt-BR, nunca
  em branco.
- O idioma é escolhido uma vez no início (`L.Use`, em `Program.Main`). Tabelas montadas em campos estáticos ficam no idioma do
  início; por isso a troca pede para reiniciar.
- Tour e Central de ajuda têm conjuntos próprios: `HelpContent.PtBr.cs`, `HelpContent.En.cs` e `HelpContent.Es.cs`, com os
  mesmos ids e a mesma ordem (o teste `HelpContentTests` confere).
- Sem `L.Use`, o idioma é pt-BR. Os testes ficam em pt-BR e não chamam `L.Use`.

## O que não se traduz

Identificadores e textos técnicos ficam fora de `L.T`: variáveis de ambiente, chaves de arquivos `.desktop`, comandos de shell,
nomes de pacote, formatos de data passados com `InvariantCulture`, valores salvos (por exemplo o preset `"Médio"`, que vai para
as sessões de FPS; a tela mostra `L.T("Médio")`). Quando o código compara um texto que vem traduzido (por exemplo
`a.Kind == L.T("GPU dedicada")`), os dois lados usam a mesma chave.

## Ferramenta (`tools/Ilmenyx.I18n`)

Rode na raiz do repositório, com o SDK local:

```
dotnet run --project tools/Ilmenyx.I18n -c Release -- check            # textos sem tradução, sem uso ou com valores trocados
dotnet run --project tools/Ilmenyx.I18n -c Release -- pending en x.json # o que falta traduzir em inglês
dotnet run --project tools/Ilmenyx.I18n -c Release -- merge es y.json   # junta {"texto pt": "tradução"} ao catálogo
dotnet run --project tools/Ilmenyx.I18n -c Release -- prune             # tira do catálogo o que o código não usa mais
dotnet run --project tools/Ilmenyx.I18n -c Release -- wrap <arquivos>   # marca com L.T os textos novos e lista os que deixou de fora
```

`check` sai com código 1 se houver texto sem uso ou tradução com valores (`{0}`, `{1}`…) diferentes da chave. `wrap` usa a lista
de palavras em inglês do sistema (cracklib) para separar texto em português de texto técnico; o relatório `i18n-wrap-report.tsv`
lista o que ficou de fora (comparações, padrões de `switch`, `const`, atributos) para decisão à mão.

## Texto novo

1. Escreva o texto em pt-BR dentro de `L.T(...)` (ou rode `wrap` no arquivo).
2. Rode `pending` para os dois idiomas, traduza e rode `merge`.
3. `check` precisa sair limpo antes do PR.
