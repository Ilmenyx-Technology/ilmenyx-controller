// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>Explicit, read-only runtime probe. No hardware providers or display connections.</summary>
public sealed class RequirementsDialog : Window
{
    public RequirementsDialog(Func<RuntimeRequirements>? inspect = null)
    {
        inspect ??= () => new LinuxRuntimeDependencies().InspectReport(
            LinuxRuntimeDependencies.IsInvariantModeRequested(),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
        Icon = AppIcon.Window; Title = "Ilmenyx — Requisitos"; Width = 740; Height = 560;
        MinWidth = 480; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var verify = new Button { Name = "VerifyRequirements", Content = "Verificar" };
        var close = new Button { Name = "CloseRequirements", Content = "Fechar", IsCancel = true };
        var report = new TextBox
        {
            Name = "RequirementsReport", IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Text = "Nenhuma verificação executada. Selecione Verificar.\n\nSondagem local de bibliotecas e presença de variáveis de ambiente; sem inventário ou benchmarks. O resultado equivale ao doctor da CLI e não certifica a interface gráfica."
        };
        AutomationProperties.SetName(report, "Relatório de requisitos — texto para leitura e cópia");
        bool closed = false;
        Closed += (_, _) => closed = true;
        close.Click += (_, _) => Close();
        verify.Click += async (_, _) =>
        {
            verify.IsEnabled = false;
            try
            {
                var result = await Task.Run(inspect);
                if (closed) return;
                using var output = new StringWriter(CultureInfo.InvariantCulture);
                RuntimeRequirementsPresentation.Write(result, output);
                report.Text = output.ToString(); report.CaretIndex = 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                if (!closed) report.Text = "Indisponível: não foi possível verificar os requisitos locais.";
            }
            finally { if (!closed) { verify.IsEnabled = true; verify.Focus(); } }
        };
        var panel = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var actions = new WrapPanel { ItemSpacing = 12, LineSpacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { verify, close } };
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var heading = new TextBlock { Text = "Requisitos · Ilmenyx", FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        panel.Children.Add(report); Content = new Border { Background = IlmenyxTheme.Ambient(), Child = panel };
        Opened += (_, _) => verify.Focus();
    }
}
