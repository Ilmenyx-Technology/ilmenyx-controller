# Estimar o FPS sem API paga ou fechada: opções e recomendação

Data: 2026-11-08. Para o mantenedor decidir. Contexto: a consulta à HowManyFPS foi **removida** (acesso à API exigia pedido
formal). A página Estimativa de FPS agora é 100 % local. Este documento compara formas de estimar o FPS médio por jogo a partir
do hardware do usuário, **sem** API paga ou com acesso restrito. O método atual está em [`fps-estimativa.md`](fps-estimativa.md).

> **Situação (2026-11-08, decisão do mantenedor): C, B e D IMPLEMENTADOS**, mais a trava de calibração e a correção dos jogos leves.
> - **(c) Calibração com os benchmarks do Ilmenyx — implementado:** botão CALIBRAR AGORA (cena de GPU de 15 s + GameSim de uma thread
>   de 10 s, para em 90 °C). Antes de calibrar, os cartões ficam "provisório — calibre o PC". Âncoras e limites em `fps-estimativa.md` §0.
> - **(b) Especificações — implementado** como fallback (`FpsModels.SpecGpuIndex`), só para GPUs com ID PCI no `GpuSpecDb`. §0.1.
> - **(d) Fator pessoal — implementado** depois de adicionar qualidade gráfica e upscaling ao perfil/sessão. §0.2.
> - **(a)** só a auditoria dos jogos leves (expoente de GPU, fontes Riot). **(e)** e **(f)**: não implementados.
> As seções abaixo são a análise original, mantida como registro.

> Aviso honesto: as faixas de precisão abaixo são **ordens de grandeza esperadas**, tiradas de como esses métodos costumam se
> comportar. **Nenhuma delas foi medida neste projeto** (não há um conjunto de sessões reais para medir o erro). Antes de prometer
> qualquer número ao usuário, é preciso medir (ver "Como medir se melhorou", no fim).
>
> Aviso jurídico: os comentários sobre licença e termos de uso são uma leitura técnica, **não parecer jurídico**. Termos de sites
> mudam; conferir o texto vigente na data, e guardar uma cópia, antes de usar qualquer fonte.

## Ideia básica (para entender as opções)

FPS num jogo é limitado pela peça mais lenta naquele momento:

- **GPU** (placa de vídeo): desenha a imagem. Mais resolução e gráficos mais altos = mais trabalho para ela.
- **CPU** (processador): prepara cada quadro (física, IA, chamadas de desenho). Não muda com a resolução. Em jogos competitivos com
  gráficos baixos, quase sempre é ela que limita.
- **Trava do jogo**: alguns jogos não passam de 60 FPS (ex.: Elden Ring).

O Ilmenyx já calcula `FPS ≈ min(teto da GPU, teto da CPU, trava do jogo)`. A pergunta deste documento é **de onde tirar números
melhores** para "teto da GPU" e "teto da CPU" sem depender de um serviço fechado.

## Resumo das opções

| | Opção | Precisão esperada (não medida) | Esforço | Risco de licença | Privacidade | Manutenção |
|---|---|---|---|---|---|---|
| a | Índices relativos × tabela de FPS por jogo (atual), calibrados melhor | ±20–35 % (desktop na tabela); pior em notebook/integrada | Baixo–médio | **Médio** se copiar tabelas de sites; baixo com dados próprios/fabricante | Ótima (local) | Alta: GPUs e jogos novos todo ano |
| b | Modelo físico pelas especificações (shaders × clock, banda, VRAM, arquitetura) | ±30–50 % entre arquiteturas; melhor dentro da mesma família | Médio | Baixo (especificações são fatos; fonte: fabricante, driver) | Ótima | Média: um fator por arquitetura nova |
| c | Calibrar com o benchmark de GPU do próprio Ilmenyx (cena Vulkan) | ±25–40 % (uma cena sintética ≠ todos os jogos) | Médio | **Nenhum** (dados próprios) | Ótima | Baixa |
| d | Fator pessoal a partir das sessões "Medido neste PC" | ±10–20 % em jogos parecidos, **depois** de 2–3 sessões | Baixo | Nenhum | Ótima (nada sai do PC) | Baixa |
| e | Base comunitária opcional (servidor da Ilmenyx Technology, anônima, com consentimento) | Potencialmente ±10–15 % para combinações populares | **Alto** | Baixo (dados próprios, com termo) | **Exige LGPD** | Alta (servidor, moderação, abuso) |
| f | Steam / ProtonDB | Não dá FPS | — | ProtonDB: ODbL | — | — |

