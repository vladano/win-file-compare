# File Compare: app plan

Windows 10/11 file and folder compare tool for programmers, modeled on Beyond Compare.

## Language and stack
C# on .NET 9 with WPF. AvalonEdit for the text panes, DiffPlex for line and character diffs, xUnit for tests, Inno Setup for the installer.
Reasons: Windows-only target, first-class shell integration, fast startup, single-file self-contained publish.
The app stays on .NET 9 for now; moving to the current LTS is listed under Risks.

## Solution layout
- `src/FileCompare.Core`: all comparison logic, no UI
  - `Text/`: text diff (Myers and Patience), inline character diff, file loading and saving with encodings, compare session with undo, view filters
  - `Folders/`: folder scan, ignore patterns, copy, delete and one-way update
  - `Binary/`: byte-by-byte comparison for the hex view
  - `Images/`: pixel comparison and difference mask
  - `Sessions/`: view detection (text, folder, hex, image), recent list, saved sessions, drag and drop rules
  - `Shell/`: Explorer menu registry entries, single instance (named mutex and pipe), remembered left side
  - `Settings/`: the JSON settings file in `%AppData%\FileCompare`
- `src/FileCompare.App`: WPF app. `ShellWindow` holds the tabs; each tab is a page: Home, Text, Folder, Hex or Image compare
  - `Tabs/` (page base class, shared menus, sessions, drag and drop), `Editor/` (diff panes), `Folders/`, `Binary/`, `Images/`, `Theming/`, `Dialogs/`, `Resources/` (icons, toolbar styles)
- `tests/FileCompare.Tests`: xUnit tests
- `installer/FileCompare.iss`: Inno Setup script; `scripts/build-installer.ps1` builds it
- `build/`: app icon and the third-party license texts (`build/licenses/`)
- `docs/`: user manual and build instructions
- Planned: `src/FileCompare.Shell` (Windows 11 context menu handler) and `packaging/` (sparse MSIX package), see phase 12

## Command line
```
FileCompare.exe                           Home tab
FileCompare.exe "left" "right"            compare two paths (the view is picked from the paths)
FileCompare.exe "path"                    one path as the left side
FileCompare.exe --register-shell          add the Explorer menu entries (used by the installer)
FileCompare.exe --unregister-shell        remove them
FileCompare.exe --select-left "path"      Explorer menu commands
FileCompare.exe --compare-to "path"
FileCompare.exe --compare-selected "path"
```
One copy runs per user (named mutex and a per-user named pipe). Later launches open a new tab in the running window.

## Context menu
- Entries: "Select left side to compare", "Compare to 'name'" (only after a left side is selected), and "Compare selected items" when two items are selected.
- Classic menu (Windows 10, and Windows 11 "Show more options"): registry verbs under `HKCU\Software\Classes\*\shell`, `Directory\shell`, `Directory\Background\shell`. No admin needed. **Done.**
- Windows 11 top-level menu: `IExplorerCommand` COM handler in a sparse MSIX package. See phase 12.

