# Fignimble for Windows

A native WPF client for the existing Fignimble Python engine. Create or open local projects, assemble a figure from plots, images, and tables, and export PDF, SVG, PNG, or TIFF. The Code drawer edits the Python code behind the figure. Canvas edits write to the real project files, and figure and panel scripts run as Python, so open only trusted projects and try existing work on a copy first.

## Install from a Windows release

Download and run **Fignimble-Windows-x64-Setup.exe** from the [Windows releases](https://github.com/franciskim-yonsei/Fignimble/releases). Setup installs for your Windows account and adds **Fignimble** to the Start menu. Run a newer installer to update. The installer bundles .NET and uses the Python 3.10 or newer installed on your PC. Use **File → Python Setup** to select it. The embedded terminal also needs the Microsoft Edge WebView2 Runtime.

To distribute the app through a shared drive, upload the installer and have each colleague download and run it. The portable ZIP is also available for colleagues who prefer to extract and run the app themselves. Each PC uses its own Python and app settings. SSH projects remain on the server.

## Install or update from the repository root

Requires .NET 10 SDK and Python 3.10 or newer. The embedded terminal also uses the Microsoft Edge WebView2 Runtime already installed on most Windows systems.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\apps\windows\install.ps1
```

This builds a Release copy at `%LOCALAPPDATA%\Fignimble\app`, sets up a reusable Python environment next to it, and makes a current-user Start menu shortcut. Close the installed app and rerun the same command to update. No administrator access is needed; projects are not copied or moved.

## Build a portable ZIP

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\apps\windows\package.ps1
```

The unsigned ZIP bundles the Windows .NET runtime. Extract it before running `Fignimble.exe`. Use **File → Python Setup** to point at an existing Python environment or run the included `setup-python.ps1` to create the app's private environment. With Inno Setup 6 installed, pass `-InstallerOutput .\Fignimble-Windows-x64-Setup.exe` to also build an installer.

## Run from the source tree in PowerShell

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -X utf8 -m pip install -r engine\requirements.txt
dotnet build apps/windows
Start-Process .\apps\windows\bin\Debug\net10.0-windows\Fignimble.exe
```

Use **Open project** to choose a directory, or add a folder or figure file after `--`. For example, to try it on a disposable copy of the demo:

```powershell
$demo = Join-Path $env:TEMP "fignimble-demo-$([guid]::NewGuid())"
Copy-Item -Recurse examples\demo $demo
Start-Process .\apps\windows\bin\Debug\net10.0-windows\Fignimble.exe -ArgumentList ('"' + $demo + '"')
```

## Workspace

The left sidebar has figure thumbnails and layers; the right inspector shows page settings or actions for the current selection. Drag the dividers to resize the panels. The center drawer groups **Code**, **Caption**, **History**, **Preflight**, and **Terminal**. **File → Open Recent** remembers local and SSH projects. **View** controls the grid, guides, and sidebar/inspector visibility. Use **Hide drawer** for more canvas room; export warnings open the Preflight tab automatically.

## Create a figure without writing a script

1. Choose **New project**, pick a shared journal style, and create or select an empty folder. **New figure** adds another figure with dimensions you can set in millimeters. **Journal width** sets the current page width and journal for preflight and export without replacing its plot style.
2. Choose **Add sample plot** for a working matplotlib panel, or **Import** for an existing `.py` panel, image, or CSV/TSV table. A Python panel should define `draw(fig)`, which receives the matplotlib figure to draw into. You can also drop supported files directly onto the page; each is placed at the drop point with a small offset for multiple files. Imports from outside the project copy the file in, not its dependencies.
3. Use **Layers** to select covered or hidden items, change stacking order, show/hide, or lock/unlock. Ctrl+click adds to or removes from a selection; the layer list also supports Shift+click ranges. Drag selected layers together, or use the alignment buttons above the canvas, the **Object** menu, or the inspector. With one layer selected, alignment is to the figure. For multiple layers, the inspector lets you align to the whole selection, the first selected layer, or the figure. It also offers equal gaps (three or more layers), fixed gaps in millimeters (two or more), and matching width or height to the first layer. Zoom with Ctrl+wheel or the zoom buttons; middle-drag, hold Space and drag with the canvas focused, or use the scroll bars to pan.
4. Select a standalone panel or image to resize it with its blue handle or to enter its exact geometry. Use **Label** to override or suppress its automatic letter. The tools above the canvas let you type directly, drag shapes, draw paths, or box-zoom. Double-click figure text or a table cell to edit it in place; Tab moves to the next table cell. **Insert → New Table** creates a standalone table figure. Click the page background or press Esc to return to page geometry. Ctrl+Z and Ctrl+Y undo and redo.
5. Review **Preflight** (select a warning to highlight its associated layer), then choose **Export** and select a format, journal resolution rules, and DPI. The engine can raise a raster export’s DPI to meet the selected journal’s minimum. Review warnings and check the journal’s current requirements before submission.

## Faster editing and export

- **Arrange** also duplicates, groups, ungroups, and removes selected layers. Drag a selection box from a blank spot on the page. Smart snapping aligns moves and resize handles to page edges, centers, other layers, and guides; hold Alt to bypass it. Drag guides from the rulers, drag their ruler markers to adjust or remove them, or use **More** to add a guide numerically or clear all guides. With the canvas focused, arrow keys nudge by 0.5 mm, Shift+arrow by 5 mm, and Ctrl+arrow by 0.1 mm.
- **Object → Edit Code** opens the selected layer's source in the drawer. The file picker also switches between figure, style, and panel files. **Apply** (Ctrl+S) is one undo step; syntax errors and external edits do not overwrite the draft. **Caption** saves figure caption text and **History** jumps between project edits.
- Select a panel, image, or table to use **More**: plot labels, image crop/rotation, an image zoom inset, a calibrated scale bar, and table cells/structure. Double-click plot text to edit it directly. **More** also exposes plot style and panel lettering. Crop before rotating or flipping an image.
- **Arrange → Make patchwork layout** puts loose panels into a grid. Select a layout and drag its editable divider lines, or use **Release layout** to put its children back on the canvas. Inset geometry can be dragged or entered numerically. Some source-defined layouts still require code editing.
- **Export** supports PowerPoint. **More** exports every figure as PDF without overwriting existing PDFs, exports a selected table to Word/Excel/CSV/TSV, and copies SVG/PDF with a bitmap fallback. Receiving programs choose which clipboard format to accept. **Copy/Paste editable layers** transfers editable elements between figures of the same project.
- **Terminal** starts an interactive PowerShell session in the project folder. Use **New** for another tab, **Split** for a second shell in the tab, and **Run…** to type a command into the active shell. **File → Install Command Line Tool** makes `fignimble` available to new local terminal sessions without changing user PATH. Launch the GUI with `Start-Process` or from Explorer/Start menu for the terminal; an inherited redirected console (for example some `dotnet run` hosts) may capture PowerShell's standard I/O.

`FIGNIMBLE_PYTHON` overrides discovery. Otherwise **File → Python Setup** supports a saved interpreter per project or as the app default. An explicit project choice wins over `.venv`/`venv`; those win over the default. **File → Check Python Packages** lists unmet engine/project requirements and installs only after approval. `FIGNIMBLE_ENGINE` can point to a different `engine` directory.

## Posters, previews, and press export

- **More → New poster from template** creates a poster with editable title, sections, and QR code. Use **Poster page settings** for orientation and safe margin, and **Place another figure** to include a live reference to another figure in the same project. Changing the page orientation does not reflow its contents automatically.
- **View** includes the figure’s dark version and live color-vision simulations; these simulations do not recolor exports. The BioCartoon browser offers category/license filters, sort, preview and insertion. Attribution travels with inserted PNG/SVG assets; right-click the image to see or copy it. The browser does not submit artwork to the shared catalog.
- **Export** includes JPEG and EPS as well as CMYK, outlined text, PDF bleed, and crop marks where supported. EPS and CMYK/outlined PDF require [Ghostscript](https://ghostscript.com/releases/gsdnld.html) on the rendering machine. Install it in the usual Windows location, add `gswin64c.exe` to `PATH`, or set `FIGNIMBLE_GHOSTSCRIPT` to its full path. Check your print shop’s requirements, especially color profiles.

## SSH projects

**SSH → Open remote project** takes the host, project directory, and the Python on the Linux server. Select **Sign in with password or verification code** for direct SSH hosts that require interactive authentication. Verify the server fingerprint when prompted. Folder browsing, project operations, and terminal tabs reuse that connection while it stays open. Fignimble remembers the host fingerprint for each Windows user. Passwords and verification codes are used only for sign-in. Use **SSH → Reconnect** if the session expires. The default uses Windows OpenSSH for key and agent authentication, including configured jump hosts. **SSH → Test login in an SSH console** is available for troubleshooting.

Fignimble runs the renderer in the selected server Python. It installs its engine files under the remote user's cache and mirrors editable files under `%LOCALAPPDATA%\Fignimble\remote`. Missing engine packages can be installed with your approval. The **Terminal** tab opens the server project directory and sets `FIGNIMBLE_PYTHON` to the server interpreter. **Local mirror shell…** opens the Windows mirror in PowerShell. The SSH menu can also import and refresh remote panel scripts. Back up the remote project before using this experimental workflow.

Windows does not reuse password/OTP sessions from the login console for background commands. Remote names must be Windows-compatible and not collide when case is ignored. The Windows tunnel and terminal launch have fixture coverage. Verify shell startup and interpreter selection on your server before relying on them.

This is not full macOS parity: clipboard formats depend on the receiving application, and visual layouts do not replace programmatic layouts. Terminal host code and xterm assets are adapted from Manimble `b88741a`; see the repository BSD license and `Terminal/LICENSE-xterm.txt`.
