# Roteiro — validação de GPU AMD/NVIDIA em hardware real

Para uma sessão do Claude Code (ou uma pessoa) num PC com GPU AMD ou NVIDIA. A máquina original de desenvolvimento só
tem GPU Intel, por isso o app mostra o aviso **"AMD/NVIDIA · Testes não realizados em hardware real"**. Este roteiro
produz a evidência que falta e corrige o que falhar. Siga o `AGENTS.md`: sem sudo, sem instalar no sistema, carga só
com autorização.

## 0. Antes de começar

- Confirme com o usuário **de quem é o PC** e se a fase de carga (passo 5) está autorizada.
- Crie uma branch: `git switch -c fix/gpu-<amd|nvidia>-<modelo>`.
- Se o `git status` acusar centenas de arquivos modificados só por permissão (cópia via pendrive FAT/exFAT):
  `git config core.fileMode false`.

## 1. Reconhecimento (só leitura)

Registre a saída em `docs/evidencias/gpu-<fabricante>-<data>/ambiente.txt`:

```bash
uname -sr; grep PRETTY_NAME /etc/os-release; echo "$XDG_SESSION_TYPE"
lspci -nnk | grep -A3 -Ei 'vga|3d|display'      # modelo e driver em uso (amdgpu, nvidia, nouveau)
lsmod | grep -E '^(amdgpu|nvidia|nouveau|i915|xe)\b'
command -v nvidia-smi && nvidia-smi --query-gpu=name,driver_version,temperature.gpu,power.draw,clocks.gr,memory.total --format=csv
ls /sys/class/drm/card*/device/hwmon/*/ 2>/dev/null | head   # sensores expostos (AMD)
command -v vulkaninfo && vulkaninfo --summary | head -40
```

Com o driver **nouveau** (NVIDIA sem driver proprietário), a falta de sensores e de Vulkan completo é esperada:
registre isso, não trate como falha do Ilmenyx.

## 2. Compilar e testar

```bash
bash scripts/get-local-sdk.sh && source scripts/env-local-sdk.sh
dotnet restore Ilmenyx.slnx && dotnet build Ilmenyx.slnx -c Release --no-restore
dotnet test Ilmenyx.slnx -c Release --no-build
dotnet build tools/Ilmenyx.UiSmoke -c Release
dotnet run --project tools/Ilmenyx.UiSmoke -c Release --no-build -- /tmp/ilx-smoke-x11
env -u DISPLAY ILMENYX_DISPLAY_BACKEND=wayland dotnet run --project tools/Ilmenyx.UiSmoke -c Release --no-build -- /tmp/ilx-smoke-wl
```

Guarde os logs e as capturas relevantes nas evidências.

## 3. CLI — leitura de GPU (só leitura)

```bash
cli="dotnet src/Ilmenyx.Cli/bin/Release/net10.0/ilmenyx.dll"
$cli bench gpu-list
$cli info --json   | python3 -m json.tool | grep -i -A20 gpu
$cli sensors --json | python3 -m json.tool | grep -i -B2 -A6 '"gpu:'
```

Compare **modelo, VRAM, temperatura, clock, uso, consumo e ventoinha** com a ferramenta do fabricante (`nvidia-smi`; para
AMD, `/sys/class/drm/card*/device/` e `sensors`). Valor inventado é bug grave. Valor ausente deve aparecer como
"indisponível", com o motivo.

## 4. Interface (só leitura)

Abra `dotnet src/Ilmenyx.Desktop/bin/Release/net10.0/Ilmenyx.Desktop.dll` e confira as páginas **Visão geral**, **GPU**,
**Sensores**, **Jogos/Estimativa de FPS**, **Overlay** e **Verificação OC**. Os comandos de OC aparecem como texto e
nunca são executados. Capture as telas para as evidências.

## 5. Carga curta de GPU (SOMENTE com autorização explícita do dono)

```bash
bash scripts/package-linux-x64.sh /tmp/ilx-pacote     # pacote autocontido; usa .artifacts/components-cache
bash scripts/test-linux-package.sh /tmp/ilx-pacote/Ilmenyx-linux-x64 /tmp/ilx-pacote-teste
bash scripts/validate-gpu-remote.sh /tmp/ilx-pacote/Ilmenyx-linux-x64 /tmp/ilx-gpu-fase1
bash scripts/validate-gpu-remote.sh /tmp/ilx-pacote/Ilmenyx-linux-x64 /tmp/ilx-gpu-fase2 --allow-gpu-load
```

São cargas de cerca de 8 s, com corte por temperatura. Stress longo, overclock e alteração de driver continuam proibidos.

## 6. Corrigir e registrar

- Para cada divergência: teste com fixture do `/sys`/`/proc`/saída real, depois a correção, depois o build e os testes.
- Atualize o `Sem_teste.md`: feche o que foi validado, com a evidência; mantenha o que não deu para testar, com o motivo.
- O aviso AMD/NVIDIA só sai para o fabricante validado, com evidência real e aprovação do mantenedor; fixtures não contam.
- Faça commits locais na branch. Sem push automático.

## 7. Levar o resultado de volta

- **Com acesso ao GitHub:** o usuário faz o push da branch pelo GitHub Desktop ou com `git push -u origin <branch>`.
- **Sem acesso ao GitHub (pendrive):**
  - Neste PC, gere o pacote da branch:
    ```bash
    git bundle create /caminho/pendrive/ilmenyx-retorno.bundle main..HEAD
    ```
    Se a `main` deste PC ainda não tiver as mudanças locais, use `--all` no lugar de `main..HEAD`.
  - Na máquina original, traga a branch para o repositório:
    ```bash
    git fetch /caminho/pendrive/ilmenyx-retorno.bundle '<branch>:<branch>'
    ```
    Depois revise e faça o merge pelo GitHub Desktop.
