# Confiabilidade do Ilmenyx

Posição em 07/11/2026. Documento para o mantenedor, Bismuth Technology.

## Resumo

O Ilmenyx já tem evidência real de inventário, sensores disponíveis e interface em alguns computadores Linux.
A confiança é maior nas leituras conferidas com a fonte do sistema, nas exportações e nas validações reduzidas dos algoritmos.
Isso não certifica todos os sensores, fabricantes, distribuições ou a saúde de uma máquina.
AMD integrada e NVIDIA de notebook foram exercitadas; AMD dedicada, NVIDIA RTX/desktop e Intel Arc ainda não.
Estimativa de FPS e compatibilidade de jogos orientam a decisão, mas não garantem desempenho nem que um jogo funcione.
Nenhum jogo real teve FPS medido nas máquinas de teste; instalação e reparo de driver pelo app também continuam NÃO TESTADOS.
Wayland nativo tem uma falha intermitente conhecida; X11/XWayland é o caminho mais seguro hoje.
É possível elevar a confiança com testes humanos direcionados, mais hardware e medições reais, sem executar carga automaticamente.

## Como ler este documento

- **Alta:** boa evidência para o recorte indicado; não significa certificação geral.
- **Média:** caminho real exercitado, mas com poucos equipamentos ou etapas importantes pendentes.
- **Baixa:** falta o uso real principal, há aproximação não calibrada ou falha conhecida.
- **IMPLEMENTADO** significa que o recurso existe; **NÃO TESTADO** significa que falta execução real daquele caminho.
- **PENDENTE DE REVISÃO** não é aprovação do dono; **BLOQUEADO** indica um requisito ausente.

Os níveis abaixo são uma avaliação documental, não uma probabilidade calculada de acerto.
“Fixture” é um arquivo de exemplo; “executor falso” simula uma ação sem fazê-la no sistema.
“Smoke” é uma verificação curta da interface. Esses testes ajudam a achar erros, mas não substituem uso real.

### Base real e limites do levantamento

- **06/11/2026:** ASUS Vivobook Go, APU Radeon 610M com amdgpu, Mint 22.3, Mesa/RADV 25.0.7, kernel 6.14.
  É um notebook da classe Ryzen 7520U; o nome exato da CPU não foi confirmado pelo firmware.
- **07/11/2026:** Dell Inspiron 7460, Intel HD 620 + GeForce 940MX Maxwell, Mint 22.3, kernel 6.14;
  primeiro nouveau, depois proprietário 580.178.04 via DKMS.
- Antes disso: Intel Iris Xe na máquina original de desenvolvimento e máquinas virtuais.
- **NÃO TESTADO:** AMD dedicada, NVIDIA RTX/desktop, Intel Arc e Windows.
  Uma VM não comprova sensores ou desempenho de hardware físico.

Fontes principais: [Sem_teste.md](../Sem_teste.md), [AGENTS.md](../AGENTS.md) e [README.md](../README.md).
As seções históricas não descrevem necessariamente o estado atual: por exemplo, a ausência antiga de NVIDIA foi superada pelo teste da 940MX.
As evidências posteriores só encerram o recorte realmente executado, não todas as pendências da área.

Este levantamento também incorpora fatos fornecidos pelo lead nesta tarefa: falha intermitente do Wayland,
reversão do Avalonia 12.1.5 e limites dos testes humanos. A 940MX desta máquina tem **4 GB** (4096 MiB na saída real
[nvidia-smi.txt](evidencias/gpu-nvidia-2026-11-07/proprietario/nvidia-smi.txt)); o "2 GB" que constava no README da evidência era
uma suposição anterior ao driver NVIDIA e foi corrigido.

## Confiança por área

O nível se refere à área inteira. Um resultado pontual pode ser mais confiável que a classificação geral.
Os passos da última coluna são propostas para o mantenedor e a equipe; não foram executados por este documento.

