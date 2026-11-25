# Text, arrows, shapes and tables

## Text

```python
fig.text("Treated", x=30, y=104, size=7, weight="bold", align="center")
fig.text("**GFP**^{+} cells express *Cd3e*", x=4, y=70, width=78)   # bold, italic, superscript; wraps at 78 mm
```

Text takes markup: `**bold**`, `*italic*`, `^{superscript}`, `_{subscript}`, and matplotlib's math between dollar
signs (`"$\\alpha$-actin"`). `width` makes a text box whose lines wrap. Double-click a text to retype it in place;
the inspector sets its size, color, weight, alignment and rotation. The default size is the style's.

In a text box, lines starting with `- ` are bullets, `1. ` numbered items and `# ` headings, and an empty line
starts a paragraph. `source="intro.md"` takes the text from a Markdown file. `height` is the box's height, and
`fit="shrink"` makes the type smaller until the text fits it. These are made for
[posters](posters.md#text-on-a-poster), and work in any figure.

## QR codes

`fig.qr("https://...", x=150, y=90, w=25)` (**Insert ▸ QR Code…**) is a QR code for a link, drawn as vector squares.
See [QR codes](posters.md#qr-codes).

## Lines, arrows and paths

```python
fig.line(10, 50, 40, 50, width=0.5, dash="dashed")
fig.arrow(96, 80, 120, 66, curve=0.25, tip="stealth")       # tips: triangle, stealth, open, bar, dot
fig.path([(130, 60), (140, 72), (152, 62)], smooth=True, closed=False, arrow="end")
```

Lines bend (`curve`, or the inspector) and take arrowheads at either end. The pen tool (<kbd>P</kbd>) draws paths:
click points, double-click or press Return to finish, click the first point to close; drag a path's points to
change it.

## Shapes and brackets

```python
fig.rect(100, 40, 20, 10, stroke="#000000", fill=None, radius=1)
fig.ellipse(130, 40, 12, 12, fill="#FFE8E8")
fig.bracket(147, 169, 67, text="**")                         # a significance bracket, its text centred above
```

## Tables

A table of the paper (Table 1, Supplementary Table 2) is a file of its own, as a figure is, and the sidebar lists the
project's tables under their own heading, apart from its figures:

```python
# table1.py
from fignimble import *
from style import STYLE

tab = Table("tables/genes.csv", style=STYLE,
            title="Differentially expressed genes in treated cells",
            decimals=[None, 0, 2, 3],                       # rounding per column (None: as written)
            notes=["FDR, Benjamini–Hochberg.", "n, cells per gene."])
```

Its title goes above the cells and its notes below, in the style's font, and its page is sized to them: there is
nothing to place. A table has no panel letters, and preflight checks what a copy editor would query: numbers that
show as 0.000 but aren't zero, a column whose numbers have different numbers of decimals, a column without a header,
a table without a title, and text below the journal's minimum size.

The cells come from a CSV, TSV or text file in the project, from `rows=[[...], ...]` written in the code, or from a
function that returns them, so a table can stay tied to the analysis the way a panel is:

```python
@cached("data/de.csv")
def genes():
    import pandas as pd
    de = pd.read_csv("data/de.csv")
    return de.nsmallest(20, "padj")[["gene", "log2FoldChange", "padj"]]     # a DataFrame: its columns, then its rows

tab = Table(genes, style=STYLE, title="The 20 most significant genes", decimals=[None, 2, 3])
```

**File ▸ New Table…** (<kbd>⇧</kbd><kbd>⌘</kbd><kbd>T</kbd>) starts one from scratch, as simple as a table gets: give
it a name and its numbers of rows and columns, and you get a grid of empty cells with lines between them
(`Table("tables/<name>.csv", style=STYLE, grid=True)`), its cells kept in `tables/<name>.csv`. Double-click a cell on
the page to type in it (<kbd>Tab</kbd> goes to the next one), or edit the cells in the inspector, which also adds and
removes rows and columns. A title and notes are optional: with nothing selected, the inspector has both, and
**Lines between all cells** off gives the rules most journals print instead of a grid.

Journals want tables as text, not pictures: **File ▸ Export Table…** (or **Export…**) writes a table as a real,
editable Word table in the style's font with its title above and its notes below, an Excel sheet (numbers stay
numbers), or a CSV or TSV file; PDF, PNG and PowerPoint are there too, for slides. From a terminal:
`fignimble export table1.py table1.docx`.

To show a table in a figure or on a poster, place it whole: `fig.figure("table1.py", x=10, y=80, w=90)` (Insert ▸
Figure…), and it changes when the table does.

A table can also be part of a figure. **Insert ▸ Table…** embeds a grid of empty cells in the figure, where you're
looking (`fig.table("tables/fig1_table1.csv", x=60, y=40, grid=True)`): move and resize it like anything else, and
double-click a cell to type in it. **Insert ▸ Table from CSV…**, or dropping a CSV or TSV file on the figure, embeds
a table of that file. `fignimble table fig1.py 3 table1.docx` writes an embedded table (element 3) as a Word file.

## Copying styles

**Edit ▸ Copy Style** (<kbd>⌥</kbd><kbd>⌘</kbd><kbd>C</kbd>) and **Paste Style** (<kbd>⌥</kbd><kbd>⌘</kbd><kbd>V</kbd>)
carry colors, line settings and text settings from one element to others.

## Locking and hiding

`locked=True` stops the pointer from catching an element (a background, say) and `hidden=True` leaves it out of the
figure; the layers list has both as buttons.
