# Auditoria inicial de privacidade e segurança — 2026-10-21

IMPLEMENTADO: inspeção dos coletores/parsers/exporters/CLI/UI do port e documentação factual.
Não é auditoria integral de binários, pentest, parecer legal ou captura universal de tráfego.
Base 3904a47; pós-renomeação 0.10.0. Original WPF separado, com funcionalidades/redes distintas.

| Dado/fonte real | Necessidade/exposição | Minimização e limite |
|---|---|---|
| CPU modelo/lógicos /proc/cpuinfo | Inventário, UI/JSON/HTML/contexto bench | Somente chaves permitidas; não lê Serial/hostname/user/IP/MAC como campos próprios |
| RAM /proc/meminfo | Total/disponível em bytes | Campos permitidos/unidades verificadas, sem processos |
| SO/kernel os-release/proc | Diagnóstico de ambiente | PRETTY_NAME/release; textos livres podem revelar customização |
| Dispositivos /sys/block | Nome e capacidade | Sem serial/modelo/mount/UUID; nomes/fontes ainda expostos, inclui virtuais |
| DRM/PCI e contadores suportados | IDs/carga/VRAM disponíveis | Sem inferência de VRAM iGPU; ausência é indisponível; IDs podem caracterizar máquina |
| hwmon/thermal/power_supply | Canal/rótulo, temperatura/energia/RPM/etc. | Só atributos permitidos; textos limitados; sem serial de bateria dedicado |
| SMART subprocesso opcional | JSON bruto em memória, health/exit | ArgumentList sem shell, opt-in explícito, parser allowlist; raw/serial/modelo não exportados |
| Runtime/datas/bench opções/hash/contagens | Proveniência e metodologia | Sem upload; fingerprint de ambiente/horário continua possível |
| Pasta do usuário em diálogo de destino | Sugerir caminho, somente local | Não é campo de relatório; erros/screenshot podem mostrar caminhos |
| Mensagens de erro/stack de testes/TRX/evidências | Diagnóstico de desenvolvimento | Podem ter paths/nomes de usuário; não presumir adequação a publicação |
| Hostname/usuário/IP/MAC/UUID/machine-id/processos | Não há coleta dedicada no port | Não prometer ausência em texto livre arbitrário ou raw SMART |

IMPLEMENTADO nesta sessão: redação de padrões óbvios em LinuxParsers.Clean (512 caracteres):
chaves hostname/username/serial/UUID/machine-id/MAC/IP, caminhos home/root, MAC/UUID/IPv4. Dados
numéricos e algoritmo não modificados. Há falsos positivos e formatos não cobertos, IPv6/nome
livre/caminho com espaços entre eles. Não substitui revisão humana. HTML continua escapando
texto; validações de unidade/tamanho, fonte e status preservadas. Warning no diálogo/ajuda/HTML.

Testes com fixtures: hostname/username/serial/MAC/IPv4/UUID/paths; campos dedicados extras não
saem dos parsers; raw SMART identificável não é exportado; modelo CPU/rótulo legítimo/kernel
preservados. Não são medições de hardware nem anonimização certificada.

Inspeção estática do port não encontrou cliente HTTP/upload/telemetria de produto, listener,
instalador, API de driver/BIOS/OC ou escrita em dispositivo bruto. System.Net é usado para escape
HTML e parsing IP, não envio. X11/Wayland/D-Bus são IPC local; não confundidos com ausência de
comunicação. Consulta SMART executa ferramenta do sistema apenas sob opt-in. Restore/audit NuGet
acessam rede na fase de desenvolvimento; SDK tem opt-out e BuildServices excluído. Captura de
tráfego de todas as dependências: NÃO TESTADO. Browser/OS/runtime podem ter comportamento próprio.

Writer cria arquivo exclusivo ao lado do destino, renomeia sem overwrite e limpa temporário em
finally; sem apagar dados do usuário. Não garante durabilidade pós-queda nem remoção de backups.
Leituras são limitadas/timeouts cooperativos; kernel bloqueado pode impedir interrupção imediata.
Full runners experimentais exigem opt-in e não têm proteção térmica; carga real continua NÃO TESTADA.

PENDENTE DE REVISÃO: evidências históricas já versionadas (incluem paths/ambiente), distribuição
nativa e avisos de terceiros, canal privado de segurança, retenção e responsabilidades sobre anexos
recebidos. .gitignore não apaga conteúdo existente/histórico e não é scanner de segredos. Nenhuma
reescrita ou publicação realizada. Relatórios antigos não foram “anonimizados” retroativamente.

[PENDENTE: mantenedor avaliar operações reais de recebimento/dados de terceiros, responsáveis,
finalidade, canal de direitos e obrigações aplicáveis]. Referência institucional, não parecer:
https://www.gov.br/anpd/pt-br e https://www.gov.br/inpi/pt-br/servicos/marcas/guia-basico.
