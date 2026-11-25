# Patchwork-style layouts

Panels can be placed by hand, or laid out the way [patchwork](https://patchwork.data-imaginist.com) composes
ggplots: describe the arrangement, and the layout gives each panel its place and lines up their plot areas.

```python
expression, counts = Panel("panels/expression.py"), Panel("panels/counts.py")
heatmap, bars = Panel("panels/heatmap.py"), Panel("panels/expression.py", function="bars")

fig.layout((expression | counts) / (bars | heatmap), x=4, y=3, w=177, h=104, heights=[2, 1])   # | beside, / below
```

<img class="fg-shot" src="../../assets/layout.jpg" alt="A layout selected on the canvas, with grips between its rows and columns">

## Composing

- `a | b` puts panels side by side, `a / b` one above the other, and parentheses group.
- `Row(a, b, widths=[2, 1])`, `Column(a, b, heights=[...])` and `Grid(a, b, c, d, ncol=2)` give a part of the layout
  its own relative sizes; `Grid` also takes `nrow` and `byrow`.
- `Design` draws the arrangement as text, one letter per cell (`#` leaves a cell empty):

```python
fig.layout(Design("""
    AAC
    BDC
""", A=expression, B=bars, C=heatmap, D=counts), x=4, y=3, w=177, h=104, widths=[1, 1, 1.2],
           guides="collect", legend="bottom")
```

- `Spacer()` leaves a cell empty and `Image("images/gel.png")` puts a picture in one.
- `gap` sets the space between cells in millimeters.

## Plot areas line up

Fignimble draws each panel, measures its plot area, and draws the panels that share an edge again with equal
margins, whatever their tick labels: a row's tops and bottoms, a column's left sides. This applies to panels placed
by hand whose edges line up, too. `Style(align_axes=False)` turns it off.

## Legends

`guides="collect"` takes the legends out of the panels and shows each distinct entry once, at the right or bottom
(`legend="bottom"`). `fig.legend({"WT": "#999999", "KO": "#d1495b"}, x=150, y=5)` draws a legend of your own.

## Insets

`fig.inset(Panel("panels/zoom.py"), within=a, x=0.6, y=0.05, w=0.35, h=0.35)` puts a panel on another, in fractions
of its box.

## On the canvas

Dragging a panel of a layout moves the whole layout. The gaps between rows and columns have grips: drag one to
resize the rows or columns, written back as `widths` and `heights`. The inspector sets the gap, the widths, the
heights and the legends.

**Object ▸ Make Layout** (<kbd>⌘</kbd><kbd>L</kbd>) turns selected hand-placed panels into a layout, and
**Release Layout** (<kbd>⇧</kbd><kbd>⌘</kbd><kbd>L</kbd>) turns a layout back into hand-placed panels where they are.

Use a layout for regular grids of plots, and hand placement for figures arranged freely; both can be on the same
figure.
