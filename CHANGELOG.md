# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Text compare: side-by-side view with highlighted differences, filler lines, line numbers and synchronized scrolling.
- Next/previous difference, swap sides, reload, ignore whitespace and ignore case.
- File info bar (date, size, encoding, line endings) and status bar.
- Command line: `FileCompare.exe "left" "right"`.
- View modes All, Diffs, Same and Context with collapsed "N filtered lines" rows, and a Minor toggle for whitespace-only and case-only differences.
- Rules dialog (ignore leading, embedded and trailing whitespace and character case; context lines) and Format dialog (encoding override per file, tab size, show whitespace and line breaks). Both are saved in `settings.json`.
- Edit mode with locked filler lines, Copy of a difference to the right or left, and Save (Ctrl+S, File menu) that keeps encoding, BOM and line endings.
- Manual test checklist (`docs/TESTING.md`) for the installer, the Explorer menu and drag and drop, with `scripts/make-test-files.ps1` to create the sample files. Running it is part of the release steps.
- Automated UI tests (FlaUI, `tests/FileCompare.UiTests`, run by `scripts/run-ui-tests.ps1`) for 46 of those 50 checks: the installer (with `-Installer`), the Explorer menu commands, and drag and drop on every page. They are opt-in (`FILECOMPARE_UI_TESTS=1`).
- `FILECOMPARE_DATA_DIR` and `FILECOMPARE_SHELL_CLASSES_KEY` environment variables move the settings folder and the Explorer menu registry key, so tests run without touching the real ones.
- Installer (Inno Setup, `installer/FileCompare.iss`, built by `scripts/build-installer.ps1`): per-user install to `%LocalAppData%\Programs\File Compare` without administrator rights, Start menu entry, optional desktop icon, optional Explorer menu entries, clean uninstall that keeps the settings, SHA-256 checksum file. It ships the license texts of AvalonEdit and DiffPlex (`THIRD-PARTY-NOTICES.md`).
- Tabs: one window holds every comparison as a tab (Ctrl+T, Ctrl+W, Ctrl+Tab, middle-click to close). Later launches, including from Explorer, open as tabs. You are asked before tabs with unsaved changes close.
- Home tab (shown when the app starts without paths): start a comparison, open saved sessions and recent comparisons.
- Sessions toolbar button and File > Recent: save a comparison under a name, open saved or recent ones. Kept in `settings.json`.
- Hex compare for binary files (up to 256 MB): offset, hex and ASCII side by side, changed and missing bytes highlighted, next/previous difference.
- Image compare for pictures: side by side or a difference picture, fit or 100 %, number of different pixels.
- The right view (text, folder, hex or image) is chosen from the paths, everywhere a comparison is opened.
- Drag and drop of files and folders from Explorer onto path boxes, editor sides or anywhere on a page.
- Unexpected errors are shown instead of closing the app, so other tabs keep their unsaved changes.
- Explorer right-click menu (classic menu, current user only): "Select left side to compare", "Compare to 'name'" (shown once a left side is selected) and "Compare selected items". Install and remove it in Tools > Explorer integration, which lists the registry keys it writes, or with `--register-shell` / `--unregister-shell`.
- Single instance: later launches pass their paths to the running copy through a per-user named pipe, and it opens a new window.
- About menu (text and folder compare windows): version, description, copyright and MIT license, and links to the source code, README, changelog, releases and issue tracker. "Copy details" copies the version, Windows and .NET details for bug reports.
- Folder compare window: tree with size, date and status columns; Different, Left only, Right only and Same filters; ignore patterns and content or size-and-time comparison (Rules); copy to either side with an overwrite prompt; delete to the Recycle Bin; one-way Update that copies new and newer files and never deletes; double-click opens a text compare; background scan that Esc cancels. Two folders on the command line, or typed into the text compare, open it.
- Inline character diffs: the characters that changed inside a changed line get a stronger highlight (Format > "Mark the characters that changed inside a line"). Lines over 2000 characters are skipped.
- Syntax highlighting by file extension (C#, C++, Java, JavaScript, Python, JSON, XML, HTML, CSS, Markdown, PowerShell, SQL and more), with colors adapted to dark themes (Format > "Syntax highlighting").
- Patience alignment as an alternative to the standard Myers diff (Rules > Alignment).
- Live re-diff: the differences update after a short pause in typing (or at once with F6), keeping the caret and scroll position.
- Undo and redo (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z, Edit menu) for typing and Copy, up to 100 steps. Typing is one step per pause. Swap and Reload clear the history.
- Prompt before unsaved changes are lost (close, reload, opening other files); Swap now keeps unsaved edits.
- `TextCompareSession` in Core holds the documents, diff, copy and save logic so it does not depend on the UI.
- Modern Windows 11 look (WPF Fluent theme) and a theme picker (toolbar "Theme" button and View > Theme): System default, Light, Dark, Solarized Light, Solarized Dark, Dracula and Nord. The theme applies immediately and is saved in `%AppData%\FileCompare\settings.json`. "System default" follows Windows live.
- Toolbar icons (vector, in `Resources/Icons.xaml`) with an icon-over-label button layout and tooltips.
- App icon (`build/icon.svg`, `.png`, `.ico`), used for the exe and the window.
- Unit tests for the diff engine, file loader and command line parsing.
- MIT license that references the project home on GitHub.

- Faster folder compare: the tree appears as soon as both folders are listed (files of the same size say "Comparing..."), and the contents are then read several files at a time in the background, with progress in files and bytes. A warm scan of about 5,000 files and 1.3 GB is 3 to 5 times faster. Folders are listed in parallel too, files are checked at the start and the end first, and a refresh after copy, delete or update only reads files that changed. Esc stops the comparison and leaves the rest "Not compared".
- Rules > "Files read at the same time": Automatic (4 on SSDs and network drives, 1 when both folders are on the same spinning disk) or a fixed number.
- `tools/FileCompare.Benchmark` times folder scans on a generated tree or on two folders you choose.
- Folder compare: **Mirror to right/left** makes one side an exact copy of the other, deletes included. It is refused when the source folder is missing or empty.
- Folder compare: **Sync both ways** brings the changes made on either side since the last sync to the other side, deletes included. The state after each successful sync is kept in `%AppData%\FileCompare\sync`, so a file deleted on one side is told apart from a new one. Files changed on both sides (or deleted on one and changed on the other) are conflicts: keep left, keep right or skip. The first sync of a pair never deletes.
- A preview for Update, Mirror and Sync: every planned copy and delete, grouped, with sizes and reasons. Items can be left out and conflicts resolved. Copies run before deletes, deletes go to the Recycle Bin, a second question comes before deleting more than 50 files or more than half of a side, and Esc stops between files.
- The folder toolbar's Update button is now **Sync**, a menu with Update right/left, Mirror to right/left and Sync both ways.

### Fixed
- A file that is written to while folder compare reads it is reported as an error ("changed while it was compared"), never as the same.
- With Edit off, the text panes could still be changed by dragging text out of them (or into them). They are now read-only until Edit is turned on.
- Every running copy now holds the `FileCompare.Running` mutex, also one started with `FILECOMPARE_DATA_DIR`, so Setup always asks to close it first.
