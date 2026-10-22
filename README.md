# Ilmenyx

by **Bismuth Technology**

Diagnóstico, monitoramento e benchmarking de hardware para Linux. Código-fonte disponível;
política de contribuições próprias futuras: uso como ferramenta profissional permitido;
venda do programa/acesso pago e derivação comercial do código restritos.
Veja [mapa de licenças](LICENSING.md): a MIT original continua permitindo uso comercial das
partes abrangidas, sem revogação de direitos anteriores. O conjunto não é anunciado como open source OSI.
Versão local experimental 0.30.0; sem lançamento público (push, release e divulgação dependem de aprovação do mantenedor).

Ilmenyx é uma adaptação independente e não oficial do [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode),
criado por Gustavo (GustavoReset). Não possui afiliação, patrocínio ou endosso da equipe RESET.
O upstream permanece no submódulo `upstream/ResetExplode`, SHA
`ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, com licença e histórico preservados.
[LICENSE](LICENSE) MIT integral e [NOTICE](NOTICE.md) são parte do produto. A auditoria e a
implementação tiveram acesso ao original; não se afirma clean-room estrito.

Ilmenyx e Bismuth Technology são nomes informados pelo mantenedor; não se afirma registro,
exclusividade ou disponibilidade jurídica. Manual de Marca ainda não aprovado: texto/UI atuais
são provisórios, sem logo, paleta ou tipografia oficial definida.

## O que existe (0.30.0)

O aplicativo desktop (Avalonia 12 + .NET 10) tem barra lateral com as páginas abaixo; a CLI (`ilmenyx`) cobre inventário,
sensores, SMART importado e os benchmarks com opt-in explícito. Tema escuro/claro, avisos de dispositivo e relatórios HTML/JSON locais.

| Grupo | Páginas | Estado |
|---|---|---|
| Diagnóstico | Visão geral (gráfico, desempenho, saúde do PC, eventos, anéis), Sistema, Sensores (hwmon + GPU), GPU, Discos (veredito + SMART importado), Energia/bateria, Rede (teste só por clique), Processos | IMPLEMENTADO; leitura somente leitura de /proc, /sys, /etc |
| Jogos | Jogos (Steam/Lutris/Heroic/manual, GameMode, MangoHud, PRIME), Estimativa de FPS, Gargalo | IMPLEMENTADO; jogo real, Heroic e API HowManyFPS NÃO TESTADOS |
| Desempenho | Benchmark (CPU render, memória, GameSim, GPU em Vulkan, disco O_DIRECT), OC (verificador CPU/RAM/GPU, erros do kernel, comandos NVIDIA, guia da BIOS), Overlay (janela + MangoHud), Stress (CPU/RAM/GPU) | IMPLEMENTADO; carga só por clique + confirmação, com limites e corte por temperatura |
| Maker | Portas seriais, drivers seriais (CH340/CP210x/FTDI…) | IMPLEMENTADO; sem placa real NÃO TESTADO |
| Ferramentas | Otimiza (fechar apps próprios, ajustes reversíveis), Integração (RGB via OpenRGB, que acompanha o pacote), Inicialização, Instalar apps (Flatpak, escopo do usuário), Drivers, Relatório, Requisitos e sobre | IMPLEMENTADO; sem sudo implícito: o que exige root vira comando para você copiar |

- **Pacote autocontido**: runtime .NET e ICU vão no pacote (`scripts/package-linux-x64.sh`); ver `docs/pacote-linux-x64.md`.
  Bibliotecas do sistema (glibc, fontconfig, X11/Wayland, EGL, loader Vulkan) e Flatpak/smartctl são do sistema. MangoHud e OpenRGB acompanham o pacote (components/) e o GameMode tem reserva embutida; se o sistema já tiver esses programas, os do sistema têm prioridade (`ilmenyx components` mostra a origem).
- **Matriz do port** em `docs/portagem-completa.md`; **tudo que não foi testado, com o motivo e a condição de reteste, em `Sem_teste.md`**.
- Windows-only e não aplicável: tweaks de registro, Game DVR, HAGS, SDKs Corsair/Razer Synapse, "números COM presos". Ray tracing de GPU: BLOQUEADO sem hardware.
- Scores (CPU, GameSim, GPU, disco) são experimentais e **não comparáveis ao Windows**.
- AMD/NVIDIA reais: NÃO TESTADO; aviso visível mantido por autorização do usuário.

Nenhuma coleta fabrica temperatura/saúde/VRAM. Capacidade de disco não é espaço livre ou saúde.
Carga completa não roda ao abrir o app ou na suíte. Sem instalação, drivers, root ou telemetria de
produto automáticos. Dependências gráficas podem usar IPC local; restore/audit NuGet acessam rede.

## O que não foi portado (contribuições são bem-vindas)
Tudo o que o RESET EXPLODE faz foi portado ou substituído por um equivalente Linux, exceto o que está abaixo. Se você quiser implementar algum destes itens, a contribuição é bem-vinda: veja `docs/portagem-completa.md` (matriz com o original), `Sem_teste.md` (o que falta validar e como) e `AGENTS.md` (regras de segurança). Respeite a licença (`LICENSING.md`).

| Item do original | Situação no Linux | Motivo |
|---|---|---|
| Benchmark de GPU com ray tracing (GpuRtBench) | **Não portado** (BLOQUEADO) | Exige `VK_KHR_ray_tracing_pipeline`, que a GPU de desenvolvimento (Intel Iris Xe) não tem. Escrever e não conseguir rodar nem uma vez seria código não verificado. Precisa de alguém com GPU AMD RX 6000+ ou NVIDIA RTX |
| Teste de GPU AMD/NVIDIA em hardware real | Código portado, **não testado** | O ambiente de desenvolvimento só tem GPU Intel integrada |
| SDK da Corsair (iCUE) e Razer Synapse | Substituídos pelo OpenRGB | Esses SDKs são do Windows e proprietários; o OpenRGB cobre os mesmos dispositivos. Controle de LEDs real **não testado** (sem hardware RGB) |
| Otimizações do Windows (Game DVR, HAGS, janela do Windows, WindowsTweaks) | Não se aplicam | Não existem no Linux; o Otimiza traz equivalentes (perfil de energia, governador, `vm.max_map_count`, etc.) |
| Limpeza de dispositivos USB fantasmas (UsbCleaner) | Não se aplica | É uma limpeza do registro do Windows |
| Prioridade elevada de processo e otimizações de tela cheia | Não se aplicam sem root | O Linux exige privilégio para subir prioridade; o GameMode do sistema cobre isso. O GameMode embutido só ajusta `ionice`/`renice` do usuário |
| FPS por ETW (FrameMonitor) | Substituído pelo MangoHud | ETW é do Windows; o FPS vem do log do MangoHud. FPS num jogo real **não testado**; MangoHud embutido só para 64 bits |
| WinRAR no catálogo de apps | Sem equivalente | Programa proprietário do Windows; o catálogo Flatpak usa alternativas |
| Flatpak embutido no pacote | Não embutível | É um serviço do sistema (precisa de root, polkit e bubblewrap); o app mostra o comando da sua distro |
| Módulos de RAM, SMART e ajustes NVIDIA (NVML) | Parcial | Exigem root; o app mostra o comando ou aceita a saída importada, sem `sudo` implícito |
| Overlay sobre jogos em Wayland | Parcial | Janela sempre-no-topo depende do compositor; o overlay do MangoHud funciona dentro do jogo |
| Pacotes nativos (PKGBUILD/deb/rpm), Alpine/musl, ARM64 e instaladores | Não feito | O pacote atual é um `tar.gz` linux-x64 (glibc) validado em VM em Ubuntu, Debian, Fedora, Arch e openSUSE |

Os pontos "não testado" têm o motivo e a condição de reteste em `Sem_teste.md`.

## Compilar e testar

Requer SDK .NET 10 no PATH e bibliotecas gráficas X11/EGL/fontconfig/Skia conforme backend.
Avalonia fixado em 12.1.4. Nenhuma instalação automática; `global.json` aceita feature band 10.0
mais recente. Para checkout novo, inicialize o submódulo antes de usar os oracles, sem alterar seu SHA.

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet restore Ilmenyx.slnx --disable-parallel -m:1
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
```

