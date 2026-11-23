# Meu PC roda isso? — metodologia e limites

Página **Meu PC roda isso?** (grupo JOGOS), no estilo do Can You Run It. Ela substitui a antiga Estimativa de FPS e a calibração deste PC,
removidas em 2026-11-23 (o método antigo está no histórico do git e em `fps-estimativa-metodos.md`, marcado como obsoleto).

**O que faz:** compara os requisitos mínimos e recomendados que o desenvolvedor publicou na Steam com o hardware lido neste PC e diz, item por item,
o que atende. **O que não faz:** não mede nada, não roda o jogo e **não estima FPS**. O FPS real vem só da prática, pela biblioteca (MangoHud).

Código: `src/Ilmenyx.Core/GameRequirements.cs` (leitor do texto), `CanRunCompare.cs` (comparação), `SteamRequirementsClient.cs` (rede e cache),
`GameCatalog.cs` (tabelas de desempenho relativo) e `src/Ilmenyx.Desktop/CanRunPage.cs` (tela).

## Fonte e privacidade

- Busca por nome: `store.steampowered.com/api/storesearch` (o `SteamAppLookup` já usado pela biblioteca, com o "Você quis dizer?").
  Também aceita AppID ou link `store.steampowered.com/app/<id>/...` (o AppID é extraído localmente, sem consulta de nome).
- Requisitos: `store.steampowered.com/api/appdetails?appids=<id>&l=brazilian`, campos `pc_requirements` e `linux_requirements`.
- **Só o nome ou o AppID vai para a Steam**, e só quando o usuário clica em Buscar ou aperta Enter. Nada do hardware é enviado.
- Limites da requisição: um host fixo, sem seguir redirecionamentos, timeout de 10 s, resposta de até 2 MB (acima disso é descartada), AppID só
  com dígitos (até 10). O HTML da resposta vira texto puro; nunca é renderizado.
- Cache local em `~/.cache/ilmenyx/compat/requirements/<appid>.json` (a mesma pasta base do cache de conquistas e capas). Um jogo já consultado abre
  sem rede (por nome, AppID ou pelos atalhos "Consultados antes"). A cópia vale 30 dias para a busca; **Atualizar da Steam** força nova consulta.
  Sem rede, a cópia salva é mostrada mesmo vencida.
- Os atalhos "Jogos da sua biblioteca Steam" vêm da leitura local da biblioteca (somente leitura) e só preenchem a busca; consultar continua sendo um clique.

## Leitura do texto (parser puro, pt-BR e inglês)

O texto da Steam vira linhas (tags removidas, entidades decodificadas, título "Mínimos:/Minimum:" descartado). Cada linha `Rótulo: valor` é
classificada pelo rótulo, sem acento e sem caixa (SO/OS, Processador/Processor, Memória/Memory, Placa de vídeo/Graphics, DirectX,
Armazenamento/Storage e variantes):

| Item | Regra |
|---|---|
| Processador, placa de vídeo | O valor é separado em alternativas por `ou`, `or`, `/`, `;` e vírgula seguida de nome de fabricante. Qualquer alternativa serve. Sobras são limpas: `®`, `™`, `:` no começo, `Ghz` com caixa qualquer, e as caudas `/ better`, `or better`, `ou superior`, `ou melhor`, `or equivalent`, `ou equivalente`. "Intel or AMD" sem modelo e "4 cores / 8 threads" ficam como uma alternativa só. |
| RAM, armazenamento | Primeiro número com unidade (`MB`, `GB`, `TB`; vírgula decimal aceita). Sem rótulo, procura "N GB de RAM" e "N GB de espaço" no texto todo. |
| VRAM | O menor número com unidade na linha da placa de vídeo (a alternativa mais fraca). O tamanho é tirado do nome do modelo antes da busca na tabela. |
| DirectX, Vulkan, OpenGL | Procurados em qualquer linha (`DirectX 11`, `Versão 12`, `Vulkan 1.3`, `OpenGL 4.5`). |
| SO | Texto como está; só informativo. |

