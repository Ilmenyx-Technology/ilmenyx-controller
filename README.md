# Ilmenyx

**Diagnóstico, monitoramento e benchmark de hardware para Linux.**
Por **Bismuth Technology**.

![versão](https://img.shields.io/badge/vers%C3%A3o-0.30.0-blue)
![plataforma](https://img.shields.io/badge/plataforma-Linux%20x64-informational)
![.NET](https://img.shields.io/badge/.NET-10-512bd4)
![status](https://img.shields.io/badge/status-experimental-orange)

O Ilmenyx mostra o que há dentro do seu computador (CPU, memória, GPU, discos, sensores, bateria,
rede) e mede o desempenho dele. Tem um **aplicativo desktop** e uma **linha de comando** (`ilmenyx`).

> **Status:** versão experimental, ainda sem lançamento público. O repositório é privado.

---

## Sumário

- [O que ele faz](#o-que-ele-faz)
- [Princípios](#princípios)
- [Como usar](#como-usar)
- [Começando](#começando)
- [Usando a linha de comando](#usando-a-linha-de-comando)
- [Benchmarks](#benchmarks)
- [O que ainda não funciona](#o-que-ainda-não-funciona)
- [Confiabilidade: o que ainda não é muito confiável e como melhorar](docs/CONFIABILIDADE.md)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Documentação](#documentação)
- [Privacidade e segurança](#privacidade-e-segurança)
- [Licença e créditos](#licença-e-créditos)

---

## O que ele faz

| Área | O que você encontra |
|---|---|
| **Diagnóstico** | Visão geral ao vivo, sistema, sensores, GPU, discos (com SMART importado), energia/bateria, rede e processos |
| **Jogos** | Biblioteca (Steam, Lutris, Heroic), compatibilidade (ProtonDB, Steam Deck, anti-cheat e relatos de quem tem hardware parecido), estimativa de FPS e análise de gargalo |
| **Desempenho** | Benchmarks de CPU, memória, GPU (Vulkan) e disco; verificador de overclock; overlay; testes de estresse |
| **Maker** | Portas seriais e drivers de placas (CH340, CP210x, FTDI) |
| **Ferramentas** | Otimizações reversíveis, RGB via OpenRGB, apps de inicialização, instalação de apps (Flatpak), relatórios HTML/JSON |

O app tem tema claro e escuro, e todos os relatórios são gerados localmente.

## Princípios

- **Somente leitura por padrão.** Lê `/proc`, `/sys` e `/etc`; não altera o sistema ao abrir.
- **Carga só com o seu clique.** Benchmarks e stress pedem confirmação, têm limites e cortam por temperatura.
- **Sem `sudo` escondido.** O que exige root aparece como um comando para você copiar e executar.
- **Sem telemetria.** Nenhum dado sai da sua máquina sem você pedir.
- **Sem dados inventados.** Se um sensor não existe, o Ilmenyx diz isso em vez de estimar.
- **Sem rede escondida.** A internet só é usada quando você confirma (ex.: consulta de compatibilidade, base do ProtonDB).

## Como usar

1. Baixe e extraia o pacote `Ilmenyx-linux-x64` (não precisa instalar o .NET: ele vem dentro). Rode `desktop/Ilmenyx.Desktop`.
   Para ter o ícone no menu do sistema, rode `packaging/linux/install-user.sh` (só no seu usuário, sem root).
2. **Não precisa de root nem de driver extra.** As temperaturas vêm dos módulos do próprio kernel (`k10temp` na AMD, `coretemp` na Intel).
3. **Temperatura ou consumo da CPU não aparecem?** O menu lateral mostra **"Por que não aparece?"**, com o motivo e o comando que
   resolve (o Linux esconde o consumo da CPU — RAPL — do usuário comum desde 2020). O Ilmenyx só mostra e copia o comando; quem roda é você.
4. **Overlay de FPS:** ligue na página Overlay e inicie o jogo pelo Ilmenyx (ou use a opção de inicialização gerada). No jogo, **Ctrl + Shift + O**
   liga e desliga o overlay (MangoHud, incluído no pacote).
5. **Stress e benchmarks:** sempre com o limite de temperatura ligado e boa ventilação; tudo pede confirmação e pode ser cancelado.
6. **Funciona em X11 e Wayland.** O app escolhe sozinho; para forçar, use `ILMENYX_DISPLAY_BACKEND=x11` ou `wayland`.

Onde ficam os dados (nada vai junto com o programa): configurações e histórico em `~/.config/ilmenyx/`, caches (compatibilidade,
componentes) em `~/.cache/ilmenyx/` e `~/.local/share/ilmenyx/`. Para começar do zero, apague essas pastas.

> **GPU:** placas NVIDIA mostram mais detalhes (VBIOS, limites de energia, CUDA) com o driver proprietário e o `nvidia-smi`.
> Em AMD os dados vêm do `amdgpu`. NVIDIA foi validada numa GeForce 940MX de notebook (driver 580, PRIME); GPU AMD dedicada ainda não foi testada (aviso no app).

## Começando

### Requisitos

- Linux x64 (glibc)
- [SDK .NET 10](https://dotnet.microsoft.com/download)
- Bibliotecas gráficas do sistema para o app desktop: X11 ou Wayland, fontconfig, EGL
- Loader Vulkan, para o benchmark de GPU

Para checar o que falta na sua máquina depois de compilar, rode `ilmenyx doctor`.

### Compilar e testar

```bash
git clone --recurse-submodules https://github.com/mr-henr/ilmenyx.git
cd ilmenyx

export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore Ilmenyx.slnx
dotnet build Ilmenyx.slnx -c Release --no-restore
dotnet test Ilmenyx.slnx -c Release --no-build --no-restore
```

### Abrir o aplicativo

```bash
dotnet run --project src/Ilmenyx.Desktop -c Release --no-build
```

Para forçar o servidor gráfico, use `ILMENYX_DISPLAY_BACKEND=x11` ou `ILMENYX_DISPLAY_BACKEND=wayland`.

### Gerar um pacote autocontido

```bash
scripts/package-linux-x64.sh
```

O pacote inclui o runtime .NET. Detalhes em [docs/pacote-linux-x64.md](docs/pacote-linux-x64.md).

## Usando a linha de comando

```bash
# Ajuda e verificação de requisitos
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- --help
dotnet run --project src/Ilmenyx.Cli -c Release --no-build -- doctor

# Inventário de hardware
... -- info --json
... -- info --html --output ~/ilmenyx-hardware.html

# Sensores
... -- sensors --json

# SMART a partir de um relatório do smartctl que você já tem
... -- smart-import --device nvme0n1 --html --output ~/smart.html < relatorio.json
```

Em `...`, use o prefixo `dotnet run --project src/Ilmenyx.Cli -c Release --no-build`.
Depois de compilar, o executável também fica em `src/Ilmenyx.Cli/bin/Release/net10.0/ilmenyx`.

O arquivo de saída (`--output`) precisa ser **novo**: o Ilmenyx não sobrescreve arquivos.

## Benchmarks

```bash
... -- bench sim --json                                  # simulação leve
... -- bench cpu --profile full --allow-cpu-load         # carga total da CPU
... -- bench sim-full --allow-cpu-load                  # simulação completa
```

Os modos `full` geram carga pesada e exigem `--allow-cpu-load`. Métodos em
[docs/benchmarks-metodologia.md](docs/benchmarks-metodologia.md).

> Os scores são **experimentais**. Não os compare com os do Windows e não os use para concluir
> que uma máquina está saudável ou estável.

## O que ainda não funciona

O que o RESET EXPLODE original faz foi portado ou substituído por um equivalente Linux, com estas exceções:

| Item | Situação | Motivo |
|---|---|---|
| Benchmark de GPU com ray tracing | **Portado (Vulkan, ray query); validado só numa APU Radeon 610M** | Falta GPU dedicada AMD RX 6000+ / NVIDIA RTX; score não comparável ao Windows |
| GPU AMD/NVIDIA em hardware real | **Não testado** | O desenvolvimento usou só GPU Intel integrada |
| RGB (Corsair, Razer) | Substituído pelo OpenRGB | Os SDKs originais são do Windows; o controle real de LEDs não foi testado |
| Ajustes do Windows (Game DVR, HAGS, registro) | Não se aplica | Não existem no Linux |
| FPS em jogo real | **Não testado** | Vem do log do MangoHud |
| Pacotes `.deb`, `.rpm`, PKGBUILD, ARM64, musl | Não feito | Hoje só há `tar.gz` linux-x64 |

O aplicativo mostra essa lista na página **Bloqueados (!)**. A tabela completa está em
[docs/portagem-completa.md](docs/portagem-completa.md) e tudo que falta validar, com o motivo e como
retestar, está em [Sem_teste.md](Sem_teste.md). **Contribuições para esses itens são bem-vindas.**

## Estrutura do projeto

```
src/
  Ilmenyx.Contracts/       tipos e contratos compartilhados
  Ilmenyx.Core/            lógica de coleta e benchmarks
  Ilmenyx.Platform.Linux/  leitura de /proc, /sys e integrações do Linux
  Ilmenyx.Gpu.Vulkan/      benchmark de GPU
  Ilmenyx.Cli/             linha de comando (ilmenyx)
  Ilmenyx.Desktop/         aplicativo desktop (Avalonia 12)
tests/                     testes automatizados
tools/                     smoke da interface e validações
scripts/                   empacotamento e validação
docs/                      documentação técnica
upstream/ResetExplode/     projeto original (submódulo)
```

## Documentação

| Assunto | Documento |
|---|---|
| Arquitetura | [docs/arquitetura.md](docs/arquitetura.md) |
| Roadmap | [docs/roadmap-linux.md](docs/roadmap-linux.md) |
| Validação e evidências | [docs/validacao.md](docs/validacao.md) |
| Compatibilidade entre distros | [docs/compatibilidade-linux.md](docs/compatibilidade-linux.md) |
| Monitor ao vivo | [docs/monitor.md](docs/monitor.md) |
| SMART importado | [docs/smart-import.md](docs/smart-import.md) |
| Histórico de versões | [docs/historico-versoes.md](docs/historico-versoes.md) |
| Regras para agentes e contribuidores | [AGENTS.md](AGENTS.md), [CONTRIBUTING.md](CONTRIBUTING.md) |

## Privacidade e segurança

Relatórios podem identificar a sua máquina (hardware, datas, rótulos). Revise antes de compartilhar;
a limpeza automática de padrões óbvios não garante anonimato. Não anexe saída bruta de SMART, dumps
ou logs com dados pessoais.

Veja [PRIVACY.md](PRIVACY.md) e [SECURITY.md](SECURITY.md).

## Licença e créditos

O Ilmenyx é uma adaptação **independente e não oficial** do
[RESET EXPLODE](https://github.com/GustavoReset/ResetExplode), criado por Gustavo (GustavoReset), sem
afiliação ou endosso da equipe RESET. O original fica no submódulo `upstream/ResetExplode`, com
licença e histórico preservados.

- A parte original continua sob a **MIT** ([LICENSE](LICENSE)).
- A parte própria tem [termos específicos](LICENSE-ILMENYX.md): pode ser usada como ferramenta
  profissional, inclusive em serviços pagos de diagnóstico, mas não pode ser vendida, ter o acesso
  cobrado nem ser copiada para um produto pago.
- O mapa completo está em [LICENSING.md](LICENSING.md). O conjunto **não** é anunciado como open source OSI.
- Créditos e avisos: [NOTICE.md](NOTICE.md) e [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

**Pendente de revisão humana:** contato de segurança, titularidade das contribuições, Manual de Marca
(a identidade visual atual é provisória) e licenças de assets. Veja o
[checklist pré-publicação](docs/legal/PRE-RELEASE-CHECKLIST.md). Material sem direitos comprovados não
deve ser redistribuído até essa revisão.
