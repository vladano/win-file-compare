# Manual test checklist

Some features depend on Windows itself (the Explorer menu, dragging files from Explorer, the installer) and cannot be
covered by the unit tests. This checklist covers them. Run it:

- before every release,
- after changes to the Explorer menu, drag and drop, the installer, or the tabbed window,
- on Windows 10 and on Windows 11 when possible.

A full run takes about 45 minutes. Each check has an ID. Mark the result directly in the tables (`Pass`, `Fail`, or `Skip`),
or copy a table into an issue. For a failure, note what happened instead.

## Before you start

1. **Build the installer** (it tests what users get): `powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1`.
   Uninstall any older File Compare first, and close every running copy.
2. **Create the test files:** `powershell -ExecutionPolicy Bypass -File .\scripts\make-test-files.ps1 -Open`.
   They go to `%TEMP%\FileCompare test files` and Explorer opens there. The paths below are relative to that folder.
3. **Note the Windows version:** run `winver` (for example "Windows 11 24H2, build 26100.2314").
4. **Optional:** back up `%AppData%\FileCompare` if you want to keep your settings, recent list and sessions exactly as they are.

On **Windows 11**, File Compare's entries are in the classic menu: right-click, then **Show more options**
(or select the item and press **Shift+F10**). The new top-level menu is planned (PLAN.md, phase 12).

## Automated checks