## Phases
1. Foundation: solution, CLI parsing, main window shell. **Done**
2. Text compare MVP: load with encoding detection, side-by-side diff, highlighting, filler lines, synced scrolling, next/prev section, swap, reload, ignore whitespace/case. **Done**
3. Editing and merge: editable panes, copy left/right per difference, save, undo/redo, live re-diff while typing. **Done**. Undo history lives in `TextCompareSession` (100 steps, cleared by Swap and Reload).
4. Display options: All/Diffs/Same/Context filters, collapsed unchanged lines, Rules and Format, inline character diffs, syntax highlighting, Patience/Myers choice. **Done**. Not planned: the Beyond Compare "grammar" and "conversion" tabs of Format.
5. Folder compare: tree with status, size and date columns; content compare; ignore patterns (.git, node_modules, bin, obj); copy/sync; double-click opens a file compare. **Done**. Sync is one-way "update" (copies new and newer files, never deletes). A mirror that deletes is phase 11, faster scans phase 10.
6. Shell integration: single instance, registry context menu, select-left/compare-to flow. **Done** (Tools > Explorer integration, `--register-shell` / `--unregister-shell`, installer option). The Windows 11 top-level menu is phase 12. The installed menu still needs a hand test (phase 9).
7. Polish: tabs, sessions, recent history, dark theme, hex and image compare, drag and drop. **Done**. Not done: dragging tabs to reorder or into a new window; hex compare finds no insertions (it compares at the same offsets); image compare has no zoom beyond fit and 100 %; sessions store the two paths, not per-session rules. Drag and drop still needs a hand test (phase 9).
8. Packaging: self-contained single-file publish and a per-user Inno Setup installer that writes and removes the Explorer menu entries. **Done**. `scripts/build-installer.ps1` publishes the exe, downloads the third-party license texts once, compiles `installer/FileCompare.iss` and writes `dist/FileCompare-Setup-<version>-x64.exe` with a SHA-256 file. The installer goes to `%LocalAppData%\Programs\File Compare` without admin rights, adds a Start menu entry, optionally a desktop icon and the Explorer menu, asks to close a running copy (mutex `FileCompare.Running`), and uninstalls cleanly while keeping the settings. Install, reinstall and uninstall were tested silently. Not done: code signing (needs a certificate; the steps are in `docs/BUILDING.md`), an ARM64 installer, and publishing releases from CI.

The next phases close what is still open. They are ordered so that cheap checks come first and the work with outside requirements (a signing certificate, a C++ toolchain) comes last.

### 9. Hand testing: Explorer menu, drag and drop, installer
**In progress**: the checklist (`docs/TESTING.md`, 50 checks: IN-01..12, EM-01..18, DD-01..20) and `scripts/make-test-files.ps1` (sample files for every check) are written, and running the checklist is a release step in `docs/BUILDING.md`. 46 of the 50 checks are automated with FlaUI in `tests/FileCompare.UiTests` (IN-01..11, EM-01..14, EM-17, EM-18, DD-01..20), run by `scripts/run-ui-tests.ps1` (`-Installer` for IN); they are opt-in (`FILECOMPARE_UI_TESTS=1`, `FILECOMPARE_INSTALLER_TESTS=1`) and sandboxed through `FILECOMPARE_DATA_DIR` and `FILECOMPARE_SHELL_CLASSES_KEY`. EM-15, EM-16 and IN-12 stay manual. The automated run found one bug, now fixed: the text panes could be changed by a drag with Edit off. Next: the first run on Windows 10 and Windows 11, then fixing what fails.

Some features cannot be checked by the automated tests or by scripts without simulating mouse input, so they get a written checklist that a person runs.

1. Write `docs/TESTING.md` with a manual checklist, one line per check, and a results table (date, Windows version and build, pass/fail, notes):
   - **Explorer menu**, on Windows 10 and on Windows 11 ("Show more options"): install with Tools > Explorer integration and with the installer task; right-click a file, a folder and the empty area of a folder; "Select left side" then "Compare to 'name'" (the label shows the name, the entry disappears after the compare); "Compare selected items" with two files, two folders, one item (becomes the left side) and three items (first two, with a message); names with spaces, `&` and non-ASCII characters; a network path; Explorer restarted; remove with the dialog and with uninstall, and check that `HKCU\Software\Classes\*\shell` has no `FileCompare.*` keys left.
   - **Drag and drop**, on each page (Home, Text, Folder, Hex, Image): one item on the left box, the right box, each editor side and elsewhere; two items anywhere; items of the wrong kind (they open in a new tab with the right view); dropping while there are unsaved edits (the prompt appears, Cancel keeps everything); dragging plain text into a path box still inserts text.
   - **Installer**: fresh install with and without the menu task; install over an older version; install while the app is running (Setup asks to close it); uninstall from Settings > Apps; the settings survive; SmartScreen behavior of the unsigned file.
