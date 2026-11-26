---
name: fignimble
description: >-
  Edit, build and check publication figures and conference posters in a
  Fignimble project, where every figure is a Python file that places panels,
  pictures, text and arrows in millimeters and every panel is matplotlib code.
  Covers how a project is laid out, the figure language (fig.panel, fig.image,
  fig.layout with patchwork-style | / + composition, fig.text, fig.arrow,
  fig.figure, Style, Labels), the rules that keep a figure draggable on the
  Fignimble canvas, and how to look at your own work (fignimble snapshot, info,
  check, export). Use whenever you work in a folder with a fignimble Figure
  (a .py file with `from fignimble import *`), or the user asks to make, change,
  restyle, lay out, check or export a figure, table or poster with Fignimble.
---

# Working in a Fignimble project

A Fignimble project is a folder of publication figures written in Python. The user sees the figures in Fignimble, a
macOS figure editor, while you edit the code, and edits them by hand there too (moving, resizing, retyping).
Fignimble re-renders a figure about a second after a file is saved, so your edits show up as you make them.

When the project is open in Fignimble it also has a `CLAUDE.md` / `AGENTS.md` / `GEMINI.md` written by Fignimble
(marked `written by Fignimble`). It says the same as this skill; if the two disagree, that file matches the installed
version and wins.

## The `fignimble` command

Find it before anything else:

```bash
command -v fignimble \
  || ls /Applications/Fignimble.app/Contents/Resources/bin/fignimble ~/Applications/Fignimble.app/Contents/Resources/bin/fignimble 2>/dev/null
```

Use whichever path exists (call it `fignimble` below). If there is none, tell the user to install Fignimble from
https://github.com/pachterlab/Fignimble/releases/latest and choose **Fignimble ▸ Install Command Line Tool…**, which
puts `fignimble` in `~/.local/bin`. Don't render figures any other way (no `python fig1.py`, no `savefig`): the
command runs the figure and panel code the way Fignimble does.

## How a project is laid out

- **`fignimble.toml`** at the top marks the project (and the Fignimble version that made it). The project root is the
  folder holding it; by convention the folder is called `fignimble`, inside the paper's repository. Leave the file
  alone.
- **Table files** (`table1.py`) make a `Table` instead: a table of the paper, laid out by itself (see below).
- **Figure files** are the top-level `.py` files that make a `Figure` (`fig1.py`, `fig2.py`, `poster.py`). Each
  places elements on its figure, one call per element. Units are **millimeters**, from the figure's **top-left
  corner, y pointing down**.
- **Panels** are drawn by Python files, usually in `panels/`. A panel file has a function `draw(fig, ...)` that gets a
  matplotlib `Figure` of exactly the panel's size (`fig.add_subplot()`, `fig.subplots(2, 2)`, `fig.colorbar(...)` all
  work). `fig.panel("panels/x.py", function="other", args={"gene": "CD3E"})` calls `other(fig, gene="CD3E")`, so one
  file can draw several panels.
- **`style.py`** holds the `Style` every figure imports: font, text size, line widths, palette, and the `Labels`
  (a, b, c...). `Style.journal("cell")` and `Labels.journal("cell")` take a journal's rules; `fignimble journals` lists
  the 40 journals and the poster sizes.
- `images/` holds pictures, `data/` data, `tables/` CSV tables, `requirements.txt` the packages panels import beyond
  matplotlib and NumPy. `.fignimble/` is Fignimble's cache: never edit it.
- `fignimble new --journal nature` (or `--poster a0`) makes a new project in a folder called `fignimble`
  (`fignimble new FOLDER` to name it otherwise).

```python
from fignimble import *
from style import STYLE

fig = Figure(width=183, height=120, style=STYLE)
fig.panel("panels/expression.py", x=2, y=2, w=88, h=55)           # lettered a automatically
fig.image("images/gel.png", x=93, y=2, w=88)                       # pictures are panels too (b); h follows aspect
fig.text("Treated", x=40, y=60, size=7, weight="bold", align="center")
fig.arrow(60, 70, 80, 70)                       # also fig.line, fig.rect, fig.ellipse, fig.bracket(x1, x2, y, "**")
```

