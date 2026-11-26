---
name: fignimble-journal
description: >-
  Get a Fignimble project's figures ready for a journal: restyle them for the
  journal (column widths, font, minimum text size, panel label style,
  resolutions), fix everything Fignimble's preflight reports (text below the
  minimum size inside panels, low-resolution pictures, elements outside the
  figure, widths that miss the columns, colors color-blind readers can't tell
  apart), and export files that meet the journal's rules (PDF/EPS/TIFF at the
  required dpi, CMYK or outlined text if asked, dark versions for talks). Use
  when asked to "restyle for Cell", "make my figures meet Nature's
  guidelines", "fix what fignimble check reports", "export my figures for
  submission", or "transfer my figures to another journal".
---

# Prepare figures for a journal

Follow the `fignimble` skill for the figure language and its rules (load it if it is installed). Find the
`fignimble` command first (`command -v fignimble`, else
`/Applications/Fignimble.app/Contents/Resources/bin/fignimble`).

## 1. Which journal, which figures

- The journal the user named, else the one `style.py` already uses (`Style.journal("...")`). `fignimble journals`
  lists the 71 journals (biology, chemistry and materials science: ACS, RSC, Elsevier, Wiley too) with their widths, fonts, minimum sizes, label style and dpi, and how each number was checked
  (against the guidelines, from summaries, or the publisher's usual values). Tell the user to check the journal's
  current guidelines for anything marked as not checked.
- The figures and tables: every top-level figure and table file, unless the user named some. Tables (`Table`
  files) have no column width to meet: skip the width step for them. Run `fignimble check` first to see where
  things stand.

## 2. Restyle

The look of every figure comes from `style.py`, so restyling is mostly one line there:

```python
STYLE = Style.journal("cell", labels=Labels.journal("cell"), palette="okabe-ito")
```

Keep the project's own palette and any `Labels(...)` overrides the user chose (`dx=`, `dy=`, `format=`) unless the
journal's rules contradict them. Then, for each figure:

- **Width**: set `Figure(width=...)` to one of the journal's column widths (single, 1.5, double) from
  `fignimble journals`. If the width changes, scale the positions of the figure's elements to fit, keeping the
  arrangement; a `fig.layout(...)` only needs its `w`. Keep numbers literal and round them to 0.5 mm.
- **Height**: what the panels need, and no taller than the journal's page allows (see its guidelines).

## 3. Fix preflight

```bash
fignimble check                 # every figure; exit status 1 while problems remain
fignimble info fig1.py          # where each element is, and the warnings for one figure
```

Fix each warning at its cause:
- **Text below the minimum size inside a panel**: the panel is too small for what it draws. Make the panel bigger,
  draw fewer ticks or shorter labels, or move a legend out (`guides="collect"`). Don't set `fontsize=` in panel code.
- **Annotation text too small**: raise its `size=` to the minimum, or remove the `size=` so the style's size applies.
- **Picture below 300 dpi at its printed size**: print it smaller, or ask the user for a higher-resolution file. Never
  upscale the image.
- **Outside the figure / overlapping**: move it inside, a few mm from the edge.
- **Colors color-blind readers can't tell apart**: change one in the palette (Okabe–Ito is safe); check with
  `fignimble snapshot` and the user's eyes.
- **Colors that print alike in grayscale** (a note, not a warning): only matters if the figure may be printed in
  black and white; then tell them apart by lightness, markers or line styles.
- **A scale bar without a pixel size**: give `pixel_size=` from the microscope (the picture's metadata had none), or
  check which is right when it disagrees with the metadata.
- **Type 3 fonts, duplicate labels**: follow the message.

Re-run `fignimble check` and open `fignimble snapshot FIG.py` for each figure after the changes: restyling can make
labels collide that didn't before.

## 4. Export

```bash
fignimble export fig1.py fig1.pdf --journal cell                 # vector, editable text
fignimble export fig1.py fig1.tiff --journal cell                # raised to the journal's dpi (line art / combination)
fignimble export fig1.py fig1.eps --journal cell --cmyk --outline   # only if the journal or printer asks
fignimble export fig1.py fig1-dark.pdf --dark                    # for talks
```

Export what the journal asks for (many take PDF; some want TIFF or EPS). The command prints the resolution it measured;
report it. Put the exports where the user asks (e.g. `submission/`), not in `.fignimble/`. `--cmyk`, `--outline` and
EPS conversions need Ghostscript (`brew install ghostscript`); say so if it is missing.

If the journal asks for source data, `fignimble source-data fig1.py` writes the values plotted in each panel to
`fig1_source_data.xlsx`, one sheet per panel. Check each panel's sheet: what couldn't be read back (shaded areas,
violins) is noted there; give that panel a `source_data(...)` function next to `draw` returning the data it plots.

Tables go to the journal as text, not pictures: `fignimble export table1.py table1.docx` for each `Table` file (Word,
with its title and notes; `.xlsx` if the journal asks for spreadsheets). A table drawn inside a figure:
`fignimble table fig1.py N tableN.docx` (N is the element number `fignimble info` shows). Table files restyle with
`style.py` like figures; their preflight (values that show as 0.000, mixed decimals, a missing title) is fixed in the
`Table(...)` call: `decimals=`, `title=`.

## 5. Report

Per figure: the width chosen, what changed, which warnings were fixed and how, any left for the user (a picture that
needs a better scan, a panel that can't fit its content at the column width), and the files written with their
measured resolution.
