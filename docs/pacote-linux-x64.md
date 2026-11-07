# Pacote local linux-x64 — Ilmenyx 0.17.0

Base: 6c01555, branch feat/ilmenyx-linux. Inspecionados scripts, projetos e metadados,
Directory.Build.props, entradas CLI/desktop, harness, avisos de terceiros, README,
licença e instruções aplicáveis. Upstream e termos preservados.

## Pacote autocontido (0.28.0, decisão do usuário em 2026-10-22)

O usuário determinou que o necessário para o app funcionar acompanhe a instalação. O pacote agora é
**autocontido** (`--self-contained true`, RID linux-x64): inclui o runtime Microsoft.NETCore.App e o
**ICU 72 app-local** (`Microsoft.ICU.ICU4C.Runtime` 72.1.0.3, opção `System.Globalization.AppLocalIcu`).
Não é preciso instalar .NET nem libicu. Restore explícito antes do script (usa rede, só pacotes NuGet):

```bash
. scripts/env-local-sdk.sh
for p in Cli Desktop; do dotnet restore src/Ilmenyx.$p -r linux-x64 -p:IlmenyxBundleIcu=true; done
bash scripts/package-linux-x64.sh /caminho/absoluto/destino-novo
```

O ICU só entra no publish com `-p:IlmenyxBundleIcu=true` (build e testes comuns seguem usando o ICU do sistema).
O doctor/preflight aceita o ICU ao lado do executável quando a opção app-local está no runtimeconfig.
Tamanho: ~110 MB compactado (~256 MB extraído), sem trimming. Licenças do runtime (MIT) e do ICU (Unicode)
vão em `licenses/` e `dependencies.tsv`; segue PENDENTE DE REVISÃO jurídica antes de distribuir.

**Validação do pacote autocontido (2026-10-22, pacote `.artifacts/ilmenyx-0.28.0-sc-r2`, árvore limpa, VMs QEMU/KVM).**
Em cada VM: `dotnet` fora do PATH, SDK de teste renomeado, libicu do sistema movida para fora e `ldconfig` sem ICU
(`ICU sistema: 0`); `env -i` com PATH mínimo. Ubuntu 24.04.5, Debian 13 e Fedora 44: `ilmenyx doctor` código 0,
`ilmenyx info --json` com versão 0.28.0 (runtime .NET 10.0.0 embutido) e `Ilmenyx.Desktop` vivo após 10–12 s sob Xvfb.
Controle: removendo `libicu*` do pacote, o runtime aborta ("Failed to load app-local ICU"), logo o ICU em uso é o embutido.
Em seguida a libicu foi restaurada nas imagens. SHA256SUMS conferido no Ubuntu. NÃO TESTADO: smoke gráfico completo e
suíte com o pacote autocontido (só a execução do pacote), Wayland nesta rodada, Arch/openSUSE/Alpine, ARM64.

**Validação do pacote 0.29.0 (2026-10-22, `.artifacts/ilmenyx-0.29.0-sc`, árvore limpa, VMs QEMU/KVM).** Sem .NET no PATH em Ubuntu 24.04.5, Debian 13 e Fedora 44:
`validate-linux-machine.sh` → IMPLEMENTED_TESTED (hashes, JSON, sensores com canais de GPU, dependências nativas resolvidas), `bench gpu-list` lista só o renderizador por CPU
(llvmpipe) com o aviso correspondente, `bench gpu-burn` sem flag recusa, e `Ilmenyx.Desktop` ficou vivo por 14 s sob Xvfb. Uma primeira rodada falhou no Debian/Fedora por
`liblttng-ust` (provedor de rastreamento opcional do runtime): removido do pacote. Evidências em `docs/evidencias/ilmenyx-vm-*-0.29.0/`. NÃO TESTADO: smoke gráfico completo e suíte nas VMs
com 0.29.0, GPU real nas VMs, Wayland, Arch/openSUSE/Alpine, ARM64.

**Continua do sistema (não dá para embutir):** glibc, fontconfig, X11/Wayland, EGL e fontes. Integrações
opcionais — GameMode, MangoHud, Flatpak, smartctl, fwupd — são programas do sistema: o app detecta a falta,
informa e degrada sem falhar. As seções abaixo sobre runtime e ICU instalados descrevem o pacote anterior
(framework-dependent, 0.17–0.27) e ficam como histórico.


## IMPLEMENTADO

