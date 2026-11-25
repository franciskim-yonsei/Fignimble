# Fignimble for Windows

A native WPF client for the existing Fignimble Python engine. Create or open local projects, assemble a figure from plots, images, and tables, and export PDF, SVG, PNG, or TIFF. The source pane shows the Python code behind the figure. Canvas edits write to the real project files, and figure and panel scripts run as Python, so open only trusted projects and try existing work on a copy first.

## Run from the repository root in PowerShell

Requires the .NET 10 SDK and Python 3.10 or newer.

```powershell
py -3 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r engine\requirements.txt
dotnet run --project apps/windows --
```

Use **Open project** to choose a directory, or add a folder or figure file after `--`. For example, to try it on a disposable copy of the demo:

```powershell
$demo = Join-Path $env:TEMP "fignimble-demo-$([guid]::NewGuid())"
Copy-Item -Recurse examples\demo $demo
dotnet run --project apps/windows -- $demo
```

## Create a figure without writing a script

1. Choose **New project**, pick a shared journal style, and create or select an empty folder. **New figure** adds another figure with dimensions you can set in millimeters. **Journal width** sets the current page width and journal for preflight and export without replacing its plot style.
2. Choose **Add sample plot** for a working matplotlib panel, or **Import** for an existing `.py` panel, image, or CSV/TSV table. A Python panel should define `draw(fig)`, which receives the matplotlib figure to draw into. Importing a file outside the project copies it in; dependencies such as data files are not copied automatically.
3. Use **Layers** to select covered or hidden items, change stacking order, show/hide, or lock/unlock. Ctrl+click adds to or removes from a selection; the layer list also supports Shift+click ranges. Drag selected layers together or use **Arrange** to align or distribute them. Zoom with Ctrl+wheel or the zoom buttons; middle-drag or use the scroll bars to pan.
4. Select a standalone panel or image to resize it with its blue handle or to enter its exact geometry. Use **Label** to override or suppress its automatic letter. **Annotate** adds text, arrows, and brackets; double-click the annotation’s layer row to edit its details. Click the page background or press Esc to return to page geometry. Ctrl+Z and Ctrl+Y undo and redo.
5. Review **Preflight**, then choose **Export** and select a format, journal resolution rules, and DPI. The engine can raise a raster export’s DPI to meet the selected journal’s minimum. Review warnings and check the journal’s current requirements before submission.

## Faster editing and export

- **Arrange** also duplicates, groups, ungroups, and removes selected layers. Drag a selection box from a blank spot on the page. Smart snapping aligns moves to page edges, centers, other layers, and guides; hold Alt to bypass it. Add or clear guides under **More**. With the canvas focused, arrow keys nudge by 0.5 mm, Shift+arrow by 5 mm, and Ctrl+arrow by 0.1 mm.
- **Edit source** opens the figure, style, or a panel file. Apply is one undo step; the editor refuses syntax errors and checks for conflicting changes on disk. The main pane still previews the figure source, and saved external edits still refresh it. **More** also opens the figure caption and the project’s edit history.
- Select a panel, image, or table and use **More** to edit plot labels, crop or rotate an image, or edit existing table cells. Table editing does not yet add or remove rows or columns. Crop before rotating or flipping an image.
- **Export** also supports PowerPoint. **More** exports every figure as PDF without clobbering existing PDFs, exports a selected table to Word/Excel/CSV/TSV, and copies a figure bitmap or pastes an image. Clipboard interchange is raster-only.

The app uses the repository's `.venv` by default. Set `FIGNIMBLE_PYTHON` to the full path of another `python.exe` with the engine dependencies installed to override it. `FIGNIMBLE_ENGINE` can point to a different `engine` directory.

This covers the local manuscript workflow, not macOS parity. Current limits include no draggable rulers, no direct editing of layout children or inset geometry, no plot-text editing by clicking the plotted text itself, no embedded terminal or per-project Python discovery, no installer or SSH projects, and no CMYK/EPS controls.
