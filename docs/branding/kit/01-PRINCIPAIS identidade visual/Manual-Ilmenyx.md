# Ilmenyx — Manual de Identidade e Interface

**Produto:** Ilmenyx, by Bismuth Technology · **Versão do manual:** 1.0 (29/10/2026) · **Status:** proposta para aprovação do mantenedor

> O Ilmenyx tem identidade própria e leva o endosso "by Bismuth Technology". Este manual não confirma registro de marca.

## 1. Conceito

A **ilmenita (FeTiO₃)** é um óxido de ferro e titânio: preta, com brilho metálico de aço, cristaliza em **placas hexagonais tabulares**, e é o principal minério do **titânio**.

O símbolo é esse cristal:
- **Ferro (corpo):** uma placa hexagonal lapidada, com mesa central e bisel em facetas de aço.
- **Titânio (energia):** uma linha de ciano a azul que atravessa o cristal, dá um pico a 60° (o ângulo do sistema trigonal) e **sobe**. É o sinal medido e a performance em ascensão.

O símbolo traduz os princípios do produto: medir com precisão, não inventar dados e mostrar a verdade do hardware.

## 2. Símbolo e logotipo

| Versão | Arquivo | Uso |
|---|---|---|
| Símbolo em cores | `assets/simbolo/ilmenyx-simbolo-cor.svg` | Uso geral |
| Uma cor | `ilmenyx-simbolo-preto.svg` · `-branco.svg` | Impressão e carimbo; o sinal vira um corte |
| Reduzido (≤ 24 px) | `ilmenyx-simbolo-reduzido-16px.svg` | Sem bisel, corpo em aço claro |
| Logotipo com endosso | `assets/logo/ilmenyx-logo-fundo-{escuro,claro}.svg` | Site, README, tela Sobre |
| Logotipo sem endosso | `ilmenyx-logo-sem-endosso-*.svg` | Dentro do próprio app, onde o endosso já aparece em Sobre |
| Ícone de app | `assets/icones/ilmenyx-app-icon.svg` (squircle de vidro escuro) | Ícone do sistema |

**Wordmark:**
- "Ilmenyx" em **Inter Bold**, com o **"I" serifado** (Inter `cv08`, glifo `I.1`), para que "Il" não seja confundido.
- O endosso usa Inter Medium e a gema plana da Bismuth.

**Área de proteção:** ¼ da altura do símbolo. **Mínimos:** logotipo com 120 px de largura; símbolo com 16 px (use a versão reduzida).

**Não faça:**
- girar o hexágono;
- mudar a cor da linha de energia;
- remover o corte do sinal;
- aplicar sombra ou brilho;
- usar o símbolo detalhado abaixo de 32 px.

## 3. Estética da interface: Material 3 + acabamento Apple

**Estrutura vinda do Material 3:**
- **Cor tonal:** todos os tons derivam de sementes (azul-titânio, aço, ciano e preto-ferro), com papéis `primary`, `secondary`, `tertiary`, `surface*` e `on*`.
- **Superfícies em camadas:** `surfaceContainerLow`, `surfaceContainer` e `surfaceContainerHigh`, em vez de bordas e sombras.
- **Componentes:**
  - botão principal **preenchido** (`primary`, em formato pílula);
  - botão **tonal** (`secondaryContainer`) para ações secundárias;
  - *chips* para filtros (1 MIN, 5 MIN, 15 MIN);
  - barra de navegação lateral com indicador em pílula.
- **Estados:** camada de estado sobre o componente com 8% de opacidade em hover e 12% em pressionado.

**Acabamento vindo da Apple:**
- **Vidro:** barras e painéis flutuantes translúcidos (superfície a 64–72%, desfoque de 24 px). No Linux, o desfoque depende do compositor; sem suporte, use a cor sólida equivalente (`surfaceContainerHigh`).
- **Tipografia grande:** título de página em *Large Title* (34 px, Bold) e métricas em 32 px.
- **Cantos generosos:** 14 px em controles, 20 px em cartões e 28 px em diálogos e folhas.
- **Movimento:** 150, 250 e 400 ms com `cubic-bezier(0.2, 0, 0, 1)`; nada pisca sem motivo.

**Como foi aplicado no app (Avalonia):**
- Botões em pílula com preenchimento tonal neutro; o principal é preenchido com `primary` e o texto usa `OnAccent`.
- Cartões com raio 20, barra lateral com 28, campos com 10 e títulos de página com 32.
- Vidro: barra lateral translúcida (`Palette.Glass`) sobre uma luz ambiente do acento. O Avalonia não oferece desfoque real de fundo no Linux.
- O acento do tema Fluent é o `primary`, então checkboxes, foco e seleção seguem a marca.

**Regras de produto:**
- Dado ausente aparece como "indisponível", em `onSurfaceVariant`, nunca como zero.
- Cada série de gráfico tem uma cor fixa, igual em toda a interface (tabela abaixo).
- O ciano de energia (`tertiary`) é reservado a medições ao vivo e benchmarks.

## 4. Cores (tema claro e escuro)

Todas as combinações de texto e fundo passam no **WCAG 2.2**: texto ≥ 4,5:1, componentes e séries de gráfico ≥ 3:1. O relatório completo está em `tokens/contraste-wcag.md`.

