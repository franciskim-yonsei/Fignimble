# Fignimble for Windows

A native WPF client for the existing Fignimble Python engine. Create or open local projects, assemble a figure from plots, images, and tables, and export PDF, SVG, PNG, or TIFF. The source pane shows the Python code behind the figure. Canvas edits write to the real project files, and figure and panel scripts run as Python, so open only trusted projects and try existing work on a copy first.

## Install or update from the repository root

Requires .NET 10 SDK and Python 3.10 or newer. The embedded terminal also uses the Microsoft Edge WebView2 Runtime already installed on most Windows systems.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\apps\windows\install.ps1
```

This builds a Release copy at `%LOCALAPPDATA%\Fignimble\app`, sets up a reusable Python environment next to it, and makes a current-user Start menu shortcut. Close the installed app and rerun the same command to update. No administrator access is needed; projects are not copied or moved.

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

## Create a figure without writing a script

1. Choose **New project**, pick a shared journal style, and create or select an empty folder. **New figure** adds another figure with dimensions you can set in millimeters. **Journal width** sets the current page width and journal for preflight and export without replacing its plot style.
2. Choose **Add sample plot** for a working matplotlib panel, or **Import** for an existing `.py` panel, image, or CSV/TSV table. A Python panel should define `draw(fig)`, which receives the matplotlib figure to draw into. Importing a file outside the project copies it in; dependencies such as data files are not copied automatically.
3. Use **Layers** to select covered or hidden items, change stacking order, show/hide, or lock/unlock. Ctrl+click adds to or removes from a selection; the layer list also supports Shift+click ranges. Drag selected layers together or use **Arrange** to align or distribute them. Zoom with Ctrl+wheel or the zoom buttons; middle-drag or use the scroll bars to pan.
4. Select a standalone panel or image to resize it with its blue handle or to enter its exact geometry. Use **Label** to override or suppress its automatic letter. **Annotate** adds text, arrows, and brackets; double-click the annotation’s layer row to edit its details. Click the page background or press Esc to return to page geometry. Ctrl+Z and Ctrl+Y undo and redo.
5. Review **Preflight**, then choose **Export** and select a format, journal resolution rules, and DPI. The engine can raise a raster export’s DPI to meet the selected journal’s minimum. Review warnings and check the journal’s current requirements before submission.

## Faster editing and export

- **Arrange** also duplicates, groups, ungroups, and removes selected layers. Drag a selection box from a blank spot on the page. Smart snapping aligns moves and resize handles to page edges, centers, other layers, and guides; hold Alt to bypass it. Drag guides from the rulers, drag their ruler markers to adjust or remove them, or use **More** to add a guide numerically or clear all guides. With the canvas focused, arrow keys nudge by 0.5 mm, Shift+arrow by 5 mm, and Ctrl+arrow by 0.1 mm.
- **Edit source** opens the figure, style, or a panel file. Apply is one undo step; the editor refuses syntax errors and checks for conflicting changes on disk. The main pane still previews the figure source, and saved external edits still refresh it. **More** also opens the figure caption and the project’s edit history.
- Select a panel, image, or table to use **More**: plot labels, image crop/rotation, an image zoom inset, a calibrated scale bar, and table cells/structure. Double-click plot text to edit it directly. **More** also exposes plot style and panel lettering. Crop before rotating or flipping an image.
- **Arrange → Make patchwork layout** puts loose panels into a grid. Select a layout and drag its editable divider lines, or use **Release layout** to put its children back on the canvas. Inset geometry can be dragged or entered numerically. Some source-defined layouts still require code editing.
- **Export** supports PowerPoint. **More** exports every figure as PDF without overwriting existing PDFs, exports a selected table to Word/Excel/CSV/TSV, and copies SVG/PDF with a bitmap fallback. Receiving programs choose which clipboard format to accept. **Copy/Paste editable layers** transfers editable elements between figures of the same project.
- **Terminal** starts an interactive PowerShell session in the project folder. Launch the GUI with `Start-Process` or from Explorer/Start menu for the terminal; an inherited redirected console (for example some `dotnet run` hosts) may capture PowerShell's standard I/O.

`FIGNIMBLE_PYTHON` overrides discovery. Otherwise a project's `.venv` or `venv` takes precedence over the source-tree or installed Python environment. Install the engine dependencies into that environment before opening the project. `FIGNIMBLE_ENGINE` can point to a different `engine` directory.

## Posters, previews, and press export

- **More → New poster from template** creates a poster with editable title, sections, and QR code. Use **Poster page settings** for orientation and safe margin, and **Place another figure** to include a live reference to another figure in the same project. Changing the page orientation does not reflow its contents automatically.
- **More** includes the figure’s dark version, a separate color-vision preview, and the BioCartoon search/preview/import browser. Check the icon’s license and preserve its credited attribution. The browser does not submit artwork to the shared catalog.
- **Export** includes JPEG and EPS as well as CMYK, outlined text, PDF bleed, and crop marks where supported. EPS and CMYK/outlined PDF require [Ghostscript](https://ghostscript.com/releases/gsdnld.html) on the rendering machine. Install it in the usual Windows location, add `gswin64c.exe` to `PATH`, or set `FIGNIMBLE_GHOSTSCRIPT` to its full path. Check your print shop’s requirements, especially color profiles.

## SSH projects (key/agent authentication)

Use **SSH → Test login in an SSH console** to check the host and verify its key. The background project connection needs a key or SSH agent usable without prompts. It respects the native Windows OpenSSH configuration; use an SSH alias for custom ports or jump hosts. **SSH → Open remote project** takes the host, project directory, and the Python on the Linux server (which must already have matplotlib, PyMuPDF, and Pillow). On connection, Fignimble installs its own engine files under the remote user’s cache, mirrors editable files under `%LOCALAPPDATA%\Fignimble\remote`, and renders on Linux. The app’s built-in terminal runs locally in the mirror. The same SSH menu imports existing remote panel scripts and refreshes their source; their data dependencies are not copied. Back up the remote project before using this experimental workflow.

Windows does not reuse password/OTP sessions from the login console for background commands. Remote names must be Windows-compatible and not collide when case is ignored. The protocol was tested with a simulated server, not a live Linux host.

This is not full macOS parity: clipboard formats depend on the receiving application, and visual layouts do not replace programmatic layouts. Terminal host code and xterm assets are adapted from Manimble `b88741a`; see the repository BSD license and `Terminal/LICENSE-xterm.txt`.
