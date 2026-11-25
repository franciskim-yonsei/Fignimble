# Fignimble for Windows (prototype)

A native WPF client for the existing Fignimble Python engine. Create or open local projects, assemble a figure from plots, images, and tables, and export PDF, SVG, or PNG. The source pane shows the Python code behind the figure. Canvas edits write to the real project files, and figure and panel scripts run as Python, so open only trusted projects and try existing work on a copy first.

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

1. Choose **New project** and create or select an empty folder. It starts with a blank figure using Fignimble's Nature preset. **New figure** adds another page, with dimensions you can set in millimeters.
2. Choose **Add sample plot** for a working matplotlib panel, or **Import** for an existing `.py` panel, image, or CSV/TSV table. A Python panel should define `draw(fig)`, which receives the matplotlib figure to draw into. Importing a file outside the project copies it in; dependencies such as data files are not copied automatically.
3. Drag a standalone panel or image to move it and use its blue handle to resize it. For exact coordinates, use the geometry fields and **Apply**. Click the page background to set the page dimensions or **Fit page to content**. Use Ctrl+Z and Ctrl+Y to undo and redo.
4. Open **Preflight** to review render and publication warnings, then **Export** to create a PDF, SVG, or PNG.

The source pane is read-only; edits made in an external editor appear automatically, or on **Reload**.

The app uses the repository's `.venv` by default. Set `FIGNIMBLE_PYTHON` to the full path of another `python.exe` with the engine dependencies installed to override it. `FIGNIMBLE_ENGINE` can point to a different `engine` directory.

The prototype does not yet include the full inspector, interactive table editing, editing grouped layouts or insets on the canvas, SSH projects, an embedded terminal, or an installer.
