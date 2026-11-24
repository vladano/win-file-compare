# File Compare: User Manual

File Compare is a free file and folder compare tool for Windows 10 and 11. It shows two files or two folders side by side, highlights what differs, and lets you copy changes from one side to the other.

This manual covers installing the app, using it, and using it from the Explorer right-click menu.

## Contents

1. [Getting the app](#1-getting-the-app)
2. [Starting the app](#2-starting-the-app)
3. [The main window](#3-the-main-window)
4. [Comparing text files](#4-comparing-text-files)
5. [Comparing folders](#5-comparing-folders)
6. [Comparing binary files (hex) and pictures](#6-comparing-binary-files-hex-and-pictures)
7. [Using the Explorer right-click menu](#7-using-the-explorer-right-click-menu)
8. [Tabs, Home and sessions](#8-tabs-home-and-sessions)
9. [Themes](#9-themes)
10. [Command line](#10-command-line)
11. [Keyboard shortcuts](#11-keyboard-shortcuts)
12. [Where settings are stored](#12-where-settings-are-stored)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Getting the app

File Compare is a single program, `FileCompare.exe`. You can install it with the installer, use a standalone copy, or run it from source. None of them needs .NET installed, except running from source.

**Option A: the installer (recommended)**

Run `FileCompare-Setup-<version>-x64.exe` (build it with `scripts\build-installer.ps1`, see [Building](BUILDING.md#installer)).

- It installs for your user only, into `%LocalAppData%\Programs\File Compare`. No administrator rights are needed.
- It adds File Compare to the Start menu, and optionally a desktop icon.
- The box **Add File Compare to the Explorer right-click menu** (on by default) installs the menu for you (see [section 7](#7-using-the-explorer-right-click-menu)).
- To remove it, use **Settings > Apps > File Compare > Uninstall**. This also removes the Explorer menu entries. Your settings (theme, rules, recent list, sessions) are kept in `%AppData%\FileCompare`; delete that folder if you want them gone too.
- Close File Compare before installing a new version; Setup reminds you if it is still running.

The installer is not code signed yet, so SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**.

**Option B: a standalone copy (no installer)**

Build the standalone exe once (see [Building](BUILDING.md)):

```powershell
dotnet publish src/FileCompare.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Copy `publish\FileCompare.exe` to a folder you will keep, for example `C:\Tools\FileCompare\`. Use `-r win-arm64` on Windows on ARM.

The exe is not code signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**.

**Option C: run from source**

Needs the [.NET SDK](https://dotnet.microsoft.com/download) 9.0 or newer:

```powershell
git clone https://github.com/vladano/win-file-compare.git
cd win-file-compare
dotnet run --project src/FileCompare.App
```

**With options B and C, put the exe in a permanent folder before you add the Explorer menu.** The menu stores the full path of the exe. If you move or delete the exe later, the menu entries stop working until you install them again (see [section 7](#7-using-the-explorer-right-click-menu)).

## 2. Starting the app

You can start File Compare in these ways:

- Double-click `FileCompare.exe`. The **Home** tab opens.
- From a terminal: `FileCompare.exe "C:\a\file1.txt" "C:\b\file1.txt"`.
- From the Explorer right-click menu (after you install it, see [section 7](#7-using-the-explorer-right-click-menu)).
- Drag two files or two folders onto the window.

Only one copy runs per user. If File Compare is already open, a new launch opens a new tab in the running window instead of a second window.

The app picks the right view for the paths you give it:

| What you open | View |
|---|---|
| Two folders (or one folder) | Folder compare |
| Pictures (PNG, JPEG, BMP, GIF, TIFF, ICO, JPEG XR) | Image compare |
| Other binary files | Hex compare |
| Everything else | Text compare |

## 3. The main window

Every comparison is a **tab** in one window. Each tab has:

- A **menu bar** (File, Edit or Actions, View, Tools, About).
- A **toolbar** with icon buttons. Hover over a button to see what it does.
- Two **path boxes**, left and right. Type a path and press Enter, click **...** to browse, or drop a file or folder on the box.
- An **info line** for each side (date, size and, for text, encoding and line endings).
- A **status bar** with the number of differences, Same or Different, and the load time.

Buttons shared by the compare views:

| Button | What it does |
|---|---|
| Home | Go to the Home tab |
| Sessions | Save this comparison, or open a saved or recent one |
| Swap | Switch left and right |
| Reload (F5) | Read both sides again from disk |
| Theme | Pick a color theme |

### Drag and drop

Drop files or folders from Explorer:

- on a **path box** or an **editor side** to set that side;
- anywhere else on a page to set the sides (two items fill both).

If you drop something that needs another view (for example folders onto a text compare), it opens in a new tab with the right view.

## 4. Comparing text files

Open two text files. The files appear side by side. Lines that differ are highlighted, and gray **filler lines** keep the two sides aligned. Scrolling one side scrolls the other.

### Moving between differences

Use **Next Section** and **Prev Section** in the toolbar. Navigation wraps at the ends. The status bar shows "Difference 3 of 12".

### View modes

| Button | Shows |
|---|---|
| All | Every line |
| Diffs | Only the differences |
| Same | Only lines that are the same |
| Context | Differences plus a few lines around them |
| Minor | Lines that differ only in unimportant ways (whitespace or case), in a separate color |

Hidden lines collapse into "N filtered lines" rows. Filtered views cannot be edited, so Edit turns off when you leave **All**.

### Rules

**Tools > Rules** (or the Rules button) chooses what counts as unimportant:

- leading whitespace (indentation)
- embedded whitespace (amount of space between words)
- trailing whitespace
- character case

It also sets the **alignment** algorithm (Standard, or Patience, which often reads better for source code where blocks moved) and how many **lines of context** the Context view shows. Lines that differ only in the chosen ways are not counted as differences. Use **Minor** to see them.

### Format

**Tools > Format** (or the Format button):

- **File encoding** for each side: Detect automatically, UTF-8, UTF-8 BOM, UTF-16 LE/BE or ANSI. Changing it reloads the files.
- **Tab size**.
- **Syntax highlighting** by file extension.
- **Mark the characters that changed inside a line** (inline character diffs).
- **Show spaces and tabs** and **show line breaks**.

### Editing and saving

1. Click **Edit** to turn on editing. You can type on either side. Filler lines are locked.
2. The differences update after a short pause in typing. Press **F6** to update at once.
3. Save with **Ctrl+S** (File > Save Left, Save Right or Save All). Files keep their encoding, BOM and line endings.

Edit menu:

- **Undo / Redo**: Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z), up to 100 steps. Swap and Reload clear the history.
- **Copy section to right / left**: copies the current difference to the other side. The **Copy** toolbar button opens the same choice.

If you close a tab, reload, or open other files while there are unsaved changes, you are asked first. **Swap** keeps unsaved edits.

## 5. Comparing folders

Open two folders. The result is a tree with name, size, date and a status for every entry:

- **Same**
- **Different**
- **Left only**
- **Right only**
- **Comparing...** (only while the scan runs)

The scan has two steps. First both folders are listed, which takes a moment even for thousands of files, and the tree appears at once: files of different sizes are already **Different**, and files of the same size say **Comparing...**. Then their contents are read in the background, several files at a time, and the statuses fill in. The status bar shows how many files and bytes are done. You can scroll, filter and open files meanwhile; Copy, Delete and Sync wait until the comparison is done.

Folders that contain differences open automatically when the comparison ends (unless you opened or closed folders yourself in the meantime). **Esc** stops the scan. Files that were not compared yet then say **Not compared**, and **F5** compares again. A file that changed while it was being compared shows an error asking you to press F5; it is never reported as the same.

Reloading after a copy, delete or update only reads the files that changed since they were compared.

### Filters and navigation

The **Different**, **Left only**, **Right only** and **Same** toggle buttons show or hide those entries. **Expand** and **Collapse** open or close every folder. Use the arrow keys (Right opens, Left closes) or click the arrow next to a folder.

### Opening files

Double-click a file, or select it and press **Enter** (or click **Open**). It opens in a new tab in the view that fits: text, image or hex compare.

### Copy, delete and sync

Select one or more entries, then:

| Button | What it does |
|---|---|
| Copy right / Copy left | Copies the selection to the other side. You are asked before existing files are replaced. |
| Delete | Deletes the selection from the left or the right side, to the Recycle Bin. |
| Sync | A menu with **Update**, **Mirror** and **Sync both ways** (below). They work on the whole folders, not the selection. |

### Update, mirror and sync

The **Sync** button (and the **Actions** menu) offers three ways to bring the folders together. Each one first shows a **preview**: what will be copied and deleted, grouped by kind, with sizes and the reason for each item. Untick an item (or select several and press **Space**) to leave it out. Nothing happens until you press the button at the bottom. Copies run first and deletes last, every delete goes to the Recycle Bin, and **Esc** stops between files (the folders are then scanned again).

| Operation | What it does | Deletes |
|---|---|---|
| **Update right / left** | Copies files that are only on one side, or newer there, to the other side. A file that differs but is older is listed as a conflict; pick **Keep left/right** to copy it anyway. | Never |
| **Mirror to right / left** | Makes one side an exact copy of the other: copies what is missing or different, and deletes what is only on the target. | On the target |
| **Sync both ways** | Brings the changes made on each side since the last sync to the other side. | On either side |

**Mirror** is refused when the source folder is missing or empty, because that would delete everything on the other side (for example when a drive is not plugged in). When an operation deletes more than 50 files, or more than half of the files on a side, you are asked again with the numbers.

**Sync both ways** remembers the state of the two folders after each successful sync (in `%AppData%\FileCompare\sync`). That is how it can tell a file that was deleted on one side from a file that is new on the other:

| Since the last sync | What happens |
|---|---|
| Changed or new on one side | Copied to the other side |
| Deleted on one side, unchanged on the other | Deleted on the other side too |
| Changed on both sides, new on both sides, or deleted on one side and changed on the other | A **conflict**: choose **Keep left**, **Keep right** or **Skip** (the default). Keeping a deleted side deletes the other one. |

A file whose time changed but whose content did not (for small files up to 256 KB) does not count as changed. The **first** sync of two folders has nothing to compare with, so it never deletes: files on one side are copied to the other, and a file that differs goes from the newer side (a conflict if neither is newer). If a sync had errors or was stopped, the state is not saved, so the next sync sees the same changes again. Sync is also refused when one side is empty after an earlier sync; use Mirror if you emptied it on purpose.

A file on one side and a folder with the same name on the other cannot be synced; it is listed as a conflict and skipped.

### Folder rules

**Tools > Rules** (or the Rules button):

- **Ignore list**: one name per line. `*` and `?` work and case does not matter. The names match files and folders at any depth. Default: `.git`, `node_modules`, `bin`, `obj`, `.vs`.
- **Compare files by**: **Content** reads the files and is exact. **Size and time** is much faster on large folders, but a file that was only touched looks different.
- **Files read at the same time**: **Automatic** reads 4 files at once on SSDs and network drives, 2 when one of the folders is on a spinning disk, and 1 when both are on the same spinning disk (where reading several files at once is slower). Pick a number to override it.

## 6. Comparing binary files (hex) and pictures

### Hex compare

Opens for binary files (up to 256 MB). It shows offset, hex bytes and ASCII for both files. Changed bytes are highlighted, and bytes that exist on one side only are shaded. Use **Next Section** and **Prev Section** to jump between differences, or scroll with the mouse wheel, arrow keys, Page Up/Down and Ctrl+Home/End.

Hex compare matches bytes at the same offsets. It does not detect inserted bytes.

If a file only looks binary, use **File > Compare as text**.

### Image compare

Opens for picture files. Two views:

- **Side by side**: both pictures with synchronized scrolling.
- **Difference**: changed pixels in red, pixels that exist on one side only in orange.

**Fit** shrinks the pictures to the window. Turn it off to see them at 100 % and scroll. The status bar shows how many pixels differ. For a GIF only the first frame is compared.

To compare the raw bytes instead, use **File > Compare as hex**.

## 7. Using the Explorer right-click menu

File Compare can add entries to the Windows Explorer right-click menu, so you can compare without opening the app first.

### Install the menu

1. Start `FileCompare.exe` from the folder where you want to keep it.
2. Choose **Tools > Explorer integration...**.
3. Check the status and the path shown under "The entries start". If the path is a build or temporary folder, copy the exe to a permanent location first.
4. Click **Install**.

The dialog lists the registry keys it writes (expand **Registry keys that are written**).

Alternative, without the window: `FileCompare.exe --register-shell`. This is handy in an installer or script.

What the install does:

- Writes entries for your user only, under `HKEY_CURRENT_USER\Software\Classes`. **No administrator rights are needed** and other users on the PC are not affected.
- Adds the entries for files, for folders, and for the empty area inside an open folder.

If the exe moves, open **Tools > Explorer integration...** from the new location and click **Install again**.

### The entries

| Entry | Where it appears | What it does |
|---|---|---|
| **Select left side to compare** | Files, folders, empty area of an open folder | Remembers the item as the left side |
| **Compare to '*name*'** | Appears after a left side is selected | Compares the remembered left side with the item you clicked |
| **Compare selected items** | Files and folders (not the empty area) | Compares the two items you selected |

### Windows 11

Windows 11 shows a short menu first. The File Compare entries are under **Show more options** (or press **Shift+F10**, or hold **Shift** while you right-click). A top-level Windows 11 entry is planned but not available yet.

### Compare two items you pick one after another

1. Right-click the first file or folder and choose **Select left side to compare**.
2. Right-click the second one and choose **Compare to 'first-name'**.

The two items open in a new tab. You can pick items from different folders. For folders you can also right-click the empty area inside an open folder: **Select left side** remembers that folder, and **Compare to** compares with it.

The left side is forgotten after a comparison. If the remembered item was deleted or moved in the meantime, the clicked item opens as the left side and you choose the other one in the app.

### Compare two items you select together

1. Select two files (or two folders) in Explorer. Hold **Ctrl** and click each.
2. Right-click one of them and choose **Compare selected items**.

Notes:

- If you select **more than two** items, only the first two are compared, and a message tells you so.
- If only **one** item is selected, it is remembered as the left side, like **Select left side to compare**.
- Pick two items of the same type (two files or two folders).

### Remove the menu

Open **Tools > Explorer integration...** and click **Remove**, or run:

```powershell
FileCompare.exe --unregister-shell
```

This deletes exactly the keys that were added, and forgets the remembered left side.

## 8. Tabs, Home and sessions

### Tabs

- **Ctrl+T**: new tab (the Home tab).
- **Ctrl+W**: close the current tab. Middle-click on a tab also closes it.
- **Ctrl+Tab** and **Ctrl+PageDown**: next tab. **Ctrl+Shift+Tab** and **Ctrl+PageUp**: previous tab.
- The window closes with its last tab. Tabs with unsaved changes ask before they close.

### Home

The Home tab is where you start a comparison. Enter or browse the two paths (use **File...** or **Folder...**), then click **Compare**. The right view opens for you. Home also lists your **saved sessions** and **recent comparisons**. Click an entry to open it. **Remove** next to a saved session deletes it. **Clear list** forgets the recent list (no files are touched).

### Sessions

A session is a saved pair of paths with a name.

- **Save**: in any comparison, click **Sessions > Save as session...** (or File > Save as session...) and type a name. If the name exists you are asked before it is replaced.
- **Open**: click **Sessions** and pick a saved session, or pick one on the Home tab.
- **Recent**: **File > Recent** (and the Sessions button) lists the last ten comparisons.

A session stores the two paths, not the rules.

## 9. Themes

Click **Theme** in the toolbar, or use **View > Theme**. Choices: System default (follows Windows), Light, Dark, Solarized Light, Solarized Dark, Dracula and Nord. The theme applies at once and is remembered.

## 10. Command line

```
FileCompare.exe                           open the app (Home tab)
FileCompare.exe "left" "right"            compare two files or two folders
FileCompare.exe "path"                    use one file or folder as the left side
FileCompare.exe --register-shell          add the Explorer right-click entries (current user)
FileCompare.exe --unregister-shell        remove them again
```

- Paths may be relative. They are made absolute before they reach the running copy.
- Unknown switches are ignored.
- The switches `--select-left`, `--compare-to` and `--compare-selected` are used by the Explorer entries. You normally do not type them yourself.

## 11. Keyboard shortcuts

| Keys | Action |
|---|---|
| F5 | Reload |
| F6 | Update differences now (text, while editing) |
| Ctrl+S | Save all (text) |
| Ctrl+Z / Ctrl+Y | Undo / Redo (text; also Ctrl+Shift+Z for redo) |
| Ctrl+T | New tab |
| Ctrl+W | Close tab |
| Ctrl+Tab, Ctrl+PageDown / Ctrl+Shift+Tab, Ctrl+PageUp | Next / previous tab |
| Enter | Open the selected file (folder compare), or compare after typing a path |
| Esc | Stop a running folder scan |
| Right / Left arrow | Open / close the selected folder (folder compare) |
| Arrow keys, Page Up/Down, Ctrl+Home/End | Scroll (hex compare) |

In a path box, Ctrl+Z and Ctrl+Y undo and redo your typing in the box.

## 12. Where settings are stored

All files are in `%AppData%\FileCompare\` (usually `C:\Users\<you>\AppData\Roaming\FileCompare\`):

| File | Contents |
|---|---|
| `settings.json` | Theme, Rules, Format, folder rules, recent list and saved sessions |
| `left-side.txt` | The item chosen with "Select left side to compare" |
| `sync\*.json` | For each pair of folders synced both ways, the state after the last sync. Delete a file to make the next sync of that pair a first sync again (which never deletes). |

The Explorer entries are stored in the registry under `HKEY_CURRENT_USER\Software\Classes` (`*\shell`, `Directory\shell` and `Directory\Background\shell`). Their key names start with `FileCompare.`.

To reset the app, close it and delete `settings.json`.

## 13. Troubleshooting

| Problem | What to do |
|---|---|
| The menu entries are not in the right-click menu | On Windows 11 look under **Show more options**. Check **Tools > Explorer integration...** shows "Installed". |
| The entries do nothing, or show an error | The exe moved or was deleted. Open the app from its new location, go to **Tools > Explorer integration...** and click **Install again**. |
| "Compare to" is missing | Choose **Select left side to compare** first. |
| Dialog says the entries start a different copy | You have two copies of the exe. Click **Install again** in the copy you want to use. |
| The Explorer integration dialog warns about a build or temporary folder | You started a debug build or ran the exe from a temp folder. Copy the exe to a permanent folder, start it there and install again. |
| A file does not show as a text compare | Binary files open in hex and pictures in image compare. Use **File > Compare as text** in the hex view. |
| Folder compare is slow | The tree appears before the contents are compared; you can start working while the statuses fill in. To read less, compare by **Size and time** in **Rules** and add big folders to the ignore list. On an SSD, a higher **Files read at the same time** can help. |
| Windows says "Windows protected your PC" | The exe is not code signed. Click **More info**, then **Run anyway**. |
| An error message appears | Unexpected errors are shown in a message instead of closing the app, so other tabs keep their unsaved changes. Use **About > Copy details** when you report a bug at <https://github.com/vladano/win-file-compare/issues>. |
