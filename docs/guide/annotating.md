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

```python
fig.table("tables/genes.csv", x=4, y=80, decimals=[None, 0, 2, 3])
```

draws a table from a CSV or TSV file in the figure's font, with rules above and below the header and at the bottom;
numbers are right-aligned. **Insert ▸ Table from CSV…**, or dropping the file on the figure, does the same.
`grid=True` draws lines between all the cells and around the table instead (**Lines between all cells** in the
inspector).

**Insert ▸ New Table…** (<kbd>⇧</kbd><kbd>⌘</kbd><kbd>T</kbd>) starts a table from scratch, in a figure of its own:
give it a name and its numbers of rows and columns. Its cells are kept in `tables/<name>.csv`. Double-click a cell on
the figure to type in it (<kbd>Tab</kbd> goes to the next one), or edit the cells in the inspector, which also adds
and removes rows and columns. **Fit Page to Table** sizes the figure to the table.

Journals want tables as text, not pictures: **File ▸ Export Table…** writes the selected table as a real, editable
Word table in the figure's font, an Excel sheet (numbers stay numbers), or a CSV or TSV file. A figure that is only
a table offers the same formats in **Export Figure…**, next to the picture formats. From a terminal,
`fignimble table fig1.py 3 table1.docx` (or `fignimble table data.csv table1.docx`) writes the Word table.

## Copying styles

**Edit ▸ Copy Style** (<kbd>⌥</kbd><kbd>⌘</kbd><kbd>C</kbd>) and **Paste Style** (<kbd>⌥</kbd><kbd>⌘</kbd><kbd>V</kbd>)
carry colors, line settings and text settings from one element to others.

## Locking and hiding

`locked=True` stops the pointer from catching an element (a background, say) and `hidden=True` leaves it out of the
figure; the layers list has both as buttons.