2. Run the checklist, record the results, and open an issue for each failure.
3. Fix the failures; where a fix is in Core logic, add a unit test for it (for example drop handling in `DropPlan`, labels in `ShellRegistration`).
4. Repeat the checklist before each release (it becomes part of the release steps in `docs/BUILDING.md`).

Done when: the checklist exists, has passed once on Windows 10 and Windows 11, and the found bugs are fixed.

### 10. Faster scans of big folders
**Done**. Before, a content scan read files one after another and showed nothing until the end: 5,189 files (1.37 GB) took 51 s with a cold disk cache and 17.5 s warm; "size and time" mode took 0.9 s.

Measured with `tools/FileCompare.Benchmark` (Release, warm cache, median of 3, 8-core PC with 32 GB, a SATA SSD and two HDDs):

| Tree | Before: content | After: content | After: first results | Readers |
|---|---|---|---|---|
| Generated, 5,050 files, 1.2 GB per side, SSD | 6.79 s | 1.37 s (5.0x) | 0.11 s | 4 |
| NuGet cache with itself, 5,644 files, 1.4 GB | 5.23 s | 1.60 s (3.3x) | 0.24 s | 4 |
| Generated, same HDD for both sides | 3.99 s | 3.41 s (1.2x) | 0.28 s | 1 (automatic) |

"Size and time" mode on the generated tree went from 0.37 s to 0.12 s (parallel listing). When the files are not in the cache, the disk sets the pace: on the SATA SSD (about 220 MB/s here), 4 readers were 1.3x faster than 1 and 8 readers no faster than 4. A cold cache cannot be measured without admin tools, so the 51 s case was not repeated.

What was built (the original steps follow):
- Core: `FolderStatus.Pending`; `FolderComparer.ScanStructure` lists both trees, in parallel per sub-folder; `ContentComparison` reads same-size files with `Parallel.ForEachAsync` and queues the results, and the caller applies them (`ApplyResults`, folders recalculated deepest first); `MarkStopped` after Esc; `ContentCache` per session; `DiskInfo` (seek penalty and disk number via `DeviceIoControl`); `FileContent` with pooled 1 MB buffers, first and last 64 KB checked first, and "same" only for files whose size and time (read from the open handle) still match the listing.
- App: rows appear after the listing; a 200 ms timer applies results in place (the list is rebuilt only when a filter is on); status bar progress in files and bytes; Copy, Delete and Update wait; opening files works meanwhile; F5 or a new scan stops the running one; folders open at the end unless the user toggled any; Rules > "Files read at the same time" (Automatic, 1, 2, 4, 8, 16).
- Tests: `FolderScanTests` (33 cases), covering parallel vs sequential on 12 random trees (also checked against reading the files directly), every place a difference can be, stop before and halfway, files changed, touched or deleted after the listing, the cache, the status rules, filters and Update with not-compared files.

1. **Measure first.** Add a small benchmark (a console project or a test category) that scans a generated tree (many small files, a few large ones, nested folders) and the real NuGet cache, and prints the time. Record the current numbers in this plan.
2. **Two-phase scan.** Phase A lists both trees and compares names, sizes and dates only (fast, like "size and time"). The tree is shown at once: files with different sizes are "Different", files of the same size are "Comparing...". Phase B reads the same-size files in the background and updates their status as results arrive. Folder statuses are recalculated when their children change. Add `FolderStatus.Pending` and show it in the grid and the summary.
3. **Parallel content reads in phase B.** Use `Parallel.ForEachAsync` (or a `Channel` with a fixed number of workers) with a configurable degree: default 4 for SSDs, 1 when both folders are on the same spinning disk (detect with `DeviceIoControl`/`IOCTL_STORAGE_QUERY_PROPERTY` seek penalty, or let the user pick in Rules). Keep cancellation (Esc) and progress (bytes read, files left).
4. **Cheaper reads.** Larger buffers (1 MB) with `FileOptions.SequentialScan`; compare the first and last 64 KB before reading the whole file, so most different files are found without a full read (a full read is still needed to call files "Same").
5. **Batch UI updates.** Collect status changes and apply them to the grid every 200 ms, so thousands of updates do not freeze it.
6. **Reuse results.** Keep a per-session cache keyed by (relative path, size, last write time) so a refresh after copy or delete only reads what changed.
7. **Tests.** The parallel scan gives exactly the same tree and statuses as the sequential one (property test on random trees); cancellation leaves a consistent tree; a file changed during the scan ends as "Different" or "Error", never "Same".

