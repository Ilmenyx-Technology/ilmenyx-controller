# AppImage

Um arquivo só, roda em qualquer distro x86_64 com glibc (sem instalar nada e sem root):

```
chmod +x Ilmenyx-<versão>-x86_64.AppImage
./Ilmenyx-<versão>-x86_64.AppImage            # interface
./Ilmenyx-<versão>-x86_64.AppImage --cli info # linha de comando
```

- Gerado por `bash scripts/package-appimage.sh /caminho/novo`: o pacote autocontido de `package-linux-x64.sh` (runtime .NET 10 e ICU
  embutidos, MangoHud e OpenRGB) dentro de um squashfs (zstd), precedido do runtime oficial `type2-runtime` (versão fixa 20251108,
  SHA-256 conferido com o publicado pelo GitHub). Não usa o appimagetool.
- A imagem é montada só para leitura num caminho que muda a cada execução; por isso o `AppRun` copia os componentes (MangoHud, OpenRGB)
  uma vez por versão para `~/.local/share/ilmenyx/bundle/<versão>` e aponta o app para lá (`ILMENYX_COMPONENTS_DIR`). Assim, opções
  de lançamento da Steam e wrappers continuam valendo depois que o Ilmenyx fecha. Versões antigas dessa pasta são apagadas.
- Precisa de FUSE 2 (`libfuse2`) para montar; sem ele: `./Ilmenyx-….AppImage --appimage-extract-and-run`.
- Menu/ícone: use um integrador de AppImage (Gear Lever, AppImageLauncher); o `.desktop` e o AppStream vão dentro da imagem.

Validado em 2026-11-07 no Dell Inspiron 7460 (Mint 22.3, X11): CLI (`--cli info --json`), interface aberta com GPU 940MX,
temperatura/carga da CPU ao vivo, componentes copiados e variável aplicada ao processo (`docs/evidencias/appimage-2026-11-07/`).
NÃO TESTADO: outras distros (Fedora, Arch, openSUSE, Debian), sistemas sem libfuse2, Wayland nativo pela AppImage, jogo real com o
MangoHud copiado.
