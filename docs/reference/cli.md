# The fignimble command

The `fignimble` command renders, checks and exports figures from a terminal, and lets a coding agent look at its own
work. It is ready in Fignimble's terminal; **Fignimble ▸ Install Command Line Tool…** installs it for Terminal and
other apps.

| Command | Does |
|---|---|
| `fignimble new myproject [--journal nature]` | a new project: `style.py` and an empty `fig1.py` |
| `fignimble new myposter --poster 48x36 [--landscape]` | a new project with a poster to start from: a [template](../guide/posters.md#making-a-poster) (`a0`, `48x36`, `72x48`, `24x36`, ...) |
| `fignimble snapshot fig1.py [--dpi 150] [--dark]` | renders to `.fignimble/snapshots/fig1.png` and prints the path |
| `fignimble info fig1.py [--json]` | every element's box in mm, the panel letters, panels' Python warnings and preflight warnings |
| `fignimble check [fig1.py ...]` | preflight warnings of every figure; exit status 1 if there are any |
| `fignimble export fig1.py fig1.pdf [--dpi 600]` | PDF, SVG, EPS, PNG, TIFF, JPEG or PowerPoint (.pptx), from the file's extension |
| `fignimble table fig1.py 3 table1.docx` | a table of the figure (its element number or name) as a Word file |
| `fignimble table data.csv table1.docx` | a CSV or TSV file as a Word table |
| `fignimble provenance fig1.pdf` | what an exported file was made from, and whether the project's files still match (exit status 1 if not) |
| `fignimble journals` | the journal presets and their column widths, and the poster sizes |

Options of `export`:

`--journal cell`
:   meet that journal's resolutions (default: the style's journal).

`--dpi N`
:   the resolution of what is drawn as pixels: the whole figure for TIFF, JPEG and PNG; panels' rasters (heatmaps,
    rasterized scatters) in PDF and EPS.

`--cmyk`, `--outline`
:   colors converted for print (PDF, EPS, TIFF, JPEG), and text turned into shapes (PDF, EPS, SVG). Both use
    Ghostscript.

`--dark`
:   the figure's dark version, for talks (also for `snapshot`).

`--bleed MM`, `--crop-marks`
:   for a print shop's PDF: the page extended MM millimeters past each edge, and marks showing where to cut.

Run the commands from the project folder, or give paths to the figure files.
