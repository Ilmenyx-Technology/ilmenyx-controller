# Quadro do time — etapa NVIDIA (máquina Dell Inspiron 7460: i7-7500U + Intel HD 620 + GeForce 940MX), 2026-11-07
Branch de integração: fix/gpu-nvidia-940mx (lead). Commit + push a cada etapa validada.

| Tarefa | Dono | Worktree / branch | Estado |
|---|---|---|---|
| Precisão da Estimativa de FPS | Agente Claude | feat/fps-accuracy | INTEGRADO (961 testes, smoke 53) |
| UX nouveau | Codex | fix/nouveau-ux | INTEGRADO (967 testes, smoke 54) |
| Driver NVIDIA 580 (precisa da senha do usuário + reboot) e validação NVIDIA real | Lead | ~/Ilmenyx · fix/gpu-nvidia-940mx | driver instalado via DKMS p/ 6.14 (kernel 7.0 quebrado removido); falta REBOOT e validar nvidia-smi |
| IDEIA DO USUÁRIO: aba "Driver NVIDIA" (detectar GPU/distro/kernel/SecureBoot, checar headers, simular, instalar por polkit, conferir dkms, PRIME on-demand, reparar/atualizar/desfazer) | próximo agente | a criar após o reboot | planejado |
| Já feito nesta máquina | Lead | — | FPS tabela ampliada + sem número inventado; GPU dedicada primeiro; PRIME com nouveau (DRI_PRIME) |

Entrega final pedida pelo usuário: docs/CONFIABILIDADE.md (o que não tem confiabilidade alta, se dá para melhorar e como).
Perguntas do usuário já respondidas: chave de API do FPS não é necessária (estimativa local sem chave); estimativa local é grosseira → agente FPS.

Retomada após reboot: validar nvidia-smi/telemetria NVIDIA/DRV-01/OC-01/gpu-burn e cena na 940MX (driver proprietário, Vulkan NVIDIA), alertas FPS (2 GB VRAM), PRIME com __NV_PRIME_RENDER_OFFLOAD; ReadyToRun (abertura) pendente; entrega final docs/CONFIABILIDADE.md; remover worktrees; atualizar memória.
