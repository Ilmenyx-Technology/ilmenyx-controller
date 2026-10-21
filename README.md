# Ilmenyx

by **Bismuth Technology**

Diagnóstico, monitoramento e benchmarking de hardware para Linux. Código-fonte disponível;
política de contribuições próprias futuras: uso como ferramenta profissional permitido;
venda do programa/acesso pago e derivação comercial do código restritos.
Veja [mapa de licenças](LICENSING.md): a MIT original continua permitindo uso comercial das
partes abrangidas, sem revogação de direitos anteriores. O conjunto não é anunciado como open source OSI.
Versão local experimental 0.20.0; sem lançamento público nesta sessão.

Ilmenyx é uma adaptação independente e não oficial do [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode),
criado por Gustavo (GustavoReset). Não possui afiliação, patrocínio ou endosso da equipe RESET.
O upstream permanece no submódulo `upstream/ResetExplode`, SHA
`ce1d9b30518bbe75bb5e6cb792d1b1e365aaddd6`, com licença e histórico preservados.
[LICENSE](LICENSE) MIT integral e [NOTICE](NOTICE.md) são parte do produto. A auditoria e a
implementação tiveram acesso ao original; não se afirma clean-room estrito.

Ilmenyx e Bismuth Technology são nomes informados pelo mantenedor; não se afirma registro,
exclusividade ou disponibilidade jurídica. Manual de Marca ainda não aprovado: texto/UI atuais
são provisórios, sem logo, paleta ou tipografia oficial definida.

## O que existe

| Recurso | Estado e limite |
|---|---|
| Inventário CPU/RAM/SO/kernel/discos | IMPLEMENTADO e testado no CachyOS x64, dados proc/sys e indisponibilidades explícitas |
| Sensores/energia/GPU DRM/PCI | IMPLEMENTADO conforme fontes disponíveis; monitor opt-in, sem calibração/proteção térmica |
| JSON e HTML local | IMPLEMENTADO, sem overwrite, HTML escapado sem scripts/recursos externos; aviso de compartilhamento |
| CLI e Avalonia .NET 10 | IMPLEMENTADO; Wayland nativo opt-in e X11/XWayland testados no KWin/CachyOS |
| CPU, RAM e GameSim reduzidos | IMPLEMENTADO, hashes/oracles independentes upstream; não são scores de carga |
| CPU/GameSim completos | Experimental, opt-in somente CLI; carga real NÃO TESTADA, sem equivalência Windows certificada |
| SMART | Leitura explícita opcional via smartctl; relatório NVMe real do usuário validado pelo parser; sem sudo no app |
| Sobre/licenças/créditos | IMPLEMENTADO, leitura local, sem abrir rede |
| AMD/NVIDIA reais | NÃO TESTADO; aviso visível mantido por autorização do usuário |
| Windows, outras distros/ISAs, embalagem final | NÃO TESTADO/PENDENTE; ver Sem_teste.md |
| Disco pesado/rede/GPU benchmark/overlay/ajustes | Não implementados; roadmap planejado, não promessas de funcionamento |

Nenhuma coleta fabrica temperatura/saúde/VRAM. Capacidade de disco não é espaço livre ou saúde.
Carga completa não roda ao abrir o app ou na suíte. Sem instalação, drivers, root ou telemetria de
produto automáticos. Dependências gráficas podem usar IPC local; restore/audit NuGet acessam rede.

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
