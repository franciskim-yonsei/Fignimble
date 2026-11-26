# Labels, style and journals

## One style for every panel

Every figure of a project imports the `Style` in `style.py`:

```python
from fignimble import *

STYLE = Style.journal("nature", labels=Labels.journal("nature"), palette="okabe-ito")
```

A `Style` sets the font, the text size, line widths, whether axes have top and right spines, and the colors panels
plot with, for every panel. Plots made at different times, by different people, match. Change `style.py` (or the
**Style** section of the inspector with nothing selected) and every figure follows. The inspector's **Font** menu
lists the fonts installed on the Mac, and Microsoft Office's fonts when Office is installed.

```python
STYLE = Style(font="Arial", size=7, linewidth=0.6, despine=True, min_font=5, min_dpi=300,
              labels=Labels(format="{}", case="lower", size=8, weight="bold"), palette="okabe-ito")
```

`rc={...}` passes any other matplotlib settings to every panel.

### Microsoft Office fonts

Office's fonts can be the style's font: Aptos (Office's default since 2023), Aptos Display, Aptos Narrow, Aptos Serif,
Calibri, Cambria and the rest (`Style(font="Aptos")`). Office doesn't install these fonts for other apps. It keeps
them inside its own apps and downloads others when a document uses them, so Fignimble uses them from where Office keeps
them and copies nothing. A font Office hasn't downloaded yet appears once a document in PowerPoint or Word has used it.
An exported PDF carries the font, and an EPS has its text as outlines, so both look the same everywhere; an SVG only
names it. On a computer without Office, a figure in Aptos draws in a substitute font: install Microsoft's free
[Aptos download](https://www.microsoft.com/en-us/download/details.aspx?id=106087) there, or choose another font.

## Panel letters

Panels and pictures are lettered in reading order and re-lettered when they move. `Labels` sets their look for
every figure:

| Setting | Values |
|---|---|
| `format` | `"{}"`, `"({})"`, `"{}."`, … |
| `case` | `"lower"` or `"upper"` |
| `numbering` | `"alpha"` (a … z, aa, ab …), `"roman"`, `"arabic"`, or `"none"` |
| `size`, `weight`, `style`, `font`, `color` | the letters' type: `weight="bold"`, `style="italic"` |
| `dx`, `dy` | the offset from each panel's corner, in mm |

Letters in a row or a column line up. Dragging a letter moves all of them, since they share one offset;
<kbd>⌥</kbd>-drag nudges one. A panel can have a letter of its own (`label="a'"`) or none (`label=None`), and
`label_dx`, `label_dy` nudge its letter alone.

To place one letter apart from its panel, double-click the letter (or **Object ▸ Unlink Label from Panel**, or
untick **Label moves with the panel** in the inspector). An unlinked letter stays where it is when its panel
moves, and dragging it moves only it. In the code it is `label_x=`, `label_y=` on the panel's line, in mm on the
figure. Double-click it again to link it back. **Labels ▸ No Letters** turns letters off for a journal that adds its
own.

## Journal presets

`Style.journal("cell")` and `Labels.journal("cell")` set what the journal asks for: font and sizes, the panel label
convention, the minimum text size and resolutions. The inspector's **Width for a journal…** menu sets the figure to
one of its column widths. Fignimble has 71 presets, computational biology's especially: the Nature Portfolio, Cell
Press, PLOS, Oxford, BMC, Genome Research, Molecular Systems Biology, eLife, Frontiers, IEEE/ACM TCBB, Science and
PNAS. For chemistry and materials science there are ACS journals (JACS, ACS Nano, Nano Letters, Chemistry of
Materials…), RSC journals (Chemical Science, ChemComm, Journal of Materials Chemistry A…), Elsevier journals (Materials
Today, Acta Materialia, Carbon…) and Wiley journals (Advanced Materials, Advanced Functional Materials, Angewandte
Chemie…). See [journal presets](../reference/journals.md), and check the journal's current guidelines before
submitting.

For a poster, `Style.poster("a0")` sets type sizes meant to be read from a meter or two (28 pt text on A0, 22 pt on
A1), the smallest text preflight accepts, thicker lines and a margin; `Figure(poster="a0")` uses it. See
[poster sizes](posters.md#making-a-poster).

## Units and sizes

Figure and element sizes are in millimeters; text sizes in points. Nature's double column is 183 mm; a single column
89 mm. The inspector, with nothing selected, shows the figure's width and height in mm, cm, inches or feet,
whichever you pick, and **File ▸ New Figure…** takes them in the same units. The code keeps millimeters.

The page is white unless you choose another **Background** in the inspector (`Figure(background="#F5F5F5")`).
`background=None` makes it transparent.
