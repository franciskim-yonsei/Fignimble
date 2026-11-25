# Posters, and figures inside figures

A poster is a figure: a large page with a title, some text and the paper's figures. In Fignimble you make one
the same way as any figure. Because the figures on it are placed from their code, the poster always shows them as
they are now. Change a panel of Figure 2 and the poster changes too.

<img class="fg-shot" src="../../assets/poster.jpg" alt="An A1 poster made in Fignimble: a title band, text sections with bullets, two figures of the demo project placed whole, and a QR code">

## Placing a figure in a figure

`fig.figure(...)` places another figure of the project whole, with its panels, panel letters and annotations:

```python
fig.figure("fig2.py", x=20, y=130, w=360)        # scaled to 360 mm wide, keeping its proportions
fig.figure("fig3.py", x=20, y=520)               # at its own size
```

The figure is drawn from its own file every time, so it is always the figure as it is now. **Insert ▸ Figure…**
places one, and so does dropping a figure file onto the canvas. Double-click it to open that figure. The inspector
switches its panel letters off, makes its background transparent, or puts it back at its own size.

Scaling a figure scales everything in it, text and lines included. A 183 mm figure with 7 pt text placed 366 mm wide
prints its text at 14 pt. Preflight checks the text and pictures of a placed figure at the size they print. It also
reports what fails inside it, such as a panel whose code doesn't run, and colors color-blind readers can't tell
apart.

