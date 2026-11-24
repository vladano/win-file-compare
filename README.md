<p align="center">
  <img src="build/icon.png" alt="File Compare icon" width="96">
</p>

<h1 align="center">File Compare</h1>

<p align="center">
  A free, open source file and folder compare tool for Windows, made for programmers.<br>
  Works offline. No account. No subscription.
</p>

<p align="center">
  <a href="https://github.com/vladano/win-file-compare/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/vladano/win-file-compare/ci.yml?branch=main&label=CI" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue" alt="Platform: Windows 10 and 11">
</p>

- Side-by-side text compare with highlighted differences, editing and merging
- Folder, hex and image compare, in a tabbed window
- Runs standalone or from the Explorer right-click menu
- Small, fast and offline

## Status

Early development. Text compare (with editing, copying differences and saving), folder, hex and image compare work today, in a tabbed window with a Home tab and saved sessions. The classic Explorer right-click menu can be added from Tools > Explorer integration. See [CHANGELOG.md](CHANGELOG.md).

**New to File Compare?** Read the [User Manual](docs/USER-MANUAL.md). It covers installing the app, every compare view, the Explorer right-click menu, sessions, shortcuts and troubleshooting.

## Install and use

There is no installer. File Compare is one program, `FileCompare.exe`.

1. **Get the exe.** Build the standalone `publish\FileCompare.exe` (see [Build](#build)) or run from source (see [Quick start](#quick-start-from-source)). Copy the exe to a permanent folder, for example `C:\Tools\FileCompare\`.
2. **Start it.** Double-click it for the Home tab, or run `FileCompare.exe "left" "right"` to compare two files or two folders. The right view (text, folder, hex or image) is chosen for you.
3. **Add the right-click menu (optional).** Choose **Tools > Explorer integration... > Install**, or run `FileCompare.exe --register-shell`. No administrator rights are needed. Only your user account is changed.

### Using it from the right-click menu

| Entry | What it does |
|---|---|
| **Select left side to compare** | Remembers the file or folder you clicked |
| **Compare to 'name'** | Appears once a left side is selected. Compares it with the item you clicked |
| **Compare selected items** | Compares two files or two folders you selected together |

- **Two steps:** right-click the first item > *Select left side to compare*, then right-click the second item > *Compare to 'first'*.
- **One step:** select two files (or two folders) with Ctrl+click, right-click one > *Compare selected items*.
- **Windows 11:** the entries are under **Show more options** (or hold Shift while you right-click).
- **Moved the exe?** Open Tools > Explorer integration from the new location and click **Install again**.
- **Remove the menu:** Tools > Explorer integration > **Remove**, or `FileCompare.exe --unregister-shell`.

Full details, including what happens with more than two selected items: [User Manual](docs/USER-MANUAL.md#7-using-the-explorer-right-click-menu).

## Features

Available now:
- Side-by-side compare of two text files with line numbers, filler lines and synchronized scrolling
- Modern Windows look with a theme picker (toolbar Theme button or View > Theme): System default, Light, Dark, Solarized Light, Solarized Dark, Dracula and Nord. The choice is applied immediately and remembered. To add a theme, add an entry to `ThemeCatalog.cs`.
- Views: **All**, **Diffs** (only differences), **Same** (only identical lines) and **Context** (differences plus a few lines around them). Hidden lines collapse into "N filtered lines" rows.
- **Minor**: show lines that differ only in unimportant ways (whitespace or case) in a separate color
- **Rules**: choose which differences are unimportant (leading, embedded and trailing whitespace, character case), the alignment algorithm (Standard or Patience, which often reads better for source code) and how many context lines to show
- Inline character diffs: the exact characters that changed inside a changed line are marked more strongly
- Syntax highlighting chosen by file extension, with colors that stay readable on dark themes
- **Format**: force the encoding of each file (UTF-8, UTF-8 BOM, UTF-16 LE/BE, ANSI), set the tab size, show spaces, tabs and line breaks
- **Edit**: type directly in either side. Filler lines are locked. The highlights update after a short pause in typing (F6 does it at once). Save with Ctrl+S (File > Save Left, Save Right or Save All). Files keep their encoding, BOM and line ending.
- **Undo / Redo**: Ctrl+Z and Ctrl+Y (also in the Edit menu) step back and forward through typing and Copy, up to 100 steps
- **Copy**: copy the current difference to the right or to the left, then save
- Next and previous difference
- **Swap** sides (unsaved edits move with them) and **Reload** from disk (F5). You are asked before unsaved changes are discarded.
- File info for each side: date, size, encoding (UTF-8, UTF-16, ANSI) and line endings
- Open from the command line: `FileCompare.exe "left" "right"`

**Folder compare** (open two folders, for example `FileCompare.exe "C:\a" "C:\b"`):
- A tree of both folders with size, date and status (Same, Different, Left only, Right only) for every file and folder. Folders that contain differences open automatically.
- Filters: show or hide Different, Left only, Right only and Same entries
- **Rules**: names to ignore (default `.git`, `node_modules`, `bin`, `obj`, `.vs`; `*` and `?` work) and how files are compared: by content (exact) or by size and time (fast)
- **Copy right / Copy left** for the selected files and folders. You are asked before existing files are replaced.
- **Delete** from either side (to the Recycle Bin)
- **Sync** menu, each with a preview of every copy and delete (untick items, resolve conflicts) before anything happens:
  - **Update**: copy new and newer files to one side. Nothing is deleted.
  - **Mirror**: make one side an exact copy of the other, deletes included (refused from an empty or missing folder).
  - **Sync both ways**: bring changes made on either side since the last sync across, deletes included; files changed on both sides are conflicts you resolve. The first sync of a pair never deletes.
  - Deletes go to the Recycle Bin, and you are asked again before large deletes.
- Double-click or Enter on a file opens it in a new tab: text, image or hex compare, whichever fits
- Fast scans: the tree appears as soon as the folders are listed, and file contents are read in the background, several at a time (one at a time on a spinning disk), with progress in files and bytes. Esc stops it.

**Explorer right-click menu** (Tools > Explorer integration, or `FileCompare.exe --register-shell`):
- **Select left side to compare** on a file or folder, then **Compare to 'name'** on another one
- **Compare selected items** when two files or two folders are selected
- Written for your user only (`HKEY_CURRENT_USER\Software\Classes`), no administrator rights needed. On Windows 11 the entries are under "Show more options". Remove them from the same dialog or with `--unregister-shell`.

**Tabs, Home and sessions**:
- Every comparison opens as a tab in one window. Ctrl+T opens a new tab, Ctrl+W closes one, Ctrl+Tab switches, and a middle-click on a tab closes it. You are asked before tabs with unsaved changes close.
- The **Home** tab starts a new comparison (the right view is picked for you) and lists saved sessions and recent comparisons
- **Sessions** (toolbar button): save the current comparison under a name, or open a saved or recent one. File > Recent lists the last ten.

**Hex and image compare** (picked automatically for binary files and pictures):
- Hex: offset, hex and ASCII for both files, changed bytes highlighted, bytes on one side only shaded, next and previous difference. Files up to 256 MB.
- Images: side by side with synchronized scrolling, or a difference picture (changed pixels red, pixels on one side only orange), fit or 100 %, with the number of different pixels. PNG, JPEG, BMP, GIF (first frame), TIFF, ICO and JPEG XR.

**Drag and drop**: drop files or folders from Explorer onto a path box or an editor side to set that side, or two items anywhere on a page to compare them.

Planned:
- Windows 11 top-level context menu entry

## Install

Run `FileCompare-Setup-<version>-x64.exe` (build it with `scripts\build-installer.ps1`, see [Building](docs/BUILDING.md#installer)).
It installs for your user only into `%LocalAppData%\Programs\File Compare`, with no administrator rights, adds a Start menu entry,
and can add the Explorer right-click menu. Uninstall it from Settings > Apps. Your settings are kept.

The installer is not code signed yet, so SmartScreen may show "Windows protected your PC": click "More info" > "Run anyway".

## Quick start (from source)

Requires Windows 10 or 11 and the [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or newer.

```powershell
git clone https://github.com/vladano/win-file-compare.git
cd win-file-compare
dotnet run --project src/FileCompare.App -- "C:\path\a.txt" "C:\path\b.txt"
```

To open the app without files, omit the arguments: `dotnet run --project src/FileCompare.App`.

## Build

| Command | Result |
|---|---|
| `dotnet build FileCompare.sln` | Debug build of everything |
| `dotnet build FileCompare.sln -c Release` | Release build (what CI runs) |
| `dotnet publish src/FileCompare.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish` | Standalone `publish\FileCompare.exe` (about 71 MB), no .NET install needed |

For the installer run `powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1` (needs [Inno Setup](https://jrsoftware.org/isinfo.php) 6 or 7). It writes `dist\FileCompare-Setup-<version>-x64.exe`.

Use `-r win-arm64` for Windows on ARM. The published exe is not code signed, so SmartScreen may show "Windows protected your PC". Click "More info" > "Run anyway". See [Building](docs/BUILDING.md) for the app icon and troubleshooting.

If the build fails with "file is in use", close every running `FileCompare.exe` and build again.

## Test

```powershell
dotnet test FileCompare.sln                 # run all unit tests
dotnet test FileCompare.sln -c Release      # same, Release configuration
dotnet test --filter "FullyQualifiedName~TextDiffServiceTests"   # one test class
```

The tests use xUnit and live in `tests/FileCompare.Tests`. They cover the diff engine (`TextDiffServiceTests`), file loading with encoding and line ending detection (`TextFileLoaderTests`), command line parsing (`CommandLineArgsTests`) and the folder scan (`FolderScanTests`: parallel against sequential on random trees, stopping, files that change during the scan, the cache). The text compare page is not covered by automated tests, so check it by hand after UI changes:

- Compare two different files: differences are highlighted and the status bar shows the section count.
- Next/Prev Section scrolls both panes and wraps at the ends.
- Swap and Reload keep the result correct. Changing Rules (for example ignoring leading whitespace) updates the diff.
- Diffs, Same and Context show "N filtered lines" rows. Minor highlights whitespace-only differences in blue.
- Edit, change a line and wait a moment: the highlights update on their own and the caret stays where it was. Then Ctrl+S, and check that the file's encoding and line endings did not change.
- Ctrl+Z and Ctrl+Y step through typing and Copy. Undo is greyed out in the Edit menu when there is nothing to undo.
- In Edit mode, typing on a gray filler line does nothing. Closing with unsaved changes asks first.
- Scrolling one pane scrolls the other.
- Compare identical files: status shows "Same".
- Open a missing or locked path: an error appears in the status bar and the app keeps running.

The installer, the Explorer menu and drag and drop have their own step-by-step checklist in [docs/TESTING.md](docs/TESTING.md). `scripts\make-test-files.ps1 -Open` creates the sample files it uses.

Most of that checklist (46 of 50 checks) is automated with FlaUI in `tests/FileCompare.UiTests`: the installer, the Explorer menu commands and drag and drop on every page. These tests drive the real app and mouse, so they are opt-in and skipped by `dotnet test`. Run them with `powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1` (add `-Installer` for the installer tests) and leave the PC alone while they run. See [Automated checks](docs/TESTING.md#automated-checks).

CI ([`ci.yml`](.github/workflows/ci.yml)) builds in Release and runs the tests on every push to `main` and every pull request, on a Windows runner.

## Command line

```
FileCompare.exe                           open the app
FileCompare.exe "left" "right"            compare two files or two folders
FileCompare.exe "path"                    use one file or folder as the left side
FileCompare.exe --register-shell          add the Explorer right-click entries (current user)
FileCompare.exe --unregister-shell        remove them again
```

Only one copy runs per user. Starting the app again (from a shortcut, the command line or Explorer) opens a new tab in the running window. With no paths it opens the Home tab. The Explorer entries use `--select-left`, `--compare-to` and `--compare-selected`.

The view is chosen from the paths: two folders (or a single folder) open a folder compare, pictures an image compare, other binary files a hex compare, and everything else a text compare. Paths may be wrapped in quotes. Arguments starting with `--` are ignored.

## Built with

- C# on .NET 9 and WPF
- [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) for the text panes
- [DiffPlex](https://github.com/mmanela/diffplex) for line diffs
- xUnit for tests

## Project structure

```
src/
  FileCompare.Core/     all comparison logic, no UI
    Text/               text diff, file loading and saving, encodings, undo, view filters
    Folders/            folder scan, ignore patterns, copy, delete, update
    Binary/             byte-by-byte comparison for the hex view
    Images/             pixel comparison and difference mask
    Sessions/           view detection, recent list, saved sessions, drag and drop rules
    Shell/              Explorer menu registry entries, single instance, left side selection
    Settings/           settings file
  FileCompare.App/      WPF app: tabbed ShellWindow with Home, Text, Folder, Hex and Image pages
    Tabs/               ComparePage base class (shared menus, sessions, history), PathComparePage (path boxes, Swap, Reload, browse, drops, theme menus), drag and drop
    Editor/             diff pane, background renderer, line number margin, scroll sync, theme
    Theming/            themes, title bar colors
    Dialogs/            Rules, Format, folder rules, Explorer integration, About, input
tests/
  FileCompare.Tests/    xUnit tests for Core and command line parsing
  FileCompare.UiTests/  opt-in FlaUI tests of the real app, Explorer and the installer
tools/
  FileCompare.Benchmark/ times folder scans (dotnet run -c Release --project tools/FileCompare.Benchmark)
installer/              Inno Setup script for the per-user installer
build/                  app icon (svg, png, ico) and the third-party license texts (licenses/)
scripts/                icon generator and installer build script
docs/                   user manual and build instructions
```

## Documentation

- [User Manual](docs/USER-MANUAL.md): installing, using every compare view, the Explorer right-click menu, sessions, shortcuts, settings and troubleshooting.
- [Building](docs/BUILDING.md): requirements, build commands, single-file publish, the installer, release steps, the app icon and troubleshooting.
- [Testing](docs/TESTING.md): the manual checklist for the installer, the Explorer menu and drag and drop.
- [Plan](PLAN.md): architecture and roadmap.
- [Changelog](CHANGELOG.md).

## License

MIT License. Free to use, copy, modify and distribute for personal and commercial purposes. Copyright (c) 2026 [vladano](https://github.com/vladano).

The copyright notice, the reference to the project home at <https://github.com/vladano/win-file-compare> and the permission notice in the [LICENSE](LICENSE) file must be kept in all copies or substantial portions of the software.

This app was created using the [AWS Kiro](https://kiro.dev) IDE environment.
