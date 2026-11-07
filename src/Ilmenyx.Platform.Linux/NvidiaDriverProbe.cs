// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// "Driver NVIDIA" tab, Linux side: a read-only probe (sysfs, os-release, /usr/src, /lib/modules, dpkg-query, apt-cache policy, dkms status,
// mokutil, prime-select, nvidia-smi — never as root) and the real `apt-get -s` dry run. The privileged runner lives in NvidiaDriverRunner.
using System.Text;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Runs a read-only system program: (program name, arguments, timeout, token) → (exit code, stdout). Throws FileNotFoundException when absent.</summary>
public delegate Task<(int ExitCode, string Output)> ReadOnlyRunner(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);

public sealed class NvidiaDriverProbe
{
    private readonly string sys, etc, proc, modules, usrSrc, boot;
    private readonly ReadOnlyRunner run;
    private readonly Func<string, bool> hasProgram;
    private readonly PciIds? ids;

    public NvidiaDriverProbe(string root = "/", ReadOnlyRunner? runner = null, Func<string, bool>? hasProgram = null, PciIds? ids = null)
    {
        // Flatpak: the host's /etc, /usr and /lib are under /run/host (HostSpawn.HostPath); /boot is not shared (kernels come from /lib/modules and packages).
        sys = Path.Combine(root, "sys"); etc = HostSpawn.HostPath(Path.Combine(root, "etc")); proc = Path.Combine(root, "proc");
        modules = Directory.Exists(HostSpawn.HostPath(Path.Combine(root, "lib/modules"))) ? HostSpawn.HostPath(Path.Combine(root, "lib/modules")) : HostSpawn.HostPath(Path.Combine(root, "usr/lib/modules"));
        usrSrc = HostSpawn.HostPath(Path.Combine(root, "usr/src")); boot = Path.Combine(root, "boot");
        run = runner ?? ((p, a, t, ct) => BoundedProcess.FindSystem(p) is null ? throw new FileNotFoundException(p) : BoundedProcess.RunAsync(p, a, t, 1 << 20, ct));
        this.hasProgram = hasProgram ?? (p => BoundedProcess.FindSystem(p) is not null);
        this.ids = ids;
    }