```python
# panels/expression.py
import numpy as np
from fignimble import color, cached, is_dark

@cached("data/expression.csv")                  # runs once; kept until this code, its arguments or the file change
def load():
    import pandas as pd
    return pd.read_csv("data/expression.csv")

def draw(fig, genes=("GAPDH", "CD3E")):
    ax = fig.add_subplot()
    df = load()
    for g in genes:
        ax.plot(df["time"], df[g], color=color(g), label=g)
    ax.set_xlabel("Time (h)")
    ax.set_ylabel("Expression (TPM)")
    ax.legend()
```

Paths in panel and figure code are relative to the project folder.

### Patchwork-style layouts

The layout gives each panel its place, lines up plot areas across rows and columns, and can collect shared legends:

```python
a, b, c, d = Panel("panels/a.py"), Panel("panels/b.py"), Panel("panels/c.py"), Image("images/gel.png")
fig.layout((a | b | c) / (d | Spacer()), x=0, y=0, w=183, h=120, heights=[2, 1], gap=4)     # | beside, / below
fig.layout(Grid(a, b, c, d, ncol=2), x=0, y=0, w=183, h=120)                               # + wraps into a grid
fig.layout(Design("""
    AAB
    CDB
""", A=a, B=b, C=c, D=d), x=0, y=0, w=183, h=120, widths=[1, 1, 1.5], guides="collect", legend="bottom")
fig.inset(Panel("panels/zoom.py"), within=a, x=0.6, y=0.05, w=0.35, h=0.35)   # fractions of a's box
fig.legend({"WT": "#999999", "KO": "#d1495b"}, x=150, y=5)                   # one legend shared by panels
```

`Row(..., widths=[2, 1])`, `Column(..., heights=[...])`, `Grid(..., ncol=, nrow=)` give part of a layout its own
sizes. Prefer a layout for regular grids of plots, and free `fig.panel(...)` for figures arranged by hand.

### Annotations, pictures, color

- `fig.text("**bold** *italic* 10^{3} CD8_{a}", x=, y=, width=60)`: markup; `width` wraps. In a text box, lines
  starting `- ` are bullets, `1. ` numbered, `# ` headings; `source="intro.md"` takes the text from a file,
  `height=` reports overflow and `fit="shrink"` fits it.
- `fig.arrow(..., curve=0.3, tip="stealth")`, `fig.path(points, smooth=True, closed=True, fill=...)`.
- Pictures (PNG, TIFF, JPEG, PDF, SVG) take `rotation=`, `flip="h"`, `crop=(left, top, right, bottom)` fractions;
  `fig.scalebar(picture, length=50, unit="µm", pixel_size=0.325)`; `fig.zoom(picture, inset, region=(x, y, w, h))`.
- `Style(palette="okabe-ito")` (color-blind safe) or `palette={"WT": "#999999", "KO": "#d1495b"}` sets the colors
  panels plot with; use `color("KO")` in panel code instead of repeating hex codes.
- Every figure has a **dark version** (`fignimble snapshot fig1.py --dark`): wherever panel code would write black,
  write `"white" if is_dark() else "black"`.
- A small table inside a figure: `fig.table("tables/counts.csv", x=4, y=80, decimals=2)`. A table of the paper
  (Table 1) is a `Table` file of its own instead: see below and the `fignimble-new-table` skill.
- `fig.rect(..., label="auto")` gives a schematic box a panel letter.

### Figures inside figures, and posters

`fig.figure("fig2.py", x=20, y=130, w=360)` places another figure of the project whole, drawn from its code and scaled
to `w` (`letters=False` drops its letters, `style=` redraws it with another Style, `label="auto"` letters it as one
panel). **Never copy a figure's panels into another figure to show it again: place the figure.** A poster is a figure:
`Figure(poster="a0")` (or "a1", "48x36", "36x48", ...; `landscape=True`) sets its size and `Style.poster`, with type
sized for reading from a meter or two. `fig.qr("https://...", x=, y=, w=40)` draws a QR code.

