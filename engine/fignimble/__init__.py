"""Fignimble: multi-panel figures for publication, assembled from Python.

    from fignimble import *

    fig = Figure(width=183, height=100, style=Style.journal("nature"))
    fig.panel("panels/expression.py", x=0, y=0, w=90, h=50)
"""

from .api import PALETTES, cached, color, is_dark, Column, Design, Element, Figure, Grid, Image, Labels, Panel, Row, Spacer, Style, Table
from .journals import JOURNALS

__version__ = "0.4.0"
__all__ = ["Figure", "Table", "Labels", "Style", "Element", "JOURNALS", "Panel", "Image", "Spacer", "Row", "Column", "Grid",
           "Design", "is_dark", "color", "cached", "PALETTES"]
