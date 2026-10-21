// New Ilmenyx additions: see LICENSING.md; reused components retain their terms.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
namespace Ilmenyx.Cli;

public static class DoctorCommand
{
    public static int Run(LinuxRuntimeDependencies dependencies, TextWriter output,
        bool invariantMode, bool x11Configured, bool waylandConfigured) =>
        RuntimeRequirementsPresentation.Write(dependencies.InspectReport(invariantMode, x11Configured, waylandConfigured), output);
}
