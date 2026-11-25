# Fignimble for Windows (prototype)

A native WPF client for the existing Fignimble Python engine. It opens local projects, displays figures and their Python source, and lets you move or resize standalone panels and images. It also handles undo, redo, PDF export and refreshes after external source edits. Canvas edits write to the real project files. Try it on a copy first.

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

Drag a panel to move it. Select it and drag the blue handle to resize it. Use Ctrl+Z and Ctrl+Y to undo and redo. The source pane is read-only; edits made in an external editor appear automatically, or on **Reload**.

The app uses the repository's `.venv` by default. Set `FIGNIMBLE_PYTHON` to the full path of another `python.exe` with the engine dependencies installed to override it. `FIGNIMBLE_ENGINE` can point to a different `engine` directory.

The prototype does not yet include project creation, the full inspector, editing grouped layouts or insets on the canvas, SSH projects, an embedded terminal, or an installer.