| Área | Nível de confiança | Por quê: evidência real ou falta dela | Dá para melhorar? | Como: próximo passo concreto e responsável |
|---|---|---|---|---|
| Diagnóstico/Sensores | Média | Inventário, leituras locais, monitor e exportações exercitados no Intel e nos notebooks AMD/NVIDIA. Unidades conferidas com sysfs/hwmon. Bateria física vista na tomada; faltam descarga, ciclos, duas baterias e mais placas. RAPL em watts após correção real continua NÃO TESTADO. | Sim | Equipe e donos de máquinas: comparar cada canal com a fonte local, registrar unidade/data/permissão; testar bateria em descarga e desktop sem bateria. Não interpretar ausência como zero. Ver SENSOR-01, ENERGY-01/02 no registro. |
| GPU AMD | Média | Radeon 610M integrada validada em 06/11: identificação, telemetria e cargas curtas. Corrigidos consumo do pacote rotulado como GPU, identificação de APU e clock da memória. Nenhuma AMD dedicada vista. [Evidência AMD](evidencias/gpu-amd-2026-11-06/README.md). | Sim | Mantenedor obter uma Radeon dedicada com amdgpu; equipe conferir GDDR, ventoinha, junction e limite de potência. Carga só autorizada e monitorada; anexar leituras comparadas à fonte. |
| GPU NVIDIA | Média | HD 620 + 940MX vistas com nouveau e 580.178.04. Telemetria proprietária e PRIME conferidos; stress/cena de 8 s. Falta RTX/desktop, módulo open, potência/ventoinha reais e outras gerações de driver. [Evidência NVIDIA](evidencias/gpu-nvidia-2026-11-07/README.md). | Sim | Testadores com RTX/desktop e outras gerações: comparar sensores com nvidia-smi, registrar campos recusados e ausências; testar escolha de GPU e corte térmico com autorização. Em notebooks Turing+ (RTD3), confirmar que a GPU dedicada volta a dormir com o Ilmenyx aberto (a 940MX não suporta RTD3). |
| GPU Intel | Média | Iris Xe e HD 620 exercitadas; identificação, clock e cargas curtas disponíveis. Carga/VRAM de iGPU têm limitações de fonte/permissão. Nenhuma Arc vista. | Sim | Equipe testar i915 e xe em máquinas distintas e uma Arc dedicada; comparar contadores com ferramenta autorizada. Decidir dependência/permissão antes de prometer carga ao vivo. Não chamar RAM compartilhada de VRAM dedicada. Ver GPU-03/05. |
| Benchmarks | Média | Correção reduzida de CPU/RAM/GameSim tem referências independentes. RAM completa teve uma execução; disco teve teste curto em HDD SATA/ext4. CPU Full e GameSim completo com carga seguem NÃO TESTADOS; stress longo e OC com carga também. GPU/RT têm recortes reais, descritos abaixo. | Sim | Equipe executar por etapas, com autorização: CPU Full/GameSim curtos, cancelamento real, RAM com pouca memória e disco em temporário autorizado. Registrar repetições, temperatura, versão e dispersão. Depois ampliar hardware e duração. Ver CPU-01, SIM-01, BENCH-02/03/04, STRESS-01 e OC-01/03. |
| Estimativa de FPS | Baixa | Tabelas aproximadas, referências RTX 4070 = 100 e Ryzen 7 7700X = 100. Faixa + confiança não calibradas com FPS real. Nenhum jogo medido. Tudo local (HowManyFPS removido em 08/11). Desde 08/11: trava de calibração (cena + GameSim deste PC), fallback por especificações, ajuste pessoal por sessões e expoente para jogos leves; âncoras com 3 GPUs e 1 CPU da equipe. [Método](fps-estimativa.md), [opções de calibração](fps-estimativa-metodos.md). | Sim | Mantenedor instalar um jogo de teste por decisão própria; equipe validar sessão MangoHud pelo Launcher e “Medido neste PC”. Repetir cenas/configurações conhecidas em GPUs variadas e medir erro da faixa. Escolher um método de calibração sem API (fps-estimativa-metodos.md). |
| Compatibilidade de jogos | Média | Consultas reais a ProtonDB/Steam Deck/anti-cheat e base de relatos exercitadas; não houve confirmação jogando. Relatos anônimos, ranking por semelhança de GPU/distro é heurístico e resumo ProtonDB usa endpoint público não oficial. | Parcial | Equipe registrar data/fonte e tratar falha de serviço; donos de máquinas confirmar jogos/anti-cheat nas versões atuais. Comparar recomendações com sessões reais. Serviço externo e relatos de terceiros não permitem garantia permanente. |
| Driver NVIDIA | Baixa | Diagnóstico real e apt-get -s no Mint aprovados no recorte observado. Instalar/reparar/atualizar/voltar ao nouveau pelo app NÃO TESTADO. Correção manual pelo lead não valida o botão. Outras distros e Secure Boot/MOK pendentes. [Método](driver-nvidia.md). | Sim | Mantenedor autorizar máquina recuperável: testar instalação, reboot, reparo e retorno pelo app, guardando logs e conferindo DKMS/módulo. Depois Secure Boot ligado e planos Debian/Fedora/Arch/openSUSE. Ver NVDRV-01…05. |
| Correções com um clique | Baixa | Lógica/UI de sensores com executor falso. Pedido real de senha via polkit, efeito em coretemp/k10temp, leitura RAPL em watts e Desfazer não exercitados de ponta a ponta pelo dono. | Sim | Dono clicar e confirmar em Intel e AMD; equipe observar janela do sistema, valores antes/depois e restauração. Guardar só resultado, nunca senha. Scripts devem permanecer fixos e limitados à lista permitida. |
| Overlay/MangoHud | Média | MangoHud embutido carregou em vkcube e glxgears, com CSV real em Vulkan/OpenGL. Isso não é jogo. Overlay em tela cheia, DXVK/Gamescope e jogos 32 bits continuam NÃO TESTADOS; componentes embutidos são 64 bits. | Sim | Equipe/dono testar jogo real Vulkan, OpenGL e Proton, com/sem bordas, X11/Wayland; comparar contador e CSV. Verificar cliques/posição do overlay e leitura ao fechar. Ver OVL-01…04 e CMP-01. |
| Jogos/Launcher/PRIME | Média | Bibliotecas/perfis e opções Steam testados com arquivos temporários. PRIME comprovado por CSV no Dell, com nouveau e proprietário, em aplicação de teste. Nenhum jogo iniciado pelo fluxo real; Steam não instalada nas máquinas recentes. | Sim | Dono usar jogo descartável na Steam: aplicar opções com backup, iniciar, fechar e desfazer. Equipe conferir GPU escolhida, processo Proton/Wine e restauração do perfil de energia. Depois Heroic e Steam Flatpak. Ver GAME-01…05. |
| Interface X11/Wayland/temas/acessibilidade | Média | Capturas e smoke em X11/Wayland, claro/escuro e mínimo 900×560. X11 é o caminho mais seguro. Wayland nativo tem crash intermitente no Dell; rodada recente usa Muffin aninhado. HiDPI/escala fracionária, Orca e contraste WCAG medido NÃO TESTADOS. [Capturas/logs](evidencias/ui-cinematic-2026-11-06/). | Parcial | Equipe reportar crash ao Avalonia e repetir smoke N vezes em futuras versões. Donos testar sessões GNOME/KDE reais, teclado físico, múltiplos monitores e 125/150/200%; revisão com Orca e medidor de contraste. Ver GUI-01/02/03. |
| Discos/SMART | Baixa | Parser/importação testados, inclusive relatório NVMe real do usuário; consulta ao vivo sem elevação com sucesso e atributos reais completos pendentes. Faltam USB/MMC, sensores de disco e casos reais com alerta. Veredito é heurística, não garantia de saúde. | Sim | Dono fornecer disco autorizado com acesso já permitido ou arquivo SMART de leitura para importar. Equipe conferir temperatura/desgaste/erros com fonte, testar SATA/NVMe e indisponibilidade. Não induzir falha física. Ver SMART-01/02 e DISK-01/02. |
| Rede | Baixa | Interface, regras e progresso com sonda falsa. Ping/download/upload reais, Wi-Fi/cabo, proxy e portal cativo NÃO TESTADOS. | Sim | Dono autorizar tráfego e endpoint; equipe registrar bytes, duração e latência comparada a ferramenta de referência em cabo/Wi-Fi. Testar cancelamento, sem rede e portal cativo. Não executar na abertura nem na suíte automática. |
| Pacote/distros | Média | Pacote autocontido com runtime/ICU e verificações locais; CLI/abertura em VMs Ubuntu, Debian, Fedora, Arch e openSUSE em versões registradas. Não certifica interação gráfica completa, hardware ou o pacote atual em todas elas. ARM64 e Alpine/musl NÃO TESTADOS. | Sim | Equipe repetir pacote atual em VMs desktop limpas e máquinas físicas: hashes, doctor, CLI, smoke e uso manual. Registrar kernel, arquitetura, driver, compositor e globalização. ARM64/musl exigem pacote e validação próprios. Ver PKG-01/02 e ARCH-01. |

