# Correção de sensores com um clique — teste real com o dono, 2026-11-07

Dell Inspiron 7460 (i7-7500U), Linux Mint 22.3. O RAPL (`/sys/class/powercap/intel-rapl:0/energy_uj`) era legível só pelo root.
- O dono clicou em "Liberar sensores da CPU" → LIBERAR CONSUMO DA CPU → Continuar e digitou a senha na janela do sistema (polkit).
- `observador-eventos.log`: 11:56:12 pkexec aguardando a senha; 11:56:17 `energy_uj` passa a `-r-----r--`, leitura OK e regra udev criada
  (`regra-udev-criada-pelo-app.rules`, vale depois de reiniciar).
- Leitura independente pelo lead: 1,88 W no pacote em repouso. O app detectou sozinho (o link de liberação sumiu, sem reiniciar o Ilmenyx).
- `visao-geral-consumo-rapl.png`: Visão geral com "CPU · CONSUMO (RAPL) 7.2 W" e o gráfico de consumo (durante o teste de interface).
- NÃO TESTADO de verdade: Desfazer; persistência após reiniciar o computador; o mesmo fluxo na AMD (k10temp/RAPL AMD) e o módulo de temperatura.
- Achado do teste: o link era pequeno demais e o dono não o encontrou; virou um botão em destaque de aviso no menu lateral.

## Tela de permissões na primeira execução (pedido do dono, 2026-11-07)
- Na primeira vez que o Ilmenyx abre e encontra sensores bloqueados, mostra todos juntos (todos marcados) e aplica com **uma única senha**:
  as correções fixas são unidas por `SensorHelp.Combined` numa só chamada pkexec; se uma falhar, as outras ainda rodam (testado em `sh` real).
- "Agora não" não roda nada; a tela não volta (`PermissionsAsked` em settings.json); o botão "Liberar sensores da CPU" do menu lateral continua.
- `primeira-execucao-permissoes.png`: captura do teste de interface (runner falso). NÃO TESTADO com a senha real nesta tela; o mesmo
  `PrivilegedFix` foi provado de verdade pelo botão do menu lateral (acima).