| Argument | Does |
|---|---|
| `w`, `h` | its size in mm; one of them keeps its proportions, both fit it inside that box, neither is its own size |
| `letters=False` | leaves out its panel letters (a poster often doesn't need them) |
| `style=` | draws it with another `Style` instead of its own, a poster's font for example |
| `background` | `"figure"` (its own page color), another color, or `None` for transparent |
| `label="auto"` | letters it as one panel of the figure it is placed in |

Use this in ordinary figures too: an overview figure that shows two others side by side, or a graphical abstract.
A figure can't be placed in itself, and the figures placed must be files of the same project.

## Making a poster

**File ▸ New Figure…** offers poster templates as well as figures of any size, and makes a poster to start from: a
title band, columns of sections, the project's first figure placed whole, and a QR code. Each template is a usual
conference size, hung the way it usually is; **Landscape** turns it.

| Template | Size | Typical for |
|---|---|---|
| A0 portrait (`a0`) | 841 × 1189 mm | European meetings; ISMB/ECCB recommends it |
| 48 × 36 in landscape (`48x36`) | 1219 × 914 mm | the most common size at US meetings |
| 36 × 48 in portrait (`36x48`) | 914 × 1219 mm | US meetings with portrait boards |
| 72 × 48 in landscape (`72x48`) | 1829 × 1219 mm | a whole 6 × 4 ft board, as at SfN |
| 72 × 36 in landscape (`72x36`) | 1829 × 914 mm | a wide poster on a 4 × 8 ft board, as ASHG has recommended |
| 46 × 46 in square (`46x46`) | 1168 × 1168 mm | square limits, as at ISMB 2024 |
| 42 × 56 in portrait (`42x56`) | 1067 × 1422 mm | large portrait boards |
| 24 × 36 in portrait (`24x36`) | 610 × 914 mm | workshops and machine learning meetings (NeurIPS 2024–25) |
| A1 portrait (`a1`) | 594 × 841 mm | workshops and smaller meetings |
| A0 landscape (`a0-landscape`) | 1189 × 841 mm | European meetings with landscape boards |
| B1 portrait (`b1`) | 707 × 1000 mm | common in Japan |
| A2 portrait (`a2`) | 420 × 594 mm | small sessions and retreats |

Conferences change their boards from year to year, so check the instructions of the one you are going to. From a
terminal, `fignimble new myposter --poster 48x36` makes a project holding a poster from a template, and
`fignimble journals` lists them.

```python
from fignimble import *

fig = Figure(poster="a1")                                     # 594 x 841 mm, 22 pt text
fig.rect(-5, -5, 604, 115, stroke=None, fill="#0B3C5D")       # a band past the edges, for the bleed
fig.text("Reproducible posters with Fignimble", x=20, y=25, size=72, weight="bold", color="#FFFFFF", width=554)
fig.text("Joseph Rich and Lior Pachter · Caltech", x=20, y=72, size=34, color="#FFFFFF", width=554)
fig.text(source="intro.md", x=20, y=135, width=170, height=480, fit="shrink")   # the prose, in Markdown
fig.figure("fig1.py", x=206, y=135, w=368)                    # the paper's figures, drawn from their code
fig.figure("fig2.py", x=206, y=400, w=368, letters=False)
fig.text(source="conclusions.md", x=20, y=650, width=460)
fig.qr("https://github.com/pachterlab/Fignimble", x=504, y=756, w=70)
```

`Figure(poster=...)` sets the page size and the style. It takes a template's name, and a size written the other way
round is the poster turned (`"48x36"` is the 36 × 48 in poster in landscape). `landscape=True` turns the page, and
`width` or `height` change either side. The style, `Style.poster(...)`, sets type sizes meant to be read from a meter
or two:

| Poster | Size (mm) | Text | Smallest text | Margin |
|---|---|---|---|---|
| `a0` | 841 × 1189 | 28 pt | 18 pt | 20 mm |
| `a1` | 594 × 841 | 22 pt | 14 pt | 15 mm |
| `a2` | 420 × 594 | 16 pt | 10 pt | 12 mm |
| `b1` | 707 × 1000 | 22 pt | 14 pt | 15 mm |
| `24x36` | 609.6 × 914.4 (24 × 36 in) | 22 pt | 14 pt | 15 mm |
| `36x48` | 914.4 × 1219.2 (36 × 48 in) | 28 pt | 18 pt | 20 mm |
| `42x56` | 1066.8 × 1422.4 (42 × 56 in) | 28 pt | 18 pt | 20 mm |
| `46x46` | 1168.4 × 1168.4 (46 × 46 in) | 28 pt | 18 pt | 20 mm |
| `36x72` | 914.4 × 1828.8 (36 × 72 in) | 28 pt | 18 pt | 20 mm |
| `48x72` | 1219.2 × 1828.8 (48 × 72 in) | 32 pt | 22 pt | 25 mm |

Preflight holds a poster to these sizes. It checks text at the size it prints, including the text of the figures
placed on it, and reports anything that must print within the margin along the edges, which printers may trim.
Rectangles and other shapes may run to the edge and past it. Pictures are checked against 150 dpi, what posters
are printed at. The inspector, with nothing selected, changes the poster's size, orientation and margin.

## Text on a poster

A poster has more prose than a figure. Text boxes (`fig.text(..., width=...)`) lay it out:

```python
fig.text("""# Results
Panels redraw at their real size, so fonts stay true.
- Letters follow the reading order
- Preflight checks every figure placed on the poster
1. Numbered items work too""", x=20, y=400, width=170)
```

- Lines starting with `- ` or `• ` are bullets and `1. ` numbered items, with their lines indented under them.
  Indent a bullet by two spaces to nest it.
- `# ` and `## ` start headings.
- An empty line starts a new paragraph.
- `**bold**`, `*italic*`, `^{superscript}` and `$math$` work as everywhere.

To keep a poster's prose out of its code, put it in a Markdown file: `fig.text(source="intro.md", width=170)`. The
lines of a paragraph join, as in Markdown. Double-click the text to edit the file in the code pane.

`height` is the box's height: preflight reports a text that runs past it. With `fit="shrink"`, the type gets smaller
until the text fits, but never smaller than the poster's smallest text size.

## QR codes

```python
fig.qr("https://doi.org/10.1038/s41586-025-09651-2", x=500, y=750, w=60)
```

**Insert ▸ QR Code…** asks for the link. `w` is the size of the square, including the light border that scanners
need. `level` sets how much of the code can be damaged and still read: `"L"`, `"M"` (the default), `"Q"` or `"H"`.
A QR code stays dark on light in a figure's dark version. Preflight warns when its squares are too small for a phone
to read. Fignimble draws QR codes itself, as vector squares, and needs no package for them.

## Printing and sending it

- **PDF for a print shop**: export with a **bleed** (3 mm is usual) and **crop marks**, which the export dialog offers
  for PDF (`fignimble export poster.py poster.pdf --bleed 3 --crop-marks`). The page then extends past the poster's
  edges, and shapes that run past the edge print into the bleed. The PDF's TrimBox is the poster.
- **PowerPoint**, which many conferences ask for: a poster exports onto a slide of its own size (PowerPoint's largest
  is 56 inches a side; a larger poster is scaled down to fit).
- **PNG, TIFF, JPEG**: written at 300 dpi at most. At a poster's size more would be hundreds of megapixels.

Every PDF, PNG, TIFF and SVG that Fignimble exports records what it was made from. See
[provenance](export.md#provenance).