### O que os números de benchmark permitem dizer

A validação reduzida dá confiança na correção do algoritmo nos casos comparados à referência.
Ela não mede desempenho sustentado nem certifica estabilidade térmica.
[benchmarks-metodologia.md](benchmarks-metodologia.md) separa algoritmo, medição e diferenças do original.
[gamesim-completo.md](gamesim-completo.md) registra o runner experimental, limites e falta de carga real;
o documento histórico desse runner também informa ausência de proteção térmica própria.
Não presumir que o corte de GPU protege qualquer modalidade de CPU/GameSim.

Ray tracing foi validado **somente na Radeon 610M**: normal 1080p, cerca de 4,51 FPS, nota 45.
Cancelamento, recusa de extrema 4K e corte térmico foram exercitados nesse recorte.
O rótulo “confiável” desse resultado significa baixa variação entre passadas, não validação universal.
A 940MX e a HD 620 recusaram RT por falta dos recursos necessários; isso não é teste de RTX.
Ver [evidência RT](evidencias/gpu-rt-2026-11-06/README.md).

Todos os scores de GPU Linux/Vulkan são experimentais e **não comparáveis ao Windows/DXR original**.
Mesmo nos algoritmos reaproveitados, runtime, driver, medição e aquecimento mudam o resultado.
Não há teste Windows que autorize equivalência dos demais scores.
O teste curto de disco comprova execução/limpeza no caso observado, não a exatidão universal da taxa.

