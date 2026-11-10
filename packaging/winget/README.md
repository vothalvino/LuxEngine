# winget

Manifest for the [Windows Package Manager](https://github.com/microsoft/winget-pkgs), so that
`winget install vothalvino.LuxEngine` works once Microsoft has accepted it.

To submit a version, copy the `manifests` folder into a fork of `microsoft/winget-pkgs` and open a pull
request there, or let [wingetcreate](https://github.com/microsoft/winget-create) do it:

```powershell
wingetcreate update vothalvino.LuxEngine --version 1.8.1 --urls https://github.com/vothalvino/LuxEngine/releases/download/v1.8.1/LuxEngine-Setup.exe --submit
```

For a new version by hand: copy the version folder, change `PackageVersion`, `InstallerUrl`, `ReleaseDate`,
`ReleaseNotesUrl` and `InstallerSha256` (`Get-FileHash LuxEngine-Setup.exe`).
