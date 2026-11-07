# Relatório de licenciamento do Ilmenyx: 2026-11-07
<!-- REUSE-IgnoreStart -->

> **Aviso:** este relatório é uma orientação técnica e didática. Não é parecer jurídico e não substitui
> um advogado. Ao final digo em que pontos vale a pena pagar por uma revisão profissional.

Arquivos lidos: `LICENSE`, `LICENSE-ILMENYX.md`, `LICENSING.md`, `NOTICE.md`, `THIRD-PARTY-NOTICES.md`,
`PRIVACY.md`, `CONTRIBUTING.md`, `docs/legal/*`, `docs/governance/OWNERSHIP-AND-CONTRIBUTIONS.md`, as seções
de licença do `AGENTS.md` e do `README.md`, os scripts de empacotamento (`scripts/fetch-components.sh`,
`scripts/package-linux-x64.sh`, manifesto Flatpak, metainfo) e os cabeçalhos dos arquivos `.cs` em `src/`.

---

## Em uma frase

A ideia está juridicamente correta. Você pode fazer o que planeja: a parte MIT continua livre e o seu
código novo fica sob termos próprios, mas **ainda não dá para saber com segurança qual arquivo está sob
qual regra**. O texto da licença própria também precisa de acabamento antes de o repositório ficar público.

---

## Como está hoje (explicado simples)

Pense no Ilmenyx como uma casa feita com tijolos de dois tipos:

1. **Tijolos MIT (do RESET EXPLODE, do Gustavo).** A licença MIT é uma autorização permanente e
   irrevogável dada a *qualquer pessoa*: pode usar, copiar, modificar e **vender**, desde que mantenha o
   aviso de copyright e o texto da MIT. Nem você nem o Gustavo conseguem "desfazer" isso. Exemplo:
   amanhã alguém pode baixar o RESET EXPLODE original, fazer o próprio port para Linux e vender. Isso é
   legal, e a sua licença não impede.