## O que precisa de hardware ou ação humana

- **Um jogo e Steam reais:** dono autorizar instalação/uso; validar Launcher, opções com backup,
  MangoHud, GPU PRIME, sessão “Medido neste PC” e desfazer. Esse único roteiro fecha várias lacunas.
- **Mais GPUs:** conseguir AMD dedicada, NVIDIA RTX/desktop e Intel Arc; começar por inventário/sensores,
  depois carga curta autorizada. Para drivers legados/open, obter também as gerações correspondentes.
- **Máquina recuperável para driver:** dono acompanhar senha do sistema, reboot e recuperação;
  testar ida/volta pelo app e Secure Boot ligado. Uma VM precisa de GPU acessível para validar o módulo real.
- **Sensores:** dono executar correção e Desfazer; conferir RAPL real em watts, permissões e bateria em descarga.
  Não basta a janela abrir ou o executor falso retornar sucesso.
- **Discos e rede:** autorização por disco/arquivo e por teste de tráfego; obter dados reais e revisar
  antes de compartilhar. Não fazer benchmark em dispositivo bruto nem forçar um disco a falhar.
- **Sessões gráficas reais:** usuários de GNOME/KDE, monitor HiDPI e pessoa usando Orca;
  testar navegação e leitura sem automação global de teclado/mouse.
- **Carga prolongada/OC:** somente máquina adequada, autorização específica e acompanhamento térmico.
  Os 8 s já executados não aprovam dez minutos; leitura de limites não aprova aplicar comandos de OC.

Cada fechamento deve registrar data, versão do Ilmenyx, máquina, SO/kernel/driver/compositor,
ação realizada, saída esperada/observada e arquivo de evidência em `Sem_teste.md`.
Manter falhas e casos inconclusivos; aprovação técnica não é aprovação humana.
Sem equipamento, registrar **BLOQUEADO** ou **NÃO TESTADO**, com a condição de reteste.
É permitido continuar outros recortes testáveis sem esconder essas pendências.

## Riscos conhecidos e como contornar hoje

### Wayland nativo: encerramento intermitente do app

Segundo o lead, no Dell o Avalonia 12.1.4 caiu em 1 de 3 rodadas; 12.1.5 caiu em 3 de 4 e a atualização foi revertida.
São amostras pequenas, não uma taxa universal de falha. O crash ocorre após fechar janelas rapidamente:
`NullReferenceException` em `Avalonia.Wayland.Server.Persistent.WXdgTopLevel.OnConfigureBatchComplete`.
O ponto identificado está no backend Wayland do Avalonia, não no código do Ilmenyx.

No modo automático, o app usa X11 quando existe `DISPLAY`, inclusive em sessões GNOME/KDE Wayland com XWayland.
Sem X11 disponível, usa Wayland nativo. Escolha explícita de backend é respeitada.
**Contorno:** `ILMENYX_DISPLAY_BACKEND=x11`, desde que X11/XWayland esteja disponível;
sem ele, a variável não cria um servidor X11.

