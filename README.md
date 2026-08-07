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
