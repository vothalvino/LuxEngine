# winget

Manifest for the [Windows Package Manager](https://github.com/microsoft/winget-pkgs), so that
`winget install vothalvino.Lumeshow` works once Microsoft has accepted it.

To submit a version, copy the `manifests` folder into a fork of `microsoft/winget-pkgs` and open a pull
request there, or let [wingetcreate](https://github.com/microsoft/winget-create) do it:

```powershell
wingetcreate update vothalvino.Lumeshow --version 2.0.1 --urls https://github.com/vothalvino/Lumeshow/releases/download/v2.0.1/Lumeshow-Setup.exe --submit
```

For a new version by hand: copy the version folder, change `PackageVersion`, `InstallerUrl`, `ReleaseDate`,
`ReleaseNotesUrl` and `InstallerSha256` (`Get-FileHash Lumeshow-Setup.exe`).
