# Validação AMD real — Radeon 610M (Mendocino), 2026-11-06

Notebook ASUS Vivobook Go E1504FA, APU AMD com Radeon 610M (PCI 1002:1506, RDNA 2), amdgpu, Mesa/RADV 25.0.7, kernel 6.14,
Linux Mint 22.3 (Cinnamon, X11). Roteiro seguido: `docs/ROTEIRO-VALIDACAO-GPU.md`, passos 1 a 5. A carga curta do passo 5 foi autorizada pelo usuário.

| Arquivo | Conteúdo |
|---|---|
| `ambiente.txt` | Reconhecimento: lspci, módulos, atributos amdgpu do sysfs, hwmon, vulkaninfo/glxinfo, sensors, DMI |
| `build.txt`, `tests.txt`, `tests.trx` | Build Release (0 avisos/erros) e testes antes das correções: 838/838 |
| `tests-apos-correcao.txt` | Testes depois das correções: 850/850 |
| `uismoke-build.txt`, `uismoke-x11.txt` | Smoke X11 com o código corrigido: 51/51 PASS |
| `cli-gpu-list.txt` | `bench gpu-list`: Radeon 610M (integrada, Vulkan 1.4.305, ray tracing) e llvmpipe |
| `cli-sensors-apos-correcao.json` | `sensors --json` depois das correções (rótulos de pacote e VRAM reservada, clock da memória) |
| `pacote-build.txt`, `pacote-teste.txt` | Pacote autocontido e `test-linux-package.sh`: PASS |
| `fase1/` | `validate-gpu-remote.sh` somente leitura (com o script corrigido) |
| `fase2/` | Cargas de 8 s: `gpu-burn.txt` (32,5 FPS), `gpu-scene.txt` (15,0 FPS, nota 150), `cena.png`, `termico.tsv` (uso/temperaturas/consumo por segundo) |
| `mangohud/` | CSV real do MangoHud embutido sobre o vkcube, depois da correção do wrapper |
| `antes-*.png` / `depois-*.png` | Páginas GPU e Visão geral antes e depois das correções, com dados reais desta máquina |

Depois das correções: 850/850 testes, smoke X11 51/51. Lista dos defeitos e do que ficou NÃO TESTADO: `Sem_teste.md`,
seção "Validação em hardware AMD real". Os valores de desempenho da Visão geral nas capturas ("6,000 pts") vêm da fixture do smoke, não de um benchmark desta máquina.