Done when: the warm 5,189-file scan is at least 3 times faster on a 4-core SSD machine, the first results appear in under a second, and all folder tests pass.

### 11. Folder mirror and two-way sync (with deletes)
**Done**. Built as planned, with these choices:
- Core: `SyncPlanner.PlanUpdate`, `PlanMirror` and `PlanTwoWay` build one `SyncPlan` type of `SyncItem`s (copy right/left, delete left/right, conflict with the choices that make sense: Skip, Keep left, Keep right; Update offers only "keep the source"; type clashes and read errors only Skip). A folder that exists on one side only is a single item. `FolderOperations.Execute` runs copies before deletes, so a stop or failure never removes the only copy. `FolderCompareSession.RunPlanAsync` runs a plan, rescans, and saves the two-way baseline only when there were no errors and no stop.
- Baseline (`SyncBaseline`, `SyncBaselineStore`): per pair (either order) in `sync\<hash>.json` below the data folder; for every entry that is the same on both sides: path, size, left and right time (UTC) and a SHA-256 for files up to 256 KB, so a touched file is not "changed". Written through a temporary file. An unreadable baseline counts as none (first sync, which never deletes).
- Rules beyond the table above: new on both sides and different is a conflict; a folder deleted on one side is deleted on the other only if nothing inside it changed or was added. Two-way sync is refused when a side is missing or empty but a baseline exists (Mirror is the way to empty a side on purpose).
- Safety: mirror refused from a missing or empty source; a second question with the numbers above 50 deletes or more than half a side's files; deletes to the Recycle Bin.
- UI: `SyncPreviewDialog` (grouped list, checkboxes, Space toggles the selection, conflict choices, live totals); the toolbar button is now "Sync", and the Actions menu has all five entries.
- Tests: `SyncTests` (32 cases) on temporary folders; every row of the rules table, first sync, refusals, conflict choices, "deletes only what the plan listed", the baseline written only after success, store order and corrupt files, and the delete warning. Checked once in the real app (preview, second question, refusal, first and second sync).

The original plan:

1. **Mirror (one way).** Make the target an exact copy of the source: copy files that are only on the source or different, delete files and folders that are only on the target. Core: `FolderOperations.PlanMirror(root, toRight)` returns a plan with copy and delete lists, counts and bytes, like `PlanUpdate`. Errors and items on one side that are a file on the other side are listed as conflicts and skipped.
2. **Two-way sync with a baseline.** Without history, a file that exists on one side only could be new there or deleted on the other side, so a safe two-way sync needs to know the state after the last sync. Store a baseline per folder pair (relative path, size, last write time, and a content hash for small files) in `%AppData%\FileCompare\sync\<hash of both roots>.json`, written after every successful sync. Rules: changed on one side since the baseline: copy to the other side; deleted on one side and unchanged on the other: delete there; changed on both sides, or deleted on one and changed on the other: conflict, shown to the user (keep left, keep right, skip). The first sync of a pair has no baseline and behaves like a two-way "update" (never deletes).
3. **Preview dialog** (shared by Update, Mirror and Sync): a list of the planned actions grouped by kind (copy right, copy left, delete left, delete right, conflicts) with sizes, checkboxes to leave items out, and totals. Nothing happens until the user confirms.
4. **Safety.** Deletes go to the Recycle Bin. Ask again, with the numbers, when a plan deletes more than 50 files or more than half of the target. Refuse to mirror when the source root is empty or missing (a common cause of mass deletes). Cancel (Esc) stops between files; afterwards the tree is scanned again.
5. **UI.** In the folder toolbar, the Update button becomes a drop-down: Update right, Update left, Mirror to right, Mirror to left, Sync both ways.
6. **Tests** on temporary folders: mirror makes both sides equal; deletes only what the plan listed; two-way sync for every row of the rules table; a missing or empty source root is refused; the baseline is written only after success.

