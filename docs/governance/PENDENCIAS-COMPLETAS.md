# Ilmenyx — tudo o que está pendente (atualizado em 2026-11-07)

Lista para continuar em qualquer máquina. Repositório: https://github.com/Bismuth-Technology/ilmenyx (privado).
Detalhes de cada item de governança: `docs/governance/PENDENCIAS.md`. Testes que faltam por área: `Sem_teste.md` e `docs/CONFIABILIDADE.md`.

## 0. Como retomar numa máquina nova
- **No seu PC pessoal (clone em `/mnt/HDD/Github/Ilmenyx`)**: o repositório mudou de dono. Aponte o clone para o endereço novo e
  traga tudo: `git remote set-url origin https://github.com/Bismuth-Technology/ilmenyx.git && git fetch --all && git checkout main && git pull`.
  (O GitHub Desktop também aceita: Repository → Repository settings → Remote.) A cópia `ilmenyx-repo.tar.gz` deste pendrive é de
  2026-11-07 (commit cce044b) e serve se estiver sem internet.
1. `git clone https://github.com/Bismuth-Technology/ilmenyx.git` e `cd ilmenyx`.
2. SDK local (sem instalar nada no sistema): `bash scripts/get-local-sdk.sh` e `source scripts/env-local-sdk.sh`; depois `dotnet restore Ilmenyx.slnx`.
3. Build e testes: `dotnet build Ilmenyx.slnx -c Release` e `dotnet test Ilmenyx.slnx -c Release --no-build`.
4. **Assinatura GPG:** a subchave de assinatura só está no Dell de teste. Numa máquina nova, importe a chave principal do seu
   pendrive pessoal (`gpg --import chave-privada-PRINCIPAL.asc`) e crie uma subchave nova (`gpg --quick-add-key 3D9716572A398DCDD0CFF0EE733D2EC78436DCE8 ed25519 sign 3y`),
   depois apague a chave principal dessa máquina de novo (veja `docs/assinatura-gpg.md`). Atualize a chave pública no repositório.

## 1. Código esperando aprovação/merge (você decide)
- [ ] **PR #8** (`feat/rodada2`, inclui os PRs #6 e #7): tudo da sessão de 2026-11-07/08 — Proton fora da Steam, proteção contra
  travamento em tela cheia (validada), "Instalar jogo de Windows", tutorial + Ajuda (tour cobrindo todas as abas), Compatibilidade
  com banner e combinação sem conflitos, Estimativa de FPS com catálogo fixo e "voltar ao topo", Overlay salvando sozinho,
  Relatório legível, Inicialização separada (sistema × seus programas), Drivers redesenhada, Bloqueados limpo (PeaZip no lugar do
  WinRAR). Testes 1162, interface 63/63. Ao aprovar o #8, os PRs #6 e #7 podem ser fechados.
- [ ] **Validar na tela**: tutorial, Compatibilidade, Estimativa de FPS, Inicialização, Drivers.

## 2. Testes que precisam de você (hardware/jogo real)
- [ ] **LEMBRETE: testar de novo o "Instalar jogo de Windows"** (branch `feat/fastexec-c`): instalar em C:\Jogos; primeiro sem
  GE-Proton (modo de máxima compatibilidade), se falhar com o mesmo erro de CRC, de novo marcando "Usar o GE-Proton". O registro
  da falha fica em `~/.local/share/ilmenyx/fastexec/logs/`.
- [ ] **LEMBRETE: SuperTuxKart pelo Ilmenyx** (jogo nativo + MangoHud + FPS medido salvo).
- [ ] **LEMBRETE: ZRAM** no Otimiza (LIGAR ZRAM, com a sua senha) e, se quiser testar em outras distros, instalar o QEMU
  (`pkexec apt-get install -y qemu-system-x86 qemu-utils cloud-image-utils`) e usar `scripts/vm-distro-check.sh`.
- [x] **Congelamento com o MKX — RESOLVIDO** (2026-11-07): causa = driver de vídeo da Intel com tela cheia sem vsync via PRIME; corrigido com a "Proteção contra travamento em tela cheia" (validada). Texto antigo:: o sistema travou no 2º carregamento da autoconfiguração do jogo, com MangoHud e janela
  sem bordas. O log não mostra erro da NVIDIA, falta de memória nem temperatura. Próximo passo: repetir com um gravador que salva
  CPU/GPU/RAM/temperaturas a cada segundo no SSD; primeiro sem MangoHud, depois com. Se travar de novo, a última linha mostra a causa.
  Hipóteses: GPU (PRIME) sob carga, temperatura (CPU chegou a 89–90 °C antes), leitura pesada do pendrive (exFAT).