Em Bash, `. scripts/env-local-sdk.sh` reutiliza o SDK já extraído em `/tmp/silex-dotnet`, se ainda
existir. Esse helper conserva nomes históricos; não o execute via source no fish. Com SDK no PATH,
os comandos `env` abaixo funcionam também no fish.

## Executar

```bash
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- --help
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- info --json
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- info --html --output /tmp/ilmenyx-hardware.html
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- sensors --json
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- bench sim --json
dotnet run --project src/Ilmenyx.Desktop -c Release --no-build
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project src/Ilmenyx.Desktop -c Release --no-build
env ILMENYX_DISPLAY_BACKEND=x11 dotnet run --project src/Ilmenyx.Desktop -c Release --no-build
```

O apphost CLI local é `src/Ilmenyx.Cli/bin/Release/net10.0/ilmenyx`; não foi instalado globalmente.
Escolha arquivo novo ao exportar. UI tem Licenças e créditos e diálogo de destino com aviso de
privacidade. `auto` preserva UsePlatformDetect/X11; `wayland` explícito não faz fallback. Alias
legado SILEX_DISPLAY_BACKEND permanece aceito quando ILMENYX_DISPLAY_BACKEND não está definido.

Smoke gráfico opt-in, sem eventos globais ou perfis full:

```bash
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project tools/Ilmenyx.UiSmoke -c Release -- /tmp/ilmenyx-ui-validation
```

