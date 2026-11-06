# Ilmenyx — direção de interface "cinematic ambient dark" (aprovada em 2026-11-06)

Decisão do mantenedor: atmosfera da referência (interface escura, iluminada por luz indireta quente e fria, superfícies translúcidas,
bordas mínimas, tipografia compacta, composição assimétrica) **com o acento azul-titânio da marca** (não o laranja da referência).
Complementa o [MANUAL-ILMENYX.md](MANUAL-ILMENYX.md); símbolo, nome e acento não mudam.

## Princípios
- A maior parte da tela é escura e discreta; cor só nos destaques (estado ativo, ação principal, valores importantes).
- Separação por **espaçamento e variação tonal**, não por bordas e divisórias. Nada de cards com borda forte, glow em tudo, neon ou vidro leitoso.
- O conteúdo principal de cada página domina (hero, número grande, gráfico); a estrutura é uma superfície que o sustenta.
- Densidade compacta: títulos de página 22 px, seção 14 px semibold, corpo 13 px, secundário 12 px, rótulos 11 px. Fonte Inter (padrão do app).
- Microinterações curtas (160–220 ms, `IlmenyxTheme.Premium`); sem bounce, sem grandes movimentos. Foco de teclado sempre visível.
- Funciona em **X11 e Wayland**, em tema escuro e claro, e no tamanho mínimo da janela (900×560) sem cortes nem sobreposição.

## Tokens (src/Ilmenyx.Desktop/Palette.cs, IlmenyxTheme.cs)
- Use sempre `Palette.*` (nunca cores literais em páginas): `Fg` texto principal, `Muted` secundário, `Faint` rótulos/desabilitado,
  `Card` superfície translúcida, `Hover`, `Line` borda quase invisível, `Panel` superfície sólida (diálogos), `Accent`/`AccentText` destaque,
  `Success`/`Warn`/`Error` estados, `Data*` séries de gráfico.
- Raios: `IlmenyxTheme.ControlRadius` (9) campos e botões, `CardRadius` (14) cartões, `ContainerRadius` (18), `Pill` chips/tags.
- Luz ambiente: só no fundo da janela (`IlmenyxTheme.AmbientLayers`). Não crie glows por cartão.

## Componentes (src/Ilmenyx.Desktop/UiKit.cs)
| Componente | Uso |
|---|---|
| `UiKit.PageHeader(título, subtítulo, ações)` | Topo de toda página (substitui títulos de 32 px). |
| `UiKit.SectionHeader(título, ação)` | Título de seção, com ação opcional à direita ("Ver tudo"). |
| `UiKit.Card(conteúdo)` | Superfície padrão, translúcida, borda quase invisível. |
| `UiKit.Hero(conteúdo, fundo, imagem)` | Elemento dominante de uma página (no máximo um por página). |
| `UiKit.FeatureCard(conteúdo)` | Destaque secundário com gradiente âmbar→vinho→azul (um por tela). |
| `UiKit.StatTile(rótulo, valor, nota, ícone)` | Métricas compactas lado a lado. |
| `UiKit.Tag(texto)` | Categorias/metadados em cápsula; `onImage: true` sobre heros. |
| `UiKit.Primary` / `UiKit.Translucent` | Ação principal (azul) / ação sobre hero ou gradiente. |
| `UiKit.IconButton`, `UiKit.Icon(Icons.X)` | Ações redondas e ícones (Material Design Icons, Apache-2.0, em Icons.cs). |
| `UiKit.Label(texto)` | Rótulo pequeno em caixa alta. |

## Composição
- Grids assimétricos quando houver vários itens do mesmo tipo (primeiro maior: proporção ~1.9 : 1 : 1 : 1).
- Tabelas: sem borda externa pesada; linhas separadas por `Palette.Line` fino, cabeçalho em `UiKit.Label`.
- Avisos (ex.: GPUs dedicadas não testadas) em `WarnBg`/`Warn`, compactos, nunca escondidos.

## Regras de engenharia
- Preservar todos os `Name` de controles, textos verificados pelo smoke e o comportamento. Mudança visual não muda regra de negócio.
- Nada de bibliotecas novas. Avalonia sem XAML (UI em C#).
- Validar: `dotnet build` sem avisos, `dotnet test`, smoke (`tools/Ilmenyx.UiSmoke`) e capturas em X11 e Wayland.

## Tema claro (referência "White Theme", 2026-11-06)
- Fundo quase branco frio (`#F4F5F8`) com luz ambiente quase imperceptível; cartões brancos sem borda visível (`Card`), sombra só nos destaques.
- Campos e ações redondas do topo em cinza-claro preenchido (`Palette.Field`, `#E9EBF0`), sem borda.
- Item ativo do menu: além do traço e do texto no acento, uma cápsula clara tingida do acento (só no tema claro).
- Destaque (`FeatureCard`): pastel pêssego → rosa → azul claro, texto escuro; a ação (`UiKit.Translucent`) vira botão sólido no acento.
- Heros sem imagem em pastel com texto `Fg`; heros com imagem continuam escuros e vibrantes (overlay de legibilidade).