2. **Tijolos seus (código novo do port: adaptadores Linux, páginas Avalonia, CLI, relatórios etc.).**
   Pela lei brasileira de software (Lei 9.609/98) e pela de direitos autorais (Lei 9.610/98), quem cria
   o código é o autor e decide como ele pode ser usado. E a lei manda interpretar de forma
   **restritiva** as autorizações de direito autoral (Lei 9.610, art. 4º): o que você não autorizou
   por escrito continua proibido. Por isso a técnica usada no `LICENSE-ILMENYX.md` ("não é concedida
   permissão para...") funciona.

Hoje os arquivos dizem o seguinte:

- `LICENSE` (raiz): MIT integral, com "Gustavo (GustavoReset)" e "Silex Audit contributors".
- `LICENSE-ILMENYX.md`: seus termos (uso livre, inclusive profissional; proibido vender, cobrar acesso,
  fazer produto pago com o código).
- `LICENSING.md`: um "mapa" que diz que os termos próprios valem para "contribuições próprias
  futuras identificadas".
- Cabeçalhos: cerca de 126 dos 192 arquivos `.cs` dizem "New Ilmenyx additions: see LICENSING.md".
  Alguns dizem "Adapted from RESET EXPLODE ... (MIT); see NOTICE.md". **Uns 50 não têm cabeçalho
  nenhum** (ex.: vários em `Ilmenyx.Contracts`, páginas Avalonia).
- `packaging/linux/*.metainfo.xml`: `project_license` = `MIT AND LicenseRef-Ilmenyx`. Está correto
  como ideia.

**Um detalhe importante a seu favor:** o repositório é privado e ninguém de fora recebeu cópias. A MIT
só "prende" o autor em relação a quem recebeu o código sob MIT. Se o código que você escreveu (inclusive
na fase "Silex Audit") nunca saiu da sua mão, você ainda pode escolher livremente a licença de **todo** o
seu código, desde o primeiro commit. Não precisa se limitar a "contribuições futuras". Isso só não vale
se você já entregou cópias a alguém sob MIT (decisão 2).

**Outro ponto:** a licença limita os *outros*, não você. Como titular, você pode vender, aceitar doações,
fazer versão paga ou licenciar a uma empresa quando quiser. A exceção é o código de outras pessoas: só
pode fazer isso com ele se elas autorizarem (veja o problema 11).

---

## O que está bom

- **MIT preservada byte a byte** e copyright do Gustavo mantido em `LICENSE`, `NOTICE.md` e na tela
  Sobre. É exatamente o que a MIT exige.
- **Aviso de "não oficial / sem endosso"** no README e no NOTICE. Isso evita confusão com o RESET.
- **Honestidade sobre não ser OSI.** O projeto não se diz "open source", o que evita propaganda enganosa.
- **A distinção principal está bem pensada**: cobrar pelo *seu serviço* usando a ferramenta (permitido)
  é diferente de vender *a ferramenta* (proibido). A seção 2 da licença explica isso bem.
- **Componentes de terceiros fixados por versão e SHA-256**, com o LICENSE de MangoHud e OpenRGB
  copiado para `licenses/components/`.
- **Dados de terceiros** (ProtonDB/ODbL, AreWeAntiCheatYet/MIT) com atribuição e sem redistribuir a base.
  SteamDB não é raspado. Muito bom.
- **Privacidade de verdade no código:** sem telemetria, rede só depois de confirmação, chave da
  HowManyFPS salva em arquivo 0600.

---

## Problemas e riscos

### 1. O `LICENSE` da raiz diz só "MIT"
- **O que é:** o GitHub lê o arquivo `LICENSE` e mostra o selo "MIT License" no topo do repositório.
  O seu `LICENSE-ILMENYX.md` não aparece ali.
- **Por que importa:** quem visitar o repositório vai entender que *tudo* é MIT. Se alguém vender o
  app e você reclamar, ele vai alegar boa-fé: "o próprio repositório dizia MIT". Isso enfraquece muito
  a sua posição.
- **Exemplo:** uma loja de informática vê o selo MIT, empacota o Ilmenyx num "kit diagnóstico" e vende.
- **Solução:** mover a MIT para `LICENSES/MIT.txt` (ou `LICENSE-MIT`) e deixar no `LICENSE` da raiz um
  texto curto: "Este repositório tem duas licenças: X para o código próprio e MIT para as partes
  derivadas do RESET EXPLODE; veja LICENSING.md". O texto da MIT continua intacto em outro arquivo,
  e isso é permitido.
- **Gravidade: Alta**

### 2. Não dá para saber qual arquivo está sob qual licença
- **O que é:** o `LICENSING.md` fala em "contribuições próprias **futuras**, **identificadas**,
  **legitimamente licenciáveis**" e diz que "demais arquivos anteriores não foram retroativamente
  restringidos". O `THIRD-PARTY-INVENTORY.md` ainda diz "Código novo: Intenção MIT". E cerca de 50
  arquivos não têm cabeçalho.
- **Por que importa:** uma licença só protege o que ela identifica com clareza. Diante de dúvida, o
  terceiro vai dizer que achou que era MIT.
- **Exemplo:** `src/Ilmenyx.Contracts/*.cs` não tem cabeçalho, e o `LICENSING.md` diz que arquivos
  anteriores não foram restringidos. Quem copiar esses arquivos para um produto pago tem um bom argumento.
- **Solução:** usar o padrão **SPDX/REUSE** (reuse.software): uma linha em cada arquivo, por exemplo
  - código só seu: `// SPDX-License-Identifier: LicenseRef-Ilmenyx-1.0`
  - arquivo adaptado do RESET: `// SPDX-License-Identifier: MIT AND LicenseRef-Ilmenyx-1.0`, junto
    com o copyright do Gustavo
  - mais uma linha `SPDX-FileCopyrightText: 2026 <seu nome>`.

  A ferramenta `reuse lint` confere se faltou algum arquivo. E troque "futuras" por uma regra simples:
  "todo arquivo marcado como LicenseRef-Ilmenyx".
- **Gravidade: Alta**

### 3. Arquivos adaptados do MIT são "mistos"
- **O que é:** em arquivos como `GeneralReport.cs` ("Adapted from RESET EXPLODE ReportBuilder.cs") há
  partes do Gustavo e partes suas. A MIT permite que você licencie **as suas modificações** sob os
  seus termos (ela autoriza "sublicense" e combinar com outro código). Só que a parte original continua
  MIT para todo mundo.
- **Por que importa:** na prática, quem quiser só a lógica original pode pegá-la direto do upstream, e
  você só consegue proteger o que acrescentou. Em uma briga, você teria que *provar* o que é seu.
- **Exemplo:** alguém copia `GeneralReport.cs` inteiro. Você só pode reclamar das linhas que escreveu,
  e o histórico do Git é a sua prova.
- **Solução:** marcar esses arquivos como `MIT AND LicenseRef-Ilmenyx-1.0` (veja o item 2), manter o
  histórico Git (não reescrever) e, se publicar, deixá-lo público. Aceite que essa proteção é parcial.
- **Gravidade: Média**

### 4. Termos vagos e difíceis de aplicar
- **O que é:** expressões como "monetização indireta", "cobrança indireta", "remuneração vinculada à
  disponibilização", "publicidade obrigatória", "uso instrumental" e "wrapper comercial" não são definidas.
- **Por que importa:** termo vago é interpretado caso a caso, e em caso de dúvida o juiz pode favorecer
  quem aderiu ao texto sem negociar. Pior: pessoas de boa-fé ficam com medo de usar o app.
- **Exemplos de dúvida real:**
  - um youtuber faz um vídeo monetizado ensinando a usar o Ilmenyx. Isso é "monetização indireta"?
  - um técnico instala o Ilmenyx no PC do cliente e cobra a visita. Isso é "cobrar pela distribuição"?
  - um site de downloads com anúncios hospeda o AppImage. Isso é "publicidade obrigatória"?
  - uma rede de assistências modifica o app **internamente** e não vende. Isso é permitido?
- **Solução:** escrever **definições** curtas e uma **lista de exemplos** dentro da própria licença
  (permitido / não permitido). As decisões 6 a 9 servem para isso.
- **Gravidade: Média**

### 5. Faltam peças comuns de uma licença
- **O que é:** o texto atual não tem:
  - **Definições** ("Software", "Material Próprio", "Uso Comercial", "Você").
  - **Rescisão:** o que acontece se alguém violar. Boa prática: a licença termina, mas volta a valer
    se a pessoa corrigir em 30 dias (o PolyForm usa 32 dias).
  - **Lei aplicável e foro:** "lei brasileira; foro da comarca de [sua cidade]". Contra consumidor o
    foro pode ser afastado, mas ajuda nos demais casos.
  - **Idioma:** se houver tradução em inglês, dizer qual versão prevalece.
  - **Versões futuras:** se você lançar a "versão 2.0" dos termos, quem baixou a 1.0 continua com a 1.0?
    O normal é dizer que sim.
  - **Patentes:** você provavelmente não tem patentes. Ainda assim, uma frase como "nenhuma licença de
    patente é concedida além do necessário para usar o software" evita discussão. Prioridade baixa.
  - **Contribuições:** sob quais termos entra o código de outras pessoas (veja o item 11).
  - **Atribuição:** existe na seção 4, mas sem dizer *como* (ex.: manter `NOTICE.md` e a tela Sobre).
- **Gravidade: Média**

### 6. Garantia e responsabilidade: texto curto para um app que mexe em drivers
- **O que é:** a seção 5 tem uma isenção de garantia breve. Só que o app oferece correções com root
  (driver NVIDIA via polkit), ajustes de overclock, iluminação e benchmarks de estresse.
- **Por que importa:** no Brasil, a cláusula "não respondo por nada" tem limites:
  - **CDC:** se houver relação de consumo, a cláusula que exonera o fornecedor é nula (arts. 25 e 51, I).
    Um app gratuito, sem nenhuma remuneração, *provavelmente* não forma relação de consumo (o CDC
    fala em serviço "mediante remuneração"). Mas doações, anúncios ou uma versão paga mudam esse quadro.
  - **Código Civil, art. 392:** em contratos gratuitos, quem faz o favor (você) só responde por **dolo**.
    Isso joga a seu favor.
  - Nenhuma cláusula afasta a responsabilidade por dolo ou culpa grave.
- **Exemplo:** um usuário aplica a troca de driver NVIDIA, a máquina não sobe a interface gráfica e ele
  culpa o app.
- **Solução:** manter a fórmula "na extensão máxima permitida pela lei", detalhar melhor (sem garantia
  de resultado, de compatibilidade ou de ausência de perda de dados) e, **mais importante que o texto**,
  manter avisos claros na tela antes de cada ação de risco, com backup e reversão. Isso já está no
  AGENTS.md: preserve. O aviso visível pesa mais que a licença num caso real.
- **Gravidade: Média**

### 7. A licença fala de si mesma como "provisória" e há documentos que se contradizem
- **O que é:** a seção 6 da licença diz "PENDENTE DE REVISÃO" e "Publicação depende dessa revisão".
  O `AGENTS.md` linha 144 diz "proíbe qualquer monetização... **profissional**", e a linha 151 diz que
  uso profissional é permitido. O `THIRD-PARTY-INVENTORY.md` diz "Intenção MIT" para código novo.
- **Por que importa:** quem estiver em conflito com você vai apontar o documento que lhe for mais
  favorável. Uma licença que se declara pendente parece não estar valendo.
- **Solução:** antes de publicar, tirar a seção 6 do texto da licença (o controle interno fica no
  checklist), corrigir a linha 144 do AGENTS.md e a linha "Código novo" do inventário.
- **Gravidade: Baixa** (mas é obrigatório corrigir antes de tornar público)

### 8. OpenRGB (GPL-2.0): a "oferta escrita" atual não cumpre a GPL-2.0
- **O que é:** o `SOURCE.txt` diz "Written offer: the corresponding source is available at the
  address above" (link do Codeberg). A **GPL-2.0** (seção 3) dá três caminhos a quem distribui o
  binário: (a) entregar o código-fonte junto; (b) uma oferta escrita, **válida por 3 anos**, de
  fornecer você mesmo o fonte a qualquer pessoa; (c) repassar a oferta que você recebeu, só para
  distribuição não comercial. "Apontar para o site de outra pessoa" é aceito pela GPL-3.0, mas **não
  está entre as opções da GPL-2.0**.
- **Mais um detalhe:** o AppImage do OpenRGB embute bibliotecas (Qt, que é LGPL, hidapi, libusb...).
  O "fonte correspondente" envolve também essas peças.
- **Por que importa:** quem cobra GPL costuma ser razoável e pede correção, mas este é o ponto mais
  "técnico-jurídico" do pacote, e corrigir é fácil.
- **Solução (a mais simples):** anexar ao GitHub Release, ao lado do AppImage, o tarball do fonte do
  OpenRGB `release_1.1` (e, se possível, a lista e os fontes das bibliotecas do AppImage) e mudar o
  `SOURCE.txt` para "o código-fonte correspondente está publicado junto com este release em <link>".
  Alternativa: escrever uma oferta de 3 anos com um e-mail de contato.
- **Gravidade: Média**

### 9. Fonte Inter (OFL) e outros avisos ausentes do inventário
- **O que é:** o `Ilmenyx.Desktop.csproj` usa `Avalonia.Fonts.Inter` (a fonte Inter, licença **SIL OFL
  1.1**), mas ela **não aparece** em `THIRD-PARTY-NOTICES.md`. O `.NET runtime` tem um arquivo
  próprio `THIRD-PARTY-NOTICES.TXT` com dezenas de componentes. Os ícones Material Design são
  **Apache-2.0**, que exige enviar uma cópia da licença.
- **Por que importa:** a OFL permite embutir a fonte em qualquer software (até comercial), mas exige que
  o texto da OFL e o copyright acompanhem a fonte. Faltar o aviso é uma violação pequena, mas real.
- **Solução:** veja o checklist abaixo.
- **Gravidade: Média** (corrigir é fácil)

### 10. Expectativa: até onde a licença própria protege de verdade
- **O que é:** ela protege **só o seu código**. O RESET EXPLODE continua MIT. Ideias, algoritmos e
  funcionalidades **não** têm proteção de direito autoral (Lei 9.610, art. 8º). Só o código escrito
  e a expressão concreta são protegidos.
- **Exemplo:** alguém reescreve do zero um app "igual ao Ilmenyx", sem copiar seu código, e vende.
  Isso é legal.
- **Também:** fazer valer a licença custa tempo e dinheiro (notificação, eventual ação). Para um
  projeto pessoal, a ferramenta realista é a **notificação** (e-mail, DMCA no GitHub e denúncias em
  lojas), não o processo.
- **Gravidade: Baixa** (é informação, não defeito)

### 11. Titularidade: "Silex Audit contributors", Bismuth, contribuidores e IA
- **O que é:**
  - O `LICENSE` atribui copyright a "Silex Audit contributors", sem dizer quem são. O Git só mostra um
    autor: você (Flávio).
  - Nenhum arquivo diz "Copyright (c) 2026 <seu nome>" para o código novo. Sem isso, não fica claro
    quem é o titular da licença própria.
  - A Bismuth Technology não é pessoa jurídica, então **não pode** ser titular hoje. Uma pessoa física
    pode, e o titular hoje é **você**.
  - Ainda não há regra para contribuições externas (o `CONTRIBUTING.md` diz "Não há CLA, DCO").
  - O código foi escrito com auxílio de IA (Claude Code e Codex, segundo o AGENTS.md).
- **Por que importa e como resolver:**
  - **Pessoa física pode ser titular.** A Lei 9.610 (art. 11) diz que autor é a pessoa física criadora.
    Coloque o seu nome civil, ou nome + "mantido como Bismuth Technology".
  - **Empresa no futuro:** quando criar a empresa, os direitos **não passam sozinhos** para ela. É
    preciso um **contrato escrito de cessão** (Lei 9.610, arts. 49 e 50) ou uma licença exclusiva sua
    para a empresa. Atenção: se você contratar funcionários, o código que eles fizerem no trabalho
    pertence à empresa (Lei 9.609, art. 4º), e isso é bom para você.
  - **Contribuições de terceiros:** cada contribuidor é autor do próprio trecho. Se você aceitar um PR
    sem nenhum termo, aquele código fica com o autor dele, e você *não* poderia, por exemplo, vender
    uma licença comercial que o inclua. Duas abordagens simples:
    - **DCO** (Developer Certificate of Origin): o contribuidor assina cada commit com
      `Signed-off-by` e declara que tem direito de enviar o código sob a licença do projeto. É leve,
      mas não deixa você relicenciar depois.
    - **Licença ampla do contribuidor** (um CLA curto): ele mantém a autoria, mas dá a você (e a um
      futuro sucessor, como a empresa) permissão para licenciar o código dele sob quaisquer termos. É
      isso que deixa a porta aberta para a futura empresa. Contribuidores costumam desconfiar de CLAs,
      então seja transparente.
  - **Código feito com IA:** o entendimento predominante hoje (Brasil, que exige autor pessoa física, e
    EUA, pelo relatório do Copyright Office de 2025) é que há proteção na medida da **contribuição
    criativa humana**: escolher, dirigir, revisar, editar e organizar. Trechos gerados de forma
    puramente automática podem não ser protegidos. O tema ainda está em debate, inclusive no projeto
    de lei de IA (PL 2338/2023), que tramita no Congresso. Recomendação prática: continue revisando e
    editando o que a IA produz (os commits mostram isso) e diga no CONTRIBUTING que contribuições com
    IA são aceitas desde que o contribuidor as revise e se responsabilize por elas.
  - **RESET EXPLODE / GustavoReset:** usar o nome para dar crédito é permitido e, para o aviso de
    copyright, obrigatório. A MIT **não** dá direito sobre marca e logo, e não implica endosso. O que
    vocês já fazem está certo: "adaptação independente e não oficial", sem logo nem assets do original.
    Evite usar "RESET" no nome do produto, no ícone ou em anúncios. Uma gentileza que ajuda: avisar o
    Gustavo antes do lançamento.
- **Gravidade: Média**

### 12. Privacidade: o `PRIVACY.md` não lista todos os acessos à rede
- **O que é:** o documento cita só ProtonDB, Steam e GitHub. O código também acessa:
  - **howmanyfps.com:** envia **modelo de CPU, modelo de GPU, jogo e resolução**, com a chave do usuário;
  - **speed.cloudflare.com, 1.1.1.1 e 8.8.8.8** (teste de rede, cerca de 100 MB);
  - **download.nvidia.com** (verificação da versão mais recente do driver).

  Todos só rodam depois de um clique, o que é ótimo. Mas a frase "nada sobre o seu hardware é enviado"
  não vale para a HowManyFPS.
- **LGPD:** como você não recebe nenhum dado (tudo fica local), em regra você **não é "controlador"**
  desses dados. Isso muda quando você receber relatórios por issue ou e-mail. Aí vale indicar um
  contato e por quanto tempo guarda os anexos (hoje está "[PENDENTE]").
- **Solução:** uma tabela "o que sai do seu PC, para onde e quando" com os 6 destinos, mais um
  e-mail de contato. O texto atual também está muito técnico para o usuário comum.
- **Gravidade: Baixa**

---

## Opções

| | **A. Licença própria melhorada** | **B. PolyForm Noncommercial 1.0.0 + permissão adicional** | **C. GPL-3.0** | **D. MIT em tudo** |
|---|---|---|---|---|
| Atende "ninguém vende" | Sim | Sim | **Não**: qualquer um pode vender, mas tem de abrir o código | **Não** |
| Atende "técnico usa em trabalho pago" | Sim | Só com a **permissão adicional** (o PolyForm NC puro proíbe uso por empresas) | Sim | Sim |
| Impede produto pago fechado com o seu código | Sim | Sim | Sim (derivado tem de ser GPL e com fonte aberto) | Não |
| Qualidade do texto | Depende de você/advogado | Alta: escrito por advogados, com rescisão de 32 dias e patentes | Altíssima, testado em tribunais | Altíssima |
| Clareza para terceiros | Baixa: texto único, em português | Boa: conhecido no meio | Muito boa | Muito boa |
| "Open source" (OSI) | Não | Não | **Sim** | **Sim** |
| Flathub | Aceita, mas mostra como não livre ("proprietário") | Igual a A | Aceita como livre | Aceita como livre |
| Distros (Debian main, Fedora) | Não empacotam (AUR sim) | Não empacotam | Podem empacotar | Podem empacotar |
| Contribuidores externos | Poucos; muitos evitam licença não livre | Poucos | Mais | Mais |
| Forks | Permitidos, mas só não comerciais | Idem | Livres, desde que GPL | Livres |
| Compatível com o MIT upstream | Sim | Sim | Sim | Sim |
| Lei e foro brasileiros | Você define | Não define (texto em inglês) | Não define | Não define |
| Trabalho para você | Alto: reescrever e revisar | **Baixo**: só a permissão adicional | Baixo | Mínimo |

**Outras opções que avaliei e não recomendo para o seu objetivo:**
- **PolyForm Small Business:** deixa empresas pequenas fazerem *tudo*, inclusive vender. É o contrário
  do que você quer.
- **PolyForm Shield:** permite qualquer uso, menos *competir* com você. Seria possível vender algo que
  "não compete" usando seu código. Não fecha a porta como você deseja.
- **Creative Commons BY-NC-SA:** a própria Creative Commons recomenda **não** usar CC em software. Ela
  não trata de código-fonte versus binário nem de patentes, e o "NC" é famoso por ser vago. Além disso,
  o "SA" obrigaria os derivados a usarem a mesma licença CC, que é incompatível com licenças de software.
- **Business Source License (BSL 1.1):** o código fica restrito e, depois de uma data (até 4 anos),
  vira automaticamente open source (ex.: GPL). Bom se você *quiser* que o código vire livre no futuro.
  Se a meta é proibir venda para sempre, não serve. Também é desenhada para "uso em produção", um
  conceito que não combina bem com um app de desktop.
- **Commons Clause + MIT:** proíbe "vender serviços cujo valor vem do software", o que pode pegar o
  **técnico que cobra diagnóstico**. Arriscado para o seu objetivo.

**O que se ganha e se perde indo para MIT ou GPL (C ou D):** você ganha selo "open source", empacotamento
em distros, mais contribuidores, mais confiança do público e zero trabalho jurídico. Perde a proibição de
venda. Com a GPL, quem vender tem de entregar o código-fonte e permitir que o comprador redistribua de
graça. Na prática, isso torna quase inviável vender o Ilmenyx sem acrescentar algo de valor próprio.
Muitos projetos escolhem a GPL justamente por isso.

**Minha leitura:** se a proibição de venda é inegociável, **B** (PolyForm Noncommercial + uma permissão
adicional curta, em português e inglês, para o uso profissional instrumental) dá o texto mais sólido com
menos trabalho. **A** é viável se você quer controle total da redação e lei brasileira, mas aí a revisão
por um advogado é quase obrigatória.

---

## Decisões que você precisa tomar

Responda com a letra, por exemplo "1) B".

1) **Qual modelo de licença para o seu código?**
   A = licença própria melhorada · B = PolyForm Noncommercial + permissão adicional (recomendado) ·
   C = GPL-3.0 · D = MIT em tudo