`bench sim-plan` prepara configuração sem carga. `bench cpu --profile full --allow-cpu-load` e
`bench sim-full --allow-cpu-load` geram carga e não foram executados automaticamente. Veja
[GameSim completo](docs/gamesim-completo.md) e [especificação CPU](docs/cpu-workload-spec.md).
Comandos históricos nesses documentos usam caminhos Silex; mapa atual em [renomeação](docs/branding/RENAME-DECISIONS.md).
Não declarar comparabilidade Windows nem saúde/estabilidade a partir de scores.

## Estrutura e continuidade

Ilmenyx.slnx: src/Ilmenyx.Contracts, Core, Platform.Linux, Cli, Desktop; tests/Ilmenyx.Tests.
Origem WPF em net8.0-windows fica separada; não buildada na solução Linux. Metadata/props em src
não afetam o submódulo. `LICENSE`, `NOTICE.md`, `THIRD-PARTY-NOTICES.md` são copiados para outputs
Linux; empacotamento completo de terceiros ainda exige revisão, sem publicação nesta sessão.

O nome anterior era Silex Audit. IDs de schemas, workloads, hashes e medição `silex-*` permanecem
estáveis para relatórios/fixtures; nomes históricos não são a identidade ativa. Nenhum dado/config
antigo foi apagado ou migrado; o app não tem preferências XDG persistentes próprias implementadas.

[Auditoria técnica](docs/portabilidade-inventario.md), [arquitetura](docs/arquitetura.md),
[roadmap planejado](docs/roadmap-linux.md), [validação](docs/validacao.md), [Sem_teste.md](Sem_teste.md).

## Privacidade, colaboração e publicação

Revise relatórios antes de compartilhar. Configuração de hardware, fontes, datas e rótulos podem
identificar uma máquina. Redação de padrões óbvios não é anonimização garantida. Não anexe raw
SMART, dumps ou logs com dados pessoais. [PRIVACY](PRIVACY.md), [SECURITY](SECURITY.md),
[contribuição](CONTRIBUTING.md), [licenças](THIRD-PARTY-NOTICES.md).

Contato privado de segurança, titularidade das contribuições, GitHub org/URL/domínio, revisão de
marca, licenças de assets e Manual de Marca: PENDENTE DE REVISÃO humana. Material sem direitos
comprovados BLOQUEIA REDISTRIBUIÇÃO ATÉ REVISÃO. [Checklist pré-publicação](docs/legal/PRE-RELEASE-CHECKLIST.md).
Nenhum logo RESET ou referência visual local foi incorporado. Commits locais autorizados;
sem push, PR, organização, registro de marca, tags públicas ou releases automáticos.


## Continuação 0.11.0 — HTML de resultados e política comercial

`bench cpu --html`, `bench memory --html` e `bench sim --html` exportam resultado reduzido com
hardware/proveniência/hashes. Sem FPS/score de desempenho; full não aceita esse formato.
[Comandos e evidências](docs/validation-html.md). Build sem avisos/erros, 276 testes aprovados,
CLI real e regressões Wayland/X11. Não há botão novo HTML de resultados na UI ainda.

