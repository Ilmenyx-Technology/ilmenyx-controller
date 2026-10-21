# Proveniência upstream

Origem: https://github.com/GustavoReset/ResetExplode — Gustavo (GustavoReset), MIT.
Submódulo: upstream/ResetExplode, ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6, preservado.
Base desta renomeação: commit 3904a47; branch feat/silex-linux-phase1. Histórico principal e do
submódulo não reescritos. Principal sem remotes; não afirmar que já existe fork GitHub.

| Origem | Uso no port | Evidência / limite |
|---|---|---|
| BenchRenderer.cs | Cena/math/RNG/tonemapping C# adaptados no Core CPU | cpu-workload-spec.md, fixtures e corpus reduzidos; full não executado |
| MemoryBench.cs | Cadeia/soma/cópia em buffers pequenos | memoria-metodologia.md, oracle reduzido; disco/loading não chamados |
| GameSimBench.cs | Métodos matemáticos e medição adaptados | gamesim-metodologia.md e gamesim-completo.md; full real NÃO TESTADO |
| Coletores Windows/WMI/sensores | Auditoria como referência; novos adaptadores proc/sys | portabilidade-inventario.md e fontes-auditadas.tsv |
| WPF/DirectX/ETW/Registry/Drivers/Assets | Mantidos apenas no submódulo, não compilados pela solução nova | csproj net8.0-windows/UseWPF; inclusão recursiva não alcança src fora do submódulo |

Core/Contracts não têm referências WPF/Windows Desktop. Props de identidade ficam em src, não
na raiz: não afetam WPF por herança MSBuild. Build Windows real continua NÃO TESTADO.

Nenhum logo, shader, DLL/driver RESET ou fonte do mantenedor foi incorporado ao port. A auditoria
pelo mesmo implementador impede alegar clean-room estrito. A adaptação é independente, não oficial,
sem afiliação, patrocínio ou endosso. Manter aviso MIT também junto a binários futuros.
