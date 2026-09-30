# SPT Installer.

### Release selection:
- The version dropdown is built from `release.json`, which publishes several releases side by side
- Each release carries its own runtime folder name, .net requirements and mirror list, so adding a release or a mirror needs no installer change
- The first mirror of a release is the unnamed default, the rest are shown suffixed with their name
- A chosen mirror is pinned. If it fails the installer reports an error rather than quietly serving the download from somewhere else

### Pre install checks:
- Checks if the game is installed
- Checks if .net 4.7.2 (or higher) is installed
- Checks the .net runtimes the selected release asks for, currently .net 9 for 4.0 and .net 10 for 4.1
- Checks if there is enough space before install
- Checks the game's launcher is closed
- Checks installer is not in a problematic path
- Checks install folder does not have game files already in it
- Checks if the game version matches the release's client version, if so skip patcher process
- Checks both zips are there, other than when the above match, patcher isnt checked for
- Downloads both zips from the selected mirror if needed

### Installer Processes:
- Copies files from the game path, found through the Steam libraries or the registry, to the new location
- Extracts, runs and deletes patcher with no user input
- Extracts the release files into the folder the release names, `SPT` for 4.0 and `SPT_Runtime` for 4.1
- Creates launcher and server shortcuts in the install folder
- Deletes both patcher and release zips at the end

### Local testing:
`SPT_RELEASE_URL` and `SPT_MIRRORS_URL` override where the metadata is fetched from, so the flow can be
driven against local files without touching the published manifests.

## Linux

The installer publishes a self-contained `linux-x64` binary named `SPTInstaller.Linux` alongside the Windows build.

```bash
chmod +x SPTInstaller.Linux
./SPTInstaller.Linux
```

Select two separate folders when prompted:

- the original Escape from Tarkov folder containing `EscapeFromTarkov.exe`;
- an empty destination for SPT.

The installer checks common native Steam and Wine-prefix locations. Set `SPT_GAME_PATH` before launch to provide the
original game folder explicitly. The folder picker remains available when automatic discovery does not match a custom
prefix layout.

When a downpatch is required, install `umu-run`, `wine64`, or `wine`. The installer checks them in that order. Custom
setups can use these environment variables:

- `SPT_LINUX_RUNNER`: full path to the Windows compatibility runner;
- `WINEPREFIX`: prefix used for the original game and patcher;
- `SPT_PROTONPATH`: value passed to `PROTONPATH` when the runner is `umu-run`;
- `SPT_UMU_PATH`: full path to `umu-run`.

Install the native UI libraries for your distribution:

Debian and Ubuntu:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libicu76  # Debian 13
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libicu74  # Ubuntu 24.04
```

Fedora:

```bash
sudo dnf install libX11 libICE libSM fontconfig libicu
```

Arch-based distributions:

```bash
sudo pacman -S --needed libx11 libice libsm fontconfig icu
```

For downpatching on Arch, install `umu-launcher` from the `multilib` repository. On other distributions, install a
package that provides `umu-run` or install Wine. Wayland sessions use XWayland because the installer uses Avalonia's
default X11 backend.

The published binary targets x86_64 distributions using the GNU C Library (glibc). CI initializes the published artifact
on Ubuntu, Debian, Fedora, and Arch. Alpine and other musl distributions, NixOS, and ARM64 are outside the current
artifact and test matrix.

The release archive is extracted without a native `7z.dll`. Archive traversal, symbolic links, duplicate paths, and
case-colliding paths are rejected before files are written. Linux launcher and server files retain or receive executable
permissions, and the installer creates `.desktop` launchers in the SPT folder.

### Linux development build

```bash
dotnet test -c Release
dotnet publish SPTInstaller/SPTInstaller.csproj -c Release -r linux-x64 \
  -p:PublishSingleFile=true --self-contained true -o ./publish/linux
```
