# Privacidade — Ilmenyx

Estado: IMPLEMENTADO como documentação do código local 0.10.0; PENDENTE DE REVISÃO humana
antes de publicação. Não é certificação de conformidade ou anonimização. Identidade mantenedora:
Bismuth Technology; responsável legal, contato, domínio e retenção de anexos recebidos:
[PENDENTE: definir pelo mantenedor].

O diagnóstico padrão lê fontes locais do kernel e do sistema: CPU, RAM, SO/kernel, capacidades de
dispositivos de bloco, IDs DRM/PCI, sensores e energia disponíveis. Fontes, timestamps, runtime,
rótulos e motivos de indisponibilidade entram em relatórios JSON/HTML locais quando solicitados.
Modelo de CPU, capacidade, kernel, IDs e combinação de sensores podem distinguir uma máquina.

Não existe no port atual coleta dedicada de hostname, usuário, IP/MAC, serial, UUID, machine-id,
processos ou credenciais, nem upload/telemetria de produto/servidor remoto implementados. Isso é
constatação de inspeção das chamadas, não prova universal por captura de tráfego. Bibliotecas
gráficas usam IPC local (X11/Wayland/D-Bus); NuGet/SDK em desenvolvimento podem acessar rede.
O código Windows original contém outros recursos e rede; esta política não o descreve.

SMART é opt-in por disco; smartctl opcional pode ler JSON bruto que inclui identificadores em
memória. O parser exporta apenas campos permitidos (status, dispositivo, exit code, relato booleano),
sem JSON bruto/serial/modelo. Nenhum privilégio é obtido pelo app. Ferramentas locais de validação
que recebem relatórios do usuário não tornam esses arquivos seguros para publicar.

Textos livres passam por limpeza limitada a 512 caracteres. Padrões óbvios como hostname=,
username=, serial=, UUID/MAC, IPv4 e caminhos /home/... ou /root/... são redigidos. Isso pode
produzir falsos positivos e não detecta todos os nomes, IPv6, caminhos com espaços ou formatos
particulares. Fontes de proveniência e nomes de dispositivos continuam presentes. Nunca afirmar
anonimização irreversível. Dados numéricos e metodologia não foram fabricados/alterados pela marca.

A janela de salvar e o HTML avisam para revisar antes de compartilhar. Destino escolhido pode
conter usuário/caminho pessoal: é usado localmente e pode aparecer em mensagens de erro; não é
um campo de relatório. Relatórios não são enviados automaticamente. Não há histórico persistente
de sensores no app; exportações permanecem no disco até o usuário gerenciá-las. Arquivos temporários
do writer são exclusivos e removidos em finally; não há garantia de apagar cópias/backups do sistema.

Antes de anexar algo a issue: abrir a cópia JSON/HTML, revisar campos/rótulos/fontes/datas/caminhos,
remover informações que não queira tornar públicas e compartilhar apenas o necessário. Não enviar
raw SMART, dumps, ambiente completo ou logs brutos com dados pessoais. Anexos públicos ficam
sujeitos à plataforma e à visibilidade escolhida; contato privado/retenção ainda não definidos.

Inventário e limites: docs/security/PRIVACY-SECURITY-AUDIT.md. Testes de fixtures não certificam
privacidade em todo hardware. Obrigações relacionadas a dados de terceiros, base/retenção/canal de
direitos e eventual aplicação de LGPD/ANPD: PENDENTE DE REVISÃO humana conforme operação real.

## Compatibilidade de jogos (página Compatibilidade e `ilmenyx compat`)

- Sem rede por padrão. Só depois de uma confirmação que nomeia os endereços:
  - **Consultar online:** envia apenas o ID Steam do jogo a `www.protondb.com` (nota), `store.steampowered.com` (Steam Deck) e
    baixa a lista pública do AreWeAntiCheatYet de `raw.githubusercontent.com`. Respostas ficam 7 dias em `~/.cache/ilmenyx/compat/online/`.
    Na mesma consulta (e para os jogos abertos depois, na mesma sessão), a imagem do jogo é baixada de `shared.akamai.steamstatic.com`
    (CDN de imagens da Steam; só o ID do jogo vai no endereço) quando o jogo não tem imagem na Steam local. Só JPEG/PNG/WebP de até 3 MB;
    fica 30 dias em `~/.cache/ilmenyx/compat/art/` (pasta limitada a 40 MB, as mais antigas são apagadas). Sem a confirmação, só a imagem
    local da Steam ou uma já guardada é mostrada.
  - **Baixar base de relatos:** lista `api.github.com/repos/bdefore/protondb-data` e baixa o arquivo mensal (~70 MB) de `raw.githubusercontent.com`,
    confere o hash git publicado, monta um índice local (~30 MB em `~/.cache/ilmenyx/compat/`) e apaga o arquivo.
- Nada sobre o seu hardware é enviado: a comparação com os relatos (GPU, distro, driver) acontece só no seu PC.
- O SteamDB não é consultado (proíbe acesso automatizado); o app só abre a página dele no navegador se você clicar.
- Relatos são de terceiros anônimos: texto puro, com pastas pessoais (`/home/...`) ocultadas. Opções de inicialização só podem ser
  aplicadas na Steam quando formadas por variáveis de ambiente, wrappers conhecidos e argumentos do jogo, sempre com confirmação e backup.
  A "Combinação recomendada" é montada no seu PC (sem rede) a partir dessas opções, sem as que conflitam entre si; verificar se um
  programa (gamemoderun, mangohud…) está instalado só olha as pastas de programas do sistema, nada é executado.

## Outros acessos à internet (sempre por ação sua)

- **Estimativa de FPS com HowManyFPS (opcional):** só se você cadastrar a sua chave. Envia a `howmanyfps.com` o **modelo da CPU, o
  modelo da GPU, o jogo e a resolução**, junto com a sua chave. Sem chave, a estimativa é 100% local.
- **Teste de rede (página Rede):** baixa e envia cerca de 100 MB de/para `speed.cloudflare.com` e mede a latência até `1.1.1.1` e
  `8.8.8.8`. Esses serviços veem o seu IP, como em qualquer teste de velocidade.
- **Driver NVIDIA:** a verificação da versão mais recente consulta `download.nvidia.com` (lista pública de versões); nada sobre o
  seu PC é enviado além da requisição em si.
