// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Core;

/// <summary>What this machine offers for the features that could not be ported or validated yet. Read-only facts; nothing here starts a test.</summary>
public sealed record MachineProbe(bool AmdGpu, bool NvidiaGpu, bool RayTracingGpu, bool RgbUsbDevice, bool GameLibrary);

public enum BlockReason { Hardware, Environment, WindowsOnly, ProprietarySdk, Privilege, Pending }

public enum BlockedState { Blocked, TestAvailable, NotApplicable }

/// <param name="TestHint">What to do when the machine qualifies (shown only when it does).</param>
public sealed record BlockedFeature(string Id, string Name, BlockReason Kind, string Reason, string Needs, string TestHint, Func<MachineProbe, bool>? Qualifies, string Reference)
{
    public BlockedState State(MachineProbe probe) => Kind == BlockReason.WindowsOnly ? BlockedState.NotApplicable : Qualifies is not null && Qualifies(probe) ? BlockedState.TestAvailable : BlockedState.Blocked;
    public string KindLabel => Kind switch
    {
        BlockReason.Hardware => "Limitação de hardware do autor",
        BlockReason.Environment => "Limitação de ambiente do autor",
        BlockReason.WindowsOnly => "Não existe no Linux",
        BlockReason.ProprietarySdk => "SDK proprietário",
        BlockReason.Privilege => "Exige privilégio de administrador",
        _ => "Ainda não implementado",
    };
}

/// <summary>Catalog of functions that are not ported or not validated, with the reason; the UI shows it so the maintainer knows when a machine can finish the work.</summary>
public static class BlockedCatalog
{
    public const string TestAvailableText = "Teste disponível nesta máquina";

    public static IReadOnlyList<BlockedFeature> All { get; } =
    [
        new("rt-bench", "Benchmark de ray tracing em GPU dedicada (AMD/NVIDIA)", BlockReason.Hardware,
            "Portado para Vulkan (VK_KHR_acceleration_structure + VK_KHR_ray_query) e validado em 2026-11-06 só numa APU Radeon 610M (RDNA2, 2 CUs, RADV); faltam GPU dedicada AMD/NVIDIA, driver NVIDIA e a comparação com o Windows.",
            "GPU AMD RX 6000+ ou NVIDIA RTX com driver Vulkan", "Rode `ilmenyx bench gpu-rt --allow-gpu-load --preset alta --resolution 1440p` (ou o tile RAY TRACING da página Benchmark) e anexe o resultado.", p => p.RayTracingGpu, "Sem_teste.md · GPU-08"),
        new("gpu-amd", "GPU AMD em hardware real (sensores, VRAM, stress, cena de jogo)", BlockReason.Hardware,
            "Leitura (inventário, sensores, VRAM reservada, consumo do pacote) validada numa APU Radeon 610M em 2026-11-06; GPU AMD dedicada e as cargas continuam só com fixtures.",
            "GPU AMD com driver amdgpu (de preferência dedicada)", "Rode scripts/validate-gpu-remote.sh com --allow-gpu-load e anexe o relatório.", p => p.AmdGpu, "Sem_teste.md · GPU-01, GPU-04"),
        new("gpu-nvidia", "GPU NVIDIA em hardware real (nvidia-smi, ajustes NVML, erros Xid)", BlockReason.Hardware,
            "O código está portado e testado só com fixtures; o autor não tem GPU NVIDIA.",
            "GPU NVIDIA com driver proprietário", "Rode scripts/validate-gpu-remote.sh com --allow-gpu-load e anexe o relatório.", p => p.NvidiaGpu, "Sem_teste.md · GPU-02, GPU-04"),
        new("rgb-real", "Controle de LEDs RGB em hardware real", BlockReason.Hardware,
            "O autor não tem periférico ou placa com RGB reconhecido pelo OpenRGB; só o protocolo foi testado.",
            "Teclado, mouse, memória, ventoinha ou placa-mãe com RGB (Corsair, Razer, ASUS, MSI…)", "Inicie o servidor na página Integração, aplique um efeito e confira as luzes.", p => p.RgbUsbDevice, "Sem_teste.md · RGB-01"),
        new("game-fps", "FPS e overlay em jogo real iniciado pelo Launcher", BlockReason.Environment,
            "Nenhum jogo foi iniciado nos testes (o smoke nunca inicia jogos); só o glxgears gerou log de FPS.",
            "Biblioteca Steam, Lutris ou Heroic com um jogo instalado", "Inicie um jogo pelo Launcher com MangoHud ligado e compare o FPS com o do próprio MangoHud.", p => p.GameLibrary, "Sem_teste.md · OVL-02, CMP-01"),
        new("corsair-sdk", "SDK nativo da Corsair (iCUE) e Razer Synapse", BlockReason.ProprietarySdk,
            "São SDKs proprietários do Windows e não são baixados pelo Ilmenyx; o OpenRGB cobre os mesmos dispositivos.",
            "Uma alternativa Linux oficial dos fabricantes", "", null, "docs/portagem-completa.md"),
        new("windows-tweaks", "Ajustes do Windows (Game DVR, HAGS, janela do Windows)", BlockReason.WindowsOnly,
            "Não existem no Linux; a página Otimiza traz os equivalentes (perfil de energia, governador, vm.max_map_count).", "—", "", null, "docs/portagem-completa.md"),
        new("usb-cleaner", "Limpeza de dispositivos USB fantasmas", BlockReason.WindowsOnly,
            "É uma limpeza do registro do Windows; o Linux não guarda esse histórico.", "—", "", null, "docs/portagem-completa.md"),
        new("process-priority", "Prioridade alta de processo e otimizações de tela cheia", BlockReason.Privilege,
            "Subir prioridade exige privilégio no Linux; o GameMode do sistema faz isso. O GameMode embutido só ajusta ionice/renice do usuário.",
            "GameMode instalado no sistema (daemon + polkit)", "", null, "Sem_teste.md · CMP-02"),
        new("winrar", "WinRAR no catálogo de apps", BlockReason.ProprietarySdk,
            "Programa proprietário do Windows sem versão Linux; o catálogo usa alternativas livres.", "—", "", null, "docs/portagem-completa.md"),
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