2) **Alguma cópia do código já saiu do seu controle** (pendrive com terceiros, repositório público
   antigo, envio para alguém)?
   A = nunca saiu, então posso colocar **todo** o meu código sob a nova licença (recomendado, se for
   verdade) · B = já saiu, então só vale daqui para frente · C = não tenho certeza

3) **Quem são os "Silex Audit contributors"?**
   A = só eu · B = houve outras pessoas · C = não sei

4) **Como você quer aparecer como titular?**
   A = nome civil completo · B = nome civil + "mantido como Bismuth Technology" (recomendado) ·
   C = pseudônimo (a lei permite, mas para fazer valer os direitos você teria de se identificar)

5) **Contribuições de outras pessoas:**
   A = DCO simples (assinatura no commit) · B = licença ampla do contribuidor (CLA curto), que mantém
   aberta a porta para a futura empresa · C = não aceitar código externo por enquanto (só issues)

6) **Vídeos, tutoriais e artigos monetizados sobre o Ilmenyx:**
   A = permitidos (recomendado: é divulgação) · B = proibidos

7) **Sites que redistribuem o AppImage:**
   A = só de graça e sem exigir cadastro ou anúncio para baixar (recomendado) · B = proibido
   redistribuir fora do GitHub oficial · C = livre

8) **Técnico que instala o Ilmenyx no PC do cliente e cobra pela visita:**
   A = permitido, desde que não cobre pelo app em si e deixe a licença junto (recomendado) · B = proibido

