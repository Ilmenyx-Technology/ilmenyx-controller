# Validação complementar em contêiner — pacote Ilmenyx 0.17.0

Base: 6121b4e, branch feat/ilmenyx-linux. Produto C# inalterado; não se criou uma
nova versão do aplicativo. Inspecionados pacote, scripts de validação, instruções,
README e pendências. Não há QEMU/virsh ou imagem guest pronta no ambiente. /dev/kvm
existe, mas isso não fornece uma VM. systemd-nspawn não foi usado com elevação.
VM/máquina limpa continua NÃO TESTADA.

## Ambiente usado e isolamento

Docker local já acessível ao usuário e imagens de serviços já presentes no cache:

- 145325b7e39f: postgres-meta Supabase, userland Debian 12/bookworm.
- 1736a63078f5: gotrue Supabase, userland Alpine 3.24.1/musl.

Identidades completas em images.txt/image.json nas evidências. Não são imagens
limpas oficiais das distros e não são dependências do produto. Não houve pull,
instalação ou redistribuição dessas imagens. Outras imagens locais examinadas
não ofereceram um ambiente glibc com ICU disponível. Nenhum serviço Supabase foi
iniciado: entrypoint substituído por /bin/sh e apenas comandos de diagnóstico.
Nenhum contêiner existente, volume de serviço ou configuração Docker foi alterado.

Novo script scripts/test-container-linux-package.sh exige imagem local explícita,
bundle/runtime/saída absolutos, saída nova e usuário host não-root. Resolve imagem
para ID local e recusa endpoint Docker remoto. Contêiner com UID/GID do usuário,
network=none, rootfs somente leitura, capabilities removidas, no-new-privileges,
limites 512 MiB/1 CPU/128 PIDs e tmpfs temporário. Bundle e runtime montados somente
leitura; único bind gravável é diretório de resultados novo. Sem sockets de GUI,
/dev do host ou Docker, privilégios elevados, acesso ao repositório ou instalação.
Docker usa o serviço local existente; o processo no contêiner não executa como root.
--rm remove somente os contêineres efêmeros criados pelo teste, não imagens/volumes
preexistentes. Não se fazem prune, mudança de daemon ou download automático.

Runtime isolado preparado copiando apenas dotnet, host/ e shared/ do SDK de teste
para temporário. Não há sdk/ nesse diretório. O teste verifica --list-sdks vazio.
Runtime continua 10.0.0, somente para esta sessão. Não compõe o bundle do produto.
Imagens compartilham kernel com o host; /proc e /sys podem refletir informação do
host ou estar restritos. Inventário do contêiner não certifica hardware de outra
máquina, nem limites de recursos do contêiner como capacidade física.

## Resultados observados

| Caso | Resultado real |
|---|---|
| Debian 12 padrão sem ICU | Runtime/lista de SDKs/help iniciaram; info/capabilities/sensors abortaram com código 134 e erro de ICU. Não aprovado no modo padrão. |
| Debian 12 após preflight | Script detecta ICU não disponível, não repete diagnósticos que abortaram e retorna 3 (dependência bloqueante). |
| Debian 12 com invariant explicitamente habilitado só no teste | CLI help/info/capabilities/sensors concluíram com código 0, exportação JSON igual ao stdout, schema 1.2/1.1 e versão 0.17.0, sem SDK. |
| Alpine 3.24.1 | Runtime/apphost glibc não iniciam, código 127; script retorna 4. Bundle linux-x64 incompatível, sem certificação linux-musl-x64. |
| GUI nessas imagens | NÃO TESTADA: sem display/socket e fontconfig ausente no Debian segundo ldd. Não houve instalação para contornar. |

A primeira execução negativa terminou por FailFast/abort do runtime; stderr foi
preservado em trecho mínimo. Preflight foi acrescentado para não repetir esse
caminho conhecido. Não se alteraram configurações de dumps/OS. O modo invariant é
ativado por ILMENYX_CONTAINER_INVARIANT=1 no script, propagado somente ao ambiente
do contêiner como DOTNET_SYSTEM_GLOBALIZATION_INVARIANT. Não é default do produto,
não modifica runtimeconfig e não substitui a validação normal com ICU. Casing,
locales e globalização nesse modo não foram certificados pela suíte unitária.