- [ ] **MKX pela interface do Ilmenyx até o fim**: overlay (já apareceu), FPS medido salvo ("Medido neste PC").
- [ ] **MKX pela Steam** como "jogo não Steam" com Proton (Steam já instalada; falta login e adicionar o jogo).
- [ ] **SuperTuxKart** (instalado) pelo Ilmenyx: caminho de jogo nativo + MangoHud + FPS medido.
- [ ] **Heroic e Lutris** (via Flatpak, sem senha): detectar e abrir um jogo.
- [ ] Aba **Driver NVIDIA**: instalação/reparo de verdade pelo app (só a detecção e a simulação foram testadas).
- [ ] **Tela de permissões na 1ª execução** com a senha real (precisa de uma máquina com sensores bloqueados, ou "Desfazer" antes).
- [ ] **Desfazer** da liberação de sensores; persistência após reiniciar (a regra udev já existe).
- [ ] **Outras distros**: Arch, Fedora, Debian, openSUSE (AppImage, Flatpak, Proton/umu com o python3 de cada uma); sistema sem python3.
- [ ] **Outros hardwares**: GPU AMD dedicada, NVIDIA RTX/desktop, Intel Arc.
- [ ] Wayland nativo em GNOME/KDE reais (só testado no Muffin aninhado).

## 3. Funcionalidades combinadas, ainda não feitas
- [ ] Ler os **"jogos não Steam"** da Steam (arquivo `shortcuts.vdf`) — fazer com o arquivo real depois que o MKX for adicionado lá.
- [ ] **Overlay e FPS medido nos jogos de loja** (Steam/Heroic/Lutris abrem o jogo sozinhos, sem as opções do Ilmenyx).
- [~] **Instalar jogos de Windows a partir do instalador** (`setup.exe`) — FEITO no código ("INSTALAR JOGO DE WINDOWS"), falta testar com um instalador real. com Proton, de forma genérica (GOG offline, mídia física,
  itch.io...). Sem recursos específicos para repacks.
- [ ] **Tutorial/Ajuda** (em construção, ver item 1).
- [ ] **Idiomas: inglês e espanhol** (extrair os textos da interface para arquivos por idioma).
- [ ] **Passe de otimização antes do lançamento público**: medir quanto o próprio Ilmenyx usa de CPU/RAM/GPU/disco/energia parado,
  monitorando e em cada página; achar e reduzir gargalos.
- [ ] Próxima release (v0.31): incluir o umu-launcher e anexar os fontes GPL dele e do python-xlib (`scripts/fetch-gpl-sources.sh`
  já faz) + assinar tudo (`scripts/sign-release.sh`).

## 4. Decisões e tarefas de governança (você)
- [ ] **Busca de marca manual** em 3 bases que bloquearam robôs: WIPO Global Brand Database, TMview, EUIPO (termos ilmenyx, ilmen*,
  bismuth; classes 9, 42, 41). Passo a passo: `docs/legal/BUSCA-MARCA-2026-11-07.md`, seção 4.
- [ ] **Depositar a marca ILMENYX no INPI** (recomendado: pessoa física, classes 9 e 42, R$ 880 no total). "Bismuth Technology":
  esperar a empresa ou escolher um nome mais distintivo.
- [ ] **E-mail da Bismuth** com encaminhamento para o seu (grátis: Gmail próprio com encaminhamento; ou domínio + Cloudflare Email
  Routing/ImprovMX). Depois trocar o contato em SECURITY.md e nas ofertas escritas das licenças GPL.
- [ ] **Doações**: configurar GitHub Sponsors na organização e/ou Pix (decisão 10-A já está na licença).
- [ ] Confirmar se as **21 páginas "layout segue o original"** copiaram algo do RESET EXPLODE (se não, viram só PolyForm). Opcional.
- [ ] Ao criar a empresa: contrato de cessão dos direitos para a pessoa jurídica + revisão curta da permissão adicional por advogado.
- [ ] `reuse lint` oficial (a ferramenta não estava disponível; um script local cobre por ora) e SBOM.
- [ ] Enviar o relato do travamento do Wayland ao Avalonia (rascunho em `docs/upstream/avalonia-wayland-crash.md`).
- [ ] **Tornar o repositório e a release públicos** quando eu disser que a validação está suficiente.
- [ ] Problemas encontrados que o app pode detectar e oferecer correção: `docs/problemas-conhecidos.md` (botão "Forçar fechamento do
  jogo", aviso de temperatura alta no jogo — a CPU chegou a 100 °C, Wi-Fi gerando milhares de erros PCIe no log, Alt+Tab).
- [ ] **HowManyFPS**: o cliente do app provavelmente usa a API errada (host/campos supostos, nunca testado com chave real). Pedir acesso
  pelo "Get API Access" do site explicando o Ilmenyx (app gratuito, não comercial), obter a documentação real, corrigir o cliente e só
  então decidir sobre chave compartilhada via proxy com cache (precisa de autorização escrita deles). Não embutir chave no app.
  Detalhes: `docs/pesquisa-linuxtoys-howmanyfps-2026-11-07.md`.
- [ ] **Ideias do LinuxToys** (só ideias; o código é GPL-3.0, incompatível): diagnóstico "por que o Proton/Flatpak não abre", cache de
  shader maior por usuário, mais apps de jogo no instalador, ZRAM pela via oficial da distro, opções por jogo (Proton Wayland, low-latency).