9) **Empresa que modifica o app para uso interno, sem vender nem oferecer a terceiros:**
   A = permitido · B = proibido

10) **Doações ou patrocínio para você** (GitHub Sponsors, Pix):
    A = quero aceitar (você pode, porque a licença limita os outros e não você; mas lembre do CDC,
    item 6) · B = não agora

11) **OpenRGB (GPL-2.0) no pacote:**
    A = publicar o código-fonte do OpenRGB junto em cada release (recomendado) · B = oferta escrita de 3
    anos com e-mail de contato · C = parar de embutir e pedir que o usuário instale

12) **Idioma da licença:**
    A = só português · B = português + inglês, com o português prevalecendo (recomendado) ·
    C = inglês prevalecendo (natural com a opção 1-B)

13) **Registro do programa no INPI** (opcional, barato, serve como prova de autoria e data):
    A = sim, antes do lançamento · B = não por enquanto

14) **Revisão por advogado:**
    A = antes do primeiro release público · B = só se aparecer interesse comercial ou conflito

---

## Checklist antes de tornar público

**Seus documentos**
- [ ] `LICENSE` da raiz: texto curto explicando as duas licenças. MIT integral em `LICENSES/MIT.txt`.
- [ ] Licença própria finalizada (decisões 1 e 6 a 12), sem a seção "pendente de revisão".
- [ ] Cabeçalho SPDX em **todos** os arquivos de `src/`, `scripts/`, `packaging/` e `tests/`, e
      `reuse lint` passando.
