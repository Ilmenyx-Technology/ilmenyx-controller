# Tour de boas-vindas e Central de ajuda

## O que o usuário vê

- **Boas-vindas (primeira execução, opcional).** Depois da tela de permissões (se ela aparecer) ou logo ao abrir, uma janela
  pequena "Bem-vindo ao Ilmenyx" oferece **Fazer o tour (2 min)** ou **Agora não**. Aparece uma única vez
  (`TourOffered` em `settings.json`, gravado antes de a janela abrir, aceite ou não).
- **Tour guiado.** Dez passos curtos. Cada passo abre a página certa, contorna em azul o item do menu (ou o controle do topo)
  e mostra um cartão ao lado do menu com "Passo X de N", **Voltar**, **Próximo/Concluir** e **Pular tour**.
  Teclado: Enter = próximo, Esc = sair, setas ←/→ dentro do cartão; o foco volta para onde estava ao fechar.
- **Central de ajuda** (item **Ajuda** no fim do menu lateral): para cada página, "Para que serve", "Como usar" em 2–5 passos
  e "Perguntas frequentes" (expansíveis), com **Abrir a página** e **Refazer o tour** no topo.
- **"?" no cabeçalho**, ao lado do título da página: abre a Central já rolada na seção daquela página.
- **Busca global**: além das páginas, encontra "Ajuda · <página>" e cada pergunta frequente (ex.: digitar "temperatura").

## Onde fica o texto

Todo texto visível do tour, das boas-vindas e da Central está em `src/Ilmenyx.Core/`:

- `HelpContent.cs` — modelo (`HelpLanguage`, `HelpSection`, `HelpFaq`, `TourStep`, `HelpGroup`), chaves da interface
  (`HelpKeys`) e a escolha do idioma (`HelpContent.For(código)`, com recuo para pt-BR).
- `HelpContent.PtBr.cs` — o conjunto de referência em português.

As telas (`WelcomeTourDialog`, `HelpCenterPage`, `ShellTour.cs`) não têm texto próprio: só usam `HelpKeys` e os registros.

### Editar um texto
Altere a frase em `HelpContent.PtBr.cs`. Descreva só o que o app faz de verdade; frases curtas e palavras simples; explique
um termo técnico em uma linha. Passos do tour: no máximo 3 frases (o teste conta).

### Nova página
Ao registrar `pages["chave"]` no shell, acrescente `S("chave", "grupo", ...)` em `HelpContent.PtBr.cs`
(e nos outros idiomas). O teste `EveryRegisteredPageHasExactlyOneHelpSectionAndNoOrphans` falha até isso ser feito.

### Novo idioma (en, es)
1. Copie `HelpContent.PtBr.cs` para `HelpContent.En.cs`, renomeie o método para `En()` e o código para `"en"`; traduza os textos.
   Mantenha iguais os identificadores: chaves de `Ui`, `Id` dos grupos/perguntas/passos, `Key` das seções, `PageKey` e `Highlights`.
2. Registre em `sets` de `HelpContent.cs`: `["en"] = En`.
3. `EveryLanguageSetHasExactlyTheReferenceEntries` compara todas as entradas com o pt-BR (falta/sobra aparece na mensagem).
4. Falta só decidir de onde vem o idioma ativo (hoje o shell usa `HelpContent.For()`, ou seja, pt-BR).

## Automação e testes

- `ILMENYX_NO_ONBOARDING=1` (o smoke define) ou `ILMENYX_NO_TOUR=1` impedem a janela de boas-vindas automática.
- Testes unitários: `tests/Ilmenyx.Tests/HelpContentTests.cs`.
- Smoke gráfico (`TourAndHelpAsync` em `tools/Ilmenyx.UiSmoke/Program.cs`): boas-vindas forçada, passos para frente/trás,
  Enter, Esc, Pular, Agora não, flag gravada, Central, "?" em Jogos e Stress test, busca por pergunta, 900x560 e tema claro.
  Capturas: `welcome-tour.png`, `tour-step.png`, `tour-step-last.png`, `tour-step-min.png`, `tour-step-light.png`,
  `help-center.png`, `help-center-stress.png`, `help-center-light.png`.
- Os eventos do smoke são roteados dentro do processo; não equivalem a teclado físico nem a leitor de tela (Orca/AT-SPI).
