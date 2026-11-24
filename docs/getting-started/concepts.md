# Key concepts

## A project is a folder

```text
myfigures/
├── style.py            the look every figure shares
├── fig1.py             a figure file
├── fig2.py
├── panels/             the code that draws the panels
│   ├── expression.py
│   └── umap.py
├── images/             pictures: micrographs, gels, schematics, BioCartoon icons
├── data/               whatever your panels read
├── requirements.txt    packages the panels need (optional)
└── .fignimble/         renders and caches (safe to delete; git-ignored)
```

**Figure files** are the top-level `.py` files that make a `Figure`. **Panel files** have a function
`draw(fig, ...)`; one file can hold several (`function="bars"`). **`style.py`** holds the `Style` every figure
imports.

## Units and coordinates

Everything is in **millimeters**, measured from the figure's **top-left corner, with y pointing down**, as in
Illustrator. Text sizes are in points. Each element is one call with literal numbers:

```python
fig.panel("panels/umap.py", x=65, y=3, w=56, h=52)
```

so that moving it on the canvas is a one-number change to its line. Values Fignimble can't write back, such as a
position computed in a loop, still draw; the canvas tells you to edit those in the code.

## Panels draw at their size

A panel's `draw(fig)` gets a matplotlib `Figure` exactly as large as the panel. Resizing the panel runs the code
again at the new size: text stays at its point size and lines at their width, instead of being scaled with the
picture. Fignimble measures each panel's plot area and, where panels share an edge, draws them again so their axes
line up.

## Three ways to edit, one set of files

The canvas, the code pane, your own editor and an AI agent all change the same files. Fignimble watches the project
and redraws about a second after anything changes. **Undo** (<kbd>⌘</kbd><kbd>Z</kbd>) undoes canvas and code-pane
edits; it leaves alone lines someone else has changed since, so it never throws away an agent's work.

## The style and the labels

`Style` sets the font, text size, line widths, colors and whether axes have top and right spines, for every panel.
`Labels` sets the panel letters: case, format (`a`, `(a)`, `A.`), size, weight and offset. `Style.journal("nature")`
sets both from a [journal preset](../reference/journals.md). Change `style.py` and every figure follows.

## Figures inside figures, and posters

A figure file can be placed whole in another with `fig.figure("fig2.py", x=..., y=..., w=...)`. It is drawn from its
own file each time, with its panels, letters and annotations, scaled to the size you give it. A poster is a figure
made this way: `Figure(poster="a0")` sets a poster's page and type sizes, and the paper's figures are placed on it.
See [posters](../guide/posters.md).

## Renders are cached

Fignimble keeps each panel's drawing in `.fignimble/` and draws a panel again only when its code, its data, its size
or the style changes. For slow work that several panels share, like loading data or fitting a model, decorate a
function with [`@cached`](../guide/panels.md#sharing-slow-work-between-panels).