Done when: mirror and two-way sync work from the preview dialog, the tests cover every rule, and the user manual describes them.

### 12. Windows 11 top-level context menu
On Windows 11 the classic registry entries only appear under "Show more options". The new menu only shows commands from apps that have a package identity and implement `IExplorerCommand`. For a desktop app that is not distributed as MSIX this means a sparse package.

1. **Shell extension DLL** (`src/FileCompare.Shell`, C++ with WRL or C++/WinRT, built with MSBuild; a .NET shell extension would load the .NET runtime into Explorer, which Microsoft advises against). It implements `IExplorerCommand` (and `IEnumExplorerCommand` for a "File Compare" submenu) with three commands:
   - `GetTitle`: "Select left side to compare", "Compare to 'name'" (the name read from `%AppData%\FileCompare\left-side.txt`), "Compare selected items".
   - `GetState`: hide "Compare to" when nothing is remembered, hide "Compare selected items" unless exactly two items are selected.
   - `Invoke`: start `FileCompare.exe` with `--select-left`, `--compare-to` or the two paths, next to the DLL. The DLL stays small and has no other logic; the app does the work.
2. **Package identity for the exe.** Add an application manifest to `FileCompare.App` with an `msix` element (publisher, package name, application id) that matches the sparse package.
3. **Sparse package** (`packaging/AppxManifest.xml`): `uap10:AllowExternalContent`, the `com:Extension` that registers the DLL's CLSID (`SurrogateServer`), and the `desktop4:FileExplorerContextMenus` entries for files (`*`), folders and the folder background. Build it with `makeappx.exe` from the Windows SDK.
4. **Signing.** A sparse package must be signed with a certificate the PC trusts. For development: a self-signed certificate (trusting it needs one admin step on that PC) or Developer Mode with a loose registration. For releases: a real code signing certificate, the same one that signs the exe and the installer (phase 8, optional signing).
5. **Installer.** On Windows 11 the installer registers the package per user with `Add-AppxPackage -Path FileCompare.msix -ExternalLocation {app}` and removes it on uninstall with `Remove-AppxPackage`. The classic registry entries stay for Windows 10 and for "Show more options".
6. **Tests.** Unit-test the title and state rules (they live in a small shared C++ part, or are mirrored in Core and tested there); add the Windows 11 menu to the phase 9 checklist.

Requirements before starting: the Visual Studio C++ build tools and the Windows SDK (for `makeappx` and `signtool`) on the build machine and in CI, and a decision on the signing certificate. Done when: the three entries appear in the Windows 11 top-level menu after a normal install and disappear after uninstall.

## Backlog
Smaller items left from earlier phases, not scheduled yet:
- Drag tabs to reorder them or into a new window.
- Hex compare that detects inserted and removed bytes.
- Image zoom steps beyond fit and 100 %, and a tolerance setting for small color changes.
- Sessions that also store their rules and view settings.
- Histogram diff as a third alignment choice.
- ARM64 installer, and building releases from CI with the checksum published next to the installer.

## Risks
- Large files: text files are read fully into memory; inline diffs are skipped on lines over 2,000 characters; hex compare is limited to 256 MB per file and image compare to 100 megapixels.
- Diff quality on code: Patience is available in Rules (Alignment). Histogram diff is not implemented.
- Windows 11 menu: registry entries alone are hidden under "Show more options" until phase 12 is done.
- Antivirus: freshly built unsigned exes and test scripts have been flagged and quarantined on the development PC (Avast and Bitdefender). Code signing would reduce this.
- Locked files: open with `FileShare.ReadWrite`.
- Encoding: preserve original encoding and line endings on save (files with mixed line endings are saved with Windows line endings).
- .NET 9 support ends in November 2026; move to the current LTS (.NET 10) once its SDK is installed on the build machine and in CI.
