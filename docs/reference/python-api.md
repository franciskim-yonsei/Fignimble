# Python API

Everything a figure file uses comes from `from fignimble import *`. Sizes and positions are in millimeters from the
figure's top-left corner, y down; text sizes in points. This page is generated from the docstrings in
[`engine/fignimble/api.py`](https://github.com/pachterlab/Fignimble/blob/main/engine/fignimble/api.py).

## Figure

```python
Figure(width: 'float | None' = None, height: 'float | None' = None, style: 'Style | None' = None, labels: 'Labels | None' = None, background: 'str | None' = '#FFFFFF', journal: 'str | None' = None, columns='double', poster: 'str | None' = None, landscape: 'bool' = False, margin: 'float | None' = None)
```

A figure (or a poster): its size (mm), its style, and the elements placed on it.

width, height  in mm. With `journal` (and `columns`) and no width, the journal's column width.
style          a Style shared by every panel (font, sizes, labels); `labels` overrides its Labels
background     page color ("#FFFFFF"), or None for transparent
poster         a poster size ("a0", "a1", "a2", "36x48", "42x56", "24x36"; see fignimble.journals.POSTERS):
               the figure is that size (`landscape=True` turns it), its style is Style.poster(poster) unless
               one is given, and preflight keeps `margin` mm along the edges clear (the preset's by default)

### fig.panel

```python
fig.panel(source, x: 'float' = 0, y: 'float' = 0, w: 'float' = 80, h: 'float' = 60, function: 'str' = 'draw', args: 'dict | None' = None, label='auto', label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, texts: 'dict | None' = None, colors: 'dict | None' = None, rotation: 'float' = 0, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False, text_style: 'dict | None' = None) -> 'Element'
```

A panel drawn by Python code. `source` is a .py file (path relative to the project) whose `function`
(default `draw`) takes a matplotlib Figure of exactly w x h mm and draws on it, or a function itself:

    def draw(fig, gene="CD3E"):
        ax = fig.add_subplot()
        ...

`args` are passed to it as keyword arguments, so one panel file can draw several panels. Resizing the
panel redraws it at the new size, so its text keeps its size. `label` is "auto" (the next letter), a
string of your own ("a'"), or None (no label); `label_dx`/`label_dy` move this panel's label only, and
`label_x`/`label_y` (mm, on the figure) unhook it: it stays there when the panel moves.
`texts` replaces texts the code draws ({"ax0.xlabel": "Time (min)", "ax0.xtick2": "10³"}), which is how
the editor retypes a text the code doesn't hold as a literal; `text_style` sets such a text's size, color,
weight and slant ({"ax0.legend1": {"size": 8, "color": "#D55E00", "weight": "bold", "style": "italic"}});
`colors` likewise recolors lines, bars and points ({"ax0.line1": "#0072B2"}); `rotation` turns the panel
(90, 180, 270).

### fig.image

```python
fig.image(source: 'str', x: 'float' = 0, y: 'float' = 0, w: 'float' = 60, h: 'float | None' = None, fit: 'str' = 'contain', crop: 'tuple | None' = None, label='auto', label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, opacity: 'float' = 1.0, rotation: 'float' = 0, flip: 'str | None' = None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A picture as a panel: PNG, JPEG, TIFF (raster) or PDF, SVG (vector, kept as vector). Without `h`, the
height follows the picture's aspect ratio. `fit` is "contain" (fit inside w x h), "cover" (fill it,
cropping) or "stretch". `crop` = (left, top, right, bottom), fractions 0-1 of the picture to cut away.
`rotation` turns it (degrees counterclockwise; any angle for PNG/TIFF/JPEG, multiples of 90 for PDF/SVG) and
`flip` mirrors it ("h" or "v").

### fig.figure

```python
fig.figure(source: 'str', x: 'float' = 0, y: 'float' = 0, w: 'float | None' = None, h: 'float | None' = None, style: 'Style | None' = None, letters: 'bool' = True, background='figure', label=None, label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

Another figure of the project, placed whole: `source` is its figure file ("fig2.py"). It is drawn from
its code every time, so it is always the figure as it is now, with its panels, letters and annotations.

Without `w` and `h` it is placed at its own size; with `w` (or `h`) it is scaled to that width (height),
text and lines with it, keeping its proportions (with both, it fits inside w x h). `style` draws it with
another Style (a poster's font, say) instead of its own; `letters=False` leaves out its panel letters;
`background` is its own page color ("figure"), another color, or None (transparent). `label` "auto"
letters it as a panel of this figure; None (the default): no letter.

### fig.text

```python
fig.text(text: 'str' = '', x: 'float' = 0, y: 'float' = 0, size: 'float | None' = None, weight: 'str' = 'normal', style: 'str' = 'normal', color: 'str' = '#000000', align: 'str' = 'left', rotation: 'float' = 0, font: 'str | None' = None, width: 'float | None' = None, opacity: 'float' = 1.0, source: 'str | None' = None, height: 'float | None' = None, fit: 'str | None' = None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

Text. (x, y) is the top-left of the text (top-center or top-right with align "center" / "right").
Size in pt; default the style's size. `width` (mm) makes it a text box whose lines wrap.
Markup: **bold**, *italic*, ^{superscript}, _{subscript} (in the style's font), and matplotlib's math:
"$\alpha$-actin". A line break is "\n".

In a text box (`width`), lines that start with "- " or "• " are bullets, "1. " numbered items and "# " or
"## " headings, with their lines indented under them; an empty line starts a paragraph.
`source` takes the text from a file of the project instead (a .md or .txt, written as Markdown: lines of a
paragraph join, "- " and "* " are bullets). `height` (mm) is the box's height: preflight says when the text
doesn't fit, and fit="shrink" makes the type smaller until it does (never below the style's minimum).

### fig.table

```python
fig.table(source: 'str | None' = None, x: 'float' = 0, y: 'float' = 0, w: 'float | None' = None, rows: 'list | None' = None, header: 'bool' = True, size: 'float | None' = None, decimals=None, align=None, font: 'str | None' = None, color: 'str' = '#000000', grid: 'bool' = False, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A table from a .csv, .tsv or .txt file (`source`, relative to the project), a function returning its
rows or a pandas DataFrame (`source=load_genes`), or from `rows` ([["Gene",
"n"], ["CD3E", 12], ...]). (x, y) is its top-left; `w` (mm) widens it (it is never narrower than its text).
The first row is the header (`header=False`: none). `decimals` rounds numbers with decimals (2, or
[None, 0, 2, 3] per column); `align` is "lrr"
(left/center/right per column), else numbers go right and words left. Size in pt; default the style's.
Rules above and below the header and at the bottom, as in a printed paper; `grid=True` draws a line between
every row and column and around the table instead (in `color`). Journals want tables as text:
the editor's Export Table as Word (and `fignimble table`) writes it as a .docx. A table of the paper (Table 1)
goes in a file of its own: see Table.

### fig.line

```python
fig.line(x1: 'float', y1: 'float', x2: 'float', y2: 'float', width: 'float' = 0.5, color: 'str' = '#000000', arrow: 'str | None' = None, dash: 'str | None' = None, head: 'float' = 1.6, curve: 'float' = 0, tip: 'str' = 'triangle', opacity: 'float' = 1.0, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A line from (x1, y1) to (x2, y2). `arrow` is None, "end", "start" or "both"; `head` the arrowhead's
length (mm); `tip` its shape: "triangle", "open", "stealth", "bar" or "dot"; `curve` bends the line
(a fraction of its length; + bends to the left of its direction); `dash` None, "dashed" or "dotted";
`width` in pt.

### fig.arrow

```python
fig.arrow(x1: 'float', y1: 'float', x2: 'float', y2: 'float', width: 'float' = 0.5, color: 'str' = '#000000', arrow: 'str' = 'end', dash: 'str | None' = None, head: 'float' = 1.6, curve: 'float' = 0, tip: 'str' = 'triangle', opacity: 'float' = 1.0, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A line with an arrowhead at its end (x2, y2).

### fig.rect

```python
fig.rect(x: 'float', y: 'float', w: 'float', h: 'float', stroke: 'str | None' = '#000000', fill: 'str | None' = None, width: 'float' = 0.5, radius: 'float' = 0, dash: 'str | None' = None, opacity: 'float' = 1.0, label=None, label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A rectangle; `radius` (mm) rounds its corners. stroke/fill None: no outline / no fill. `label` "auto"
letters it like a panel (a schematic drawn on it, say), or is a letter of its own; None (the default): no
letter.

### fig.ellipse

```python
fig.ellipse(x: 'float', y: 'float', w: 'float', h: 'float', stroke: 'str | None' = '#000000', fill: 'str | None' = None, width: 'float' = 0.5, dash: 'str | None' = None, opacity: 'float' = 1.0, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

An ellipse in the box (x, y, w, h).

### fig.bracket

```python
fig.bracket(x1: 'float', x2: 'float', y: 'float', text: 'str' = '*', tick: 'float' = 1.2, size: 'float | None' = None, width: 'float' = 0.5, color: 'str' = '#000000', group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A significance bracket from x1 to x2 at height y, its ends dropping `tick` mm, with `text` ("*",
"**", "n.s.", "P = 0.003") centered above it.

### fig.path

```python
fig.path(points: 'list', closed: 'bool' = False, smooth: 'bool' = False, stroke: 'str | None' = '#000000', fill: 'str | None' = None, width: 'float' = 0.5, dash: 'str | None' = None, arrow: 'str | None' = None, tip: 'str' = 'triangle', head: 'float' = 1.6, opacity: 'float' = 1.0, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A line through `points` [(x, y), ...] (mm): a polygon with closed=True, a curve through them with
smooth=True. `arrow`, `tip` and `head` as for fig.line.

### fig.zoom

```python
fig.zoom(source, target, region: 'tuple' = (0.35, 0.35, 0.25, 0.25), color: 'str' = '#000000', width: 'float' = 0.5, dash: 'str | None' = None, lines: 'bool' = True, sync: 'bool' = True, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A zoom box: a rectangle on `source` (a panel or picture) around `region` = (x, y, w, h), fractions of
its box, and lines from it to `target` (the panel or inset showing it magnified). With `sync`, a target
picture of the same file is cropped to the region, so moving the box on the figure changes what the
magnified view shows.

### fig.scalebar

```python
fig.scalebar(image, length: 'float', unit: 'str' = 'µm', pixel_size: 'float' = 1.0, corner: 'str' = 'lower right', x: 'float | None' = None, y: 'float | None' = None, margin: 'float' = 1.5, color: 'str' = '#FFFFFF', thickness: 'float' = 0.6, size: 'float | None' = None, label: 'bool' = True, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A scale bar on a picture: `length` in `unit`, where one pixel of the original picture is `pixel_size`
units (e.g. 0.325 µm). It sits in `corner` ("lower right", "lower left", "upper right", "upper left"),
`margin` mm in, or at (x, y), fractions of the picture's box for the bar's left end. `thickness` in mm.

### fig.layout

```python
fig.layout(node: 'Node', x: 'float' = 0, y: 'float' = 0, w: 'float | None' = None, h: 'float | None' = None, gap: 'float' = 4, widths: 'list | None' = None, heights: 'list | None' = None, guides: 'str | None' = None, legend: 'str' = 'right', align: 'bool' = True, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

Arrange panels in the box (x, y, w, h) as `node` says, patchwork style:

    a, b, c = Panel("panels/a.py"), Panel("panels/b.py"), Panel("panels/c.py")
    fig.layout((a | b) / c, x=0, y=0, w=183, h=120, heights=[2, 1])

`widths`/`heights` (relative) apply to the outermost Row, Column, Grid or Design; `gap` (mm) is the space
between cells. guides="collect" takes the legends out of the panels and shows each distinct entry once,
at the `legend` side ("right" or "bottom"). With `align`, the panels' plot areas line up across rows and
columns. Without w or h, the layout fills the figure from (x, y).

### fig.inset

```python
fig.inset(item: 'Node', within, x: 'float' = 0.55, y: 'float' = 0.05, w: 'float' = 0.4, h: 'float' = 0.4, label=None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A Panel or Image on top of another panel (`within`: what fig.panel/fig.image returned, or a Panel in a
layout). x, y, w, h are fractions of that panel's box, from its top-left corner.

### fig.legend

```python
fig.legend(entries: 'dict', x: 'float' = 0, y: 'float' = 0, ncol: 'int' = 1, title: 'str | None' = None, size: 'float | None' = None, marker: 'str | None' = None, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A legend of its own, shared by several panels: {"label": color or {"color", "marker", "linestyle",
"kind": "line"|"patch"|"marker"}}. (x, y) is its top-left corner.

### fig.guide

```python
fig.guide(x: 'float | None' = None, y: 'float | None' = None)
```

A guide line at x (vertical) or y (horizontal), in mm, for the editor's snapping.

### fig.caption

```python
fig.caption(text: 'str')
```

The figure's caption: free text for the paper, kept with the figure but not drawn on it. The editor's
Caption tab edits it.

### fig.qr

```python
fig.qr(data: 'str', x: 'float' = 0, y: 'float' = 0, w: 'float' = 30, color: 'str' = '#000000', background: 'str | None' = '#FFFFFF', level: 'str' = 'M', quiet: 'int' = 4, group: 'str | None' = None, name: 'str | None' = None, locked: 'bool' = False, hidden: 'bool' = False) -> 'Element'
```

A QR code for `data` (a link, usually), `w` mm square including its `quiet` border of light modules
(4, as the standard asks; scanners need it). `level` is how much of it can be damaged and still read:
"L" 7%, "M" 15%, "Q" 25%, "H" 30% (more makes a denser code). It stays dark on light in dark versions.

## Table

```python
Table(source=None, rows: 'list | None' = None, style: 'Style | None' = None, title: 'str | None' = None, notes=None, header: 'bool' = True, decimals=None, align=None, grid: 'bool' = False, size: 'float | None' = None, font: 'str | None' = None, color: 'str' = '#000000', w: 'float | None' = None, pad: 'float' = 3, journal: 'str | None' = None, background: 'str | None' = '#FFFFFF')
```

A table of the paper (Table 1, Supplementary Table 2), in a file of its own, as a figure is:

    from fignimble import *
    from style import STYLE

    tab = Table("tables/genes.csv", style=STYLE, title="Differentially expressed genes",
                decimals=[None, 0, 2, 3], notes="FDR, Benjamini-Hochberg.")

Tables are numbered apart from figures, have no panel letters, and go to the journal as text: they export as
Word (.docx, with the title above and the notes below), Excel, CSV or TSV, and also as PDF, PNG or PowerPoint
for slides. A figure or poster can show one with fig.figure("table1.py", ...).

source    a .csv, .tsv or .txt file of the project, or a function that returns the rows (a list of lists, the
          header first) or a pandas DataFrame; it runs at every render, so put slow work under @cached
rows      the cells written in the code instead of `source`
title     the table's title, above it (bold)
notes     what goes under it: footnotes, abbreviations, the test used (a str; "\n" separates lines, or a list)
header, decimals, align, grid, size, font, color   as in Figure.table
w         mm: the table at least this wide
pad       mm of page around the table; the page is sized to the title, table and notes

## Style

```python
Style(font: 'str' = 'Arial', size: 'float' = 7, linewidth: 'float' = 0.6, labels: 'Labels | None' = None, despine: 'bool' = True, rc: 'dict | None' = None, min_font: 'float' = 5, min_dpi: 'float' = 300, journal: 'str | None' = None, align_axes: 'bool' = True, palette=None, poster: 'str | None' = None)
```

What every panel of a figure shares: font, text size, line widths, label style. Put one in a `style.py`
next to the figure files and import it in each, so every figure of a paper looks the same.

font      font family for panels, text and labels ("Arial", "Helvetica", ...)
size      text size (pt) of tick labels, axis labels, legends and text elements
linewidth line width (pt) of axes, ticks and plotted lines
labels    a Labels
despine   hide the top and right axis lines in panels
rc        more matplotlib rcParams, applied to every panel
min_font  the smallest text size (pt) the preflight check accepts
min_dpi   the lowest resolution (dots per inch, as printed) the preflight check accepts for pictures
align_axes line up the plot areas of panels that share an edge (a row's tops and bottoms, a column's left
          and right sides), as patchwork does, whatever their tick labels
palette   the project's colors: a list, {"WT": "#999999", "KO": "#d1495b"}, or the name of one of fignimble's
          (PALETTES: "okabe-ito", "tol-bright", "tol-muted", "tol-vibrant", "colorbrewer-set2", ...). The
          colors panels plot with in turn, offered as swatches in the editor, and color("KO") in panel code
poster    the poster size this style is for (see Style.poster): ticks and padding grow with the text

### Style.journal

```python
Style.journal(name: 'str', **overrides) -> "'Style'"
```

A journal's font, text size and label convention, with any setting overridden.

### Style.poster

```python
Style.poster(name: 'str' = 'a0', **overrides) -> "'Style'"
```

Type sizes for a poster read from a meter or two (see fignimble.journals.POSTERS): body text, the
smallest text preflight accepts, line widths and panel letters for that size of poster, with any setting
overridden. Pictures are checked against 150 dpi, what a poster is printed at.

## Labels

```python
Labels(format: 'str' = '{}', case: 'str' = 'lower', numbering: 'str' = 'alpha', font: 'str | None' = None, size: 'float' = 8, weight: 'str' = 'bold', style: 'str' = 'normal', color: 'str' = '#000000', dx: 'float' = 0.0, dy: 'float' = 0.0, order: 'str' = 'reading', align: 'bool' = True)
```

How panels are labeled (a, b, c ... or A, B, C ...), the same way on every panel.

format    the label around the letter: "{}" -> a, "({})" -> (a), "{})" -> a), "{}." -> a.
case      "lower" or "upper"
numbering "alpha" (a, b, c), "roman" (i, ii, iii), "arabic" (1, 2, 3) or "none" (no panel letters at all,
          a panel's own label="a'" included)
font, size (pt), weight ("bold" or "normal"), style ("normal" or "italic"), color
dx, dy    where a label sits, in mm from its panel's top-left corner (negative: outside, to the left / above)
order     "reading" (rows top to bottom, then left to right) or "source" (the order of the code)
align     line up the labels of panels in the same row (and column), so they don't sit at ragged heights

### Labels.journal

```python
Labels.journal(name: 'str', **overrides) -> "'Labels'"
```

A journal's label convention (see fignimble.journals), with any setting overridden.

### Labels.text

```python
Labels.text(i: 'int') -> 'str'
```

The label of the i-th panel (1-based).

## Panel

```python
Panel(source, function: 'str' = 'draw', args: 'dict | None' = None, label='auto', label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, texts: 'dict | None' = None, colors: 'dict | None' = None, rotation: 'float' = 0, name: 'str | None' = None, text_style: 'dict | None' = None)
```

A panel for a layout: like fig.panel(...) without a position, which the layout gives it.

## Image

```python
Image(source: 'str', fit: 'str' = 'contain', crop: 'tuple | None' = None, label='auto', label_dx: 'float | None' = None, label_dy: 'float | None' = None, label_x: 'float | None' = None, label_y: 'float | None' = None, opacity: 'float' = 1.0, rotation: 'float' = 0, flip: 'str | None' = None, name: 'str | None' = None)
```

A picture for a layout: like fig.image(...) without a position. It fills its cell (fit="contain").

## Spacer

```python
Spacer()
```

An empty cell.

## Row

```python
Row(*items, widths: 'list | None' = None, gap: 'float | None' = None)
```

Items side by side; `widths` are relative (e.g. [2, 1, 1]); `gap` (mm) overrides the layout's.

## Column

```python
Column(*items, heights: 'list | None' = None, gap: 'float | None' = None)
```

Items one above the other; `heights` are relative.

## Grid

```python
Grid(*items, ncol: 'int | None' = None, nrow: 'int | None' = None, widths: 'list | None' = None, heights: 'list | None' = None, byrow: 'bool' = True, gap: 'float | None' = None)
```

Items wrapped into a grid, row by row (or column by column with byrow=False). Without ncol or nrow, the
grid is as square as it can be, as with patchwork's `+`.

## Design

```python
Design(design: 'str', widths: 'list | None' = None, heights: 'list | None' = None, gap: 'float | None' = None, **areas)
```

A layout drawn as text, as in patchwork: each letter is an area, `#` an empty cell.

    Design("""
        AAB
        CDB
    """, A=a, B=b, C=c, D=d)

Each letter's area must be a rectangle.

## color

```python
color(name, default: 'str | None' = None) -> 'str'
```

A color of the project's palette (Style(palette=...)) by name, or by number for a list.

## is_dark

```python
is_dark() -> 'bool'
```

True while a panel is drawn for the dark version of its figure: panel code that picks its own colors can
pick ones that read on black (`"white" if is_dark() else "black"`).

## cached

```python
cached(*files)
```

Keep what a function returns, so the panels that need the same slow result (data loaded and cleaned, a model
fitted) compute it once:

    @cached("data/recordings.csv")
    def spikes(states):
        ...

The result is kept in memory and on disk (the project's .fignimble/cache) until the function's file changes,
its arguments change, or one of `files` (relative to the project) changes. Results must pickle. Every caller gets
the same object, so copy a result (`df.copy()`) before changing it in place.