- [ ] `SPDX-FileCopyrightText` com o seu nome no código novo. Copyright do Gustavo nos arquivos adaptados.
- [ ] Corrigir `AGENTS.md` (linha 144), `THIRD-PARTY-INVENTORY.md` ("Intenção MIT") e `LICENSING.md`
      ("futuras").
- [ ] `CONTRIBUTING.md` com DCO ou CLA (decisão 5) e a regra sobre código feito com IA.
- [ ] `PRIVACY.md` com a tabela dos 6 destinos de rede e um contato.
- [ ] metainfo: `project_license` coerente com a decisão 1 (ex.: `MIT AND LicenseRef-Ilmenyx-1.0`, ou
      `MIT AND PolyForm-Noncommercial-1.0.0`).

**Terceiros embutidos no AppImage, Flatpak e tar.gz**
- [ ] **RESET EXPLODE (MIT):** texto da MIT e copyright do Gustavo no pacote e na tela Sobre. (já feito)
- [ ] **.NET runtime (MIT):** `LICENSE.TXT` e **`THIRD-PARTY-NOTICES.TXT`** do runtime em `licenses/`.
- [ ] **ICU (Unicode License v3):** texto completo da licença em `licenses/`. (o script já prevê;
      confira que o arquivo está no pacote final)