### Tables of the paper

`table1.py` makes one `Table`; it lays out its title, cells and notes and sizes its page itself (no positions, no
letters), and is listed and numbered apart from the figures:

```python
from fignimble import *
from style import STYLE

tab = Table("tables/genes.csv", style=STYLE, title="Differentially expressed genes",
            decimals=[None, 0, 2, 3], notes=["FDR, Benjamini-Hochberg.", "n, cells per gene."])
```

Cells come from a CSV/TSV, `rows=`, or a function returning rows or a pandas DataFrame (`Table(genes, ...)`).
`fignimble export table1.py table1.docx` writes the Word table journals want (also .xlsx, .csv, .tsv);
`fig.figure("table1.py", ...)` shows it in a figure or on a poster.

For every argument: `python -c "import fignimble; help(fignimble.Figure)"` with Fignimble's Python, or the Python API
reference at https://pachterlab.github.io/Fignimble/reference/python-api/.

## Rules that keep the figure editable by hand

Fignimble moves and resizes an element by rewriting the numbers in the call that made it, so:

- Give `x`, `y`, `w`, `h` (and line ends) as **literal numbers**. A computed position (`x=a.right + 2`) can't be
  dragged; use one only when asked to tie elements together.
- **Never draw panel letters yourself** (no `fig.text("a", ...)`, no `ax.set_title("a")`, no `ax.text(..., "A")`).
  Labels are automatic, in reading order, styled by the one `Labels(...)` in `style.py`. `label=None` on a panel
  leaves it unlettered; `label="a'"` gives it its own.
- In panel code, **don't set font sizes, font families or line widths, and don't call `plt.figure`, `figsize`,
  `savefig`, `tight_layout`, `subplots_adjust` or `plt.show`**. The style sets fonts and sizes so all panels match,
  and Fignimble sizes and saves each panel. Use the `fig` you are given, not `plt`.
- Leave alone what the user set on the canvas: `texts={...}`, `text_style={...}`, `colors={...}`, `locked=True`,
  `hidden=True`. (You may move a `texts`/`colors` entry into the panel's code and remove the entry.)
- Don't reformat or reorder code you weren't asked to change: the order of calls is the stacking order, and the
  user's own edits are in those lines. Make one change at a time.
- List packages panels import (beyond matplotlib and numpy) in `requirements.txt`; put slow shared work (loading,
  fitting) in a `@cached("data/file")` function.

## Look at what you made

After every change:

```bash
fignimble snapshot fig1.py      # renders to .fignimble/snapshots/fig1.png and prints the path: open that picture
fignimble info fig1.py          # every element's box in mm, the panel letters, preflight warnings, panel errors
fignimble check                 # preflight every figure in the folder; exit status 1 on problems
```

Look at the snapshot yourself before saying a figure is done: overlapping labels, clipped tick labels, empty panels
and a panel drawn red (its error) only show up there. `info` prints a failing panel's traceback; fix the code.
Preflight warns about text below the journal's minimum size (inside panels too), pictures under 300 dpi at their
printed size, elements outside the figure, overlapping panels, letters drawn inside a panel or typed with `fig.text`,
text in a font other than the style's, colors color-blind readers can't tell apart, and widths that miss the
journal's columns.

```bash
fignimble export fig1.py fig1.pdf                  # PDF, EPS, SVG, TIFF, PNG, JPEG, PPTX
fignimble export fig1.py fig1.tiff --journal cell  # that journal's resolutions; every export is measured
fignimble export fig1.py fig1-dark.pdf --dark      # the dark version, for talks
fignimble provenance fig1.pdf                      # what an export was made from, checked against the project
```

## Telling the user

Say which figure and which panel (by letter) each change touched, and show the snapshot path. If something is left
that only the user can decide (what a panel should show, which journal), ask rather than guess.