Some checks also have an automated UI test in `tests/FileCompare.UiTests` (xUnit with [FlaUI](https://github.com/FlaUI/FlaUI)).
Run them with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1                     # EM and DD, about 5 minutes
powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1 -Filter EM          # only the Explorer menu tests
powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1 -Installer          # also IN: builds the installer, about 10 minutes
powershell -ExecutionPolicy Bypass -File .\scripts\run-ui-tests.ps1 -Installer -SkipInstallerBuild -Filter Installer
```

The script builds the Debug app, tests it, and prints the results by check ID. The drag and drop tests open File
Explorer and move the real mouse, so do not use the PC while they run. The tests are sandboxed: the app gets its own
data folder (`FILECOMPARE_DATA_DIR`) and writes its Explorer menu entries to a throwaway key
(`FILECOMPARE_SHELL_CLASSES_KEY` = `HKCU\Software\FileCompare.UiTests\Classes`), so your settings, recent list,
Explorer menu and the copies of File Compare you have open are not touched. Everything is removed afterwards.
Without `FILECOMPARE_UI_TESTS=1` (which the script sets) the tests are skipped, so `dotnet test` and CI skip them.

The installer tests need a second switch, `-Installer` (`FILECOMPARE_INSTALLER_TESTS=1`), because they really install
File Compare for your user: into `%LocalAppData%\Programs\File Compare`, with a Start menu entry and, in IN-09, a
desktop shortcut. Every test uninstalls again at the end. Setup inherits the sandbox variables, so its Explorer menu
entries also go to the throwaway key; the tests fail (and clean up) if the real menu was changed anyway. They are
skipped if File Compare is already installed, so a real installation is never replaced or removed.

| Automated | How |
|---|---|
| IN-01..IN-11 | The real Setup from `dist\`, run silently (`/VERYSILENT`, `/MERGETASKS` for the task boxes), and the real uninstaller. IN-06 runs Setup with its windows, waits for the "is currently running" message, closes the app and clicks OK. The checkboxes and pages of the wizard themselves are not clicked. |
| EM-01..EM-14, EM-17, EM-18 | The menu is registered in the throwaway key, and the tests run its stored commands the way Explorer does. They check the entries, labels and what the app does, but not that Explorer shows the menu. |
| DD-01..DD-20 | Real drags from an Explorer window onto every page. DD-11 drags text from the app's own editor instead of Notepad (the same kind of drag, without touching your Notepad tabs). DD-19 drags out of a `.zip` folder. DD-20 compares a hash of every test file before and after the drops. |

An automated pass does not replace the manual run before a release. Check by hand:
- EM-01 to EM-03 once in the real Explorer menu (the tests run the menu commands, not Explorer),
- IN-01 once through the wizard pages,
- EM-15 (restarting Explorer would disturb the desktop), EM-16 (needs a network share) and IN-12 (SmartScreen needs a downloaded file). These are manual only.

## Results summary

| Date | Tester | Windows version | File Compare version | Failed checks | Notes |
|---|---|---|---|---|---|
| | | | | | |

## 1. Installer

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| IN-01 | Run `dist\FileCompare-Setup-<version>-x64.exe` and keep the defaults. | No administrator prompt. Installs to `%LocalAppData%\Programs\File Compare`. The last page offers to start File Compare, and the Home tab opens. | | |
| IN-02 | Open the Start menu and type "File Compare". | The app is listed and starts. | | |
| IN-03 | Open Settings > Apps > Installed apps. | "File Compare", version shown, publisher "vladano". | | |
| IN-04 | Right-click `text\left.txt` in Explorer. | "Select left side to compare" and "Compare selected items" are in the menu, with the app icon. | | |
| IN-05 | In File Compare, open Tools > Explorer integration. | Status: "Installed for this copy of File Compare". The path is the installed exe, not a build folder. | | |
| IN-06 | Run the installer again while File Compare is open. | Setup says File Compare is running and asks you to close it. Close it and continue; the install finishes. | | |
| IN-07 | Run the installer again and untick "Add File Compare to the Explorer right-click menu". | After the install, the entries are gone from the right-click menu. | | |
| IN-08 | Run the installer again and tick the box again. | The entries are back. | | |
| IN-09 | Run the installer again with "Create a desktop shortcut" ticked. | A desktop shortcut appears and starts the app. | | |
| IN-10 | Uninstall from Settings > Apps. | The program folder, the Start menu entry, the desktop shortcut and the Explorer entries are gone. `%AppData%\FileCompare` (settings) is still there. | | |
| IN-11 | Install again. | Your theme, recent list and sessions from before are still there. | | |
| IN-12 | Optional: download the installer through a browser, or copy it from another PC, and run it. | SmartScreen shows "Windows protected your PC" (the file is not signed). "More info" > "Run anyway" continues. | | |

## 2. Explorer menu

Install the menu first (IN-08, or Tools > Explorer integration > Install).

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| EM-01 | Right-click a file, a folder, and the empty area inside an open folder. | "Select left side to compare" everywhere. "Compare selected items" on files and folders only. No "Compare to" yet. | | |
| EM-02 | Right-click `text\left.txt` > Select left side to compare. Then right-click `text\right.txt`. | The menu shows "Compare to 'left.txt'". No File Compare window opened for "Select left side". | | |
| EM-03 | Click "Compare to 'left.txt'". | A text compare of left.txt and right.txt opens. Right-click again: "Compare to" is gone. | | |
| EM-04 | Same as EM-02 and EM-03 with the folders `folders\left` and `folders\right`. | A folder compare opens. | | |
| EM-05 | Open `folders\left`, right-click the empty area > Select left side. Open `folders\right`, right-click the empty area > Compare to 'left'. | A folder compare of the two folders opens. | | |
| EM-06 | Select `text\left.txt` and `text\right.txt` together, right-click > Compare selected items. | One comparison of the two files opens (not two windows). | | |
| EM-07 | Same with the two folders `folders\left` and `folders\right`. | A folder compare opens. | | |
| EM-08 | Right-click one item > Compare selected items. Then right-click another item. | Nothing opens; the item became the left side, and the second menu shows "Compare to '<first name>'". | | |
| EM-09 | Select three files > Compare selected items. | A message says only two can be compared; the first two open. | | |
| EM-10 | Select left side on `text\notes & ideas (v1).txt`, then right-click `text\notes & ideas (v2).txt`. | The menu reads "Compare to 'notes & ideas (v1).txt'" with a normal "&" (no underlined letter). The compare opens. | | |
| EM-11 | Same with the two `text\Zürich` files. | The name shows correctly and the compare opens. | | |
| EM-12 | Compare `images\before.png` with `images\after.png`, and `binary\app-v1.bin` with `binary\app-v2.bin`, from the menu. | An image compare and a hex compare open. | | |
| EM-13 | With File Compare already open, compare two items from the menu. | They open as a new tab in the open window. No second window. | | |
| EM-14 | Close File Compare. Use Select left side. Check Task Manager after a few seconds. | No FileCompare.exe is left running. | | |
| EM-15 | Restart Explorer (Task Manager > Windows Explorer > Restart), then right-click a file. | The entries are still there. | | |
| EM-16 | Optional: compare two files on a network share or mapped drive. | The compare opens. | | |
| EM-17 | Tools > Explorer integration > Remove. Right-click a file. | The entries are gone. | | |
| EM-18 | Run in PowerShell: `'*','Directory','Directory\Background' \| % { Get-ChildItem "Registry::HKEY_CURRENT_USER\Software\Classes\$_\shell" -EA 0 } \| ? PSChildName -like 'FileCompare.*'` | No output (no keys left behind). | | |

## 3. Drag and drop

Drag items from the test files folder in Explorer onto File Compare. While dragging over a place that accepts files,
the cursor shows "Copy". Nothing is moved or copied on disk.

### Home tab

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-01 | Drop `text\left.txt` on the Left box. | The box shows its path. The note says "Now pick or drop the other side". | | |
| DD-02 | Drop `text\right.txt` on the Right box, then click Compare. | The note says "Press Compare to start" after the drop; Compare opens the text compare in the same tab. | | |
| DD-03 | In a new Home tab (Ctrl+T), drop `text\left.txt` and `text\right.txt` together anywhere on the page. | The comparison opens at once, in place of the Home tab. | | |
| DD-04 | In a new Home tab, drop `folders\left` and `folders\right` together. | A folder compare opens. | | |

### Text compare

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-05 | Open left.txt / right.txt. Drop `text\notes & ideas (v1).txt` on the left path box. | The left side is replaced and compared again. | | |
| DD-06 | Drop `text\notes & ideas (v2).txt` on the right editor (the text area). | The right side is replaced. | | |
| DD-07 | Drop `text\left.txt` and `text\right.txt` together on the status bar. | Both sides are replaced. | | |
| DD-08 | Drop `folders\left` and `folders\right` together on the page. | A folder compare opens in a new tab. The text tab says "Opened in a new folder compare tab". | | |
| DD-09 | Turn on Edit, type a few characters, then drop another file on the left path box. Answer Cancel. | You are asked about unsaved changes. After Cancel the paths and your typing are unchanged. | | |
| DD-10 | Repeat DD-09 and answer No. | The new file is loaded and your typing is discarded. | | |
| DD-11 | Select some text in Notepad and drag it into a path box. | The text is inserted into the box as usual. Nothing is compared until you press Enter. | | |

### Folder compare

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-12 | Open the two folders. Drop `mixed\a folder` on the left path box. | The left side is replaced and scanned again. | | |
| DD-13 | Drop `folders\left` and `folders\right` together on the list. | Both sides are replaced. | | |
| DD-14 | Drop `text\left.txt` and `text\right.txt` together on the list. | A text compare opens in a new tab. The folder tab is unchanged. | | |

### Hex compare

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-15 | Open the two `.bin` files. Drop `binary\app-v1.bin` on the right path box. | Both sides are the same file; the status says the files are identical. | | |
| DD-16 | Drop `images\before.png` and `images\after.png` together on the page. | An image compare opens in a new tab. The hex tab is unchanged. | | |

### Image compare

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-17 | Open before.png / after.png. Drop `images\other-size.png` on the right picture. | The right side is replaced. The status starts with "The sizes differ". | | |
| DD-18 | Drop `mixed\a file.txt` and `mixed\another file.txt` together on the page. | A text compare opens in a new tab. The image tab is unchanged. | | |

### Anywhere

| ID | Do this | Expected | Win 10 | Win 11 |
|---|---|---|---|---|
| DD-19 | Drag a file from inside a .zip folder in Explorer onto a path box. | It is either used (Windows extracts a temporary copy) or ignored. The app does not crash. | | |
| DD-20 | After all drops, look at the test folder in Explorer. | No file was moved, copied or changed. | | |

## Reporting a failure

Open an issue on GitHub with:
- the check ID and what happened instead of the expected result,
- the Windows version (`winver`),
- the text from **About > Copy details** (File Compare version, Windows and .NET versions).

When a failure is fixed, run its check again and, if the fix is in Core logic, add a unit test for it.