    private static string Read(string path, int max = 4096)
    {
        try { return HostSysfs.Hidden(path) || File.Exists(path) ? SysfsText.Read(path, max).Trim() : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private async Task<(int Code, string Output)?> TryRun(string program, IReadOnlyList<string> args, int seconds, CancellationToken ct, List<string> notes)
    {
        if (!hasProgram(program)) return null;
        try { return await run(program, args, TimeSpan.FromSeconds(seconds), ct); }
        catch (Exception e) when (e is FileNotFoundException or IOException or TimeoutException or System.ComponentModel.Win32Exception) { notes.Add($"{program}: {e.Message}"); return null; }
    }

    public async Task<NvidiaDiagnosis> DiagnoseAsync(CancellationToken ct = default)
    {
        var notes = new List<string>();
        var pci = ids ?? await Task.Run(() => PciIds.Load(), ct);
        var (gpus, others) = Gpus(pci);
        string osRelease = Read(Path.Combine(etc, "os-release"), 8192);
        if (osRelease.Length == 0) osRelease = Read(Path.Combine(Path.GetDirectoryName(etc)!, "usr/lib/os-release"), 8192);
        var distro = DistroInfo.Parse(osRelease);
        string kernel = Read(Path.Combine(proc, "sys/kernel/osrelease"), 128);
        if (!NvidiaScripts.ValidKernel(kernel)) { notes.Add("Versão do kernel em uso ilegível."); kernel = ""; }
        string arch = Read(Path.Combine(proc, "sys/kernel/arch"), 32) is { Length: > 0 } a ? a : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        { System.Runtime.InteropServices.Architecture.X64 => "x86_64", System.Runtime.InteropServices.Architecture.Arm64 => "aarch64", var o => o.ToString().ToLowerInvariant() };

        var secure = SecureBootState.Unknown;
        if (await TryRun("mokutil", ["--sb-state"], 10, ct, notes) is { } mok) secure = NvidiaProbeText.SecureBoot(mok.Output);
        if (secure == SecureBootState.Unknown) secure = SecureBootFromEfi();

        bool primeTool = hasProgram("prime-select");
        string? prime = primeTool && await TryRun("prime-select", ["query"], 10, ct, notes) is { Code: 0 } pq ? NvidiaProbeText.Prime(pq.Output) : null;

        string? loaded = Read(Path.Combine(sys, "module/nvidia/version"), 64) is { Length: > 0 } lv ? lv : null;
        bool smiTool = hasProgram("nvidia-smi");
        string? smi = smiTool && await TryRun("nvidia-smi", ["--query-gpu=driver_version", "--format=csv,noheader"], 15, ct, notes) is { Code: 0 } so ? NvidiaProbeText.SmiVersion(so.Output) : null;

        IReadOnlyList<DpkgPackage> packages = [];
        string? candidate = null;
        if (distro.UsesApt && await TryRun("dpkg-query", ["-W", "-f=${db:Status-Abbrev}|${binary:Package}|${Version}\n", "*nvidia*", "linux-image-*", "linux-headers-*", "linux-modules-*", "linux-objects-*", "linux-signatures-*", "linux-main-modules-*", "linux-hwe-*"], 30, ct, notes) is { } dq)
            packages = DpkgPackage.Parse(dq.Output);
        var installedDriver = new NvidiaDiagnosis { Packages = packages }.InstalledDriver;
        if (installedDriver is { } i && await TryRun("apt-cache", ["policy", "nvidia-driver-" + i.Branch + (i.Open ? "-open" : "")], 20, ct, notes) is { Code: 0 } pol)
            candidate = NvidiaProbeText.Policy(pol.Output).Candidate;

        IReadOnlyList<DkmsEntry> dkms = await TryRun("dkms", ["status"], 20, ct, notes) is { } ds ? DkmsEntry.Parse(ds.Output) : [];

        var kernels = Kernels(kernel, packages);
        var moduleFiles = kernel.Length == 0 ? [] : ModuleFiles(kernel);
        return new NvidiaDiagnosis
        {
            Gpus = gpus, OtherGpus = others, Distro = distro, RunningKernel = kernel, Architecture = arch, Kernels = kernels, SecureBoot = secure,
            PrimeMode = prime, PrimeSelectAvailable = primeTool, LoadedVersion = loaded, SmiVersion = smi, SmiInstalled = smiTool,
            Packages = packages, Dkms = dkms, ModuleFiles = moduleFiles, CandidateVersion = candidate, Notes = notes,
        };
    }

    /// <summary>Real `apt-get -s` (simulation; apt says so and needs no root). Only the action's validated arguments are passed.</summary>
    public async Task<AptSimulation> SimulateAsync(NvidiaAction action, CancellationToken ct = default)
    {
        if (action.SimulationArguments is not { Count: > 0 } args) throw new InvalidOperationException("Esta ação não tem simulação.");
        if (!args.All(a => a is "install" or "purge" or "--purge" || NvidiaScripts.ValidPackage(a.TrimEnd('-')))) throw new ArgumentException("Argumento inválido.");
        var (code, output) = await run("apt-get", ["-s", .. args], TimeSpan.FromMinutes(2), ct);
        return AptSimulation.Parse(output, code);
    }

    private (IReadOnlyList<NvidiaGpu>, IReadOnlyList<string>) Gpus(PciIds pci)
    {
        var gpus = new List<NvidiaGpu>(); var others = new List<string>();
        string dir = Path.Combine(sys, "bus/pci/devices");
        if (!Directory.Exists(dir)) return (gpus, others);
        foreach (var dev in Directory.EnumerateDirectories(dir).OrderBy(p => p, StringComparer.Ordinal).Take(512))
        {
            if (!Read(Path.Combine(dev, "class"), 16).StartsWith("0x03", StringComparison.Ordinal)) continue;
            string vendor = Read(Path.Combine(dev, "vendor"), 8).ToLowerInvariant().Replace("0x", ""), device = Read(Path.Combine(dev, "device"), 8).ToLowerInvariant().Replace("0x", "");
            string driver = "";
            try { if (new DirectoryInfo(Path.Combine(dev, "driver")).LinkTarget is { } t) driver = Path.GetFileName(t.TrimEnd('/')); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            string name = pci.Device(vendor, device) ?? $"GPU {vendor}:{device}";
            if (vendor == "10de")
            {
                var (gen, chip) = NvidiaGenerations.Classify(device);
                gpus.Add(new NvidiaGpu(Path.GetFileName(dev), device, name, gen, chip, driver));
            }
            else others.Add((vendor switch { "8086" => "Intel ", "1002" => "AMD ", _ => "" }) + name + (driver.Length > 0 ? $" ({driver})" : ""));
        }
        return (gpus, others);
    }

    private SecureBootState SecureBootFromEfi()
    {
        string efi = Path.Combine(sys, "firmware/efi");
        if (!(HostSysfs.Hidden(efi) ? HostSysfs.DirectoryExists(efi) : Directory.Exists(efi))) return Directory.Exists(sys) ? SecureBootState.NotUefi : SecureBootState.Unknown;
        string var = Path.Combine(efi, "efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c");
        if (HostSysfs.Hidden(var)) return HostSysfs.ReadBytes(var, 5) is { Length: >= 5 } hb ? hb[4] == 1 ? SecureBootState.Enabled : SecureBootState.Disabled : SecureBootState.Unknown;
        try { if (File.Exists(var)) { var b = File.ReadAllBytes(var); if (b.Length >= 5) return b[4] == 1 ? SecureBootState.Enabled : SecureBootState.Disabled; } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return SecureBootState.Unknown;
    }

    /// <summary>Installed kernels (/boot/vmlinuz-*, /lib/modules/*/vmlinuz, linux-image-* packages) with their header check.</summary>
    private IReadOnlyList<KernelHeaders> Kernels(string running, IReadOnlyList<DpkgPackage> packages)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        if (running.Length > 0) set.Add(running);
        try
        {
            if (Directory.Exists(boot)) foreach (var f in Directory.EnumerateFiles(boot, "vmlinuz-*").Take(64)) set.Add(Path.GetFileName(f)["vmlinuz-".Length..]);
            if (Directory.Exists(modules)) foreach (var d in Directory.EnumerateDirectories(modules).Take(64)) if (File.Exists(Path.Combine(d, "vmlinuz"))) set.Add(Path.GetFileName(d));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        foreach (var p in packages.Where(p => p.Present && p.Name.StartsWith("linux-image-", StringComparison.Ordinal)))
            if (p.Name["linux-image-".Length..] is var k && NvidiaScripts.IsUbuntuKernel(k)) set.Add(k);
        return set.Where(NvidiaScripts.ValidKernel).Select(Headers).ToList();
    }

    private KernelHeaders Headers(string kernel)
    {
        string? dir = new[] { Path.Combine(modules, kernel, "build"), Path.Combine(usrSrc, "linux-headers-" + kernel), Path.Combine(usrSrc, "kernels", kernel) }.FirstOrDefault(Directory.Exists);
        if (dir is null) return new KernelHeaders(kernel, false, KernelHeaders.RequiredFiles);
        return new KernelHeaders(kernel, true, KernelHeaders.RequiredFiles.Where(f => !File.Exists(Path.Combine(dir, f))).ToList());
    }

    private IReadOnlyList<string> ModuleFiles(string kernel)
    {
        string root = Path.Combine(modules, kernel);
        if (!Directory.Exists(root)) return [];
        try
        {
            return Directory.EnumerateFiles(root, "nvidia.ko*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true })
                .Take(4).Select(f => Path.GetRelativePath(root, f)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}

/// <summary>
/// Runs one fixed script of <see cref="NvidiaPlanner"/> as root through polkit (pkexec), streaming every output line. Only after the
/// user's click and confirmation; the system asks for the password (Ilmenyx never sees it). pkexec exit 126 = dialog dismissed, 127 = not authorized.
/// </summary>
public static class NvidiaDriverRunner
{
    public static bool Available => BoundedProcess.FindSystem("pkexec") is not null;

    public static async Task<(bool Ok, string Message)> RunAsync(string script, Action<string> line, CancellationToken ct = default)
    {
        string? pkexec = BoundedProcess.FindSystem("pkexec");
        if (pkexec is null) return (false, "O pedido de senha do sistema (pkexec/polkit) não está instalado.");
        var start = HostSpawn.Start(pkexec, ["/bin/sh", "-c", script]);
        using var process = new System.Diagnostics.Process { StartInfo = start };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct); stop.CancelAfter(TimeSpan.FromMinutes(40));
        try
        {
            if (!process.Start()) return (false, "Não foi possível iniciar o pkexec.");
            var tail = new StringBuilder();
            async Task Pump(StreamReader r)
            {
                string? l;
                while ((l = await r.ReadLineAsync(stop.Token)) is not null)
                {
                    if (l.Length > 2000) l = l[..2000];
                    lock (tail) { tail.AppendLine(l); if (tail.Length > 4000) tail.Remove(0, tail.Length - 4000); }
                    line(l);
                }
            }
            await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync(stop.Token));
            return process.ExitCode switch
            {
                0 => (true, "Pronto."),
                126 => (false, "Cancelado: a senha não foi informada. Nada foi alterado."),
                127 => (false, "O sistema não autorizou (sua conta precisa ser de administrador)."),
                3 => (false, "Abortado pela verificação de segurança antes de alterar o sistema (veja o log)."),
                var c => (false, "Falhou (código " + c + "). Veja o log."),
            };
        }
        catch (OperationCanceledException) { return (false, ct.IsCancellationRequested ? "Interrompido." : "Tempo esgotado (40 min)."); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { return (false, "Falhou: " + e.Message); }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
        }
    }
}
