// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Contracts;

public sealed record RuntimeRequirements(bool Glibc, bool Icu, bool Fontconfig, bool X11, bool Egl,
    bool InvariantMode, bool X11Configured, bool WaylandConfigured)
{
    public bool CliReady => Glibc && (InvariantMode || Icu);
}
