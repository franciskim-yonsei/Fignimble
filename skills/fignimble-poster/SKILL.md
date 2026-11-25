---
name: fignimble-poster
description: >-
  Make a conference poster with Fignimble from a paper's own figures: a poster
  is a Fignimble figure at a poster size (A0, A1, 48x36, 36x48, ...) that
  places the paper's figures whole with fig.figure, so it is drawn from their
  code and keeps up with them, plus a title band, text sections from Markdown
  files, and a QR code; then checks it at printed size and exports a PDF with
  bleed and crop marks or a PowerPoint slide. Use when asked to "make a poster",
  "make an A0 poster from fig1 and fig2", "turn my paper into a poster", or
  "export my poster for the print shop".
---

# Make a poster with Fignimble

Follow the `fignimble` skill for the figure language and its rules (load it if it is installed). Find the
`fignimble` command first (`command -v fignimble`, else
`/Applications/Fignimble.app/Contents/Resources/bin/fignimble`).

## 1. Size, content, and where it goes

Ask for what you can't find:
- **Size and orientation**: from the conference's poster instructions if the user has them. `fignimble journals`
  lists presets (`a0`, `a1`, `a2`, `b1`) and templates (`48x36` is 48 in wide, landscape; `36x48` portrait; ...).
- **Content**: which figures of the paper, and the text (title, authors and affiliations, abstract or sections,
  references, a link for the QR code). Take text from the manuscript if the user points to it; keep it short: a
  poster is read from a meter or two away.
- **Project**: posters live in the paper's Fignimble project next to its figures, so `fig.figure("fig1.py")` can
  place them. If there is no project yet, `fignimble new FOLDER --poster a0` makes one with a `poster.py` to start
  from; for an existing project, start from the same layout by running that in a scratch folder and copying
  `poster.py` in.

## 2. Build it

```python
from fignimble import *

fig = Figure(poster="a0")                           # size, margin, and Style.poster type sizes; landscape=True turns it
fig.rect(-5, -5, 851, 140, stroke=None, fill="#0B3C5D")             # title band, run past the edges for the bleed
fig.text("Title of the poster", x=30, y=30, size=96, weight="bold", color="#FFFFFF", width=781)
fig.text("Author One, Author Two · Lab, Institution", x=30, y=80, size=48, color="#FFFFFF", width=781)

fig.text(source="text/background.md", x=30, y=160, width=250, height=420, fit="shrink")   # Markdown: # headings, - bullets
fig.figure("fig1.py", x=300, y=160, w=520)          # the paper's figure, whole, drawn from its code and scaled
fig.figure("fig2.py", x=300, y=620, w=520, letters=False)
fig.qr("https://doi.org/...", x=761, y=1104, w=60)
```

- **Place figures whole with `fig.figure(...)`. Never copy their panels into the poster**: placed figures change when
  the paper's figures do. `letters=False` drops their panel letters when the poster doesn't refer to them;
  `style=STYLE_POSTER` redraws one with another style if its text is too small even scaled up.
- Keep prose in Markdown files (`text/*.md`) placed with `fig.text(source=..., width=, height=)`; `height` makes
  preflight report overflow, `fit="shrink"` shrinks the type to fit (never below the poster's minimum).
- Columns of equal width with equal gutters, sections aligned at their tops, everything inside the margin. Literal
  numbers, as everywhere.
- Don't set text sizes below the style's (`Style.poster`); headings bigger, not body text smaller.

## 3. Check at printed size

```bash
fignimble snapshot poster.py --dpi 30    # a poster is large: a low dpi is enough to look at
fignimble check poster.py
```

Preflight checks text at the size it prints, inside the placed figures too, keeps the margin clear, and reports text
boxes that overflow. A placed figure whose text comes out too small needs a larger `w`, or `style=` with bigger type.
Look at the snapshot for balance: columns of similar length, nothing crowding the edges, figures large enough to read.

## 4. Export

```bash
fignimble export poster.py poster.pdf --bleed 3 --crop-marks   # for a print shop (ask whether they want bleed)
fignimble export poster.py poster.pdf                          # for a plotter or a PDF upload
fignimble export poster.py poster.pptx                         # a PowerPoint slide the poster's size
```

Every export records the git commit and a fingerprint of each file it was made from; `fignimble provenance
poster.pdf` checks it against the project later.

## 5. Report

The size, what's on the poster (which figures, which text files), the preflight result, the files written, and what
the user should check (author list, affiliations, the QR link, the conference's size rules).
