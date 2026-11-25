---
name: fignimble-new-figure
description: >-
  Make a new multi-panel publication figure with Fignimble from data files and
  a description of what each panel should show: start a project if there is
  none, write one matplotlib panel file per kind of plot, place the panels
  (by hand or as a patchwork-style layout), annotate, then render a snapshot,
  look at it, and fix what preflight reports until the figure is clean. Use
  when asked to "make a figure", "build figure 2 from these CSVs", "add a
  volcano plot as panel c", "lay out these plots as a 3 by 2 grid", or
  "start a Fignimble project for my paper".
---

# Make a figure with Fignimble

Follow the `fignimble` skill for the figure language and its rules (load it if it is installed). The ones that matter
most here: literal millimeter positions, never draw panel letters, no font sizes or line widths in panel code, look at
the snapshot after each step.

## 1. Find the project and the command

- Find `fignimble` (`command -v fignimble`, else `/Applications/Fignimble.app/Contents/Resources/bin/fignimble` or the
  same under `~/Applications`). If it is missing, tell the user to install Fignimble and use **Fignimble ▸ Install
  Command Line Tool…**.
- A project is a folder with `style.py` and figure files. If the user names one, work there. If there is none, ask
  which journal the paper is for (or use the one they named), then
  `fignimble new FOLDER --journal KEY` (`fignimble journals` lists the keys: `nature`, `cell`, `science`,
  `plos-computational-biology`, `bioinformatics`, `elife`, ...). It writes `style.py` and an empty `fig1.py` at the
  journal's double-column width.
- For an existing project, read `style.py`, the figure file and the panels it already uses before adding anything, so
  new panels match how the others are written.

## 2. Plan before writing

From the user's description and the data, list the panels: what each shows, which data file it reads, and its rough
size. Panels of the same kind with different data are **one function with arguments** in one file
(`fig.panel("panels/expression.py", args={"gene": "CD3E"})`), not copies. If what a panel should show is not clear
from the request and the data, ask once with your plan rather than guessing.

Choose the arrangement:
- A regular grid of plots: `fig.layout(...)` with `|`, `/`, `Grid` or a `Design` string, and `guides="collect"` when
  panels share a legend.
- Pictures, schematics and plots of different sizes: `fig.panel(...)` / `fig.image(...)` placed by hand, a few mm
  apart, inside the figure's width.

## 3. Write the panels

One file per kind of plot in `panels/`, each with `draw(fig, ...)` (or named functions). Read data with paths relative
to the project. Put loading and slow computation in a `@cached("data/file.csv")` function shared by the panels.
Add every package beyond matplotlib and numpy to `requirements.txt`. Use `color("name")` for colors that mean
something (conditions, genotypes) and define them once in `style.py`'s palette. Axis labels with units; no titles that
repeat the caption; no panel letters.

## 4. Place them

Write the calls into the figure file, one per element, literal numbers. Set the figure's height to what the panels
need (the width comes from the journal). Add only the annotations asked for (`fig.text`, `fig.arrow`,
`fig.bracket(x1, x2, y, "**")` for significance).

## 5. Look, fix, repeat

```bash
fignimble snapshot fig1.py      # open the PNG it prints
fignimble info fig1.py          # boxes, letters, warnings, panel errors
```

Open the picture and check: every panel drew (none red), letters in reading order, nothing overlapping or clipped,
legends not covering data, text readable. Fix panel errors from the traceback `info` prints. Then fix every preflight
warning (`fignimble check fig1.py`): text below the minimum size usually means the panel is too small for what it
draws, so make the panel bigger or simplify it rather than setting a font size. Repeat until the snapshot looks right
and `check` passes.

## 6. Report

Tell the user which panels you made (by letter, with their files), what data each reads, the snapshot path, and
anything you could not decide. If Fignimble is open on the project, the figure is already on their screen; they can
drag panels to adjust it, and that edits the same numbers you wrote.
