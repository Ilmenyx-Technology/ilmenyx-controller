# FPS medido na prática — metodologia e limites

O FPS mostrado na biblioteca (cartão **Desempenho medido**, em Jogos) vem da média do jogo executado neste PC, não de teste nem de
calibração. Código: `src/Ilmenyx.Core/FpsPractice.cs` (`MangoHudPlay`, `FpsHistory`), `src/Ilmenyx.Platform.Linux/MangoHudLogReader.cs`
(`ReadPlay`), `GameQualityDetector.cs` e `src/Ilmenyx.Desktop/LauncherPage.cs`.

## Quando há medição

Só em sessões abertas pelo Ilmenyx com **MangoHud** ligado no perfil do jogo. Ao fechar o jogo, o Ilmenyx lê os CSV da própria pasta de
logs (`~/.local/share/ilmenyx/mangohud`). Nada é medido sem o jogo aberto pelo Ilmenyx e nenhum número é inventado.

## Por que não é uma média simples

O log inclui launcher, menus e carregamentos, que têm FPS muito diferente do jogo. O cálculo:

1. **Log certo.** Os CSV começam pelo nome do processo (`nome_AAAA-MM-DD_...csv`). Entram só os que casam com o executável do jogo
   (sem extensão, com `.exe` do Wine ou cortado em 15 caracteres, comparação sem caixa e pontuação). Se nenhum casar, vale o de mais tempo válido.
   Logs de launcher e de `steamwebhelper` ficam de fora.
2. **Jogo de verdade.** Descartam-se os primeiros 20 s (carregamento, shaders) e os últimos 3 s. Quadros com frametime acima de 250 ms
   (carregamento) saem da média e são contados à parte (travadas).
3. **Janelas de 1 s.** FPS da janela = quadros / tempo da janela (quadros = FPS da amostra × intervalo entre amostras), não média
   de FPS por amostra.
4. **Resultado da sessão.** FPS típico = mediana das janelas; faixa = P10 a P90; 1% low = 1000 / média dos piores 1 % dos frametimes
   restantes (mesmo método do overlay). Vale com **60 s válidos** ou mais (`MangoHudPlay.MinValidSeconds`).
   `FpsSession.MinSeconds` (30 s) continua só para ler sessões antigas, que não aparecem mais no cartão (média simples sem segundos válidos).
   Uma sessão presa num limite comum (vsync, limitador) é marcada: o PC pode render mais.

Poucos segundos de menu a 500 FPS não mexem na mediana.

## Várias sessões

Por jogo: agrupa por **resolução + qualidade**; usa as últimas 10 sessões do grupo; FPS típico = mediana ponderada pelo tempo válido
das medianas das sessões (idem para P10, P90 e 1% low). Mostra número de sessões e horas. Só entra a GPU atual (a da sessão mais
recente), então trocar de placa não mistura histórico.

## Qualidade usada no jogo (detecção)

Leitura somente, ao selecionar o jogo e ao fim da sessão, fora da interface, com prazo de 3 s e sem rede. Nada é escrito no jogo.

- **Unreal Engine:** `GameUserSettings.ini` em `<prefixo>/drive_c/users/*/AppData/Local/<Projeto>/Saved/Config/{Windows,WindowsNoEditor,WinGDK}/`
  (Proton) e `~/.config/Epic/<Projeto>/Saved/Config/Linux*/` (nativo). Projeto escolhido pelo nome parecido com o jogo ou o executável
  (ou o único existente). Lê `sg.*Quality` (0 Baixo, 1 Médio, 2 Alto, 3 e 4 Ultra): a moda com mais da metade dos valores vira o preset,
  senão "Personalizado". Lê também resolução e limite de FPS.
- **Unity:** empresa e produto de `<exe>_Data/app.info`; Proton: `user.reg` do prefixo (`UnityGraphicsQuality_h…`, `Screenmanager Resolution …`);
  nativo: `~/.config/unity3d/<Empresa>/<Produto>/prefs`. O nível Unity é um índice sem nome padrão: aparece como "nível N" e **não** vira preset;
  agrupa o histórico como "Unity nível N". A resolução é confiável.
- **Prefixo:** o escolhido no perfil, o do Ilmenyx para o `.exe` ou o `compatdata/<appid>` da Steam; cada um também com subpasta `pfx`.
- Outros motores: indisponível; use os botões de qualidade.

A escolha manual (botões do cartão **Qualidade no jogo**) prevalece sobre a detecção; "Automático" volta a usar a detectada. A
resolução do perfil, quando não for "Padrão", prevalece sobre a detectada.

## Limites conhecidos

- Os arquivos do motor podem estar defasados (jogo que não salvou ainda, ou que usa outro arquivo): a detecção é só uma pista.
- Frametime em amostras de 100 ms enxerga um quadro por amostra: o 1% low é uma estimativa, igual ao overlay.
- Jogos que o MangoHud não consegue injetar (alguns Vulkan/anti-cheat) não geram log.
- Não testado com jogos reais ainda: `Sem_teste.md` (FPS-MEDIDO-01, QUALITY-DETECT-01).
