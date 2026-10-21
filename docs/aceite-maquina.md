# Aceite do pacote em outra máquina

IMPLEMENTADO e executado no CachyOS: `scripts/validate-linux-machine.sh`. Este
incremento é de ferramentas de validação; produto continua 0.20.0. Não certifica
VM limpa automaticamente. O processo não instala dependências nem abre GUI,
executa benchmarks, SMART, root ou configurações do sistema.

## Preparação pelo responsável do ambiente

Use máquina/guest Linux x86_64 glibc com .NET 10 e ICU já disponíveis. Para GUI,
fontconfig/fontes e bibliotecas do backend também precisam estar disponíveis.
Não use o bundle linux-x64 como certificação de Alpine/musl. A procedência, distro,
versão do runtime e condição de VM limpa precisam ser confirmadas pelo responsável.

Copie o bundle extraído e estes dois scripts mantendo-os na mesma pasta:

- `scripts/validate-linux-machine.sh`
- `scripts/test-linux-package.sh`

Não é necessário levar o SDK, fonte C#, upstream, cache NuGet ou harness para o
aceite CLI. O roteiro usa Bash, jq, rg, sha256sum, timeout, uname, ldd, cmp, cp e
sort; todos devem estar instalados previamente. Hash local prova integridade,
não autenticidade, titularidade ou autorização para redistribuir.

```bash
# Use caminhos absolutos reais e um destino que ainda não existe.
# DOTNET_ROOT só é necessário se o runtime estiver em local isolado.
env DOTNET_ROOT=/caminho/runtime bash /caminho/scripts/validate-linux-machine.sh /caminho/Ilmenyx-linux-x64 /caminho/resultado-novo
```

Em instalação padrão pode omitir `env DOTNET_ROOT=...`. O roteiro funciona sem SDK
se o apphost conseguir localizar o runtime. Não habilita invariant por conta
própria. Se o ambiente já o habilitou, o doctor registra isso; esse teste não aprova
globalização padrão. Confira doctor.txt antes de concluir o aceite.

## Saídas e interpretação

- `result.json`: resultado estruturado, escopo CLI e flags de GUI/VM/hardware
  sempre falsas. Não representa aprovação humana.
- `package.json`, `os-release.txt`, `kernel.txt`, `libc.txt`: metadados da plataforma
  observada e pacote; sem coleta intencional de hostname, serial ou valores de env.
- `integrity.txt`: conferência SHA256 antes de qualquer comando do aplicativo.
- `doctor.txt`, `doctor.stderr`, `doctor-exit-code.txt`: requisitos básicos CLI;
  prazo de 15 s, com término forçado após mais 2 s se necessário.
- `package-check/`: smoke existente com dados reais, JSON exportado, sensores,
  recusa de overwrite, notices e dependências nativas. Prazo de 90 s (+2 s).

| Código | Significado |
|---|---|
| 0 | Smoke CLI passou nesta máquina; não certifica GUI, VM limpa ou hardware adicional |
| 1 | Integridade/smoke falhou, timeout do smoke ou erro operacional; conferir logs |
| 2 | Argumento, ferramenta ou pré-requisito do roteiro inválido |
| 3 | Doctor não confirmou requisitos, inclusive runtime ausente/timeout; não inicia inventário |

Erros anteriores à criação da saída, como diretório existente ou sem permissão,
podem terminar sem result.json. Não reutiliza/remove diretórios existentes.
Falhas preservam evidência parcial. Após integridade aprovada, o smoke coleta
inventário e sensores reais somente para um diretório novo. Revise esses dados
antes de compartilhar; não os envie automaticamente ao GitHub.

## Testes realizados nesta sessão

Bundle local 0.20.0, CachyOS x64/KWin, runtime 10.0.0 do SDK isolado da sessão:

