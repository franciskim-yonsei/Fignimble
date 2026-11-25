"""Figure 2, laid out patchwork style: the layout gives every panel its place, the plot areas line up across rows
and columns, and the gene legend the panels share is collected and shown once."""
from fignimble import *
from style import STYLE

fig = Figure(width=183, height=110, style=STYLE)

expression = Panel("panels/expression.py")
counts = Panel("panels/counts.py")
heatmap = Panel("panels/heatmap.py")
bars = Panel("panels/expression.py", function="bars")

fig.layout(Design("""
    AAC
    BDC
""", A=expression, B=bars, C=heatmap, D=counts), x=4, y=3, w=177, h=104, gap=6, widths=[1, 1, 1.2],
           guides="collect", legend="bottom")
fig.inset(Panel("panels/counts.py", function="zoom"), within=counts, x=0.55, y=0.08, w=0.4, h=0.35)