- [ ] **Avalonia, SkiaSharp, HarfBuzzSharp, Silk.NET, Tmds.DBus, NWayland, MicroCom (MIT):**
      LICENSE de cada um. Para os binários nativos do Skia e do HarfBuzz, também os avisos de
      componentes nativos (FreeType, libpng, zlib etc.).
- [ ] **Fonte Inter (SIL OFL 1.1):** acrescentar em `THIRD-PARTY-NOTICES.md` e copiar o `OFL.txt` com o
      copyright ("Copyright The Inter Project Authors") para `licenses/`. Não vender a fonte sozinha.
      Se um dia modificar a fonte, mudar o nome dela.
- [ ] **Material Design Icons (Apache-2.0):** cópia da licença Apache-2.0 em `licenses/`.
- [ ] **MangoHud (MIT):** LICENSE copiado (já feito). Conferir se o pacote oficial traz avisos de
      subcomponentes (ex.: ImGui) e incluí-los.
- [ ] **OpenRGB (GPL-2.0-only):** AppImage oficial **sem modificação** (o SHA-256 garante isso); texto
      da GPL-2.0 junto (já feito); **código-fonte da mesma versão (`release_1.1`) publicado junto ao
      release** ou oferta escrita de 3 anos (decisão 11); `SOURCE.txt` reescrito conforme essa escolha.
