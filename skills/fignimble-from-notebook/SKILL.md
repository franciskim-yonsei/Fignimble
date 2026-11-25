---
name: fignimble-from-notebook
description: >-
  Turn the plots of a Jupyter notebook, a Python analysis script, or a
  folder of matplotlib/seaborn scripts into Fignimble panels and assemble
  them into a publication figure: move each plot's code into a panel function
  that draws into the figure Fignimble gives it, strip figsize, font sizes,
  savefig and plt.show, share data loading through @cached, then lay the
  panels out and check the result. Use when asked to "turn this notebook's
  plots into a figure", "make figure 2 from analysis.ipynb", "convert my
  plotting scripts to Fignimble panels", or "rebuild this published figure
  from the authors' code".
---

# From a notebook or scripts to a Fignimble figure

Follow the `fignimble` skill for the figure language and its rules (load it if it is installed). Find the
`fignimble` command first (`command -v fignimble`, else
`/Applications/Fignimble.app/Contents/Resources/bin/fignimble`).

The goal: each plot becomes a panel function the figure calls, drawing the same thing from the same data, so the
figure stays reproducible from the analysis and the user can rearrange it on the canvas.

## 1. Read the source

- A notebook: read the `.ipynb` cells in order (code and outputs). Note which cells load data, which compute, and
  which draw each plot. Rendered outputs show what each plot should look like: keep them to compare against.
- Scripts: find each `plt.figure`/`plt.subplots` ... `savefig` block and what it reads.
- List the plots, and ask which go in the figure and in what order if the user didn't say. Don't run the notebook end
  to end if it is slow or needs a server; the panels will run the parts they need.

## 2. Set up the project

Use the user's project, or `fignimble new FOLDER --journal KEY` (ask which journal if unknown). Copy or link the data
the plots read into `data/` (ask before copying large files; a panel may read an absolute path, but the project is
then not self-contained). Add the packages the code imports (pandas, seaborn, scanpy, ...) to `requirements.txt`.

## 3. Turn each plot into a panel function

Group related plots in one file (`panels/qc.py`, `panels/clusters.py`). For each:

```python
# before (notebook)
fig, ax = plt.subplots(figsize=(4, 3))
sns.violinplot(data=df, x="group", y="score", ax=ax)
ax.set_title("Score by group", fontsize=12)
plt.tight_layout(); plt.savefig("score.pdf"); plt.show()

# after (panels/qc.py)
def score(fig):
    ax = fig.add_subplot()
    sns.violinplot(data=load(), x="group", y="score", ax=ax)
```

- Draw into the `fig` passed in: `fig.add_subplot()`, `fig.subplots(1, 3)`. Replace `plt.gca()`/`plt.` calls with
  the axes. Remove `figsize`, `dpi`, `fontsize=`, `linewidth=`, `rcParams`, `sns.set_theme`/`set_context`,
  `tight_layout`, `subplots_adjust`, `savefig`, `show`: Fignimble sizes each panel and the style sets fonts and lines.
- Remove plot titles that are really panel letters or caption text. Keep axis labels (add units).
- Colors that mean something become a palette in `style.py` (`palette={"WT": "#999999", "KO": "#d1495b"}`) used as
  `color("KO")`; drop colors that only restate matplotlib's defaults.
- Data loading and heavy computation shared between plots go into `@cached("data/file")` functions at the top of the
  file (or a `panels/common.py` the panels import). A computation the notebook did once in an early cell belongs
  there, not repeated in each panel.
- Parameters that differed between copies of a plot (gene, condition, column) become arguments:
  `fig.panel("panels/qc.py", function="score", args={"column": "n_genes"})`.

## 4. Assemble and compare

Place the panels in the figure file (a `fig.layout(...)` for a grid, `fig.panel(...)` by hand otherwise), literal
numbers, no letters. Then:

```bash
fignimble snapshot fig1.py      # open the PNG
fignimble info fig1.py          # panel errors with tracebacks, warnings
```

Compare each panel with the notebook's output: same data, same marks, same axes. Differences in font and size are
expected (that is the style); differences in the data are bugs. Fix preflight warnings (`fignimble check fig1.py`)
by resizing panels, not by setting font sizes.

## 5. Report

List each panel (letter, file, function) next to the notebook cell or script it came from, what you removed (sizes,
fonts, savefig), anything that didn't port (interactive widgets, plots whose data wasn't available), and the snapshot
path.