A validação recente AMD/NVIDIA de Wayland usou apenas Muffin aninhado, não login real em GNOME/KDE.
Há registros anteriores de KWin e GNOME/Weston headless em `Sem_teste.md`; eles não aprovam o cenário recente do Dell/redesign.
Próximo passo: equipe preparar reprodução mínima e stack trace para Avalonia, com revisão antes de envio;
nenhum relato remoto foi enviado nesta tarefa.
Para cada versão candidata, definir N rodadas antes do teste, repetir abrir/fechar rápido e guardar todas as saídas,
contagem de falhas e compositor. Repetir também a regressão X11; uma rodada aprovada não encerra a intermitência.

### Temperatura: margem curta no Dell

Em 8 s de carga de GPU a CPU chegou a **89–90 °C**. A iGPU compartilha o chip;
a dedicada e a CPU também dependem da refrigeração do notebook.
O corte por temperatura foi exercitado de verdade na Radeon 610M (com limite de 50 °C a carga foi abortada); no Dell ele não chegou a disparar,
porque o pico ficou em 89–90 °C, colado no limite padrão de **90 °C**: a margem é pequena.
Isso não prova segurança em qualquer notebook, sensor ou duração.

**Contorno:** evitar carga longa nesse equipamento, acompanhar CPU e GPU e começar por duração curta.
Quando a modalidade permitir, adotar limite mais conservador revisado pelo responsável pela máquina;
parar se o sensor estiver ausente ou o comportamento for anormal. Não elevar o limite para concluir o teste.
Antes de ampliar duração, conferir ventilação e se aquela modalidade realmente tem guarda térmica.

### NVIDIA: ramo correto e campos que mudam

Na fotografia de 07/11/2026, o ramo global mais recente informado pelo lead é **595**,
mas Maxwell/Pascal/Volta precisam do ramo **580**. A página foi corrigida para comparar dentro do ramo suportado.
A consulta de rede por ramo ainda consta NÃO TESTADA em NVDRV-05; parser testado não equivale ao clique online.
A tabela de gerações/ramos exige manutenção manual quando a NVIDIA mudar suporte.
Não interpretar esses números como recomendação permanente para qualquer placa.

**Contorno:** conferir geração, ramo e pacotes da distribuição; não instalar um número maior só por ser “latest”.
A instalação manual inicial puxou kernel com cabeçalhos incompletos; a correção manual não valida o instalador do app.
Até testar o ciclo real, usar diagnóstico/plano/simulação como evidência limitada e reservar alterações para ambiente recuperável.
Ver [driver-nvidia.md](driver-nvidia.md) e [status real](evidencias/nvidia-driver-tab-2026-11-07/status.txt).

O 580 recusou `clocks.max.video` e `clocks_throttle_reasons.active`, derrubando a consulta inteira.
O código agora retira campos recusados; outras gerações podem mudar mais campos, e só o 580 foi observado.
Potência e ventoinha podem vir como `N/A` em notebooks; limites de potência de preenchimento são ignorados.
**Contorno:** preservar “indisponível”, comparar com nvidia-smi e não transformar ausência em zero ou margem de OC.
No nouveau, a 940MX não apareceu como Vulkan; testar uma aplicação OpenGL via PRIME não muda essa limitação.
A [validação nouveau](validacao-nouveau-ux.md) registra ainda tentativas bloqueadas pelo sandbox;
não tratá-las como testes aprovados do incremento específico.

### NVIDIA em notebook: GPU que não pode dormir

O app chamava o `nvidia-smi` a cada 2 s com a janela aberta. Em notebooks Turing+ com RTD3 isso acorda a GPU dedicada o tempo todo
(bateria e calor). Agora o estado de energia é lido antes pelo sysfs, que não acorda a placa, e uma GPU em repouso aparece como
"em repouso". A 940MX não suporta RTD3 (fica sempre ativa), então esse caminho foi testado só com fixture.
**Como aumentar a confiança:** num notebook RTX com `prime-select on-demand`, deixar o Ilmenyx aberto e conferir que
`/sys/bus/pci/devices/<GPU>/power/runtime_status` chega a `suspended`.

### Tempo de abertura

Medido no Dell (07/11): ~0,95 s de inicialização do .NET/Avalonia/Skia/fontes, ~0,3 s montando a janela e a Visão geral e ~0,5 s
até o primeiro desenho (cerca de 1,8 s no total; o RESET EXPLODE abre em ~0,5 s no Windows). O ReadyToRun foi testado e não trouxe ganho
medível, por isso não foi adotado. **Como melhorar:** medir com perfilador (dotnet-trace) qual parte das ~0,95 s iniciais é Skia/fontes e
adiar a montagem das partes da Visão geral que não aparecem na primeira tela.