- [ ] **Pacotes de teste (xunit etc.)** fora do pacote final. (já previsto)
- [ ] **Dados (ProtonDB ODbL, AreWeAntiCheatYet MIT):** atribuição na página. (já feito)

**Sobre misturar um programa GPL com um app não livre ("mera agregação")**

A GPL-2.0 diz que colocar um programa GPL "no mesmo volume de armazenamento ou meio de distribuição"
que outro programa **não** submete o outro programa à GPL. Isso é a "mera agregação". É como um CD de
revista com vários programas: cada um mantém a sua licença. O que puxa a GPL é formar *um único
programa*, por exemplo ligando bibliotecas (linking) ou trocando estruturas internas de forma íntima.

No seu caso:
- o OpenRGB vai como um **AppImage separado e intacto**;
- é executado como **processo separado**, iniciado por clique;
- o Ilmenyx conversa com ele pelo **protocolo de rede do SDK do OpenRGB** em 127.0.0.1, que é uma
  interface pública feita justamente para outros programas usarem.

A FAQ da FSF trata comunicação por sockets ou linha de comando como, em regra, programas separados.
Conclusão: **é mera agregação, risco baixo**, desde que você não copie código do OpenRGB para o Ilmenyx.
(Atenção: `OpenRgb.cs` foi adaptado do *RESET EXPLODE*, que é MIT. Confirme que nem o original nem o seu
copiaram trechos do código-fonte do OpenRGB. Se só implementam o protocolo, está ok.) E a obrigação do
fonte (item 8) continua valendo, porque você está *distribuindo* o binário GPL.

---

## Quando vale chamar um advogado

Vale pagar uma consulta (de preferência com quem trabalha com propriedade intelectual e software) quando:

1. **Você escolher a opção 1-A (licença própria).** Um texto jurídico feito sem advogado é o ponto mais
   frágil de todo o projeto. Uma revisão única, antes do release, resolve a maior parte. Na opção B o
   texto-base já está pronto, e a revisão pode se limitar à permissão adicional.
2. **For criar a empresa:** contrato de cessão ou licença dos seus direitos para a pessoa jurídica,
   escolha do tipo societário e, se for o caso, registro de marca no INPI para "Ilmenyx".
3. **Alguém vender ou copiar comercialmente o seu código:** antes de mandar qualquer notificação formal.
4. **Começar a receber dinheiro ligado ao app** (doações recorrentes, versão paga, anúncios): isso pode
   criar relação de consumo (CDC) e muda o peso da cláusula de garantia.
5. **Receber uma reclamação de dano** (ex.: "o app estragou meu PC").

Para todo o resto (avisos de terceiros, SPDX, OpenRGB, privacidade e contribuições), o checklist acima
basta e você mesmo consegue fazer.
<!-- REUSE-IgnoreEnd -->