O empacotador passou a declarar explicitamente glibc/ICU e fontconfig no LEIA-ME
que gera, além da incompatibilidade Alpine do RID atual. Nenhum novo archive foi
gerado nesta etapa: o pacote 0.17.0 anterior mantém seu hash e README genérico;
consulte esta nota de requisitos. Não se afirma suporte a Debian padrão sem ICU.

## Comandos reproduzíveis

Requer Docker local já acessível sem sudo, imagens já presentes, bundle extraído
e runtime .NET 10 Linux x64 sem SDK. Nunca executar instaladores automaticamente.
IDs abaixo correspondem ao cache observado, não são comandos de pull.

```bash
cd /mnt/HDD/Github/Ilmenyx
# Prepare destinos novos. Ajuste os caminhos ao runtime e bundle locais.
TEST_ROOT=$(mktemp -d /tmp/ilmenyx-container-manual.XXXXXX)
mkdir "$TEST_ROOT/runtime" "$TEST_ROOT/extracted"
cp -a /tmp/silex-dotnet/dotnet /tmp/silex-dotnet/host /tmp/silex-dotnet/shared "$TEST_ROOT/runtime/"
tar -xzf .artifacts/ilmenyx-0.17.0-local/Ilmenyx-linux-x64-local.tar.gz -C "$TEST_ROOT/extracted"
bash scripts/test-container-linux-package.sh 145325b7e39f "$TEST_ROOT/extracted/Ilmenyx-linux-x64" "$TEST_ROOT/runtime" "$TEST_ROOT/debian-default"
# Saída esperada 3 nesta imagem sem ICU; não indica aprovação do produto.
ILMENYX_CONTAINER_INVARIANT=1 bash scripts/test-container-linux-package.sh 145325b7e39f "$TEST_ROOT/extracted/Ilmenyx-linux-x64" "$TEST_ROOT/runtime" "$TEST_ROOT/debian-invariant"
bash scripts/test-container-linux-package.sh 1736a63078f5 "$TEST_ROOT/extracted/Ilmenyx-linux-x64" "$TEST_ROOT/runtime" "$TEST_ROOT/alpine"
# Saída esperada 4 para runtime/apphost incompatível.
```

Código 0 exige todos os casos CLI com sucesso, schemas/JSON válidos e SDK ausente.
Código 3 indica dependência ICU não detectada; 4, runtime/apphost não iniciado;
2 é argumento/ambiente inválido; outras falhas de comandos/asserts não são aprovação.
O script é coleta/validação CLI e não exige bibliotecas gráficas para retornar 0.
ldd gráfico é registrado separadamente. Preflight usa o cache ldconfig; um sistema
sem esse mecanismo pode exigir revisão manual da detecção de ICU.

## Evidências e aceite

[evidencias/ilmenyx-container](evidencias/ilmenyx-container): probes, IDs, execução
negativa original, estados com preflight, execução invariant, runtimes/SDKs, erros
mínimos de ICU/musl e dependências nativas. Arquivos de hardware JSON e runtime
copiado permanecem em temporários locais; não foram commitados. Bash -n passou
para o script novo e o empacotador. Os próprios testes de contêiner foram executados;
não se repete nem se atribui uma nova execução dos 279 testes C# da etapa anterior,
pois aplicativo, algoritmos e bibliotecas C# não foram alterados.

IMPLEMENTADO e testado: protocolo de contêiner e explicitação dos requisitos.
Aceite técnico apenas para coleta negativa e CLI invariant na imagem observada.
BLOQUEADO para aceite padrão Debian nesta imagem: ICU ausente. NÃO TESTADO: VM
limpa, Debian com ICU no modo padrão, GUI Debian, outra arquitetura/glibc, pacote
musl, hardware adicional e tecnologias assistivas. Revisões de licenças/marca e
redistribuição continuam PENDENTES; material de direitos desconhecidos permanece
BLOQUEADO para redistribuição. Sem push, release ou publicação.

## Próxima validação em VM/dispositivo

Disponibilizar guest Linux limpo com runtime .NET 10 e ICU já configurados pelo
responsável do ambiente. Confirmar glibc, ICU e bibliotecas de GUI/fontconfig antes
do teste; não instalar com root automaticamente. Transferir o bundle, conferir hash
e executar scripts/test-linux-package.sh. Com display, seguir o smoke de assemblies
extraídos em pacote-linux-x64.md. Registrar distro, runtime, bibliotecas, flags,
modo padrão, código de saída e limitações de hardware. Esse resultado, não os
contêineres atuais, poderá fechar o recorte de instalação limpa em PKG-01.
