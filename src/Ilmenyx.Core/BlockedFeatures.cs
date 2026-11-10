// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>What this machine offers for the features that could not be ported or validated yet. Read-only facts; nothing here starts a test.</summary>
/// <param name="AmdDedicatedGpu">An AMD card that is not an APU (the integrated Radeon path was validated on 2026-11-06; a dedicated one was not).</param>
public sealed record MachineProbe(bool AmdGpu, bool NvidiaGpu, bool RayTracingGpu, bool RgbUsbDevice, bool GameLibrary, bool AmdDedicatedGpu = false);

public enum BlockReason { Hardware, Environment, ProprietarySdk, Privilege, Pending }

public enum BlockedState { Blocked, TestAvailable }

/// <param name="TestHint">What to do when the machine qualifies (shown only when it does).</param>
public sealed record BlockedFeature(string Id, string Name, BlockReason Kind, string Reason, string Needs, string TestHint, Func<MachineProbe, bool>? Qualifies, string Reference)
{
    public BlockedState State(MachineProbe probe) => Qualifies is not null && Qualifies(probe) ? BlockedState.TestAvailable : BlockedState.Blocked;
    public string KindLabel => Kind switch
    {
        BlockReason.Hardware => "Limitação de hardware do autor",
        BlockReason.Environment => "Limitação de ambiente do autor",
        BlockReason.ProprietarySdk => "SDK proprietário",
        BlockReason.Privilege => "Exige privilégio de administrador",
        _ => "Ainda não implementado",
    };
}

/// <summary>
/// Catalog of functions that are not ported or not validated, with the reason; the UI shows it so the maintainer knows when a machine can finish the work.
/// Items that do not exist on Linux at all (Windows tweaks, USB ghost cleaner, WinRAR) are not listed: see docs/bloqueados-removidos.md.
/// </summary>
public static class BlockedCatalog
{
    public const string TestAvailableText = "Teste disponível nesta máquina";

    public static IReadOnlyList<BlockedFeature> All { get; } =
    [
        new("rt-bench", "Benchmark de ray tracing em GPU dedicada (AMD/NVIDIA)", BlockReason.Hardware,
            "Portado para Vulkan (VK_KHR_acceleration_structure + VK_KHR_ray_query) e validado em 2026-11-06 só numa APU Radeon 610M (RDNA2, 2 CUs, RADV); faltam uma GPU dedicada AMD/NVIDIA com ray tracing (a GeForce 940MX testada em 2026-11-07 não tem) e a comparação com o Windows.",
            "GPU AMD RX 6000+ ou NVIDIA RTX com driver Vulkan", "Rode `ilmenyx bench gpu-rt --allow-gpu-load --preset alta --resolution 1440p` (ou o tile RAY TRACING da página Benchmark) e anexe o resultado.", p => p.RayTracingGpu, "Sem_teste.md · GPU-08"),
        new("gpu-amd", "GPU AMD dedicada em hardware real (Radeon RX: VRAM, ventoinha, limite de energia, cargas)", BlockReason.Hardware,
            "A GPU AMD integrada foi validada em 2026-11-06 numa APU Radeon 610M (inventário, sensores, VRAM reservada, stress e cena curtos). Uma placa dedicada não foi testada: ela tem VRAM própria (GDDR) com temperatura de memória e junction, ventoinha com rpm e PWM, limite de energia ajustável (power1_cap) e caminhos de sysfs diferentes, que hoje só passam por fixtures.",
            "Placa AMD Radeon dedicada (RX 400 ou mais nova) com driver amdgpu", "Rode scripts/validate-gpu-remote.sh com --allow-gpu-load e anexe o relatório; confira VRAM, ventoinha, junction e power1_cap com o radeontop/sysfs.", p => p.AmdDedicatedGpu, "Sem_teste.md · GPU-01, GPU-04 · docs/evidencias/gpu-amd-2026-11-06/"),
        new("gpu-nvidia", "GPU NVIDIA de desktop ou RTX (limite de energia ajustável, ventoinha, NVENC, driver open)", BlockReason.Hardware,
            "A NVIDIA de notebook foi validada numa GeForce 940MX (Maxwell, driver 580) em 2026-11-06 e 2026-11-07: identificação, telemetria pelo nvidia-smi, PRIME, stress e cena curtos. Essa placa não tem limite de energia ajustável, ventoinha própria, NVENC útil nem suporte ao driver open, então essas partes continuam só com fixtures.",
            "GPU NVIDIA RTX (Turing ou mais nova) ou placa de desktop com driver proprietário", "Rode scripts/validate-gpu-remote.sh com --allow-gpu-load e anexe o relatório; compare a página GPU e a OC com o nvidia-smi.", p => p.NvidiaGpu && p.RayTracingGpu, "Sem_teste.md · GPU-04, NVDRV-03 · docs/evidencias/bloqueados-2026-11-07/"),
        new("rgb-real", "Controle de LEDs RGB em hardware real", BlockReason.Hardware,
            "O autor não tem periférico ou placa com RGB reconhecido pelo OpenRGB; só o protocolo foi testado.",
            "Teclado, mouse, memória, ventoinha ou placa-mãe com RGB (Corsair, Razer, ASUS, MSI…)", "Inicie o servidor na página Integração, aplique um efeito e confira as luzes.", p => p.RgbUsbDevice, "Sem_teste.md · RGB-01"),
        new("corsair-sdk", "SDK nativo da Corsair (iCUE) e Razer Synapse", BlockReason.ProprietarySdk,
            "São SDKs proprietários do Windows e não são baixados pelo Ilmenyx; o OpenRGB cobre os mesmos dispositivos.",
            "Uma alternativa Linux oficial dos fabricantes", "", null, "docs/portagem-completa.md"),
        new("process-priority", "Prioridade alta de processo e otimizações de tela cheia", BlockReason.Privilege,
            "Subir prioridade exige privilégio no Linux; o GameMode do sistema faz isso. O GameMode embutido só ajusta ionice/renice do usuário.",
            "GameMode instalado no sistema (daemon + polkit)", "", null, "Sem_teste.md · CMP-02"),
        new("flatpak-embed", "Flatpak dentro do pacote", BlockReason.Privilege,
            "Flatpak é um serviço do sistema (root, polkit e bubblewrap) e não cabe em um pacote de usuário; a página Apps mostra o comando da sua distro.",
            "—", "", null, "Sem_teste.md · CMP-04"),
        new("mangohud-32", "MangoHud de 32 bits embutido", BlockReason.Pending,
            "Só as bibliotecas de 64 bits vão no pacote; jogos de 32 bits dependem do MangoHud do sistema.", "Incluir lib32 e o manifesto x86 no pacote", "", null, "Sem_teste.md · CMP-01"),
        new("wayland-overlay", "Overlay do Ilmenyx sempre no topo em Wayland", BlockReason.Pending,
            "A janela sempre-no-topo depende do compositor; o overlay do MangoHud dentro do jogo funciona.", "Protocolo layer-shell ou integração com o compositor", "", null, "Sem_teste.md · OVL-01"),
    ];

    public static int AvailableCount(MachineProbe probe) => All.Count(f => f.State(probe) == BlockedState.TestAvailable);
}
