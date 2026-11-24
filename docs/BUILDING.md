# Building

File Compare is a C# WPF app. It targets Windows 10 and 11.

## Requirements

- Windows 10 or 11
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or newer

## Build commands reference

| Command | Result |
|---|---|
| `dotnet build FileCompare.sln` | Build everything |
| `dotnet test FileCompare.sln` | Run the unit tests |
| `dotnet run --project src/FileCompare.App -- "left.txt" "right.txt"` | Run from source and compare two files |
| `dotnet publish src/FileCompare.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish` | Standalone `publish\FileCompare.exe` (about 71 MB) that needs no .NET install |

For Windows on ARM use `-r win-arm64`.

## Folder scan benchmark

`tools/FileCompare.Benchmark` times folder compare scans in Release, after one warm-up run:

```powershell
dotnet run -c Release --project tools/FileCompare.Benchmark                                  # generated tree in %TEMP%, about 2.4 GB
dotnet run -c Release --project tools/FileCompare.Benchmark -- --profile small               # 500 files, 60 MB per side
dotnet run -c Release --project tools/FileCompare.Benchmark -- --left C:\a --right D:\b      # two real folders (only read)
dotnet run -c Release --project tools/FileCompare.Benchmark -- --parallel 1                  # one file at a time
```

It prints the total time, the time until the tree can be shown, and the number of files read at once, for
"size and time" and for content mode. Add `--delete` to remove the generated tree afterwards, or `--root <folder>`
to put it on another disk. The numbers from the last measurement are in PLAN.md, phase 10. Warm runs mostly measure
the code; to see the disk, the files must not fit in (or must have dropped out of) the Windows file cache.

## Installer

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) 6 or 7 from `installer/FileCompare.iss`.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

The script:
1. Reads the version from `<Version>` in `src/FileCompare.App/FileCompare.App.csproj`.
2. Downloads the license texts of AvalonEdit and DiffPlex into `build/licenses/` the first time (commit them, so later builds work offline; `-SkipLicenseDownload` fails instead of downloading).
3. Publishes the self-contained single-file `publish\FileCompare.exe` (no `.pdb`). `-SkipPublish` reuses the last one.
4. Finds `ISCC.exe` in the usual Inno Setup 7 and 6 folders (or pass `-Iscc <path>`) and compiles the installer.
5. Writes `dist\FileCompare-Setup-<version>-x64.exe` and a `.sha256` file next to it.

What the installer does:

| | |
|---|---|
| Installs to | `%LocalAppData%\Programs\File Compare`, for the current user only. No administrator rights. |
| Shortcuts | Start menu; desktop is optional (off by default) |
| Explorer menu | Optional task (on by default). It runs `FileCompare.exe --register-shell`; unticking it on a later install runs `--unregister-shell`. |
| Uninstall | Settings > Apps, or `unins000.exe`. Removes the files, the shortcuts and the Explorer menu entries. Your settings in `%AppData%\FileCompare` are kept. |
| Running app | Setup and uninstall ask you to close File Compare first (the app holds the `FileCompare.Running` mutex). |

Silent install, for scripts: `FileCompare-Setup-1.0.0-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Add `/MERGETASKS="!explorermenu"` to skip the Explorer menu entries.

Keep the `AppId` in `FileCompare.iss` unchanged, or Windows treats a new version as a different app.

## Release steps

1. Change `<Version>` in `src/FileCompare.App/FileCompare.App.csproj` and add the changes to `CHANGELOG.md`.
2. `dotnet test FileCompare.sln` passes.
3. Build the installer and run the UI tests with it: `scripts/run-ui-tests.ps1 -Installer` (it calls `scripts/build-installer.ps1`; see [TESTING.md](TESTING.md#automated-checks)). Uninstall File Compare first, or the installer tests are skipped.
4. Run the manual checklist in [TESTING.md](TESTING.md) with that installer (on Windows 10 and Windows 11 when possible) and fill in its results summary.
5. Sign the files if you have a certificate (see below), then publish the installer and its `.sha256` file.

## Code signing (optional)

Unsigned installers and programs make SmartScreen show "Windows protected your PC" until they build up a reputation.
Signing needs a code signing certificate from a certificate authority (or Azure Trusted Signing) and `signtool.exe` from the Windows SDK. Neither is part of this repo.

With a certificate installed in your user store:

```powershell
# Sign the program before the installer is built, then the installer itself.
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a publish\FileCompare.exe
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SkipPublish
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\FileCompare-Setup-1.0.0-x64.exe
```

Sign before creating the `.sha256` file you publish (the script writes it, so run the last step again or recreate the checksum after signing). Inno Setup can also sign during the compile with its `SignTool` setting; see its help.

## App icon

The icon lives in the `build/` folder:

| File | Used for |
|---|---|
| `build/icon.svg` | Editable source (1024x1024) |
| `build/icon.ico` | exe icon, shortcuts, taskbar and window icon (16 to 256 px) |
| `build/icon.png` | README and web use (512x512) |

`FileCompare.App.csproj` sets `ApplicationIcon` to `build/icon.ico` and embeds it as a resource, `ShellWindow.xaml` uses it as the window icon, and the installer uses it as the setup icon.

To change the design, edit `build/icon.svg`, update the matching shapes in `scripts/generate-icon.ps1`, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\generate-icon.ps1
```

## Troubleshooting

| Problem | Fix |
|---|---|
| Build fails with "file is in use" | Close every running `FileCompare.exe` and build again. |
| SmartScreen blocks a published exe | Expected for unsigned builds. Click "More info" > "Run anyway", or sign the exe. |
| Icon looks old in Explorer | Windows caches icons. Restart Explorer or rename the exe. |
| `build-installer.ps1` says ISCC.exe was not found | Install Inno Setup, or pass `-Iscc "C:\path\to\ISCC.exe"`. |
| Antivirus deletes or locks a freshly built exe | New unsigned programs are sometimes flagged. Check the antivirus history, restore the file, and consider an exclusion for your build folder. A restart releases files the antivirus still holds. |