## (a) Método atual, calibrado melhor

**Como é hoje.** `GameCatalog.cs` tem dois índices "a olho" (RTX 4070 = 100 para GPU, Ryzen 7 7700X = 100 para CPU) e, para cada
um dos 32 jogos, o FPS de referência nessas peças em 1080p/Alto (`GpuRef`) e o teto da CPU (`CpuCap`). Os fatores de resolução,
preset e upscaling são fixos para todos os jogos (1440p = ×0,72, 4K = ×0,42 etc.). Esses números são aproximados e **nunca
foram conferidos contra FPS real** neste projeto.

**Onde erra mais.**

1. Fator de resolução igual para todos os jogos: jogos leves ficam limitados pela CPU e quase não caem em 1440p; jogos pesados caem
   mais que ×0,72 em GPUs com pouca VRAM/banda.
2. Uma GPU "50 % mais lenta" não é 50 % mais lenta em todo jogo: arquiteturas diferentes (ex.: AMD × NVIDIA, Maxwell × RDNA 2)
   se comportam diferente por motor gráfico.
3. Notebooks: a mesma "RTX 3060 Laptop" vai de ~60 W a ~130 W conforme o fabricante. O nome não diz o limite de energia.
4. Integradas dependem da RAM (um ou dois canais, DDR4 × DDR5) mais do que do nome.

**Como calibrar melhor, e de quais fontes.**