| Caso real | Resultado |
|---|---|
| Roteiro normal no host | 0, CLI/JSON/sensores/notices/ldd/overwrite passaram |
| Dois scripts copiados fora do repositório, PATH padrão e runtime-only sem SDK | 0, mesmo smoke passou; lista de SDKs vazia |
| DOTNET_ROOT e DOTNET_ROOT_X64 apontando para diretório vazio novo | 3; apphost informou runtime ausente, sem inventário |
| Pacote copiado e package.json alterado | 1; falha de hash, doctor não executado |
| Caminho relativo | 2, recusado |
| Destino existente | 1, recusado sem sobrescrever |
| Bash -n | Passou |

[Evidências](evidencias/ilmenyx-aceite-maquina). JSON de inventário/sensores permanece
somente em temporários locais. O teste usa pacote e scripts; não houve alterações
C# ou nova execução da suíte C# (301 testes aprovados na etapa anterior).

Validação gráfica do pacote extraído: Wayland nativo passou; resultado X11
registrado na conclusão abaixo. Copiou-se desktop para
um diretório temporário separado e acrescentou-se somente Ilmenyx.UiSmoke.dll.
O harness usa runtimeconfig/deps do desktop publicado e exige Assembly.Location
igual ao diretório da cópia. O pacote original e tar.gz permanecem intactos;
o harness não integra a distribuição. Testes gráficos sequenciais, sem injeção
global de teclado/mouse, incluem somente validações reduzidas e exports locais.

## Pendências

NÃO TESTADO: outra distro com ICU no modo padrão, VM limpa, Orca/AT-SPI, teclado
físico e matriz de hardware/DPI. Nesta sessão não foram encontrados qemu/virsh/
VirtualBox ou imagens guest nos locais examinados; /dev/kvm isolado não basta.
Não foram feitos download ou instalação para criar esse ambiente.
PKG-01 permanece parcial. Contêiner Debian sem ICU anterior cobre somente o caso
negativo; não substitui VM/dispositivo. AMD/NVIDIA mantêm aviso de testes reais
ausentes. PENDENTE DE REVISÃO: aceite humano e licenças/marca antes da distribuição;
material sem direitos conhecidos continua BLOQUEADO para redistribuição.


## Redimensionamento no harness

A primeira execução Wayland encontrou novamente timeout no mínimo da janela Sobre
(200%); log preservado em wayland-first-timeout.txt. O harness agora solicita um
tamanho distinto e depois o mínimo, com até três tentativas limitadas e diagnóstico
do tamanho recebido. Exige as mesmas tolerâncias anteriores (<1 DIP) em ambas as
dimensões e confirmação após estabilização; não reduz fonte ou altera o produto.
Causa do timeout original não confirmada. Repetição completa Wayland passou; isso
não prova eliminação definitiva de intermitências do backend/compositor.


## Conclusão gráfica do pacote

Wayland nativo (DISPLAY removido, Avalonia.Wayland.WindowImpl) e X11
(Avalonia.X11.X11Window) passaram sequencialmente usando o desktop publicado.
Teclado roteado, licenças, requisitos, exports JSON/HTML, cancelamentos,
validações reduzidas e 12 layouts por backend com texto 100/150/200% passaram.
Todos os arquivos do desktop copiado foram comparados por SHA256 ao bundle;
somente Ilmenyx.UiSmoke.dll foi acrescentado. Capturas dos novos mínimos a 200%
e listas vazias de erros estão nas evidências. Não é teste de teclado físico,
leitor de tela, outra distro ou VM limpa. Nenhum código do produto foi alterado.


## Complemento: resultado estruturado 1.1

O roteiro agora registra modo de globalização, versão do pacote, runtime informado
pela CLI e códigos de saída separados. Formato de doctor desconhecido/ambíguo
bloqueia coleta; versão do inventário divergente dos metadados reprova o smoke.
Invariant permanece um teste distinto, com aviso e flag padrão falsa.
[Regras, testes reproduzíveis e evidências](aceite-globalizacao.md).