### FPS e compatibilidade: orientação não é medição

A faixa de FPS é aproximação; “confiança média” não é intervalo estatístico calibrado.
Não há fonte externa de FPS (a consulta à HowManyFPS foi removida em 08/11); opções de calibração local em `fps-estimativa-metodos.md`.
**Contorno:** usar a faixa apenas para expectativa inicial e priorizar **“Medido neste PC”** de sessão MangoHud real.
Essa é a fonte mais confiável para o desempenho daquela sessão, mas o fluxo completo com jogo continua NÃO TESTADO.
A sessão vale para as configurações usadas, não muda ao trocar o preset da estimativa.
O 1% low usa registros de 100 ms e pode suavizar engasgos; não equivale a capturar cada quadro.
O método também registra risco de gravações concorrentes em `settings.json` sobrescreverem campos;
a equipe deve corrigir/testar persistência antes de prometer preservação de toda sessão.

ProtonDB reúne relatos anônimos e antigos; semelhança de GPU/distro não prova a mesma experiência.
O endpoint de resumo não oficial pode mudar. Anti-cheat também pode mudar sem alteração do Ilmenyx.
**Contorno:** olhar data e fonte, confirmar no jogo atual e revisar opções antes de aplicá-las.
“Aplicar na Steam” ainda não foi usado em instalação real; manter backup e testar reversão antes de uso habitual.

### Aparência, saúde e privacidade

Capturas em claro/escuro e 900×560 mostram o recorte visual revisado, não acessibilidade completa.
HiDPI, escala fracionária, Orca e contraste WCAG medido continuam pendentes.
**Contorno:** testar legibilidade na tela do usuário e registrar cortes/foco/contraste;
a equipe deve medir contraste dos textos sobre gradientes e corrigir com os tokens aprovados.
Ver [direção visual](branding/UI-CINEMATIC.md) e [capturas](evidencias/ui-cinematic-2026-11-06/).

Vereditos de disco/bateria são heurísticas, não laudo nem previsão de falha.
“Sem dados” ou “não verificado” não significa saudável; estabilidade por alguns segundos não prova estabilidade de OC.
Relatórios locais também não são automaticamente anônimos.
**Contorno:** revisar JSON/HTML e logs antes de compartilhar, enviar só o necessário e nunca publicar SMART bruto indiscriminadamente.
[PRIVACY.md](../PRIVACY.md) descreve a política e limitações; passagens históricas não comprovam todos os recursos posteriores.

## Prioridades sugeridas: maior ganho de confiança por esforço

1. **Validar um jogo real de ponta a ponta.** Com autorização do dono, usar Steam + Launcher + MangoHud;
   conferir PRIME, CSV, “Medido neste PC”, opções e restauração. Resolve várias lacunas com uma sessão dirigida.
   Guardar resolução, preset, driver, duração e comparação com contador; depois medir erro da estimativa.
2. **Testar correção de sensores e Desfazer na máquina disponível.** Dono acompanha a janela polkit;
   equipe registra antes/depois, RAPL em watts e restauração. Fecha uma lacuna real com escopo pequeno,
   sem confundir testes falsos com alteração de permissão bem-sucedida.
3. **Estabilizar o caminho gráfico.** Manter X11/XWayland como contorno, reunir reprodução Wayland
   para Avalonia e repetir N rodadas em candidato futuro e GNOME/KDE reais. Somar revisão manual de teclado,
   escala e contraste; não atualizar a biblioteca apenas porque uma versão maior existe.
4. **Fechar o ciclo do driver NVIDIA em ambiente recuperável.** Validar primeiro Mint/Ubuntu conhecido,
   com autorização, backup/plano de recuperação, senha, reboot e retorno ao nouveau. Conferir também a consulta
   online por ramo. Secure Boot e outras distros vêm depois, sem generalizar o primeiro sucesso.
5. **Ampliar hardware por empréstimo/testadores.** Priorizar AMD dedicada e NVIDIA RTX/desktop,
   depois Intel Arc; começar por leituras comparadas e cargas curtas monitoradas.
   Registrar versões e indisponibilidades. Deixar stress longo para ambiente adequado, nunca para automação comum.

Este arquivo é uma avaliação documental com evidências existentes e fatos atribuídos ao lead.
Nenhum build, teste de hardware, carga, tráfego, instalação ou publicação foi executado para produzi-lo.