A política própria permite usar Ilmenyx como ferramenta em serviços pagos, inclusive diagnóstico
e entrega de relatórios. Não autoriza vender o programa, cobrar acesso nem copiar/incorporar o
código próprio para produto/serviço pago. [Termos](LICENSE-ILMENYX.md) e [mapa](LICENSING.md);
MIT/terceiros e direitos já concedidos preservados, revisão jurídica antes de publicação.

Pasta/repositório local: `/mnt/HDD/Github/Ilmenyx`; branch `feat/ilmenyx-linux`. O repositório
principal não possui remoto: nenhuma organização ou nome de repositório GitHub foi alterado.

### Exportação dos resultados reduzidos na interface (0.12.0)

Após validar CPU, memória ou GameSim, use o botão HTML do respectivo painel.
O arquivo usa o mesmo resultado/contexto do JSON, sem nova coleta ou execução.
O destino precisa ser novo; cancelamento e recusa de sobrescrita foram testados.
Build sem avisos e 279 testes aprovados; regressões reais em Wayland nativo e X11.
Veja [evidências e limites](docs/validation-html-ui.md).

### Teclado e acessibilidade (0.13.0)

Diálogos têm rótulos/instruções acessíveis e retorno do foco ao botão de origem.
Tab/Shift+Tab, Espaço, Enter, Escape e setas foram testados por eventos internos
em Wayland nativo e X11. Leitor de tela e entrada física NÃO TESTADOS.
Veja [escopo, evidências e comandos](docs/acessibilidade-teclado.md).

### Layout mínimo (0.14.0)

As ações do dashboard quebram em linhas; o diálogo de exportação permite rolagem
e o título de Sobre quebra com texto ampliado.
[Método, evidências e limites](docs/layout-minimo.md).
279 testes aprovados; smokes Wayland nativo e X11 passaram nos tamanhos mínimos
com texto normal e 150% no harness. Outras escalas e tecnologias assistivas pendentes.

### Leitura de licenças (0.15.0)

Sobre agora usa um seletor compacto dos quatro documentos e uma área maior de leitura
somente leitura. Preserva integralmente créditos, MIT, dependências e termos próprios.
[Fluxos de teclado, testes e limites](docs/licencas-leitura.md).
Build sem avisos/erros, 279 testes aprovados e regressões Wayland nativo/X11 passaram.
Área de leitura medida em cerca de 208 DIP normal e 135 DIP a 150%, no mínimo 480×360.

### Texto ampliado (0.16.0)

Rótulos/títulos corrigidos para quebra de linha e cabeçalho Sobre compacto.
[Matriz de texto 100%, 150% e 200%, comandos e limites](docs/texto-200.md).
Build sem avisos/erros, 279 testes aprovados e nove casos de layout por backend
passaram em Wayland nativo/X11. Leitores de tela e outras matrizes continuam pendentes.

### Pacote local linux-x64 (0.17.0)

Bundle CLI/desktop dependente do runtime .NET 10, com hashes e avisos locais.
Scripts de geração e validação não instalam nem publicam; destino deve ser novo.
[Comandos, dependências, evidências e pendências](docs/pacote-linux-x64.md).
279 testes aprovados; CLI extraída e regressões dos assemblies empacotados passaram
em Wayland nativo/X11. Instalação em máquina limpa e revisão de distribuição pendentes.

### Validação complementar em contêiner (pacote 0.17.0)

Debian 12 sem ICU bloqueou os diagnósticos padrão; CLI passou somente com modo
invariant explicitamente usado no teste. Alpine/musl não executa este RID glibc.
Requisitos do bundle: runtime .NET 10, glibc/ICU; GUI também requer bibliotecas como
fontconfig. Isso não certifica VM limpa. [Resultados e roteiro](docs/validacao-container.md).

### Falta de ICU na CLI (0.18.0)

Diagnósticos protegidos agora retornam código 5 e mensagem explícita se ICU não for
encontrada, antes da coleta, sem JSON parcial. Não se ativa invariant automaticamente.
284 testes aprovados e pacote testado no CachyOS; caso negativo reproduzido em Debian
sem ICU. [Evidências, comandos e limites](docs/icu-cli.md).

### Diagnóstico de requisitos (0.19.0)