| Fonte | O que dá | Pode usar? |
|---|---|---|
| Sites de review (TechPowerUp, Tom's Hardware, Notebookcheck, Gamers Nexus, Hardware Unboxed…) | FPS por jogo e GPU, índices relativos | **Ler e citar, sim; raspar (scraping) ou copiar tabelas inteiras, não.** Os termos desses sites em geral proíbem acesso automatizado e redistribuição; e uma tabela compilada pode ser protegida como base de dados (no Brasil, Lei 9.610/98, art. 7º, XIII, protege a seleção/organização de compilações). Um número isolado de uma review é um fato; tirar algumas dezenas à mão, com fonte citada, é bem menos arriscado que copiar a tabela. Confirmar os termos vigentes. |
| PassMark, 3DMark (UL), Geekbench | Notas relativas de GPU/CPU | Bases comerciais; os termos restringem reuso/redistribuição. **Não usar** sem licença escrita. |
| OpenBenchmarking.org (Phoronix Test Suite) | Resultados enviados por usuários, muitos no Linux, alguns jogos com FPS | Promissor por ser Linux, mas **verificar a licença dos resultados** antes; o Phoronix Test Suite em si é GPL. Usar só como referência de conferência até isso estar claro. |
| Wikipedia (listas de GPUs NVIDIA/AMD/Intel) | Shaders, clocks, barramento, banda | Especificações são fatos; o texto/tabela é CC BY-SA 4.0. Extrair os números (não a tabela formatada) e preferir a página do fabricante como fonte citada. |
| Páginas do fabricante (NVIDIA, AMD, Intel) | Especificações oficiais | Sim, como fatos citados. |
| **Medições próprias** (máquinas da equipe e testadores, sessões MangoHud) | FPS real, no Linux, com driver/Proton reais | **Sim, sem risco.** É a melhor fonte para o Ilmenyx: mede o que o usuário vai ver no Linux (Proton/DXVK costuma mudar o FPS em relação ao Windows das reviews). |

**Ajustes concretos para (a):**

- Fator de resolução **por jogo** (ou por "classe": competitivo leve × AAA pesado) em vez de um só.
- Guardar a **fonte e a data** de cada número da tabela (comentário no código ou arquivo de dados), para saber o que revisar.
- Substituir, aos poucos, números "a olho" por medições próprias no Linux (ver (c) e (d)).

## (b) Modelo físico pelas especificações

**Ideia.** Em vez de uma tabela de índices digitada à mão, estimar a força da GPU pelo que ela tem:

- **Poder de cálculo** ≈ nº de shaders × clock de boost × 2 (operações por ciclo) → TFLOPS.
- **Banda de memória** = largura do barramento × velocidade efetiva da memória. Em resoluções altas e em integradas, costuma limitar
  mais que os shaders.
- **ROPs × clock** (preenchimento de pixels): pesa em resoluções altas.
- **VRAM**: abaixo do que o jogo pede (ex.: 4 GB em jogo que quer 8 GB) o FPS despenca e engasga. É um corte, não uma escala.
- **Fator de arquitetura**: um TFLOPS de uma geração não vale o mesmo de outra. Ex.: a Ampere dobrou a contagem de FP32 por SM e
  quase não dobrou o FPS; RDNA 3 conta "dual issue". Sem esse fator, o erro entre famílias é grande.

**De onde vêm os dados no Ilmenyx.** O `GpuSpecDb.cs` já tem shaders, ROPs, TMUs, tipo de memória e clocks por ID PCI (poucas
GPUs ainda). Parte disso também pode ser lida do próprio PC: no `amdgpu`, o driver informa CUs ativos, clock máximo e largura do
barramento de VRAM; no NVIDIA proprietário, o `nvidia-smi`/NVML informa clocks máximos e VRAM; no Intel, o número de EUs vem da
consulta de topologia do i915/xe. (Conferir campo a campo em cada driver antes de usar; nouveau expõe pouco.)

**Fórmula sugerida (simples, ajustável):**

```
forçaGPU = fatorArq × min( TFLOPS / TFLOPS_ref , (banda / banda_ref) ^ p_res )
```

com `p_res` maior em resoluções altas, e um corte de VRAM. Os fatores de arquitetura seriam **ajustados** (regressão simples)
para que o modelo reproduza a tabela de índices atual nas GPUs que já estão nela; aí o modelo serve para as GPUs que **não** estão.

**Prós:** cobre GPUs novas/raras só com o ID PCI; especificações são fatos (risco de licença baixo); explica o "porquê" (ex.: "pouca
banda de memória"). **Contras:** erra mais entre arquiteturas e em notebooks (limite de energia não está no nome nem no ID); exige
manter o `GpuSpecDb` crescendo. **Uso recomendado:** como **fallback** quando a GPU não está na tabela, com confiança "muito baixa",
em vez de mostrar "SEM ESTIMATIVA".

**CPU pelo mesmo caminho.** O teto de FPS limitado pela CPU acompanha sobretudo o desempenho em **uma thread** (clock × IPC da
arquitetura) e o cache (os X3D mostram isso). O Ilmenyx já tem um benchmark de CPU single-thread (`CpuScore`): ver (c).

## (c) Calibrar com o benchmark do próprio Ilmenyx

**Ideia.** O Ilmenyx já roda uma cena Vulkan de jogo (`GpuScene`: terreno, 40 mil árvores, sombras, água, HDR, resolução fixa,
mesma câmera em todo PC) e um benchmark de CPU. Se soubermos o resultado dessa cena em várias GPUs de **índice conhecido**, dá para
converter a nota da cena de qualquer GPU em índice — inclusive de GPUs que não estão na tabela, de notebooks com limite de energia
baixo e de integradas com RAM em um canal, porque **mede a máquina real**.

**O que já existe de dado (das evidências da equipe, cena 1080p curta):**

| Máquina | GPU | Cena | Índice na tabela |
|---|---|---|---|
| Dell Inspiron 7460 | GeForce 940MX (proprietário 580) | 17,3 FPS (nota 172) | 4,5 |
| ASUS E1504FA | Radeon 610M (integrada) | 15,0 FPS (nota 150) | 3 |

Só dois pontos, e já mostram o valor do método: pela cena, a 940MX é ~1,15× a 610M; pela tabela, 1,5×. Não dá para dizer qual
está certo sem medir um jogo nas duas — é exatamente o tipo de divergência que a calibração resolveria.

**Como montar:**

1. Rodar a cena (sempre com a mesma resolução/duração) em todas as GPUs a que a equipe e os testadores tiverem acesso, anotando
   GPU, driver, energia (tomada) e temperatura.
2. Ajustar `índice ≈ k × nota^α` (reta em escala log-log) com as GPUs que também estão na tabela.
3. Para uma GPU fora da tabela: índice pela nota da cena. Para uma GPU **dentro** da tabela: usar a nota para corrigir notebooks/
   integradas (ex.: média geométrica entre tabela e cena), com confiança baixa.
4. O mesmo para CPU: nota single-thread do `CpuScore` → índice de CPU.

**Cuidados:** a cena exige consentimento (gera carga máxima e calor; os notebooks da equipe chegam a 90–100 °C); o resultado depende
do driver (nouveau × proprietário) e de estar na tomada. A cena é **uma** carga sintética: jogos com muito CPU, muito ray tracing
ou muita VRAM vão divergir. Licença: nenhum risco, são dados próprios. Privacidade: tudo local.

## (d) Aprender com as sessões medidas neste PC

**Ideia.** Quando o usuário joga pela página Jogos com MangoHud, o Ilmenyx guarda o FPS real ("Medido neste PC"). Comparando o
medido com o que a estimativa teria dito **para as mesmas configurações**, obtemos um **fator pessoal** (ex.: "este PC rende 0,8× do
que a tabela prevê") e aplicamos aos outros jogos — principalmente aos do mesmo tipo (mesmo motor/gênero, mesmo gargalo).

**Fórmula sugerida (prudente):**

```
razão_i  = FPS medido / FPS estimado nas mesmas configurações     (por sessão válida, ≥ 30 s)
fator    = exp( mediana(ln razão_i) × n/(n+2) )                    (puxa para 1 com poucas sessões)
fator    limitado a 0,5…2,0; aplicado só a jogos cujo gargalo estimado é o mesmo (GPU com GPU, CPU com CPU)
```

**Por que não implementei agora (a brief permitia, se fosse óbvio e seguro):**

1. A sessão guarda a resolução do perfil (quando definida), mas **não guarda o preset gráfico nem o upscaling** usados no jogo.
   Sem isso, a "estimativa nas mesmas configurações" é um chute, e o fator misturaria "o PC é mais lento" com "você jogou no Ultra".
2. O caminho completo de sessão (jogo + MangoHud + leitura ao fechar) **ainda não foi testado com um jogo real** (`Sem_teste.md`,
   FPS-02). Não há nenhuma sessão real para conferir se o fator ajuda ou atrapalha.
3. O erro do fator ficaria escondido dentro de uma faixa que parece mais confiável — o oposto do que o app promete.

**O que falta para ficar seguro:** (1) o usuário dizer (ou o perfil do jogo guardar) preset e upscaling da sessão; (2) pelo menos
uma sessão real validada; depois disso é pouco código e fácil de testar. Rótulo no cartão: "ESTIMATIVA AJUSTADA ÀS SUAS SESSÕES"
e, no texto, quantas sessões e de quais jogos.

**Privacidade:** nada sai do PC. **Licença:** nenhum risco. **Manutenção:** quase nenhuma.

## (e) Base comunitária opcional (servidor da Ilmenyx Technology)

**Ideia.** Usuários que aceitarem enviam, de forma anônima, o resumo das sessões: jogo, GPU, CPU, driver, distro, resolução,
preset, FPS médio e 1 % low. Com volume, a estimativa vira "mediana de quem tem um PC parecido".

**O que exige:**

- **Infraestrutura:** API de envio e de consulta, banco, backup, monitoramento, custo mensal; um novo host em `PRIVACY.md`.
- **LGPD:** mesmo "anônimo", hardware raro + horário + IP pode identificar alguém. Precisa: consentimento explícito e revogável
  (art. 7º, I), finalidade clara, não guardar IP (ou descartar logo), arredondar horários, permitir apagar o que foi enviado,
  política de retenção, um encarregado/contato, e um termo dizendo sob qual licença os dados agregados são publicados.
- **Abuso:** envios falsos (FPS absurdo, spam) — rejeitar fora da faixa física, exigir sessão com duração/amostras mínimas, limitar
  por instalação, usar mediana (resiste a valores falsos), revisar combinações com poucos envios. Não há como provar que um envio
  é verdadeiro; dá só para torná-lo caro e pouco influente.
- **Massa crítica:** com poucos usuários, a maioria das combinações terá zero ou um envio; até lá, não ajuda mais que (c)+(d).

**Prós:** com volume, é a fonte mais precisa e é **toda Linux** (Proton, drivers reais). **Contras:** maior esforço e
responsabilidade legal; só faz sentido com base de usuários grande. **Recomendação:** depois, se o app crescer; desenhar desde já
o formato da sessão local (com preset) para ser o mesmo que um dia seria enviado.

## (f) Steam e ProtonDB: o que existe e o que não existe

- **ProtonDB:** relatos dizem se o jogo **roda** (Platinum/Gold/…), com notas de texto e opções de inicialização. **Não há campo de
  FPS.** Os dumps públicos (repositório `bdefore/protondb-data`) são ODbL. Útil para compatibilidade (o Ilmenyx já usa), não para FPS.
- **Steam:** **não há API pública de FPS.** A Steam tem um overlay de desempenho, mas não publica esses números. O selo "Steam Deck
  Verificado" diz que o jogo é jogável no Deck, não quantos FPS. A **Pesquisa de Hardware da Steam** mostra quais GPUs/CPUs são
  mais comuns: serve para **priorizar** quais peças calibrar em (a)/(c), não para estimar FPS.
- **Requisitos mínimos/recomendados** das lojas: indicam "roda a ~30/60 FPS em 1080p" de forma vaga; podem ser um limite de
  sanidade (abaixo do mínimo → confiança muito baixa), não uma estimativa.

## Recomendação

**Agora (tudo local, sem risco de licença, pouco esforço):**

1. **Guardar preset e upscaling na sessão medida** (o usuário escolhe ao fechar o jogo, ou vem do perfil do jogo). Pré-requisito
   de (d) e de qualquer validação.
2. **Validar uma sessão real** (FPS-02) numa máquina da equipe, e medir o erro da faixa atual nesse jogo.
3. **(c) Tabela própria cena Vulkan → índice**: rodar a cena nas GPUs disponíveis (com o protocolo de temperatura), ajustar a reta,
   e usar a nota da cena **deste PC** quando a GPU não estiver na tabela (hoje aparece "SEM ESTIMATIVA") ou for notebook/integrada.
   Rótulo da fonte: "estimativa pela nota do benchmark deste PC".

**Em seguida:**

4. **(d) Fator pessoal** com encolhimento para 1, limitado e rotulado, assim que (1) e (2) existirem.
5. **(b) Fallback por especificações** para GPUs sem tabela e sem benchmark rodado, ajustado à tabela atual; confiança "muito baixa".
6. **(a) Revisar a tabela de jogos** com medições próprias no Linux e, quando preciso, poucos números citados de reviews (à mão,
   com fonte e data). **Nunca raspar** sites de benchmark.

**Depois (só se houver base de usuários):** 7. **(e)** base comunitária, com LGPD e antiabuso desenhados antes do primeiro envio.

## Precisão esperada e como mostrar a incerteza

O app já mostra **faixa + confiança** e nunca um número solto; isso deve continuar. Sugestão de regra, do mais ao menos confiável:

| Base da estimativa | Confiança | Faixa típica |
|---|---|---|
| Medido neste PC (mesmo jogo) | — (é medição) | número + 1 % low |
| Tabela + fator pessoal (≥ 3 sessões, mesmo gargalo) | média | ±12–20 % |
| Tabela, desktop, perto da referência (hoje) | média | ±15 % (hoje) |
| Tabela, notebook/integrada, ou pela nota do benchmark deste PC | baixa | ±25–35 % |
| Só especificações (b) | muito baixa | ±40–50 % |
| Nada conhecido | sem estimativa | — |

Os percentuais de "spread" de hoje (`FpsAccuracy`) são uma margem escolhida, **não** um intervalo estatístico. Quando houver
sessões reais suficientes (da equipe, ou de (d)), trocar por um intervalo medido: por exemplo, a faixa que contém 80 % dos erros
observados para aquele tipo de base. O texto da fonte deve sempre dizer **de onde veio** o número ("tabela", "benchmark deste PC",
"especificações", "ajustado às suas N sessões").

## Como medir se melhorou

Montar um pequeno conjunto de validação próprio: para cada máquina disponível, 3–5 jogos do catálogo, sessão MangoHud de alguns
minutos numa cena repetível, com resolução/preset/upscaling anotados. Para cada método, calcular o erro `ln(estimado/medido)` e
reportar a mediana e o percentil 80 do erro absoluto. Um método só entra no app se reduzir esse erro **e** se a faixa mostrada
contiver o valor medido na proporção prometida.