scripts/package-linux-x64.sh gera um bundle local de revisão, separado em cli/ e
desktop/, e um tar.gz. Destino absoluto deve ser novo: não instala, sobrescreve,
restaura ou baixa dependências, nem usa privilégios. Requer SDK .NET 10, Bash, jq,
rg, tar, sha256sum e utilitários usuais. Restauração por RID é etapa explícita.
Não faz single-file, trimming ou inclusão de símbolos de depuração.

Modo framework-dependent, RID linux-x64: NÃO inclui runtime/SDK; precisa de
Microsoft.NETCore.App 10.0 e bibliotecas nativas do SO. Não há runtime autocontido
no cache disponível. O teste usou o runtime 10.0.0 do SDK isolado da sessão;
nenhuma instalação automática. Não certifica qualquer versão de distro/glibc.

Copiados LICENSE, LICENSES/, NOTICE, LICENSING e THIRD-PARTY-NOTICES na raiz (até 2026-11-07 também LICENSE-ILMENYX)
e junto de cada executável. dependencies.tsv inventaria 19 pacotes runtime
resolvidos pelos deps.json. licenses/ preserva manifests nuspec e arquivos
LICENSE/NOTICE/COPYING/THIRD-PARTY disponíveis na raiz de cada pacote do cache,
incluindo avisos nativos Skia/HarfBuzz. Não inclui ícones de pacotes, WPF, drivers,
smartctl, testes ou harness. Avalonia.Win32.dll é dependência gerenciada do pacote
multiplataforma, não uma distribuição do executável WPF Windows original.

Inventário não prova atendimento integral às obrigações de redistribuição:
pacotes sem texto completo/copyright e avisos nativos exigem revisão individual.
PENDENTE DE REVISÃO antes de distribuição; material com direitos desconhecidos
continua BLOQUEADO para redistribuição. Bundle é somente revisão local.

package.json registra versão, RID, dependência de runtime, SHA base e estado da
árvore. Neste teste sourceWorkingTree=dirty porque o incremento ainda não estava
commitado ao gerar o pacote; não se afirma que o SHA base sozinho reproduza o
conteúdo novo. SHA256SUMS cobre arquivos do bundle exceto o próprio manifesto;
hash separado cobre o tar.gz. Hashes não são assinatura ou certificação de origem.
Timestamps do tar não foram normalizados; não se promete archive reproduzível.

## Comandos para reproduzir

