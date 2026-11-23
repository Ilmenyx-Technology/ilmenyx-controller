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
| Processador, placa de vídeo | O valor é separado em alternativas por `ou`, `or`, `/`, `;` e vírgula seguida de nome de fabricante. Qualquer alternativa serve. |
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
| Processador, placa de vídeo | **Índice de desempenho relativo em jogos** (RTX 4070 = 100; Ryzen 7 7700X = 100), tabelas aproximadas já existentes em `GameCatalog`. O PC atende se o índice dele é maior ou igual ao da alternativa mais fraca **conhecida**. Se ele fica abaixo e alguma alternativa pedida não está na tabela, o resultado é "não deu para comparar", nunca "abaixo". |
| Casamento de nomes | Estrito: a chave precisa estar inteira ("1600" não casa em "i5-11600K"), sem sufixo que muda o chip e a tabela não conhece (Ti, SUPER, XT, Laptop, Mobile), e nomes genéricos de iGPU ("Radeon Graphics") nunca casam. Sem entrada na tabela = "não deu para comparar"; nada é inventado. |
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

## Limites

- É uma comparação de **requisitos publicados**, não uma medição. O texto do desenvolvedor pode estar desatualizado ou ser impreciso.
- As tabelas de desempenho relativo são aproximadas e cobrem só parte dos modelos (principalmente 2016 em diante); CPUs e GPUs antigas ou raras
  saem como "não deu para comparar".
- Compara desempenho relativo, não clock, núcleos, cache ou memória em canal duplo. Notebook com limite de energia rende menos que a tabela.
- Compatibilidade com Linux e Proton **não é garantida**. A página sugere conferir o ProtonDB (só como texto; nada é acessado).
- Jogos com versão para Linux mostram um seletor entre os requisitos do Windows (Proton) e os do Linux nativo.
- Textos fora do padrão (uma única linha com tudo misturado, formatos antigos) podem render poucos itens; o texto original fica na página.

## Testes

`tests/Ilmenyx.Tests/CanRunTests.cs`: leitura em inglês e português, MB/GB/TB e vírgula, texto solto, HTML sempre como texto, AppID/link, JSON da Steam,
casamento estrito de modelos, cada status e cada resumo, VRAM de iGPU, cliente com servidor falso (host fixo, redirecionamento, resposta grande,
offline, cancelamento) e cache. O smoke gráfico (`tools/Ilmenyx.UiSmoke`) usa serviços falsos. Nenhum teste usa rede ou hardware real; a busca real é
o caso **CANRUN-01** em `Sem_teste.md` (NÃO TESTADO).