O que não é reconhecido (linha sem rótulo, observações, rede, som) fica em **"Não deu para comparar"**, com o texto original, e nunca entra no resumo.

## Comparação

| Item | Como compara |
|---|---|
| RAM | Total lido pelo sistema contra o pedido, com tolerância de 90% (o sistema lê um pouco menos que o instalado). |
| Armazenamento | Espaço livre do disco da pasta pessoal contra o pedido; a página avisa que não é necessariamente o disco de instalação. |
| VRAM | Só quando o driver informa e a GPU não é integrada (a VRAM de iGPU não é deduzida), tolerância de 95%. |
| Processador, placa de vídeo | **Índice de desempenho relativo em jogos** (RTX 4070 = 100; Ryzen 7 7700X = 100), calculado em três níveis de confiança (seção abaixo). O PC atende se o índice dele alcança o da alternativa mais fraca **avaliável**. Abaixo disso, só vale "abaixo do mínimo" se **todas** as alternativas foram avaliadas e a distância passa da margem do nível de confiança; senão é "não deu para comparar", nunca "abaixo" sem dados. |
| Casamento de nomes | Âncoras (`GameCatalog`): a chave precisa estar inteira ("1600" não casa em "i5-11600K"), sem sufixo que muda o chip (Ti, SUPER, XT, Laptop, Mobile; em CPU também H/U/P/Y/M/T e X3D). Tabela de especificações: o nome normalizado (sem fabricante, memória, "or better"/"ou superior"; "GTX1060" = "GTX 1060") tem de ser igual à chave; variantes têm chave própria e nunca são fundidas. Nome genérico de iGPU ("Radeon Graphics") nunca casa pelo nome. |
| API gráfica | Só informativa: DirectX 9 a 11 rodam no Linux pelo DXVK e DirectX 12 pelo VKD3D-Proton, ambos exigem Vulkan (1.3 nas versões atuais). A página diz se a biblioteca Vulkan está instalada; a versão que o driver anuncia não é lida aqui. |
| Sistema | Só informativo: pedido de Windows mostra que o jogo roda pelo Proton, sem garantia. |

Resultado por item: **atende o recomendado**, **atende o mínimo**, **abaixo do mínimo** ou **não deu para comparar**. Quando a lista recomendada
não cita um item, ele não pede mais que o mínimo.

Resumo geral (só os itens numéricos e indexados: processador, placa de vídeo, VRAM, RAM, armazenamento):

- **Abaixo do mínimo em: …** se algum item ficou abaixo do mínimo.
- **Sem dados suficientes** se menos de dois itens puderam ser comparados.
- **Roda no recomendado** se todos os itens comparados atendem o recomendado e há lista recomendada.
- **Roda no mínimo** nos demais casos.

Itens não comparados aparecem contados ("N item(ns) não deu para comparar"); eles não puxam o resumo para cima nem para baixo.

## Índices por especificação (CPU e GPU)

Base própria, calculada de números de especificação do fabricante (fatos técnicos); **nenhuma base proprietária** (PassMark, UserBenchmark, 3DMark) foi copiada.
Código: `src/Ilmenyx.Core/HardwareIndex.cs` (`GpuIndexer`, `CpuIndexer`). Níveis, do mais ao menos confiável:

1. **por modelo**: o modelo está nas tabelas de âncoras do `GameCatalog` (índices ajustados a pontos de benchmark);
2. **por especificação**: GPU pelo nome exato na tabela de especificações, ou CPU "família + GHz" que o próprio jogo informa (ex.: "Core 2 Duo 2.4 GHz");
3. **estimado por família**: GPU só pelo ID PCI de iGPU Intel (a contagem de EUs varia por CPU; usa a configuração típica da família), CPU pelo modelo→geração/linha
   (config típica da geração, sem o clock exato), "dual core 2.0 GHz" genérico, ou teto de família antiga.

