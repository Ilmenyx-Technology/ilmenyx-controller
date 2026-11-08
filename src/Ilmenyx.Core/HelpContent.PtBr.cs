// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Texts of the welcome tour and of the Help Center in Brazilian Portuguese (reference set). Describe only what the app really does.
namespace Ilmenyx.Core;

public static partial class HelpContent
{
    private static HelpFaq Q(string id, string question, string answer) => new(id, question, answer);

    private static HelpSection S(string key, string group, string title, string purpose, string[] steps, params HelpFaq[] faq) => new(key, group, title, purpose, steps, faq);

    private static HelpLanguage PtBr() => new(DefaultLanguage,
        new Dictionary<string, string>
        {
            [HelpKeys.WelcomeWindow] = "Ilmenyx — Boas-vindas",
            [HelpKeys.WelcomeTitle] = "Bem-vindo ao Ilmenyx",
            [HelpKeys.WelcomeBody] = "O Ilmenyx mostra o que tem dentro do seu PC, como ele está agora e ajuda a jogar melhor no Linux. Quer um passeio rápido pelas principais telas?",
            [HelpKeys.WelcomeNote] = "O tour é opcional. Dá para fazer depois em Ajuda, no fim do menu lateral.",
            [HelpKeys.WelcomeStart] = "Fazer o tour (2 min)",
            [HelpKeys.WelcomeLater] = "Agora não",
            [HelpKeys.TourName] = "TOUR DO ILMENYX",
            [HelpKeys.TourCounter] = "Passo {0} de {1}",
            [HelpKeys.TourBack] = "Voltar",
            [HelpKeys.TourNext] = "Próximo",
            [HelpKeys.TourFinish] = "Concluir",
            [HelpKeys.TourSkip] = "Pular tour",
            [HelpKeys.TourKeys] = "Enter: próximo · Esc: sair",
            [HelpKeys.TourAccessible] = "Tour do Ilmenyx, passo {0} de {1}: {2}",
            [HelpKeys.HelpNav] = "Ajuda",
            [HelpKeys.HelpTitle] = "Central de ajuda",
            [HelpKeys.HelpSubtitle] = "Para que serve cada página, como usar em poucos passos e respostas para as dúvidas mais comuns",
            [HelpKeys.HelpRetour] = "Refazer o tour",
            [HelpKeys.HelpIndex] = "IR PARA",
            [HelpKeys.HelpPurpose] = "PARA QUE SERVE",
            [HelpKeys.HelpSteps] = "COMO USAR",
            [HelpKeys.HelpFaq] = "PERGUNTAS FREQUENTES",
            [HelpKeys.HelpOpenPage] = "Abrir a página",
            [HelpKeys.HelpContext] = "Ajuda sobre esta página",
            [HelpKeys.HelpSearchPrefix] = "Ajuda · ",
            [HelpKeys.HelpStepAccessible] = "Passo {0}: {1}",
        },
        [
            new("start", "COMEÇO"), new("diag", "DIAGNÓSTICO"), new("games", "JOGOS"), new("perf", "DESEMPENHO"),
            new("maker", "MAKER"), new("tools", "FERRAMENTAS"),
        ],
        [
            S("help", "start", "Primeiros passos",
                "Como se orientar no Ilmenyx: o menu, a busca, os botões do topo e onde achar ajuda.",
                [
                    "No menu lateral, as páginas ficam em grupos (Diagnóstico, Jogos, Desempenho, Maker e Ferramentas). Clique no nome do grupo para abrir ou fechar.",
                    "Na busca do topo, digite parte do nome de uma página ou de uma dúvida (ex.: temperatura) e escolha na lista.",
                    "Os botões redondos do topo trocam o tema (escuro ou claro) e pausam ou retomam as leituras ao vivo.",
                    "O botão \"?\" ao lado do título abre esta ajuda já na parte da página em que você está.",
                    "Para ver o passeio de novo, clique em Refazer o tour no topo desta página.",
                ],
                Q("privacy", "O Ilmenyx envia meus dados para algum lugar?", "Não há telemetria. A internet só é usada quando você clica em algo que precisa dela, como o teste de Rede ou as consultas online de Compatibilidade, FPS e Drivers."),
                Q("auto", "Algum teste pesado roda sozinho?", "Não. Benchmark, Stress test e a verificação de OC só começam depois do seu clique e de uma confirmação que diz o que vai acontecer."),
                Q("password", "Quando o Ilmenyx pede senha?", "Só em ações que mudam o sistema, como liberar os sensores da CPU ou instalar o driver NVIDIA. Quem pede é a janela do próprio sistema; o Ilmenyx nunca vê a senha.")),

            S("dash", "start", "Visão geral",
                "A primeira tela: um resumo ao vivo do PC, com o modelo do computador, a carga e a temperatura da CPU, a GPU, a memória, os componentes e a nota do último benchmark.",
                [
                    "Veja o cartão grande do topo: nome do PC, processador e as leituras de CPU, GPU e RAM agora.",
                    "No gráfico Atividade, escolha o que ver (Carga, Temperatura ou Consumo) e o período (1, 5 ou 15 minutos).",
                    "Clique num componente (CPU, memória, disco…) para abrir a página com os detalhes.",
                    "O cartão DESEMPENHO mostra a nota do último benchmark; ABRIR BENCHMARK leva ao teste.",
                    "Se aparecer o botão amarelo \"Liberar sensores da CPU\" no menu lateral, clique nele para ver a temperatura e o consumo da CPU.",
                ],
                Q("cputemp", "Por que a temperatura da CPU não aparece?", "Às vezes o Linux não carrega o módulo do sensor ou esconde a leitura de usuários comuns. Clique em \"Liberar sensores da CPU\" no menu lateral: o sistema pede a sua senha uma vez e dá para desfazer pelo mesmo botão. Em máquina virtual o sensor pode não existir."),
                Q("power", "Por que o botão Consumo está apagado?", "O consumo da CPU vem de um contador do kernel (RAPL) que muitas distribuições só deixam o administrador ler. O botão \"Liberar sensores da CPU\" resolve isso também. Alguns processadores não têm esse contador."),
                Q("dash", "O que quer dizer \"—\" no lugar de um número?", "Que aquela leitura não está disponível neste PC. Não é sinal de defeito: o Ilmenyx prefere mostrar \"—\" a inventar um valor."),
                Q("monitor", "As leituras ao vivo deixam o PC lento?", "Não. O monitor só lê arquivos do sistema uma vez por segundo. Se quiser, o botão de pausa no topo para as leituras.")),

            S("system", "diag", "Sistema",
                "Lista o hardware e o software encontrados: sistema, placa-mãe, processador, memória, placa de vídeo e armazenamento.",
                [
                    "Escolha uma categoria na lista (Sistema, Placa-mãe, Processador, Memória, Placa de vídeo ou Armazenamento).",
                    "Em Armazenamento, os anéis mostram quanto de cada disco está ocupado.",
                    "Clique em Atualizar para ler de novo, ou exporte em JSON ou relatório HTML.",
                    "Em Memória, Importar módulos de RAM lê um arquivo salvo do dmidecode para mostrar cada pente.",
                ],
                Q("ram", "Por que não aparecem os detalhes de cada pente de RAM?", "O Linux só mostra esses detalhes ao administrador. Rode no terminal o comando dmidecode indicado na página, salve a saída num arquivo e use Importar módulos de RAM. O Ilmenyx não roda esse comando e não mostra números de série.")),

            S("sensors", "diag", "Sensores",
                "Todas as temperaturas, ventoinhas, tensões e leituras de energia que o kernel oferece, do jeito que vêm.",
                [
                    "Clique em Atualizar sensores para ler tudo de novo.",
                    "Monitorar (2 s) atualiza sozinho a cada 2 segundos; Parar monitor encerra.",
                    "Mostrar texto bruto exibe os valores como o kernel informa, útil para técnicos.",
                    "Exportar sensores JSON ou HTML salva as leituras num arquivo.",
                ],
                Q("dup", "Por que alguns sensores aparecem repetidos?", "Alguns chips são lidos por mais de um driver do kernel. O Ilmenyx mostra cada fonte como ela é, sem somar nem esconder."),
                Q("hwmon", "O que é hwmon?", "É a parte do Linux que reúne os sensores da placa-mãe, da CPU, da GPU e dos discos. O Ilmenyx só lê esses dados; não muda nada.")),

            S("gpu", "diag", "GPU",
                "Especificações completas da placa de vídeo e sensores ao vivo (uso, clock, temperatura e memória) quando o driver informa.",
                [
                    "Se houver mais de uma placa de vídeo, escolha nos botões do topo.",
                    "Os cartões mostram fabricante, modelo, memória, driver e recursos como Vulkan e ray tracing.",
                    "A parte ao vivo atualiza sozinha enquanto a página está aberta.",
                ],
                Q("nocounters", "Por que a GPU não mostra uso ou temperatura?", "Nem todo driver informa esses contadores (por exemplo o nouveau ou algumas GPUs integradas). Nesse caso a página avisa em vez de mostrar um número inventado."),
                Q("notice", "O que significa o aviso sobre AMD e NVIDIA?", "Que o Ilmenyx ainda não foi testado em todas as placas: GPU AMD dedicada não foi testada e a NVIDIA foi validada só numa GeForce 940MX. Os dados vêm do driver, mas confira os resultados.")),

            S("disks", "diag", "Discos",
                "Saúde, desgaste e temperatura de cada disco, com um veredito: SAUDÁVEL, ATENÇÃO, CRÍTICO ou SEM DADOS.",
                [
                    "Veja o cartão de cada disco e o veredito dele.",
                    "Para dados mais completos, clique em Consultar SMART (somente leitura) no cartão do disco.",
                    "Se você já tem um relatório do smartctl salvo, importe-o em SMART · importação offline.",
                    "Clique em ATUALIZAR para ler de novo.",
                ],
                Q("verdict", "O veredito do disco é garantido?", "Não. É uma estimativa feita com os dados do próprio disco. Faça backup de qualquer disco com aviso, não só dos críticos."),
                Q("usb", "Por que o pendrive aparece como SEM DADOS?", "Pendrives e alguns HDs externos não informam a saúde ao sistema."),
                Q("smart", "O que é SMART?", "É o relatório que o próprio disco guarda sobre erros, horas de uso e desgaste. O Ilmenyx só lê; nunca escreve no disco nem roda testes nele.")),

            S("energy", "diag", "Energia",
                "Bateria (carga, saúde e ciclos), fonte de energia ligada e consumo lido pelos sensores do kernel.",
                [
                    "Abra a página: ela lê a bateria e a fonte na hora.",
                    "Clique em ATUALIZAR para ler de novo.",
                    "Num PC de mesa sem bateria, aparecem só a fonte e o consumo.",
                ],
                Q("psu", "O consumo mostrado é o da tomada?", "Não. É o que os sensores de potência do kernel informam (CPU, GPU…), não o da fonte (PSU) do PC."),
                Q("cycles", "Por que não aparecem os ciclos ou a capacidade original da bateria?", "Alguns notebooks não informam esses dados ao Linux.")),

            S("net", "diag", "Rede",
                "Mostra o tráfego da rede e faz um teste para descobrir se o lag vem do seu PC, do Wi-Fi ou do provedor.",
                [
                    "O gráfico mostra o tráfego dos últimos 5 minutos.",
                    "Clique em TESTAR e confirme. O teste usa a internet e leva cerca de 30 segundos.",
                    "Leia o resultado: tempo de resposta (ping), variação (jitter), perda e velocidade, com a explicação de onde está o problema.",
                    "CANCELAR interrompe o teste.",
                ],
                Q("data", "O teste de rede gasta internet?", "Sim, cerca de 100 MB (mais em conexões muito rápidas). Por isso ele só roda depois da sua confirmação."),
                Q("ping", "O que são ping e jitter?", "Ping é o tempo que um pacote leva para ir e voltar. Jitter é o quanto esse tempo varia; jitter alto causa travadinhas em jogos online.")),

            S("procs", "diag", "Processos",
                "Os programas que mais usam CPU e memória agora, a partir do monitor ao vivo.",
                [
                    "A lista atualiza sozinha a cada leitura.",
                    "Clique em CPU ou Memória, no topo da tabela, para ordenar.",
                    "Pause o monitor no topo da janela para congelar a lista.",
                ],
                Q("names", "O Ilmenyx guarda ou envia a lista de programas?", "Não. Os nomes só aparecem nesta tela e nunca são exportados.")),

            S("launcher", "games", "Jogos",
                "Junta os seus jogos num lugar só e abre cada um com um perfil para mais desempenho.",
                [
                    "Os jogos da Steam, do Heroic (Epic e GOG) e do Lutris aparecem sozinhos; ATUALIZAR LISTA procura de novo.",
                    "Para outro jogo, clique em + ADICIONAR EXECUTÁVEL e use PROCURAR… para escolher o arquivo: um programa do Linux ou um .exe de Windows.",
                    "Escolha o jogo e ajuste o perfil: GPU dedicada, MangoHud, GameMode, perfil de energia e argumentos. PERFIL PARA MAIS FPS liga as opções recomendadas.",
                    "Clique em JOGAR. Nos jogos da Steam, os argumentos só valem depois de APLICAR NAS OPÇÕES DA STEAM.",
                    "Tem só o instalador (setup.exe) de um jogo de Windows? Clique em INSTALAR JOGO DE WINDOWS, escolha o instalador e a pasta do jogo e siga as dicas da janela.",
                ],
                Q("winfirst", "Por que o jogo de Windows demora na primeira vez?", "Jogos .exe rodam com o Proton (a camada que faz jogos de Windows funcionarem no Linux), sem precisar da Steam. Na primeira vez o Proton é baixado (cerca de 1 GB) e a pasta do jogo é preparada, o que leva alguns minutos. Depois abre normalmente."),
                Q("wininstall", "Como instalo um jogo a partir do instalador do Windows?", "Na página Jogos, clique em INSTALAR JOGO DE WINDOWS. Escolha o instalador (.exe) e a pasta onde o jogo vai ficar; essa pasta aparece para o instalador como o disco D:, então escolha D: quando ele perguntar onde instalar. Desmarque DirectX, .NET e Visual C++: o Proton já cuida disso. O instalador roda uma vez num Windows descartável, apagado no fim; depois, clique em ADICIONAR ao lado do jogo encontrado para ele entrar na lista."),
                Q("guard", "O que é a proteção contra travamento em tela cheia?", "Em notebooks com duas placas de vídeo (Intel + NVIDIA), alguns jogos de Windows em tela cheia podem travar a tela inteira, e só reiniciando o PC ela volta. A opção \"Proteção contra travamento em tela cheia (vsync)\" do perfil faz o Proton sincronizar os quadros com o monitor, o que evita o travamento. Ela vem ligada, só aparece nesses notebooks e limita o FPS à taxa da tela; desmarque se quiser FPS sem limite."),
                Q("dgpu", "O que é GPU dedicada?", "Notebooks com duas placas de vídeo têm uma integrada, que economiza bateria, e uma dedicada, mais forte. A opção faz o jogo usar a dedicada (PRIME offload). Com uma única GPU não há o que escolher."),
                Q("mangohud", "O que é o MangoHud?", "Um overlay que mostra FPS, temperatura e uso por cima do jogo. Ao marcar a opção no perfil do jogo, o Ilmenyx cria sozinho a configuração dele. Jogando pelo menos 30 segundos com ele ligado, o FPS da sessão aparece em Estimativa de FPS como \"Medido neste PC\"."),
                Q("gamemode", "O que é o GameMode e por que a opção está apagada?", "O GameMode dá prioridade ao jogo enquanto ele roda. A opção fica apagada quando o programa gamemoderun não está instalado; instale o pacote gamemode pela sua distribuição."),
                Q("steamclose", "Por que a Steam fecha quando aplico as opções?", "A Steam regrava as opções ao sair; por isso precisa estar fechada para o Ilmenyx salvar. Antes de alterar, é feito um backup do arquivo."),
                Q("steamid", "Para que serve o ID Steam num jogo de Windows?", "É opcional. Com o número do jogo na loja (store.steampowered.com/app/ID, ou na página Compatibilidade), o Proton aplica as correções conhecidas para ele.")),

            S("compat", "games", "Compatibilidade",
                "Diz se um jogo roda bem no Linux: nota do ProtonDB, selo do Steam Deck, anti-cheat e o que funcionou para quem tem hardware parecido com o seu.",
                [
                    "Escolha um jogo da sua Steam ou busque pelo nome ou pelo ID da Steam.",
                    "BAIXAR BASE DE RELATOS traz os relatos de outros jogadores para o PC.",
                    "CONSULTAR ONLINE busca os dados mais recentes, só quando você clica.",
                    "Copie as opções de inicialização sugeridas ou aplique na Steam; Desfazer volta ao que era.",
                ],
                Q("guarantee", "A nota garante que o jogo vai rodar?", "Não. Ela conta relatos de outros jogadores, ordenados por semelhança de hardware: é um ponto de partida para testar, não uma garantia."),
                Q("anticheat", "O que é anti-cheat?", "É o programa que alguns jogos online usam contra trapaças. Alguns não aceitam Linux, e aí o modo online pode não abrir mesmo que o jogo rode bem.")),

            S("fps", "games", "Estimativa de FPS",
                "Mostra quantos FPS (quadros por segundo) esperar em cada jogo neste PC, como uma faixa com o nível de confiança.",
                [
                    "Escolha resolução, qualidade e upscaling para refazer a conta.",
                    "Role a página: ao chegar em Jogos do catálogo, o título e a busca ficam fixos no alto e só a lista de jogos rola. A seta redonda no canto inferior direito volta ao topo.",
                    "Leia a faixa e a confiança (média, baixa ou muito baixa).",
                    "Jogos abertos pela página Jogos com MangoHud ligado mostram \"Medido neste PC\": o FPS real das suas sessões.",
                    "Opcional: com uma chave da HowManyFPS, CONSULTAR API traz o FPS medido por outros jogadores (usa a internet só ao clicar).",
                ],
                Q("range", "Por que aparece uma faixa e não um número exato?", "Porque é uma estimativa calculada a partir do desempenho relativo da sua GPU e CPU, não uma medição. Cena, driver e configurações mudam o resultado."),
                Q("none", "Por que aparece SEM ESTIMATIVA?", "Quando a sua GPU ou CPU não está na tabela de referência, o Ilmenyx não calcula: seria um número inventado."),
                Q("real", "Como vejo o FPS real do meu PC?", "Abra o jogo pela página Jogos com MangoHud ligado e jogue pelo menos 30 segundos. Ao fechar, a sessão aparece aqui como \"Medido neste PC\".")),

            S("bottleneck", "games", "Detector de gargalo",
                "Descobre o que está limitando o seu FPS: GPU, CPU, memória ou temperatura.",
                [
                    "Deixe a página aberta: o veredito e os medidores ao vivo atualizam sozinhos.",
                    "Para uma resposta melhor, clique em INICIAR ANÁLISE, jogue alguns minutos e clique em PARAR ANÁLISE.",
                    "Leia o resultado: quanto tempo cada parte limitou e dicas do que fazer.",
                ],
                Q("gargalo", "O que é gargalo?", "É a peça que trava o desempenho das outras. Se a GPU está sempre no máximo e a CPU sobra, a GPU é o gargalo, e vice-versa."),
                Q("fpsneeded", "Preciso abrir o jogo pelo Ilmenyx?", "Para os medidores, não. Para o FPS entrar na análise, o jogo precisa ter sido aberto com MangoHud (página Jogos).")),

            S("bench", "perf", "Benchmark",
                "Mede o desempenho de CPU, memória, GameSim (uma simulação de jogo), GPU, ray tracing e disco, e guarda o histórico neste PC.",
                [
                    "Escolha o teste.",
                    "Clique em INICIAR e leia a confirmação: ela diz quanta carga, memória ou espaço o teste vai usar.",
                    "Espere terminar (dá para cancelar a qualquer momento).",
                    "O resultado vai para o histórico local e para o cartão DESEMPENHO da Visão geral.",
                ],
                Q("safe", "O benchmark é seguro?", "Sim. Nada começa sem a sua confirmação, há limite de temperatura e o teste de disco usa só um arquivo temporário, apagado no final."),
                Q("windows", "Posso comparar a nota com a do Windows?", "Não. As notas do Ilmenyx são experimentais e não são comparáveis às do Windows."),
                Q("vary", "Por que o resultado variou entre uma rodada e outra?", "Programas em segundo plano, temperatura ou limite de energia atrapalham. Feche outros programas e rode de novo antes de comparar.")),

            S("oc", "perf", "OC",
                "Ajuda com overclock sem mexer no hardware: verificador de estabilidade, ajustes da GPU NVIDIA por comandos e um guia para a BIOS.",
                [
                    "Para ver se o PC está estável, escolha a duração e clique em INICIAR VERIFICAÇÃO (com confirmação).",
                    "A verificação mede a nota, aplica carga e confere o log do kernel; para no primeiro erro ou no limite de temperatura.",
                    "Em GPU (só NVIDIA com driver oficial), GERAR COMANDOS cria os comandos de ajuste para você rodar no terminal; COMANDOS DE RESTAURAÇÃO desfaz.",
                    "Leia o guia de BIOS se quiser fazer overclock por lá.",
                ],
                Q("auto", "O Ilmenyx faz overclock sozinho?", "Não. Ele nunca altera hardware nem BIOS; só gera comandos que você decide rodar."),
                Q("what", "O que é overclock?", "É fazer uma peça trabalhar acima da velocidade de fábrica. Ganha desempenho, mas esquenta mais e pode causar travamentos; por isso existe o verificador de estabilidade.")),

            S("overlay", "perf", "Overlay",
                "Mostra métricas como FPS, CPU, GPU e temperatura por cima da tela.",
                [
                    "Escolha os itens, o canto e o tamanho.",
                    "MOSTRAR OVERLAY abre uma janela pequena do Ilmenyx com as métricas do sistema.",
                    "Para ver dentro do jogo, marque MangoHud no perfil do jogo (página Jogos): a configuração é criada sozinha. O que você muda aqui é salvo sozinho (aparece \"Salvo\") e vale também para o MangoHud.",
                    "Se você abre o jogo direto pela Steam, copie a linha de opção de inicialização mostrada nesta página.",
                ],
                Q("fullscreen", "Por que o overlay não fica por cima do jogo em tela cheia?", "Isso depende do gerenciador de janelas (no Wayland, quem decide é o compositor). Dentro do jogo, use o MangoHud."),
                Q("nofps", "Por que o FPS não aparece no overlay?", "O FPS só existe para jogos iniciados com o MangoHud e a configuração do Ilmenyx: marque MangoHud no perfil do jogo na página Jogos, ou copie a opção de inicialização desta página na Steam.")),

            S("validate", "perf", "Validações reduzidas",
                "Testes curtos que conferem se os cálculos de CPU, memória e GameSim dão o resultado esperado. Não medem desempenho nem saúde.",
                [
                    "Escolha CPU, Memória ou GameSim e clique para executar.",
                    "Espere alguns segundos pelo resultado.",
                    "Exporte em JSON ou HTML se precisar enviar para alguém.",
                ],
                Q("diff", "Qual a diferença para o Benchmark?", "A validação só confere se o cálculo está certo, em poucos segundos e com pouca carga. O Benchmark mede a velocidade.")),

            S("stress", "perf", "Stress test",
                "Coloca CPU, GPU e/ou RAM em carga máxima para ver se o PC aguenta, com corte automático por temperatura.",
                [
                    "Marque CPU, GPU e/ou RAM.",
                    "Escolha a duração (0 = sem limite) e o limite de temperatura (de 50 a 105 °C).",
                    "Salve o seu trabalho, clique em INICIAR e confirme.",
                    "Acompanhe a temperatura e os erros: o teste para sozinho se passar do limite, ou quando você mandar parar.",
                ],
                Q("safe", "É seguro rodar o stress test?", "Sim, com cuidado: ele só começa depois da sua confirmação, para sozinho quando a temperatura passa do limite escolhido e sempre deixa pelo menos 1 GiB de RAM livre. O PC pode ficar lento durante o teste, por isso salve o seu trabalho antes."),
                Q("why", "Para que serve um stress test?", "Para ver se o PC aguenta carga pesada sem travar, desligar ou esquentar demais, por exemplo depois de montar o PC, trocar a pasta térmica ou fazer overclock.")),

            S("makerports", "maker", "Maker · Portas",
                "Lista as portas seriais USB (Arduino, ESP32 e outras placas) sem abrir nenhuma.",
                [
                    "Conecte a placa no USB.",
                    "Clique em ATUALIZAR para ver a porta (ex.: /dev/ttyUSB0) e o chip.",
                    "Se o seu usuário não tiver acesso à porta, a página mostra o comando sugerido para você executar.",
                ],
                Q("open", "Por que o Ilmenyx não abre a porta?", "Abrir a porta pode reiniciar placas como Arduino e ESP. O Ilmenyx só lê as informações do sistema.")),

            S("makerdrv", "maker", "Maker · Drivers seriais",
                "Mostra se os drivers dos chips USB-serial mais comuns estão carregados e leva à página oficial de cada um.",
                [
                    "Clique em VERIFICAR para conferir os chips.",
                    "Veja o estado de cada família de chip.",
                    "Use ABRIR PÁGINA OFICIAL quando precisar do driver do fabricante.",
                ]),

            S("optimize", "tools", "Otimiza",
                "Prepara o PC para jogar: fecha programas que atrapalham e liga ajustes do sistema, tudo sem senha e reversível.",
                [
                    "Em PROGRAMAS PARA FECHAR, marque o que quer fechar (indexadores de arquivos, sincronizadores…).",
                    "Clique em OTIMIZA: os marcados fecham como se você fechasse a janela, sem nada forçado.",
                    "Em AJUSTES DO SISTEMA PARA JOGOS, ligue o que quiser, como o perfil de energia Alto desempenho ou desligar a aceleração do mouse.",
                    "RESTAURAR ORIGINAIS volta cada ajuste ao que era antes.",
                ],
                Q("undo", "Dá para desfazer o Otimiza?", "Sim. O Ilmenyx guarda o valor original de cada ajuste e RESTAURAR ORIGINAIS devolve tudo. Os programas fechados podem ser abertos de novo normalmente."),
                Q("root", "O Otimiza precisa de senha?", "Não. Só aparecem ajustes que o seu próprio usuário pode fazer.")),

            S("lighting", "tools", "Integração",
                "Controla as luzes RGB de placa-mãe, memórias, ventoinhas, teclados e mouses pelo OpenRGB, que acompanha o Ilmenyx.",
                [
                    "Clique em INICIAR SERVIDOR OPENRGB (ou use o seu OpenRGB com o servidor ligado).",
                    "Clique em CONECTAR AO OPENRGB para listar os dispositivos.",
                    "Escolha a cor, o efeito e a velocidade, marque os dispositivos e clique em ATIVAR CONTROLE (com confirmação).",
                    "DEVOLVER AO ORIGINAL, ou fechar o app, volta cada dispositivo ao modo que estava.",
                ],
                Q("local", "O OpenRGB acessa a internet?", "Não. O servidor iniciado pelo Ilmenyx só aceita conexões do próprio PC (127.0.0.1) e é fechado quando você sai do app.")),

            S("startup", "tools", "Inicialização",
                "Lista os programas que abrem junto com a sua sessão para você desativar ou remover o que não precisa.",
                [
                    "Clique em ATUALIZAR para ver a lista.",
                    "Desligue o interruptor de um programa para ele não abrir mais com a sessão (dá para religar).",
                    "REMOVER tira um programa seu da lista; RESTAURAR REMOVIDOS traz de volta.",
                ],
                Q("uninstall", "Remover da inicialização desinstala o programa?", "Não. O programa continua instalado; só deixa de abrir sozinho. A mudança vale na próxima vez que a sessão iniciar.")),

            S("apps", "tools", "Instalar apps",
                "Instala de uma vez os aplicativos essenciais depois de instalar o sistema, pelo Flathub, só para o seu usuário e sem senha.",
                [
                    "Se for pedido, clique em ADICIONAR FLATHUB.",
                    "Marque os apps que quer (MARCAR TODOS e DESMARCAR ajudam).",
                    "Clique em INSTALAR SELECIONADOS; PARAR interrompe.",
                ],
                Q("flatpak", "O que são Flatpak e Flathub?", "Flatpak é um jeito de instalar apps que funciona em qualquer distribuição Linux; o Flathub é a loja principal desses apps. Se o Flatpak não estiver instalado, o Ilmenyx mostra o comando da sua distribuição.")),

            S("driver", "tools", "Drivers",
                "Mostra os drivers em uso (kernel e módulos), a versão do driver NVIDIA, o suporte do fabricante e atualizações de firmware. Esta página não instala nada.",
                [
                    "Clique em VERIFICAR TUDO.",
                    "VERIFICAR VERSÃO MAIS RECENTE consulta a internet só quando você clica.",
                    "PROCURAR FIRMWARE (fwupd) mostra se há atualização de firmware para as suas peças.",
                    "Para instalar o driver NVIDIA, use a página Driver NVIDIA.",
                ],
                Q("amd", "Preciso instalar driver para Intel ou AMD?", "Normalmente não: no Linux, o driver delas já vem com o kernel e o Mesa da sua distribuição.")),

            S("nvdriver", "tools", "Driver NVIDIA",
                "Instala, verifica, repara e atualiza o driver NVIDIA certo para a sua placa e o seu sistema.",
                [
                    "Clique em VERIFICAR para analisar a placa e o driver atual.",
                    "Veja o plano sugerido; VER O SCRIPT mostra exatamente o que será feito.",
                    "Confirme: o sistema pede a sua senha na janela dele.",
                    "Quando pedido, reinicie o PC (COMO REINICIAR explica) e abra esta página de novo para conferir.",
                ],
                Q("safe", "Esta página muda algo sem eu pedir?", "Não. Tudo é simulado antes e nada muda sem o seu clique e a senha do sistema. O Ilmenyx também não reinicia o PC sozinho.")),

            S("report", "tools", "Relatório",
                "Junta num único arquivo tudo o que o Ilmenyx lê da máquina, para guardar ou enviar a um técnico.",
                [
                    "Clique em EXPORTAR HTML (para ler) ou EXPORTAR JSON (para programas).",
                    "Escolha onde salvar: o arquivo é sempre novo, nunca sobrescreve outro.",
                    "Revise o arquivo antes de compartilhar.",
                ],
                Q("personal", "O relatório tem dados pessoais?", "Números de série, nome do computador e do usuário, endereços MAC e IP e caminhos da pasta pessoal são retirados. Mesmo assim, revise antes de compartilhar.")),

            S("blocked", "tools", "Bloqueados (!)",
                "Lista as funções que ainda não foram terminadas ou testadas e explica o porquê.",
                [
                    "Clique em (!) ao lado de um item para ver o motivo.",
                    "Se o menu mostrar \"p/ testar\", este PC tem o necessário para testar algum item bloqueado.",
                ]),

            S("tools", "tools", "Requisitos e sobre",
                "Confere os requisitos do sistema, mostra licenças e créditos e guarda algumas preferências.",
                [
                    "Requisitos verifica se o sistema tem o que o Ilmenyx precisa.",
                    "Licenças e créditos mostra a versão, as licenças e os créditos do projeto.",
                    "Painel clássico de diagnósticos abre a janela de diagnósticos antiga.",
                    "Marque ou desmarque o aviso de pendrive ou dispositivo USB conectado ou removido.",
                ]),
        ],
        [
            new("overview", "dash", ["dash"], "Visão geral",
                "Esta é a primeira tela: um resumo ao vivo do seu PC. O cartão grande mostra CPU, GPU e RAM agora, e o gráfico mostra a atividade dos últimos minutos. Um \"—\" quer dizer que a leitura não existe neste PC, não que algo está errado."),
            new("sensors", "dash", ["sensorfix"], "Liberar sensores",
                "Se aparecer o botão amarelo \"Liberar sensores da CPU\" aqui no menu, clique nele para ver a temperatura e o consumo da CPU. O sistema pede a sua senha uma vez (o Ilmenyx nunca a vê), e dá para desfazer depois."),
            new("diag", "system", ["system", "sensors", "gpu", "disks", "energy", "net", "procs"], "Diagnóstico",
                "Aqui ficam os detalhes do PC: Sistema (as peças), Sensores, GPU, Discos (saúde), Energia (bateria), Rede (teste de lag) e Processos (o que está usando a CPU). Estas páginas só leem: nada é alterado."),
            new("games", "launcher", ["launcher"], "Jogos",
                "Os jogos da Steam, do Heroic e do Lutris aparecem sozinhos; + ADICIONAR EXECUTÁVEL inclui outro, até um .exe de Windows (roda com o Proton), e INSTALAR JOGO DE WINDOWS roda o instalador de um jogo. No perfil de cada jogo dá para ligar GPU dedicada, MangoHud (FPS na tela) e GameMode."),
            new("compat", "compat", ["compat"], "Compatibilidade",
                "Antes de jogar, veja se o jogo roda bem no Linux: nota do ProtonDB, selo do Steam Deck e anti-cheat. Também mostra o que funcionou para quem tem hardware parecido com o seu."),
            new("fps", "fps", ["fps"], "Estimativa de FPS",
                "Mostra uma faixa de FPS esperada para cada jogo, com o nível de confiança: é uma estimativa, não uma medição. Jogos que você abriu pela página Jogos com MangoHud mostram \"Medido neste PC\", o FPS real das suas sessões."),
            new("perf", "bench", ["bench", "oc", "stress"], "Benchmark, OC e Stress",
                "Benchmark mede o desempenho, OC confere a estabilidade e Stress test põe o PC em carga máxima. Nenhum teste começa sem a sua confirmação, e eles param sozinhos se a temperatura passar do limite."),
            new("overlay", "overlay", ["overlay"], "Overlay",
                "Mostra FPS, temperatura e uso por cima da tela. Escolha os itens e o canto (salvos sozinhos); dentro do jogo, quem desenha é o MangoHud."),
            new("tools", "optimize", ["optimize", "driver", "nvdriver", "apps"], "Ferramentas",
                "Otimiza fecha programas que atrapalham e liga ajustes para jogos; RESTAURAR ORIGINAIS desfaz tudo. Drivers mostra os drivers em uso e Driver NVIDIA instala o certo para a sua placa. Instalar apps instala os essenciais pelo Flathub."),
            new("help", "help", ["search", "theme", "pagehelp", "help"], "Busca, tema e ajuda",
                "Use a busca do topo para achar uma página ou uma dúvida (ex.: temperatura), e o botão redondo para trocar o tema. O botão \"?\" ao lado do título abre a ajuda da página atual. Esta Central de ajuda fica no fim do menu e tem o botão Refazer o tour."),
        ]);
}