| Papel | Claro | Escuro |
|---|---|---|
| `primary` | `#1045CF` | `#93B7FE` |
| `onPrimary` | `#FFFFFF` | `#000F50` |
| `primaryContainer` | `#CEDFFE` | `#032790` |
| `onPrimaryContainer` | `#00073E` | `#D8E5FD` |
| `secondary` | `#434D63` | `#AEB7CB` |
| `secondaryContainer` | `#D6DEEF` | `#2A3345` |
| `tertiary` | `#00627B` | `#7DCAE7` |
| `tertiaryContainer` | `#B1E8FD` | `#073E4F` |
| `error` | `#A70220` | `#FD8B87` |
| `errorContainer` | `#FFDBD8` | `#640411` |
| `success` | `#06633A` | `#7ECB9C` |
| `successContainer` | `#BAF5CF` | `#03381F` |
| `warning` | `#704903` | `#DDAE6F` |
| `warningContainer` | `#FEDFB8` | `#402802` |
| `surface` | `#F7F8FC` | `#020203` |
| `surfaceContainerLow` | `#F0F2F5` | `#050608` |
| `surfaceContainer` | `#E9EBEF` | `#08090C` |
| `surfaceContainerHigh` | `#E3E5E8` | `#101215` |
| `surfaceContainerHighest` | `#DCDEE2` | `#1B1D20` |
| `onSurface` | `#0C0D10` | `#E3E5E8` |
| `onSurfaceVariant` | `#383D47` | `#B2B8C1` |
| `outline` | `#636976` | `#747A87` |
| `outlineVariant` | `#BFC4CE` | `#292E37` |
| `data-cpu` | `#3D70D1` | `#86B1FE` |
| `data-gpu` | `#8459C3` | `#BF9BFC` |
| `data-memoria` | `#1A8585` | `#2EC9C9` |
| `data-disco` | `#9A6A15` | `#E4A339` |
| `data-rede` | `#118B41` | `#68CA80` |
| `data-temperatura` | `#C04637` | `#FD8C7B` |

**Equivalências no código atual (`Palette.cs`):**

| Nome no código | Papel no Material 3 |
|---|---|
| `Bg` | `surface` |
| `Panel` | `surfaceContainerLow` |
| `Card` | `surfaceContainer` |
| `Hover` | `surfaceContainerHighest` |
| `Line` | `outlineVariant` |
| `Fg` | `onSurface` |
| `Muted` | `onSurfaceVariant` |
| `Faint` | `outline` |
| `Accent` | `primary` |
| `Warn` | `warning` |

O arquivo `tokens/avalonia/Palette.cs` substitui o atual mantendo os mesmos nomes públicos e acrescenta `OnAccent`, `AccentContainer`, `Success`, `Error`, `Energy` e as cores das séries de dados.

## 5. Tipografia

| Papel | Fonte | Tamanho / altura de linha / peso |
|---|---|---|
| Large Title | Inter | 34 / 41 / 700 |
| Headline | Inter | 28 / 36 / 600 |
| Title | Inter | 22 / 28 / 600 |
| Corpo | Inter | 16 / 24 / 400 e 14 / 20 / 400 |
| Rótulo | Inter | 14 / 20 / 600 · 12 / 16 / 600 |
| Métricas | Inter com `tnum` (números tabulares) | 32 / 36 / 600 |
| Código e logs | IBM Plex Mono | 13–14 px |

Inter e IBM Plex Mono são fontes SIL OFL; confira a licença ao embuti-las no pacote.

## 6. Aplicação no software

1. **Paleta:** substitua `src/Ilmenyx.Desktop/Palette.cs` por `tokens/avalonia/Palette.cs` e revise os novos papéis.
2. **Ícone da janela:** `Window.Icon = new WindowIcon("ilmenyx-window-icon-256.png")`.
3. **Integração com o desktop:** app ID **`io.github.bismuth_technology.Ilmenyx`**, usado no `.desktop`, nos ícones hicolor, no AppStream, no WM_CLASS (X11) e no app_id (Wayland). No pacote, `bash linux/install-user.sh` registra menu e ícone só para o usuário, sem sudo; `uninstall-user.sh` desfaz.
4. **README e site:** use `assets/icones/favicon.svg`, `ilmenyx.ico` e o logotipo com endosso.
5. **Tela Sobre:** logotipo com endosso, crédito ao RESET EXPLODE e licença, separados da assinatura de marca.

## 7. Arquivos

**Telas de referência:** `assets/interface/tela-visao-geral-{escuro,claro}.png` mostram a estética aplicada à tela Visão geral do app real: navegação em vidro com indicador em pílula, Large Title, métricas com anel, gráfico ao vivo, cartão de desempenho, botões preenchido e tonal e tabela com números em fonte mono.

**Manual visual:** `manual/MANUAL-ILMENYX.pdf`, com 5 páginas: capa, cores, ícones e as duas telas.

**Arquivos do dia a dia:** `01-PRINCIPAIS/`.

```
assets/simbolo   símbolo (cores, uma cor, reduzido) — SVG + PNG 512/1024/2048
assets/logo      com e sem endosso; fundo escuro, claro, preto, branco — SVG + PNG
assets/icones    ícone de app, maskable, favicon, .ico, ícone de janela, linux/hicolor (nomeado pelo app ID), ícone simbólico, .desktop
assets/interface telas de referência da interface (claro/escuro)
tokens           JSON, CSS (claro/escuro), avalonia/Palette.cs, contraste-wcag.md
manual           PDF e páginas (SVG/PNG)
01-PRINCIPAIS    seleção para uso diário
processo-criativo  pranchas de exploração até o símbolo aprovado
source           geradores: generate.py → boards.py → destaques.py
```