**GPU.** `índice = unidades × 2 × clock de boost (GFLOPS FP32) × fator da arquitetura`. Tabela: **242 GPUs** (GeForce 8/9/200/400/500/600/700/900/10/16/20/30/40/50 incl. Ti/SUPER/Mobile
quando a especificação difere, Radeon HD 2000–7000, R7/R9 200/300, Fury, RX 400/500, Vega, Radeon VII, RX 5000/6000/7000/9000, iGPUs Radeon 610M–890M, Intel HD 2000–UHD 770, Iris Plus,
Iris Xe, Arc A/B), clock de sombreador para Tesla/Fermi, EUs×8 faixas para Intel Gen7+. **77 IDs PCI** inequívocos (`vendor:device`) casam o modelo quando o nome é genérico; o resto
cai no `GpuSpecDb` existente. Fatores (índice por GFLOPS), ajustados como média geométrica sobre as GPUs que estão nas duas fontes (âncoras) e erro médio absoluto por arquitetura:

| Arquitetura | Fator | Âncoras | Erro médio |
|---|---|---|---|
| Blackwell / Ada / Ampere | 3,32 / 3,19 / 3,32 | 6 / 9 / 9 | 11% / 8% / 13% (máx. 27%) |
| Turing | 6,03 | 11 | 7% |
| Pascal / Maxwell | 5,07 / 4,84 | 9 / 7 | 13% / 14% (máx. 34%) |
| RDNA 1 / 2 / 3 / 4 | 5,16 / 5,17 / 4,65 / 5,92 | 4 / 13 / 11 / 2 | 5% / 12% / 17% (iGPU 890M: 43%) / 8% |
| GCN 4–5 (Polaris, Vega) | 4,84 | 6 | 19% (RX 590: 38%; RX 550: 33%) |
| Arc / Battlemage | 3,0 / 4,17 | 4 / 1 | 13% / 0% |
| Intel Gen9 / Gen11 / Xe-LP | 3,58 / 3,11 / 4,07 | 5 / 1 / 2 | 19% / 0% / 18% |
| **Sem âncora (encadeados)** | GCN 1–3 4,0; Kepler 4,4; Fermi 5,0; Tesla 4,9; TeraScale 2,7; Intel Gen6/7 2,5 | 0 | **±20% a ±30%, não medido** |

Os fatores sem âncora vêm de pares de mesma classe conhecidos por comparativos públicos (GTX 680 ≈ GTX 960; GTX 780 Ti ≈ GTX 970; GTX 560 Ti ≈ GTX 650 Ti; HD 7970 ≈ GTX 960;
R9 290X ≈ GTX 970; HD 6870 ≈ GTX 560 Ti): são calibração por equivalência, não medição, e por isso o resultado fica "por especificação", com margem maior. O teste
`SpecFormulaStaysNearTheCatalogAnchors` trava o erro médio em 15% e o máximo em 45% sobre 23 âncoras.

**CPU.** `índice = 8,6 × IPC(microarquitetura, Skylake = 1) × GHz de boost × núcleos^0,2` (núcleos = P + metade dos E). O expoente e a constante foram ajustados em 54 CPUs de mesa
das âncoras (erro médio 7,6%; máx. 19% no 9950X; Skylake/Coffee/Comet 15%; Zen 1 a Raptor 2% a 9%). Notebook aplica um fator de classe sobre a peça de mesa da mesma geração/linha:
U 0,80, Y 0,45, P 0,70, H 0,91 (≤ 11ª) ou 0,86 (≥ 12ª), HX 0,95, G7 0,82; Ryzen U 0,62, H/HS 0,83, HX 0,93, G 0,92 (erro de ±10% a ±15% nas âncoras móveis). IPCs de Zen a LionCove e
Haswell conferem com as âncoras; Nehalem, Sandy, Ivy, Broadwell, Core 2, K8, K10, Bulldozer/Piledriver, NetBurst são razões de IPC publicadas e **não têm âncora**. Cobre Core 2 Duo/Quad,
Pentium 4/D/Dual-Core, Athlon 64/X2/II/X4, Phenom/Phenom II, FX, Core i3/i5/i7/i9 da 1ª à 14ª geração, Core Ultra (séries 1 e 2), Ryzen 1000 a 9000 e móveis. Requisito "família + GHz"
usa a mesma fórmula com o clock que o jogo informa; "N GHz" sem família usa o IPC de Core 2 (o mais antigo, para não superestimar o pedido). "4 cores / 8 threads" sem clock fica "não deu para comparar".

