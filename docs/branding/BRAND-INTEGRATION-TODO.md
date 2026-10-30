# Integração de marca

Atualizado em 29/10/2026: o mantenedor aprovou o Manual de Identidade e Interface do Ilmenyx
(`docs/branding/MANUAL-ILMENYX.md`). Ativos originais criados para o projeto; marca sem presunção de registro.

- [x] Aprovar manual e proveniência dos ativos (logo, ícone, símbolo): aprovados pelo mantenedor; fontes OFL não embutidas no app.
- [x] Tokens/temas: `Palette.cs` (Material 3 tonal, claro/escuro, WCAG 2.2 verificado no kit) e acento Fluent em `IlmenyxTheme.cs`.
- [x] Ícone da janela e símbolo do cabeçalho (`src/Ilmenyx.Desktop/Assets/`, carregados por `AppIcon.cs`).
- [x] Capturas reais com a nova identidade: UiSmoke em X11 e Wayland, 51 verificações cada (29/10/2026).
- [x] `packaging/linux/` integrado ao pacote (`scripts/package-linux-x64.sh` copia para `linux/`): `.desktop` e AppStream validados
      (`desktop-file-validate`, `appstreamcli validate`), ícones hicolor e instalador opcional só do usuário (`install-user.sh`/`uninstall-user.sh`).
- [x] App ID `io.github.bismuth_technology.Ilmenyx` (decisão do mantenedor, 30/10/2026; org GitHub Bismuth-Technology): WM_CLASS no X11
      (verificado com xprop) e app_id no Wayland (`DesktopBackend.AppId`).
- [ ] Flatpak/Flathub e capturas públicas no AppStream — PENDENTE: repositório público na organização.
- [x] Estética do manual: botões em pílula com preenchimento tonal, cartões 20, contêineres 28, campos 10, títulos de página 32,
      barra lateral em vidro (`Palette.Glass`) sobre luz ambiente (`IlmenyxTheme.Ambient`). Desfoque real de fundo não é
      suportado de forma confiável no Linux pelo Avalonia; o vidro é translucidez sobre a luz ambiente.
- [ ] CMYK/Pantone para impressão — não aplicável ao app; validar fisicamente se houver material impresso.
- [ ] Revisão de direito de marca antes de publicação; nenhuma ação remota automática.
