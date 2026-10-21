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