`ilmenyx doctor` informa bibliotecas nativas e presença das variáveis de display,
sem inventário ou benchmarks. Funciona também para explicar a falta de ICU.
Somente texto, sem --json/--output; 0 indica requisitos básicos CLI encontrados,
5 indica ausência. Isso não certifica funcionamento gráfico.
[Comandos, testes e limites](docs/doctor-cli.md).

### Requisitos na interface (0.20.0)

No dashboard, abra **Requisitos** e pressione **Verificar**. O relatório local usa a
mesma apresentação do `ilmenyx doctor`; abrir a janela não inicia a sondagem.
Não coleta hardware, conecta ao display ou executa benchmarks. Bibliotecas
carregáveis não certificam fontes, ABI, drivers ou funcionamento gráfico.
[Implementação, testes e limites](docs/requisitos-ui.md).


### Roteiro de aceite em outra máquina — pacote 0.20.0

IMPLEMENTADO e testado no CachyOS: integridade antes da execução, doctor com prazo,
smoke CLI existente e resultado estruturado sem afirmar certificação de GUI/VM.
Runtime ausente e pacote alterado recusados; destino existente preservado.
NÃO TESTADO: VM limpa e outra distro com ICU; PKG-01 permanece parcial.
[Comandos, evidências e pendências](docs/aceite-maquina.md).


### Aceite CLI — distinção de globalização

IMPLEMENTADO e testado: resultado do roteiro schema 1.1 distingue padrão/invariant,
registra runtime/versão/códigos e recusa formato desconhecido/ambíguo ou versão
divergente. Casos positivos usam o pacote real; protocolo/metadados negativos usam
cópias de teste. Produto permanece 0.20.0. VM limpa e outra distro com ICU continuam
NÃO TESTADAS; revisões humanas/jurídicas permanecem PENDENTES.
[Comandos e evidências](docs/aceite-globalizacao.md).


### Privacidade em rótulos — 0.21.0

A limpeza de texto Linux agora oculta IPv6 comuns, inclusive escopo de interface,
além dos padrões anteriores. Não é anonimização garantida: revise antes de
compartilhar. [Testes, comandos e limites](docs/privacidade-ipv6.md).


### HTML de sensores pela CLI — 0.22.0

`sensors --html --output /caminho/novo.html` gera relatório offline com fontes,
unidades, horários, qualidade, indisponibilidades e créditos. Não ativa monitor,
SMART ou benchmark; JSON anterior preservado. [Testes e comandos](docs/sensores-html.md).
Exportação HTML de sensores pela interface foi acrescentada em 0.23.0; veja abaixo.


### HTML de sensores na interface — 0.23.0

No painel de sensores, escolha **Exportar sensores HTML**. Se o monitor atualizar
a seleção durante o diálogo, a gravação é recusada; pare o monitor e selecione
novamente. O mesmo controle protege JSON. [Resultados e comandos](docs/sensores-html-ui.md).


### SMART offline — 0.24.0

`smart-import --device nvme0n1 --html --output /caminho/novo.html < relatorio.json`
converte relatório smartctl existente sem consultar o disco. Entrada limitada,
saída filtrada e data da análise distinta da coleta original não confirmada.
[Uso, testes e limites](docs/smart-import.md). Não é garantia de saúde atual.

### Monitor ao vivo — 0.26.0

A janela inicial segue a estrutura do RESET EXPLODE: lateral por grupos, Visão geral com gráfico ao vivo, tabela de componentes e
páginas Sistema, Sensores, GPU, Discos (SMART offline), Energia, Rede, Processos e Validações reduzidas (somente leitura de /proc e
hwmon, histórico só em memória). O painel antigo continua em "Painel clássico de diagnósticos".
[Dados, limites e o que falta](docs/monitor.md). Aplicações agrupadas e GPU por processo ainda não existem.

### SMART importado na interface — 0.25.0

O dashboard tem o painel "SMART · importação offline": informe o disco e o caminho
completo de um JSON smartctl existente, importe e exporte JSON/HTML. Só arquivo regular
(sem link, /dev, /proc, /sys), sem smartctl nem consulta ao disco. Pacote 0.25.0 validado
por CLI em VM Ubuntu 24.04; GUI real, Wayland e Orca na VM seguem NÃO TESTADOS
([evidências](docs/evidencias/ilmenyx-vm-ubuntu-2404-0.25.0/README.md), [limites](docs/smart-import.md)).
