// New Ilmenyx additions: see LICENSING.md; reused components retain their terms.
using System.Runtime.InteropServices;

namespace Ilmenyx.Platform.Linux;

/// <summary>Conservative native preflight; presence is not certification of ICU/runtime compatibility.</summary>
public sealed class LinuxRuntimeDependencies(Func<string, bool>? libraryAvailable = null)
{
    private readonly Func<string, bool> available = libraryAvailable ?? CanLoad;

    public bool CanUseGlobalization(bool invariantMode)
    {
        if (invariantMode) return true;
        if (Pair("")) return true;
        // Common system ICU sonames. ASCII construction avoids globalization during preflight.
        for (int major = 99; major >= 60; major--)
        {
            string suffix = "." + new string([(char)('0' + major / 10), (char)('0' + major % 10)]);
            if (Pair(suffix)) return true;
        }
        return false;
    }

    public LinuxRuntimeRequirements Inspect() => new(
        available("libc.so.6"), CanUseGlobalization(false),
        available("libfontconfig.so.1"), available("libX11.so.6"), available("libEGL.so.1"));

    private bool Pair(string suffix) => available("libicuuc.so" + suffix) && available("libicui18n.so" + suffix);

    public static bool IsInvariantModeRequested()
    {
        if (AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant) return true;
        string? value = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");
        return value == "1" || value is { Length: 4 } &&
            value[0] is 't' or 'T' && value[1] is 'r' or 'R' &&
            value[2] is 'u' or 'U' && value[3] is 'e' or 'E';
    }

    private static bool CanLoad(string library)
    {
        try
        {
            if (!NativeLibrary.TryLoad(library, out nint handle)) return false;
            NativeLibrary.Free(handle);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException)
        { return false; }
    }
}

public sealed record LinuxRuntimeRequirements(bool Glibc, bool Icu, bool Fontconfig, bool X11, bool Egl);
