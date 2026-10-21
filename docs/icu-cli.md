# Falta de ICU na CLI — Ilmenyx 0.18.0

Base: d23bfe3, branch feat/ilmenyx-linux. A procura por um ambiente local com ICU
compatível com Debian não encontrou imagem pronta. Bibliotecas ICU do CachyOS não
foram misturadas ao Debian para fabricar um aceite de distro. VM limpa e modo
padrão Debian com ICU continuam pendentes. O incremento corrige o erro real
reproduzido na etapa anterior, sem instalar componentes.

## IMPLEMENTADO

LinuxRuntimeDependencies, no adaptador Linux, verifica disponibilidade de pares
libicuuc/libicui18n antes dos diagnósticos CLI. Usa NativeLibrary.TryLoad/Free,
nomes sem versão e sonames 60–99, sem shell, arquivo de hardware, rede ou elevação.
Os handles adquiridos pelo teste de carga da biblioteca são liberados. Sufixos
ASCII são montados sem depender de globalização: chamar APIs culturais antes do
preflight poderia reproduzir o FailFast que se quer evitar.

Na falta das bibliotecas, info/capabilities/sensors e demais rotas de diagnóstico
protegidas retornam código 5, mensagem explícita em stderr e nenhum JSON parcial,
antes da coleta/exportação. Help mantém código 0. A rota sim-plan já retorna antes
dessa guarda, sem executar workload; esta etapa não a converte em teste de carga.
Erros de argumentos continuam 2, plataforma não suportada 3, falhas de coleta/IO 1
e cancelamento 130. Não se alteram algoritmo, schema, scores ou dados de hardware.

Invariant permanece exclusivamente opt-in via configuração/env do runtime. A guarda
reconhece o switch AppContext e a variável DOTNET_SYSTEM_GLOBALIZATION_INVARIANT;
não escreve essas configurações, não ativa fallback silencioso e não instala ICU.
GUI não recebeu essa guarda: ausência de ICU/bibliotecas gráficas na GUI fora do
host continua pendente. Encontrar bibliotecas não certifica compatibilidade completa
entre runtime, ABI, símbolos, versões e culturas. App-local ICU, sonames especiais,
versões fora da faixa e bibliotecas incompatíveis exigem validação adicional;
o preflight pode ser conservador e não garante prevenção de todo FailFast possível.

## Verificação de código e execução real

Cinco casos novos: invariant não consulta a biblioteca, par ausente/incompleto
ou com versões diferentes não é aceito, pares sem versão/.72/.78 são reconhecidos.
Fixtures aprovam controle do componente, não certificam esses binários ICU em
hardware. Integração real usa ICU existente no CachyOS e ausência real no Debian.

- Build solução/harness sem avisos ou erros; 284 testes aprovados, zero falhas/ignorados.
- Novo pacote local linux-x64 0.18.0: integridade, notices, CLI factual, sensores,
  JSON, dependências nativas e overwrite recusado passaram no CachyOS.
- CLI CPU/RAM/GameSim reduzidos concluíram sem score e com versão 0.18.0. Nenhum
  perfil full ou workload sustentado foi executado.
- Debian 12 da imagem local sem ICU, UID não-root, sem rede/SDK: help=0;
  info/capabilities/sensors=5, mensagens esperadas, stdout vazio, info.json inexistente.
  Aborts 134 anteriores não reapareceram nos três casos do pacote 0.18.0.
- Regressão dos assemblies extraídos passou em Wayland nativo (DISPLAY removido,
  Avalonia.Wayland.WindowImpl) e X11 (Avalonia.X11.X11Window), com nove casos de
  layout por backend até 200%, exportações e documentos integrais. Código 0 em ambos.
- Imagem Debian com invariant explicitamente habilitado: diagnósticos/JSON passaram.
  Isso não aprova globalização padrão Debian nem a GUI nessa imagem.

O script de contêiner ganhou ILMENYX_CONTAINER_TEST_ICU_GUARD=1 para exercitar a
guarda do aplicativo em vez de bloquear antes da execução. Só aceita pacote 0.18.0,
para não reiniciar inadvertidamente os builds antigos que abortavam. Sem esse opt-in,
a verificação prévia de ICU permanece. O modo de teste retorna 0 somente se os três
comandos retornarem 5, mensagens forem as esperadas e não houver saída JSON parcial;
isso significa teste negativo aprovado, não inventário bem-sucedido sem ICU.

Runtime/kernel/Avalonia da sessão: .NET 10.0.0, SDK 10.0.100, CachyOS x64
7.2.11-1-cachyos, Avalonia 12.1.4. Contêiner é userland Debian 12 de imagem local
Supabase postgres-meta; identidade completa nas evidências. Não é VM limpa e
compartilha kernel com o host. Nenhuma instalação/pull foi executada.

## Reproduzir

```bash
cd /mnt/HDD/Github/Ilmenyx
. scripts/env-local-sdk.sh
dotnet build Ilmenyx.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore -m:1
# Destino deve ser novo; restore por RID conforme pacote-linux-x64.md se necessário.
bash scripts/package-linux-x64.sh "$PWD/.artifacts/ilmenyx-018-repro"
```

O pacote já gerado está em .artifacts/ilmenyx-0.18.0-local/Ilmenyx-linux-x64-local.tar.gz,
com hash ao lado. Apenas revisão local, não release. Proveniência dirty no metadata
registra que foi construído antes do commit; não se afirma archive reproduzível.
Use scripts/test-linux-package.sh e o roteiro de runtime sem SDK em
validacao-container.md. Com bundle 0.18.0 extraído e runtime preparados:

```bash
# Substitua por diretórios absolutos reais; saída precisa ser nova.
ILMENYX_CONTAINER_TEST_ICU_GUARD=1 bash scripts/test-container-linux-package.sh 145325b7e39f /caminho/bundle/Ilmenyx-linux-x64 /caminho/runtime-sem-sdk /caminho/saida-nova
```

[Logs, TRX e resultados](evidencias/ilmenyx-icu-guard). JSON de inventário fica em
arquivos temporários locais e não foi commitado. A mensagem e os códigos negativos
não contêm detalhes de hardware. Scripts passam bash -n e diffs git diff --check.

## Limites e próxima etapa

NÃO TESTADO: VM limpa, Debian padrão com ICU instalada, GUI sem ICU, outras versões
ICU/ABI/sonames, globalização em todas as culturas, outras distros/ISAs e leitores
de tela. Alpine/musl continua incompatível com o RID glibc atual. PKG-01 e GUI-01/02
seguem parciais. Revisões de direitos/marca/redistribuição continuam PENDENTES;
material de direitos desconhecidos segue BLOQUEADO para redistribuição.

Próximo aceite externo permanece: guest Linux com .NET 10 e ICU, sem instalação
automática pelo agente. Enquanto isso, um próximo incremento independente possível
é um diagnóstico explícito de requisitos por CLI, reaproveitando esta guarda,
com saída factual e limites claros, sem iniciar inventário ou benchmarks.
