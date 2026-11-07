# Travamento da interface com jogo em tela cheia (Dell 7460, Intel HD 620 + GeForce 940MX, X11, Proton) — 2026-11-07

- Sintoma: na autoconfiguração do Mortal Kombat X (que troca para tela cheia exclusiva), imagem, teclado e mouse param. O sistema
  continua vivo (acesso remoto funciona), mas o Xorg (driver modesetting da Intel) não responde mais, nem fechando o jogo; só reiniciando.
- Xorg-no-travamento.log: antes, centenas de 'Present-flip: queue async flip during flip on CRTC 0 failed' e depois 'flip queue retry'.
  teste1-travou.csv: o jogo para de desenhar às ~17:14:50 (GPU 0%, 135 MHz), sem calor, falta de memória ou disco ocupado. Sem MangoHud.
- Correção testada: vsync forçado no DXVK (DXVK_CONFIG='dxgi.syncInterval = 1'). teste2-vsync-sem-travar.csv: ~16 min em tela cheia,
  zero avisos de flip no Xorg, interface normal depois de fechar.
- Outro achado: a CPU chegou a 100 °C no carregamento (refrigeração fraca deste notebook).
- gravador.sh: o gravador por segundo usado no teste.
