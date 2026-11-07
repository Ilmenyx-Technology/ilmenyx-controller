# UX nouveau — 2026-11-07

IMPLEMENTADO na branch `fix/nouveau-ux`:

- Sensores NVIDIA ligados ao nouveau explicam a ausência de carga/clocks/VRAM/consumo e indicam a página Drivers. Outros drivers preservam o motivo genérico. Proveniência nouveau é sysfs, não nvidia-smi.
- Telemetria nouveau usa hwmon: `in0_label=GPU core` converte mV para V (fixture 900 → 0,9 V); `temp1_input` só aparece se fornecido pela fonte. `vddgfx` continua aceito; rótulos desconhecidos são recusados.
- Drivers mostra “Driver NVIDIA recomendado” somente para NVIDIA/nouveau. Detecta `mintdrivers` no Mint e `software-properties-gtk --open-tab=4` no Ubuntu/família via `BoundedProcess.FindSystem`. Ausência da ferramenta ou outra distribuição mostra a orientação/comando existente, sem executá-lo.
- Gerenciador é iniciado somente por clique, sem shell/root, sem esperar ou encerrar o programa externo. A ferramenta da distribuição controla qualquer instalação posterior. Launcher e descoberta são injetáveis; smoke usa inventário sintético e launcher falso.

Ambiente: Linux Mint 22.3, linux-x64, SDK 10.0.403 / runtime 10.0.14 em `/tmp/silex-dotnet`; telemetria de build desativada por `scripts/env-local-sdk.sh`.

Validação e limitações serão registradas abaixo. Fixture não certifica hardware. Aceite humano PENDENTE DE REVISÃO.

- Comando de ambiente: `source scripts/env-local-sdk.sh`.
- Restore padrão e build padrão encerraram com código 1 sem diagnóstico útil; a repetição serial foi necessária neste ambiente.
- `dotnet restore Ilmenyx.slnx --disable-parallel -m:1 /nr:false`: sucesso.
- `dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 /nr:false -p:UseSharedCompilation=false`: sucesso, 0 avisos / 0 erros, 24,44 s na validação final.
- `dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1 /nr:false`: abortado antes dos casos, socket VSTest negado pelo sandbox (erro 13); nenhum teste declarado aprovado.
- Commit fica para o lead: `.git` aponta para `/home/ssd/Ilmenyx/.git/worktrees/nouveau`, fora dos dois locais autorizados pelo brief para escrita. Não tentei escrever ali. Identidade/trailer do brief devem ser preservados pelo lead.
- `dotnet build tools/Ilmenyx.UiSmoke -c Release -m:1 /nr:false -p:UseSharedCompilation=false`: sucesso, 0 avisos / 0 erros, 21,67 s.
- `timeout 60 dotnet run --project tools/Ilmenyx.UiSmoke -c Release --no-build -- /tmp/ilx-nouveau-smoke-20261107-1`: saída 134, `XOpenDisplay failed` antes de iniciar os casos. Smoke/capturas X11 e Wayland NÃO TESTADOS; repetir na sessão gráfica fora do sandbox. Nenhum PASS atribuído ao novo caso com launcher falso.
- `git diff --check`: sucesso. Arquivos visuais protegidos preservados.
