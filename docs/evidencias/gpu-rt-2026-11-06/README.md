# Ray tracing Vulkan (port do GpuRtBench) — validação real em 2026-11-06

Ambiente: [ambiente.txt](ambiente.txt). APU AMD Radeon 610M (RDNA2, 2 CUs, RADV/Mesa 25.0.7), Linux Mint 22.3, X11.
Todas as cargas foram curtas, com o uso e as temperaturas registrados a cada segundo em paralelo (CSV nesta pasta).

## O que foi portado

`src/Ilmenyx.Gpu.Vulkan/GpuRtBench.cs` + `Shaders/rt_trace.comp` (adaptados de `upstream/ResetExplode/GpuRtBench.cs`, MIT):
mesma cena procedural (semente 20261015, 4.000 objetos: chão, 7 esferas grandes espelho/ouro, esferas, toros, colunas caneladas e caixas;
fosco/metal/espelho e ~3 % emissivos), mesmo path tracer por pixel com raios inline (RayQuery do DXR 1.1 → `GL_EXT_ray_query` num compute shader):
visibilidade, sombra suave do sol, oclusão de ambiente e rebatidas de reflexo/GI; uma BLAS por malha e TLAS refeita a cada quadro com os objetos girando;
câmera em trajeto fixo pelo tempo da passada; tempo de quadro por timestamps da GPU (fim a fim entre quadros); aquecimento sem medir + N passadas;
nota = FPS mediano × 10; variação = (maior − menor) ÷ mediana; confiável com ≥ 3 passadas e variação < 3 %; GigaRays/s = FPS mediano × pixels × spp × rebatidas × 3.
Presets normal (2 spp, 2 rebatidas), alta (4, 3), extrema (8, 4); 1080p/1440p/4K. Diferenças: render offscreen (sem janela), e um **quadro de teste**
(1/8 das linhas, extrapolado) recusa preset/resolução cujo quadro levaria mais de 2 s (um quadro é um envio único à GPU; o driver reinicia a GPU após ~10 s).

## Comandos e resultados (CLI)

| Execução | Comando (`ilmenyx bench …`) | Resultado | Temp. máx. GPU / CPU |
|---|---|---|---|
| Normal 1080p | `gpu-rt --allow-gpu-load --preset normal --resolution 1080p --warmup-seconds 3 --pass-seconds 5 --passes 3` | 4,51 FPS (passadas 4,48 / 4,51 / 4,53), 1% low 3,86, **nota 45**, variação 1,2 % (confiável), 0,112 GigaRays/s — [cli-normal.txt](cli-normal.txt) | 55 / 55,0 °C |
| Alta 1080p | `gpu-rt --allow-gpu-load --preset alta --resolution 1080p --warmup-seconds 3 --pass-seconds 5 --passes 3` | 1,48 FPS (1,48 / 1,48 / 1,47), 1% low 1,33, **nota 15**, variação 0,5 % (confiável), 0,110 GigaRays/s — [cli-alta.txt](cli-alta.txt) | 59 / 59,5 °C |
| Normal 1080p, binário final, `--json` | mesmo da 1ª linha + `--json` | 4,48 FPS (4,485 / 4,478 / 4,482), nota 44,8, variação 0,16 % (confiável), 0,112 GigaRays/s — [cli-normal-1080p-final.json](cli-normal-1080p-final.json) | 59 / 58,1 °C |
| Extrema 4K (proteção) | `gpu-rt --allow-gpu-load --preset extrema --resolution 4k …` | Recusado após o quadro de teste: "levaria cerca de 7.8 s por quadro … limite 2 s" — [cli-extrema4k.txt](cli-extrema4k.txt) | 56 / 56,6 °C (arquivo com o cancelamento) |
| Cancelamento | normal 1080p interrompido por SIGINT aos 7 s (`timeout -s INT 7`) | `Cancelled`, "nenhum valor é publicado" — [cli-cancel.txt](cli-cancel.txt) | idem |
| Corte por temperatura | normal 1080p com `--temp-limit 50` (CPU já a ~51 °C) | `ABORTADO: CPU a 51.3 °C, limite 50 °C` (GPU integrada usa a CPU) — [cli-tempcut.txt](cli-tempcut.txt) | 51,3 °C |
| Quadro PNG | `gpu-rt-snapshot --allow-gpu-load --output /tmp/…/rt-normal-1080p.png --preset normal --resolution 1080p` | 1920×1080 salvo; conferido visualmente: esferas espelho/ouro com reflexos e interreflexos, sombras suaves, objetos coloridos, céu; ruído de 2 spp como esperado. Cópia reduzida: [quadro-normal-1080p-960x540.png](quadro-normal-1080p-960x540.png); SHA-256 do original em [quadro-normal-1080p.sha256](quadro-normal-1080p.sha256) | 47 / 45,3 °C |

Uso da GPU em 100 % durante as passadas (CSV; `sensores-snapshot.csv` tem linhas duplicadas porque dois registradores ficaram ativos por engano). Nenhuma execução passou de 60 °C; o limite de parada (90 °C) nunca foi tocado.
FPS baixo é esperado: a Radeon 610M tem 2 CUs (cerca de 0,11 bilhão de raios por segundo neste teste).

## Testes automáticos

- Build: `dotnet build Ilmenyx.slnx -c Release --no-restore` → 0 avisos, 0 erros ([build.txt](build.txt)).
- Testes: 907/907 ([tests.txt](tests.txt)); novos em `tests/Ilmenyx.Tests/GpuRtTests.cs` (mediana, variação, nota, confiabilidade, presets/limites,
  escolha do adaptador sem llvmpipe, motivo para Tiger Lake, parsing da CLI, sensores com fixture). Nenhum cria dispositivo Vulkan.
- Teste real opt-in (`ILMENYX_RUN_GPU_TESTS=1`, 1 passada de 2 s normal 1080p): aprovado ([teste-opt-in.txt](teste-opt-in.txt)).
- Smoke X11: 52 PASS ([smoke-x11.txt](smoke-x11.txt)); a verificação da página Benchmark usa motor falso para o tile RAY TRACING (confirmação, recusa,
  corte de GPU quente, resultado confiável, histórico) e uma página sem suporte (tile desativado com o motivo). Captura: [smoke-bench-rt.png](smoke-bench-rt.png).

## Não validado

- GPU dedicada AMD (RX 6000+) e NVIDIA RTX (driver proprietário); Intel Arc.
- Comparação com o original no Windows (DXR 1.1): os números não são comparáveis sem metodologia própria.
- Clique real em INICIAR no tile RAY TRACING com a GPU real (o smoke usa motor falso; o motor real foi exercitado pela CLI e pelo teste opt-in).
- Smoke em Wayland nesta rodada (só X11); resolução 1440p/4K e preset extrema executados até o fim (nesta GPU, extrema 4K é recusado de propósito).
- Adaptador llvmpipe explícito (`--adapter 1`): não executado (seria carga de CPU); só a recusa automática é testada.
