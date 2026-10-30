# Ilmenyx — instruções para o Claude Code

@AGENTS.md

## Contexto rápido (30/10/2026)

- **Produto:** Ilmenyx, by Bismuth Technology. Diagnóstico, monitoramento e benchmark de hardware para Linux.
  .NET 10 + Avalonia 12, com UI montada em C# (sem XAML).
- **Identidade visual aprovada e aplicada** (branch `feat/identidade-visual`):
  - manual: `docs/branding/MANUAL-ILMENYX.md`;
  - código: `Palette.cs`, `IlmenyxTheme.cs`, `AppIcon.cs` + `Assets/`;
  - app ID: `io.github.bismuth_technology.Ilmenyx`;
  - integração com o desktop: `packaging/linux/`.
  Mudar a identidade exige nova aprovação do mantenedor.
- **A máquina original de desenvolvimento só tem GPU Intel.** A pendência principal é validar AMD/NVIDIA em hardware real.
  Se este PC tiver AMD ou NVIDIA, siga `docs/ROTEIRO-VALIDACAO-GPU.md`.

## Ambiente

- **SDK sem sudo:** `bash scripts/get-local-sdk.sh` baixa o .NET 10 oficial em `/tmp/silex-dotnet` e confere o SHA-512.
  Depois, num shell bash: `source scripts/env-local-sdk.sh`.
  O SDK fica em `/tmp` e some ao reiniciar; basta rodar de novo.
- **Compilação e testes:**
  ```
  dotnet restore Ilmenyx.slnx
  dotnet build Ilmenyx.slnx -c Release --no-restore
  dotnet test Ilmenyx.slnx -c Release --no-build
  ```
- **O `tools/Ilmenyx.UiSmoke` não está no `.slnx`.** Compile-o explicitamente (`dotnet build tools/Ilmenyx.UiSmoke -c Release`)
  antes de rodar; senão ele carrega uma cópia antiga do `Ilmenyx.Desktop.dll`.
- **Teste gráfico:**
  - X11: `dotnet run --project tools/Ilmenyx.UiSmoke -c Release --no-build -- /tmp/saida-nova`
  - Wayland: o mesmo comando, prefixado por `env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland`.

## Regras para validar em outro PC

- Sem sudo e sem instalar nada no sistema.
- Nada de carga de GPU, stress ou benchmark pesado sem autorização explícita do dono do PC.
- Faça commits locais numa branch própria (ex.: `fix/gpu-nvidia`), sem push automático.
- Evidências vão para `docs/evidencias/<nome>/`. O que não puder ser testado vai para `Sem_teste.md`.
