// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Contracts;

public sealed record RuntimeRequirements(bool Glibc, bool Icu, bool Fontconfig, bool X11, bool Egl,
    bool InvariantMode, bool X11Configured, bool WaylandConfigured)
{
    public bool CliReady => Glibc && (InvariantMode || Icu);
}
