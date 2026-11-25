# The demo figure

The repository's [`examples/demo`](https://github.com/pachterlab/Fignimble/tree/main/examples/demo) project is a
six-panel figure: an expression time course, a UMAP, a correlation heatmap, a micrograph, a schematic and a bar chart
with a significance bracket.

<img class="fg-figure" src="../assets/figure.png" alt="The demo figure: six panels lettered A to F">

Open it with **File ▸ Open Project…**, or from a clone:

```bash
open -a Fignimble examples/demo
```

The whole figure is this file, `fig1.py`:

```python
from fignimble import *
from style import STYLE

fig = Figure(width=183, height=118, style=STYLE)

fig.panel("panels/expression.py", x=4, y=3, w=56, h=52)
fig.panel("panels/scatter.py", x=65, y=3, w=56, h=52)
fig.panel("panels/heatmap.py", x=126, y=3, w=56, h=52)
fig.image("images/cells.png", x=4, y=63, w=52)
fig.image("images/schematic.svg", x=65, y=63, w=56, h=47)
fig.panel("panels/expression.py", x=129, y=63, w=53, h=50, function="bars")
fig.text("Treated", x=30, y=104, size=7, weight="bold", align="center")
fig.arrow(57, 82, 64, 82, width=0.6)
fig.bracket(147, 169, 67, text="**")
```

Things to try:

- Drag panel B over panel A: the letters swap.
- Resize panel C and watch its tick labels stay 7 pt.
- Click the y-axis label of panel A and retype it; then open `panels/expression.py` and see the change.
- **View ▸ Dark Version of Figure** (<kbd>⌥</kbd><kbd>⌘</kbd><kbd>D</kbd>).
- In `style.py`, change `case="upper"` to `"lower"`, or the journal, and save.
- **View ▸ Color Vision ▸ Deuteranopia**.
- Open the terminal and ask an agent to add a seventh panel.

For a large, real figure, see [rebuilding a Nature figure](examples/nature-rebuild.md): 37 panels, lettered a to ak,
from the authors' published code and data.