Com SDK .NET 10 e cache da sessão disponível (ajuste o helper ao seu ambiente):

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
mkdir -p .artifacts/offline-feed
# Offline; falha explicitamente se faltar pacote no cache.
dotnet restore Ilmenyx.slnx -r linux-x64 -p:SelfContained=false -p:EnableRuntimePackDownload=false --source "$PWD/.artifacts/offline-feed" --disable-parallel -m:1
# Escolha destino que ainda não existe; não reutilize uma execução anterior.
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-local-017"
mkdir "$PWD/.artifacts/ilmenyx-extraido-017"
tar -xzf .artifacts/ilmenyx-local-017/Ilmenyx-linux-x64-local.tar.gz -C .artifacts/ilmenyx-extraido-017
bash scripts/test-linux-package.sh "$PWD/.artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64" "$PWD/.artifacts/ilmenyx-teste-017"
```

Em outro ambiente pode-se restaurar das fontes NuGet configuradas, removendo
--source offline, com rede e dependências explicitamente disponíveis. O script de
empacotamento sempre usa --no-restore. Restore inicial sem desabilitar download de
runtime falhou no cache vazio; flags acima foram verificadas para este bundle.

Executar o pacote extraído (com runtime acessível ao apphost; DOTNET_ROOT é
necessário quando instalado de forma isolada):

```bash
export DOTNET_ROOT=/tmp/silex-dotnet # somente esta sessão; ajuste para seu runtime
.artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64/cli/ilmenyx --help
.artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64/cli/ilmenyx info --json
ILMENYX_DISPLAY_BACKEND=x11 .artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64/desktop/Ilmenyx.Desktop
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland .artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64/desktop/Ilmenyx.Desktop
```

## Testes e evidências

scripts/test-linux-package.sh valida integridade, metadados, igualdade das licenças,
executáveis, CLI help/info/capabilities/sensors, JSON exportado igual ao stdout,
recusa de overwrite e dependências de apphosts/.so via ldd. Não inicia workloads
nem SMART. Falhas preservam saídas em diretório de teste novo. Argumentos inválidos
e destino existente foram recusados pelo empacotador sem alterar o archive.

O desktop apphost real foi iniciado separadamente em Wayland/X11, permaneceu ativo
por 4 s e foi encerrado por SIGTERM somente no processo criado pelo teste. Isso
valida startup, não interação. package-checks.json separa esse escopo dos testes UI.

Para testar interações com bibliotecas publicadas, foi copiado o desktop extraído
para um diretório separado e acrescentado SOMENTE Ilmenyx.UiSmoke.dll de teste.
O bundle original continua intacto. dotnet exec usa runtimeconfig/deps do desktop
publicado. ILMENYX_EXPECT_DESKTOP_DIR valida Assembly.Location para impedir
carregamento acidental de DLL de desenvolvimento; o log registra esse diretório.
O harness nunca entra no tar.gz. Exemplo após build do harness:

```bash
dotnet build tools/Ilmenyx.UiSmoke -c Release --no-restore -m:1 -p:UseSharedCompilation=false
mkdir .artifacts/ilmenyx-ui-teste-017
cp -a .artifacts/ilmenyx-extraido-017/Ilmenyx-linux-x64/desktop .artifacts/ilmenyx-ui-teste-017/desktop
cp tools/Ilmenyx.UiSmoke/bin/Release/net10.0/Ilmenyx.UiSmoke.dll .artifacts/ilmenyx-ui-teste-017/desktop/
# Executar a partir da raiz; APP_TEST_DIR é somente uma variável deste comando.
APP_TEST_DIR="$PWD/.artifacts/ilmenyx-ui-teste-017/desktop"
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland ILMENYX_EXPECT_DESKTOP_DIR="$APP_TEST_DIR" dotnet exec --runtimeconfig "$APP_TEST_DIR/Ilmenyx.Desktop.runtimeconfig.json" --depsfile "$APP_TEST_DIR/Ilmenyx.Desktop.deps.json" "$APP_TEST_DIR/Ilmenyx.UiSmoke.dll" "$PWD/.artifacts/smoke-package-wayland"
ILMENYX_DISPLAY_BACKEND=x11 ILMENYX_EXPECT_DESKTOP_DIR="$APP_TEST_DIR" dotnet exec --runtimeconfig "$APP_TEST_DIR/Ilmenyx.Desktop.runtimeconfig.json" --depsfile "$APP_TEST_DIR/Ilmenyx.Desktop.deps.json" "$APP_TEST_DIR/Ilmenyx.UiSmoke.dll" "$PWD/.artifacts/smoke-package-x11"
```

Build/testes da solução seguem comandos do README. Evidências em
[evidencias/ilmenyx-package](evidencias/ilmenyx-package): publish/restore/build/testes,
TRX, integridade, notices, hashes, ELF, dependências nativas, startup e regressões UI.
JSON de hardware/sensores fica somente em temporários locais, não foi commitado.

## Resultados finais

IMPLEMENTADO e VERIFICADO POR TESTE no CachyOS x64, kernel 7.2.11-1-cachyos,
SDK 10.0.100, Avalonia 12.1.4. Build Release da solução/harness sem erros/avisos,
279 testes aprovados, zero falhas/ignorados. Scripts passaram bash -n. Restore
framework-dependent offline e publish por RID passaram; bundle tar.gz cerca de
9,3 MiB. 19 pacotes runtime inventariados, sem runtime .NET/testes/WPF/drivers.

CLI extraída passou help/info/capabilities/sensors, JSON consistente, notices
idênticos e overwrite recusado. Arquivos/ELF e bibliotecas nativas examinados;
ldd não reportou dependência ausente no host. Cópia adulterada de NOTICE.md foi
recusada por hashes. Hash de archive permaneceu igual após tentativas inválidas.
Apphosts desktop reais permaneceram ativos nos dois backends no teste de startup.

Regressão gráfica com assemblies da cópia extraída passou em Wayland nativo
(DISPLAY removido, Avalonia.Wayland.WindowImpl) e X11 (Avalonia.X11.X11Window),
com código 0 e nove layouts por backend até 200%. Origem/MIT/dependências/termos
integrais, teclado, sensores, CPU/RAM/GameSim reduzidos e exportações passaram.
Assembly.Location foi conferido contra o diretório extraído de teste.

Archive de revisão preservado, ignorado pelo Git:
.artifacts/ilmenyx-0.17.0-local/Ilmenyx-linux-x64-local.tar.gz, acompanhado do hash.
Não é release, não foi instalado ou enviado a terceiros. Proveniência dirty e
SHA256SUMS nas evidências correspondem ao pacote testado antes do commit.

## Pendências

NÃO TESTADO: instalação em VM/máquina limpa, outras distros/glibc/arquiteturas,
runtime ausente em máquina limpa, pacote autocontido, .deb/.rpm/AppImage, integração
.desktop/menu, atualização/desinstalação, assinatura e pipeline de distribuição.
Build local e ldd no CachyOS não certificam essas condições. Bibliotecas carregadas
via dlopen podem exigir mais dependências que ldd não enumera; GUI real foi testada
somente na sessão presente. Matriz assistiva e de hardware continua em Sem_teste.md.

Sem publicação, push, PR, release, stress, instalação, sudo ou configuração de sistema.
Próxima tarefa: validar este bundle em VM Linux limpa quando disponível e fechar a
lista de dependências nativas/licenças antes de escolher formato de distribuição.


## Complemento de ambiente isolado — pacote 0.17.0

Sem QEMU/imagem guest pronta; VM limpa continua NÃO TESTADA (PKG-01 parcial).
Contêineres locais, sem root/rede/instalação: Debian 12 sem ICU bloqueou o modo
padrão; diagnósticos CLI passaram somente no teste invariant explícito, sem SDK.
Alpine 3.24.1 é incompatível com o bundle glibc; GUI no contêiner NÃO TESTADA e
fontconfig ausente no Debian. Preflight evita repetir aborts por ICU conhecida
ausente. Requisitos documentados; nada altera o modo padrão do aplicativo.
[Comandos, códigos de saída e limites](validacao-container.md).


## Atualização 0.18.0 — guarda ICU na CLI

IMPLEMENTADO e testado: pré-verificação nativa no adaptador Linux; falta de ICU
retorna código 5 antes da coleta, com mensagem e sem JSON parcial. Não é fallback
invariant nem certificação de versões/ABI. Build sem avisos/erros, 284 testes
aprovados; caso negativo real em Debian e modo normal no CachyOS passaram.
VM limpa, Debian padrão com ICU, GUI sem ICU e matriz adicional seguem NÃO TESTADOS.
Os aborts anteriores de 0.17.0 permanecem históricos. [Detalhes e comandos](icu-cli.md).


## Atualização 0.19.0 — doctor CLI

IMPLEMENTADO e testado: `doctor` textual, bibliotecas carregáveis e variáveis de
display não vazias/ausentes, sem provedores de inventário ou workloads. Build sem
avisos/erros e 296 testes aprovados. Host normal: código 0; Debian sem ICU: relatório
com código 5 sem abort; invariant explícito: código 0, sem certificação do modo padrão.
JSON/exportação do doctor indisponíveis; opções recusadas. VM limpa, Debian com ICU
e matrizes assistivas/de hardware seguem NÃO TESTADAS. [Evidências e limites](doctor-cli.md).


## Dependências por distribuição — pacote 0.25.0

Observado em VMs QEMU/KVM de imagens cloud oficiais (2026-10-21), x86_64, glibc. Evidências:
`docs/evidencias/ilmenyx-vm-{ubuntu-2404,debian,fedora}-0.25.0/`. Lista de bibliotecas testadas, **não**
uma garantia de suporte: outras versões, derivadas e escritórios gráficos reais não foram avaliados.

| Distro (imagem) | ICU | Fontconfig | X11 | Para testar com Xvfb |
|---|---|---|---|---|
| Ubuntu 24.04.5 | `libicu74` (já na imagem) | `libfontconfig1` (já na imagem) | `libx11-6` (já na imagem) | `xvfb`, `x11-utils`, `fonts-dejavu-core` |
| Debian 13 (trixie) | `libicu76` | `libfontconfig1` | `libx11-6` | `xvfb`, `x11-utils`, `fonts-dejavu-core` |
| Fedora 44 Cloud | `libicu` (77.1) | `fontconfig` | `libX11` | `xorg-x11-server-Xvfb`, `xwininfo`, `xauth`, `dejavu-sans-fonts` |

Pacotes de Xvfb só servem para teste automatizado sem sessão gráfica; usuários com desktop não precisam deles.

**Runtime .NET.** O bundle é framework-dependent e exige Microsoft.NETCore.App 10.0. Nos testes o runtime
10.0.0 veio do arquivo oficial do SDK, copiado para a VM e apontado por `DOTNET_ROOT`. Pacotes `dotnet-runtime`
das distros **não foram testados** aqui; instale o runtime pelo método que você aprovar, nunca por instalação
automática do Ilmenyx.

**ICU é requisito.** Sem ICU instalado, o runtime .NET termina com "Couldn't find a valid ICU package"
(código 134) ainda no início, antes do preflight do Ilmenyx; `validate-linux-machine.sh` registra BLOQUEADO.
Não habilite `System.Globalization.Invariant` só para contornar: o modo invariant não aprova a globalização
padrão. Instale o pacote da tabela e rode `ilmenyx doctor`.

**Observações.** `ilmenyx doctor` listou EGL como não encontrada nas VMs sem GPU; a janela abriu sob Xvfb mesmo
assim, o que não prova renderização em sessão real. Fedora rodou com SELinux em Enforcing sem ajustes.
Nomes de pacote foram os usados nos testes; versões e nomes futuros podem mudar.
NÃO TESTADO: Wayland em compositor real, GPU, Arch/openSUSE/Alpine (musl), ARM64, runtime empacotado pela distro.


## Componentes integrados (0.30.0)
O pacote traz `components/` com MangoHud 0.8.4 (bibliotecas 64 bits, MIT) e OpenRGB 1.1 (AppImage, GPL-2.0, programa separado), baixados por `scripts/fetch-components.sh` com SHA-256 fixado (feito na montagem do pacote, nunca pelo app). O Ilmenyx gera wrappers na própria pasta de dados (`$XDG_DATA_HOME/ilmenyx/components/`): `mangohud` (LD_PRELOAD do shim + manifesto de camada Vulkan com caminho absoluto), `openrgb` (AppImage com extração automática, sem FUSE) e `gamemoderun` (reserva embutida: ionice/renice do usuário). Programa do sistema tem prioridade. `ilmenyx components [--check-openrgb]` mostra a origem de cada um e, só com a opção, inicia o servidor em 127.0.0.1 por alguns segundos.
Flatpak não é embutível; a página Apps mostra o comando da distribuição. O OpenRGB embutido é um aplicativo Qt e precisa de bibliotecas gráficas de qualquer desktop (libEGL, libOpenGL, harfbuzz); numa instalação mínima sem elas o app mostra a biblioteca que falta (VM Ubuntu sem elas: erro 127 reproduzido; com elas: servidor iniciou e respondeu). Itens não verificados: CMP-01..04 em `Sem_teste.md`.

Validação do 0.30.0 nas VMs (evidências em `docs/evidencias/ilmenyx-vm-*-0.30.0/`): Ubuntu 24.04, Debian 13 e Fedora 44 → `validate-linux-machine.sh` IMPLEMENTED_TESTED e janela viva por 14 s; `ilmenyx components` mostra mangohud/openrgb "pacote" e gamemoderun "embutido" (nenhum existe no sistema dessas VMs). Servidor OpenRGB embutido: iniciou só no Ubuntu (após instalar as bibliotecas gráficas); Debian/Fedora mínimos não têm libusb/libOpenGL e o app informou a biblioteca. Uma primeira rodada falhou porque a versão 0.29.0 estava escrita no código do inventário (corrigido) e porque a checagem `ldd` incluía as bibliotecas de components/ (agora separada em `components-dependencies.txt`).

## Validar GPU AMD/NVIDIA numa máquina emprestada (sem custo)
`scripts/validate-gpu-remote.sh /pacote/Ilmenyx-linux-x64 /saida-nova [--allow-gpu-load]`: a fase 1 só lê (GPUs, sensores, extensões Vulkan de ray tracing); a fase 2 (cargas de 8 s com `bench gpu-burn`/`gpu-scene`/`gpu-snapshot`) só roda com a flag e com o dono da máquina de acordo. Sem root, sem instalação, sem rede. Os relatórios voltam para `Sem_teste.md` como evidência (GPU-04..08).

Arch Linux e openSUSE Tumbleweed (VMs mínimas, 2026-10-22): `validate-linux-machine.sh` IMPLEMENTED_TESTED e janela viva por 14 s nas duas. Na Arch, sem `libvulkan` instalada, `bench gpu-list` abortava com exceção não tratada e a janela caía; corrigido para mostrar "biblioteca Vulkan não encontrada" (rc=1 no CLI). Pré-requisitos instalados nas VMs antes do teste: jq, ripgrep, xvfb, libxkbcommon, fontconfig (e diffutils no openSUSE). O OpenRGB embutido não iniciou nelas por falta de libharfbuzz (mensagem correta no app). openSUSE tem llvmpipe (Vulkan por CPU) e a Arch não tinha loader.
