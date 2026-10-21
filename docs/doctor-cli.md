# Diagnóstico de requisitos — Ilmenyx 0.19.0

Base: 4676f47, branch feat/ilmenyx-linux. Revisados CLI/parser, preflight Linux,
projetos, scripts, instruções, README e licença. Nenhum algoritmo, fixture golden,
schema de inventário ou módulo Windows foi modificado.

## IMPLEMENTADO

Comando `doctor`, somente texto, sem argumentos adicionais. Executa antes da
verificação que bloqueia os comandos de inventário quando falta ICU, para poder
explicar essa ausência. Não instancia provedores de hardware/sensores, SMART,
renderizadores ou runners. Não abre conexão X11/Wayland nem executa ferramentas
externas, workloads ou instaladores. O runtime e os loaders ainda podem ler
arquivos/recursos para sua própria inicialização; isso não significa inventário.

LinuxRuntimeDependencies.Inspect retorna fatos de bibliotecas nativas carregáveis:
glibc, par ICU, fontconfig, X11 e EGL. NativeLibrary.TryLoad/Free e busca ICU da
etapa anterior são reutilizados. O DTO específico fica no adaptador Linux;
Core/Contracts não recebem dependências da UI ou APIs nativas. DoctorCommand
formata strings fixas/ASCII sem APIs culturais, JSON, datas ou formatação numérica,
para funcionar também sem ICU. Nome/assinatura: Ilmenyx, by Bismuth Technology.

DISPLAY/WAYLAND_DISPLAY são informadas apenas como valor não vazio/ausente; seus
conteúdos não são exibidos, evitando registrar nomes/caminhos arbitrários. Presença
da variável e biblioteca carregável NÃO provam conexão, driver, fontes, desempenho
ou compatibilidade completa da ABI. Doctor declara que não testou a GUI.

Saídas:

- 0: glibc carregável e ICU encontrada, ou invariant solicitado explicitamente.
- 5: requisito básico da CLI não encontrado/carregável. O relatório textual ainda
  é emitido. Bibliotecas gráficas ausentes não bloqueiam a CLI sem interface.
- 2: opção inválida, como --json, --html, --output ou flags de workload/dispositivo.
- 3: plataforma não Linux, mantendo a proteção existente.

`doctor --json`/exportação não são funcionalidades disponíveis neste incremento;
são recusados antes da sondagem. Para guardar o texto pode-se usar redirecionamento
normal do shell por escolha do usuário. `info` e outros comandos mantêm seus JSONs
versionados e guarda ICU. O novo membro de CliOperation foi acrescentado ao final,
preservando os valores numéricos dos membros anteriores. Não é um novo schema.

## Compilar e executar

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- doctor
```

Pacote local já gerado, somente para revisão:
.artifacts/ilmenyx-0.19.0-local/Ilmenyx-linux-x64-local.tar.gz. Exige runtime .NET 10
Linux x64, como no pacote anterior. Após extrair, com DOTNET_ROOT configurado se o
runtime for isolado, execute `./Ilmenyx-linux-x64/cli/ilmenyx doctor`.
Geração/validação seguem pacote-linux-x64.md; destino deve ser novo. Não se publicou
release nem se instalou o pacote. Proveniência dirty no metadata registra geração
antes do commit; não se promete archive reproduzível.

## Testes reais e automatizados

296 testes aprovados, zero falhas/ignorados; build da solução/harness sem erros ou
avisos. Casos novos: parse seguro, opções recusadas, glibc/ICU ausentes/presentes,
invariant explícito, bibliotecas gráficas opcionais e ausência de alegação de GUI
funcional. Probes unitários são fixtures, não certificação de uma biblioteca real.

No CachyOS x64 (kernel 7.2.11-1-cachyos, SDK 10.0.100/runtime 10.0.0): doctor=0,
bibliotecas carregáveis e variáveis de display não vazias. `doctor --output` retorna
2 e não cria arquivo. O pacote extraído passou integridade/notices/CLI/JSON/sensores,
recusa de overwrite e ldd. Sem stress ou perfis full.

Na imagem local Debian 12 sem ICU/SDK e sem rede, usuário não-root: doctor=5, com
relatório legível de ICU/fontconfig/X11/EGL indisponíveis, sem abort. Info/sensores/
capacidades continuam retornando 5 na guarda. No mesmo ambiente com invariant
explicitamente solicitado, doctor=0, mantendo aviso de que isso não certifica
globalização padrão; os diagnósticos/JSON anteriores passaram. Nenhuma biblioteca
foi instalada para o teste. Contêiner não equivale a VM limpa e compartilha kernel.

O script de contêiner aceita ILMENYX_CONTAINER_TEST_DOCTOR=1 somente para 0.19.0.
A verificação negativa da guarda aceita 0.18.0/0.19.0, preservando proteção contra
aborts dos builds mais antigos. Reproduzir após preparar bundle/runtime sem SDK
conforme validacao-container.md:

```bash
ILMENYX_CONTAINER_TEST_DOCTOR=1 ILMENYX_CONTAINER_TEST_ICU_GUARD=1 bash scripts/test-container-linux-package.sh 145325b7e39f /caminho/bundle/Ilmenyx-linux-x64 /caminho/runtime-sem-sdk /caminho/saida-nova
```

[Evidências](evidencias/ilmenyx-doctor): build/tests/TRX, publish, stdout doctor,
casos negativos, identidades/estados do contêiner, pacote e smokes UI. JSON de
hardware não foi commitado. Rebuild final preservou a numeração do enum; conteúdo
publicado do desktop foi comparado byte a byte com o usado no smoke Wayland
(desktop-identity.json), e a CLI final foi revalidada no host e nos contêineres.

Regressões dos assemblies empacotados passaram em Wayland nativo com DISPLAY
removido (Avalonia.Wayland.WindowImpl) e X11 (Avalonia.X11.X11Window), código 0,
nove layouts por backend até 200%, com teclado, documentos e exportações reais.
No rebuild final apenas a CLI mudou para preservar os valores anteriores do enum;
todos os arquivos publicados do desktop eram byte a byte idênticos aos do teste.

## Pendências e limites

NÃO TESTADO: VM limpa, Debian padrão com ICU, GUI Debian sem dependências, Alpine/
musl para este RID glibc, outras versões/ABI/sonames, leitores de tela, teclado
físico e matriz adicional de DPI/displays. Doctor não funciona se o próprio runtime
ou o apphost não conseguir iniciar; não é instalador nem substitui o diagnóstico
dessas camadas. Sonames ICU especiais/fora de 60–99 e bibliotecas incompatíveis
continuam sujeitos aos limites do preflight anterior.

PENDENTE DE REVISÃO: usabilidade humana e checklist jurídico/marca anterior à
publicação. Material de direitos desconhecidos continua BLOQUEADO para
redistribuição. MIT, créditos e SHA upstream preservados. Sem root, push, PR,
release, modificações de sistema ou instalação automática.

Próxima melhoria independente possível: integrar a informação de requisitos ao
fluxo de ajuda/Sobre da interface, sem fazer sondagens ao abrir o aplicativo ou
habilitar funcionalidades gráficas com base apenas na presença de bibliotecas.
A validação externa em VM com ICU continua necessária para PKG-01.
