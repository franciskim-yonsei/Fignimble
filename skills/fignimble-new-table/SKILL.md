---
name: fignimble-new-table
description: >-
  Make a table of a paper (Table 1, Supplementary Table 2) with Fignimble: a
  Table file of its own whose cells come from a data file, from rows written
  in the code, or from a function that computes them from the analysis (a
  pandas DataFrame works), with a title, units in the headers, consistent
  decimals and footnotes; check it with preflight and export it as the
  editable Word (or Excel) table journals ask for. Use when asked to "make
  table 1", "make a table of the top 20 genes", "turn this DataFrame into a
  table for the paper", "add a supplementary table", or "export my tables as
  Word".
---

# Make a table with Fignimble

Follow the `fignimble` skill for the project layout and the `fignimble` command (load it if it is installed). Find the
command first (`command -v fignimble`, else `/Applications/Fignimble.app/Contents/Resources/bin/fignimble`).

A table of the paper is **not a figure**: it is a `Table` file of its own (`table1.py`), listed and numbered apart from
the figures, laid out by itself (title above, cells, notes below, the page sized to them), and sent to the journal as
text. Never draw a paper's table as a picture, and never build it from `fig.text` calls.

## 1. What goes in it

From the user's request, the manuscript and the data, settle:
- **The rows and columns**: which data, which subset (top 20 by adjusted p, one row per sample...), which order.
- **Headers with units**: `Age (years)`, `Dose (mg/kg)`, `log₂ fold change`; short, no abbreviations the notes don't
  explain.
- **Decimals per column**: as many as the measurement supports, the same down a column. p values: 2 or 3 significant
  digits, or `< 0.001`.
- **Title**: one sentence saying what the table shows (journals put "Table 1" in front themselves; don't write it).
- **Notes**: abbreviations, the statistical test and correction, what n is, symbols (`*`, `†`) used in cells.

Ask once if the content isn't clear; otherwise make it and say what you chose.

## 2. Write the table file

Name it as the paper does (`table1.py`, `table_s2.py`). Pick where the cells come from:

**Computed from the analysis** (best when the numbers come from data: the table stays right when the analysis
changes):

```python
from fignimble import *
from style import STYLE


@cached("data/de.csv")
def top_genes():
    import pandas as pd
    de = pd.read_csv("data/de.csv")
    top = de.nsmallest(20, "padj")
    return pd.DataFrame({"Gene": top["gene"], "log₂ fold change": top["log2FoldChange"],
                         "Adjusted p": top["padj"], "Mean expression (TPM)": top["baseMean"]})


tab = Table(top_genes, style=STYLE, title="The 20 genes most changed by treatment",
            decimals=[None, 2, 4, 1],
            notes=["Adjusted p, Wald test with Benjamini-Hochberg correction (DESeq2).",
                   "TPM, transcripts per million."])
```

A function returns a list of rows (the header first) or a pandas DataFrame (its column names become the header; a
named index becomes the first column). Add pandas (or anything else it imports) to `requirements.txt`.

**From a file** (results someone else produced, or values typed in): put a CSV in `tables/` and
`Table("tables/cohort.csv", ...)`. **Written in the code** (a handful of fixed values): `Table(rows=[["Cohort", "n"],
["Discovery", 412], ...], ...)`.

Don't give `x`, `y`, sizes or fonts: the style sets the type, and the table places itself. `grid=True` only if the
journal wants lines between every cell; the default (rules above and below the header and at the bottom) is what
most journals print.

## 3. Check it

```bash
fignimble snapshot table1.py     # open the PNG it prints
fignimble check table1.py
```

Look at the picture: headers read well, numbers line up on the right, nothing wraps oddly. Fix what preflight says
in the `Table(...)` call: a value that shows as `0.000` but isn't zero (more decimals, or write `< 0.001` in the
data), a column with mixed decimals (`decimals=`), no title (`title=`), text below the journal's minimum (remove a
`size=` that is too small).

## 4. Export

```bash
fignimble export table1.py tables/table1.docx    # the editable Word table journals ask for, title and notes included
fignimble export table1.py tables/table1.xlsx    # when the journal wants spreadsheets (supplementary tables often)
```

Put the exports where the user wants them. To show the table in a figure or on a poster too, place it whole there:
`fig.figure("table1.py", x=..., y=..., w=...)`.

## 5. Report

The file made, where its cells come from, the title, notes and decimals chosen, the preflight result, and the files
exported.
