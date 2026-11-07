// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Reflection;
using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;
namespace Ilmenyx.Tests;

public class IdentityPrivacyTests
{
    [Fact]
    public void IdentityMetadataAndCopiedNoticesPreserveOrigin()
    {
        foreach(var assembly in new[]{typeof(ProductIdentity).Assembly,typeof(CliArguments).Assembly,typeof(DesktopBackend).Assembly})
        {
            Assert.Equal("Ilmenyx",assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
            Assert.Equal("Bismuth Technology",assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        }
        Assert.Equal("ilmenyx",typeof(CliArguments).Assembly.GetName().Name);
        Assert.Contains("RESET EXPLODE",CliArguments.Help);Assert.Contains("GustavoReset",ProductIdentity.Attribution);
        Assert.Contains("não oficial",ProductIdentity.Attribution);Assert.Contains("sem afiliação",ProductIdentity.Attribution);
        var mit=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"LICENSES","MIT.txt"));
        Assert.Contains("Copyright (c) 2026 Gustavo (GustavoReset)",mit);
        Assert.Contains("Permission is hereby granted, free of charge",mit);
        Assert.DoesNotContain("Bismuth",mit);
        var license=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"LICENSE"));
        Assert.Contains("# PolyForm Noncommercial License 1.0.0",license);
        Assert.Contains("Required Notice: Copyright (c) 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)",license);
        Assert.Contains("LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt",license);
        Assert.Contains("LICENSES/MIT.txt",license);
        Assert.DoesNotContain("Copyright (c) 2026 Bismuth",license);
        Assert.EndsWith(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"LICENSES","PolyForm-Noncommercial-1.0.0.txt")),license);
        var permission=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"LICENSES","LicenseRef-Ilmenyx-Permissao-Adicional.txt"));
        Assert.Contains("prevalece o",permission);Assert.Contains("only ADDS rights",permission);
        Assert.Contains("Gustavo (GustavoReset)",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"NOTICE.md")));
        Assert.Contains("não oficial",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"NOTICE.md")));
        Assert.Contains("Avalonia.Wayland",File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"THIRD-PARTY-NOTICES.md")));
    }
    [Theory]
    [InlineData("hostname=private-host","private-host")]
    [InlineData("username=private-user","private-user")]
    [InlineData("serial_number=private-serial","private-serial")]
    [InlineData("MAC 02:ab:cd:ef:12:34","02:ab:cd:ef:12:34")]
    [InlineData("address 192.0.2.19","192.0.2.19")]
    [InlineData("path /home/private-user/report.txt","/home/private-user")]
    [InlineData("uuid 12345678-1234-1234-1234-123456789abc","12345678-1234-1234-1234-123456789abc")]
    public void ObviousFreeTextIdentifiersAreRedactedByDefault(string input,string identifier)
    {
        var clean=LinuxParsers.Clean(input);Assert.DoesNotContain(identifier,clean);Assert.Contains("redigido",clean);
    }
    [Fact]
    public void HardwareTextAndUnitsArePreservedAndPrivacyIsNotOverclaimed()
    {
        Assert.Equal("Intel Core i5 @ 2.40GHz",LinuxParsers.Clean("Intel Core i5 @ 2.40GHz"));
        Assert.Equal("Package id 0",LinuxParsers.Clean("Package id 0"));
        Assert.Equal("7.2.11-1-cachyos",LinuxParsers.Clean("7.2.11-1-cachyos"));
        Assert.Contains("Não é anonimização",ProductIdentity.SharingNotice);
        Assert.Equal("coretemp",LinuxParsers.Clean("coretemp"));
    }
    private sealed class Files : ILinuxFiles
    {
        public IReadOnlyList<string> BlockDevices()=>[];
        public Task<string> ReadAsync(string path,CancellationToken ct)=>Task.FromResult(path switch
        {
            "/proc/cpuinfo"=>"processor : 0\nmodel name : Fixture CPU\nhostname : PRIVATEHOST\nusername : PRIVATEUSER\nSerial : PRIVATESERIAL\nIP : 192.0.2.19\nMAC : 02:ab:cd:ef:12:34",
            "/proc/meminfo"=>"MemTotal: 1024 kB\nMemAvailable: 512 kB",
            "/etc/os-release"=>"PRETTY_NAME=Fixture Linux\nHOSTNAME=PRIVATEHOST\nUSERNAME=PRIVATEUSER",
            "/proc/sys/kernel/osrelease"=>"fixture-kernel",
            _=>throw new FileNotFoundException()
        });
    }
    [Fact]
    public async Task DedicatedSensitiveFieldsAreNotExportedToJsonOrHtml()
    {
        var snapshot=await new LinuxHardwareInfoProvider(new Files()).CollectAsync();
        foreach(var output in new[]{SnapshotReport.Json(snapshot),HardwareHtmlReport.Render(snapshot)})
        foreach(var identifier in new[]{"PRIVATEHOST","PRIVATEUSER","PRIVATESERIAL","192.0.2.19","02:ab:cd:ef:12:34"})Assert.DoesNotContain(identifier,output);
        var smart=SmartctlDiagnostic.Parse("nvme0n1",new(0,"""
            {"smartctl":{"exit_status":0},"smart_status":{"passed":true},"serial_number":"PRIVATESERIAL","hostname":"PRIVATEHOST","username":"PRIVATEUSER","ip":"192.0.2.19","mac":"02:ab:cd:ef:12:34","path":"/home/PRIVATEUSER/secret"}
            """));
        Assert.Equal("available",smart.Status);Assert.DoesNotContain("PRIVATE",SnapshotReport.Json(smart));
    }
    [Fact]
    public void CurrentBackendNameWinsAndLegacyAliasStillWorks()
    {
        Assert.Equal("wayland",DesktopBackend.Resolve(null,"wayland"));
        Assert.Equal("x11",DesktopBackend.Resolve("x11","wayland"));
        Assert.Equal(DisplayBackend.Auto,DesktopBackend.Parse(DesktopBackend.Resolve("","wayland")));
        Assert.Throws<ArgumentException>(()=>DesktopBackend.Parse(DesktopBackend.Resolve("invalid","wayland")));
    }
}