**Margens por confiança (o lado mais fraco decide).** Por modelo: atende com razão ≥ 1; abaixo se < 1. Por especificação: abaixo só com razão < 0,85. Estimado por família: abaixo só com razão < 0,6.
Entre a margem e 1, "não deu para comparar". **Família antiga sem modelo na tabela** (GeForce 8/9/200 teto 4, 400/500 e Radeon HD 2000–6000 teto 8): atende "estimado" se o PC tem ≥ 2× o teto da
família; nunca "abaixo". A página mostra o nível junto do status ("por modelo", "por especificação", "estimado por família") e explica na nota.

## Limites

- É uma comparação de **requisitos publicados**, não uma medição. O texto do desenvolvedor pode estar desatualizado ou ser impreciso.
- Os índices são aproximados. A fórmula de GPU ignora largura de banda de memória e cache (placas com barramento estreito, como RX 550, RX 6500 XT e MX, saem superestimadas);
  a de CPU ignora cache (X3D fica de fora) e usa o clock de boost típico da geração, não o do modelo exato. Notebook com limite de energia rende menos que a tabela.
- GPU de notebook só entra com especificação fixa (GTX 9xxM/10xx Mobile e as âncoras Laptop); RTX móveis variam por TGP e saem "não deu para comparar" fora das âncoras.
- iGPU Intel por ID PCI vale a configuração típica da família (ex.: Iris Xe = 80 EUs, 1,3 GHz); o modelo real pode ter 48 ou 96 EUs. O caso Iris Xe com nome conhecido usa a âncora do catálogo.
- CPU sem GHz nem modelo ("4 cores / 8 threads", "Core i5" só pela linha) é estimada ou fica sem comparação; CPU/GPU fora de x86/GeForce/Radeon/Intel (ARM, Apple, Loongson) não são avaliadas.
- Sem hardware real variado, a coerência dos índices foi medida só contra as âncoras (caso **CANRUN-02**, NÃO TESTADO).
- Compatibilidade com Linux e Proton **não é garantida**. A página sugere conferir o ProtonDB (só como texto; nada é acessado).
- Jogos com versão para Linux mostram um seletor entre os requisitos do Windows (Proton) e os do Linux nativo.
- Textos fora do padrão (uma única linha com tudo misturado, formatos antigos) podem render poucos itens; o texto original fica na página.

## Testes

`tests/Ilmenyx.Tests/CanRunTests.cs`: leitura em inglês e português, MB/GB/TB e vírgula, texto solto, HTML sempre como texto, AppID/link, JSON da Steam,
casamento estrito de modelos, `HardwareIndexTests.cs` (21 textos reais de requisito de CPU/GPU em pt-BR e inglês, índices por família, variantes Ti/SUPER, IDs PCI, margens, família antiga, erro do ajuste, o caso da Iris Xe com i5-1135G7), cada status e cada resumo, VRAM de iGPU, cliente com servidor falso (host fixo, redirecionamento, resposta grande,
offline, cancelamento) e cache. O smoke gráfico (`tools/Ilmenyx.UiSmoke`) usa serviços falsos. Nenhum teste usa rede ou hardware real; a busca real é
o caso **CANRUN-01** em `Sem_teste.md` (NÃO TESTADO).
