"""The language figures are written in.

A figure file makes one `Figure` and places elements on it, one call per element:

    from fignimble import *

    fig = Figure(width=183, height=100, style=Style.journal("nature"))
    fig.panel("panels/expression.py", x=0, y=0, w=90, h=50)          # labeled a
    fig.panel("panels/umap.py", x=93, y=0, w=90, h=50)               # labeled b
    fig.image("images/gel.png", x=0, y=53, w=60)                     # labeled c
    fig.text("Treated", x=70, y=60, size=7)
    fig.arrow(65, 70, 85, 70)

Units are millimeters, measured from the figure's top-left corner, y pointing down (as in Illustrator).
These calls only record what the figure contains; the renderer (`fignimble.render`) draws it. Each call remembers
the line of code that made it, so Fignimble's editor can change that line when the element is moved on the canvas.
"""

from __future__ import annotations

import os
import sys

from . import journals

_HERE = os.path.dirname(os.path.abspath(__file__))
# every Figure made since the renderer last cleared this list, in order
_FIGURES: list["Figure"] = []
# whether the panel being drawn is for the dark version of its figure
_DARK = [False]


_STYLE: list = [None]                  # the style of the figure being drawn

# named palettes: Style(palette="okabe-ito"). The first four are safe for color-blind readers.
PALETTES = {
    "okabe-ito": ["#E69F00", "#56B4E9", "#009E73", "#F0E442", "#0072B2", "#D55E00", "#CC79A7", "#000000"],
    "tol-bright": ["#4477AA", "#EE6677", "#228833", "#CCBB44", "#66CCEE", "#AA3377", "#BBBBBB"],
    "tol-muted": ["#332288", "#88CCEE", "#44AA99", "#117733", "#999933", "#DDCC77", "#CC6677", "#882255", "#AA4499"],
    "tol-vibrant": ["#EE7733", "#0077BB", "#33BBEE", "#EE3377", "#CC3311", "#009988", "#BBBBBB"],
    "colorbrewer-set2": ["#66C2A5", "#FC8D62", "#8DA0CB", "#E78AC3", "#A6D854", "#FFD92F", "#E5C494", "#B3B3B3"],
    "colorbrewer-dark2": ["#1B9E77", "#D95F02", "#7570B3", "#E7298A", "#66A61E", "#E6AB02", "#A6761D", "#666666"],
    "tableau": ["#1F77B4", "#FF7F0E", "#2CA02C", "#D62728", "#9467BD", "#8C564B", "#E377C2", "#7F7F7F", "#BCBD22", "#17BECF"],
}


def palette_colors(pal) -> dict:
    """A palette (a name, a list or a dict) as {name: color}, list entries named by their number."""
    if isinstance(pal, str):
        pal = PALETTES.get(pal.lower())
        if pal is None:
            raise ValueError(f"no palette named {pal!r}; there are {', '.join(PALETTES)}")
    if isinstance(pal, dict):
        return {str(k): v for k, v in pal.items()}
    return {str(i): c for i, c in enumerate(pal or [])}


def color(name, default: str | None = None) -> str:
    """A color of the project's palette (Style(palette=...)) by name, or by number for a list."""
    st = _STYLE[0]
    pal = st.props.get("palette") if st is not None else None
    try:
        if isinstance(pal, str):
            pal = PALETTES[pal.lower()]
        if isinstance(pal, dict):
            return pal[name]
        if pal:
            return pal[int(name) % len(pal)]
    except (KeyError, ValueError, TypeError):
        pass
    if default is not None:
        return default
    raise KeyError(f"no color {name!r} in the style's palette")


_CACHE: dict = {}                      # what cached() kept in this process: shared by the panels of a render


def cached(*files):
    """Keep what a function returns, so the panels that need the same slow result (data loaded and cleaned, a model
    fitted) compute it once:

        @cached("data/recordings.csv")
        def spikes(states):
            ...

    The result is kept in memory and on disk (the project's .fignimble/cache) until the function's file changes,
    its arguments change, or one of `files` (relative to the project) changes. Results must pickle. Every caller gets
    the same object, so copy a result (`df.copy()`) before changing it in place."""
    import functools
    import hashlib
    import inspect
    import os
    import pickle
    from pathlib import Path

    def wrap(fn):
        @functools.wraps(fn)
        def inner(*args, **kwargs):
            src = inspect.getsourcefile(fn) or ""
            try:
                code = Path(src).read_bytes()
            except OSError:
                code = fn.__code__.co_code
            stamps = []
            for f in files:
                try:
                    st = os.stat(f)
                    stamps.append((f, st.st_mtime_ns, st.st_size))
                except OSError:
                    stamps.append((f, None))
            h = hashlib.sha256(pickle.dumps((os.getcwd(), fn.__module__, fn.__qualname__, code, args,
                                             sorted(kwargs.items()), stamps))).hexdigest()[:24]
            if h in _CACHE:
                return _CACHE[h]
            folder = Path(".fignimble") / "cache"         # panels run in the project folder
            f = folder / f"{fn.__name__}-{h}.pkl"
            try:
                value = pickle.loads(f.read_bytes())
            except Exception:                              # noqa: BLE001 (not kept yet, or unreadable)
                value = fn(*args, **kwargs)
                try:
                    folder.mkdir(parents=True, exist_ok=True)
                    tmp = f.with_suffix(f".{os.getpid()}.tmp")
                    tmp.write_bytes(pickle.dumps(value))
                    tmp.replace(f)
                except Exception:                          # noqa: BLE001 (unpicklable: memory only)
                    pass
            _CACHE[h] = value
            return value
        return inner
    return wrap


def is_dark() -> bool:
    """True while a panel is drawn for the dark version of its figure: panel code that picks its own colors can
    pick ones that read on black (`"white" if is_dark() else "black"`)."""
    return _DARK[0]


def _site() -> dict | None:
    """Where the user's code called into fignimble: {file, line, pos} (pos = (line, end line, col, end col) of the call
    on Python 3.11+, else None)."""
    f = sys._getframe(1)
    while f is not None and os.path.dirname(os.path.abspath(f.f_code.co_filename)) == _HERE:
        f = f.f_back
    if f is None:
        return None
    pos = None
    try:
        positions = list(f.f_code.co_positions())
        p = positions[f.f_lasti // 2]
        if p[0] is not None and p[2] is not None:
            pos = tuple(p)
    except (AttributeError, IndexError):
        pass
    return {"file": os.path.abspath(f.f_code.co_filename), "line": f.f_lineno, "pos": pos}


def _roman(n: int) -> str:
    out = ""
    for v, s in ((1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
                 (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")):
        while n >= v:
            out += s
            n -= v
    return out


def _alpha(n: int) -> str:
    """1 -> a, 26 -> z, 27 -> aa."""
    s = ""
    while n > 0:
        n, r = divmod(n - 1, 26)
        s = chr(97 + r) + s
    return s


class Labels:
    """How panels are labeled (a, b, c ... or A, B, C ...), the same way on every panel.

    format    the label around the letter: "{}" -> a, "({})" -> (a), "{})" -> a), "{}." -> a.
    case      "lower" or "upper"
    numbering "alpha" (a, b, c), "roman" (i, ii, iii), "arabic" (1, 2, 3) or "none" (no panel letters at all,
              a panel's own label="a'" included)
    font, size (pt), weight ("bold" or "normal"), style ("normal" or "italic"), color
    dx, dy    where a label sits, in mm from its panel's top-left corner (negative: outside, to the left / above)
    order     "reading" (rows top to bottom, then left to right) or "source" (the order of the code)
    align     line up the labels of panels in the same row (and column), so they don't sit at ragged heights
    """

    def __init__(self, format: str = "{}", case: str = "lower", numbering: str = "alpha", font: str | None = None,
                 size: float = 8, weight: str = "bold", style: str = "normal", color: str = "#000000",
                 dx: float = 0.0, dy: float = 0.0, order: str = "reading", align: bool = True):
        self.props = dict(format=format, case=case, numbering=numbering, font=font, size=size, weight=weight,
                          style=style, color=color, dx=dx, dy=dy, order=order, align=align)
        self.site = _site()
        self.preset = None

    @classmethod
    def journal(cls, name: str, **overrides) -> "Labels":
        """A journal's label convention (see fignimble.journals), with any setting overridden."""
        j = journals.JOURNALS[name.lower()]
        lab = cls(**{**j["labels"], **overrides})
        lab.site = _site()
        lab.preset = name.lower()
        return lab

    def __getattr__(self, k):
        try:
            return self.__dict__["props"][k]
        except KeyError:
            raise AttributeError(k) from None

    def text(self, i: int) -> str:
        """The label of the i-th panel (1-based)."""
        p = self.props
        if p["numbering"] == "none":
            return ""
        core = {"alpha": _alpha, "roman": _roman, "arabic": str}.get(p["numbering"], _alpha)(i)
        core = core.upper() if p["case"] == "upper" else core.lower()
        return p["format"].replace("{}", core) if "{}" in p["format"] else core


class Style:
    """What every panel of a figure shares: font, text size, line widths, label style. Put one in a `style.py`
    next to the figure files and import it in each, so every figure of a paper looks the same.

    font      font family for panels, text and labels ("Arial", "Helvetica", ...)
    size      text size (pt) of tick labels, axis labels, legends and text elements
    linewidth line width (pt) of axes, ticks and plotted lines
    labels    a Labels
    despine   hide the top and right axis lines in panels
    rc        more matplotlib rcParams, applied to every panel
    min_font  the smallest text size (pt) the preflight check accepts
    min_dpi   the lowest resolution (dots per inch, as printed) the preflight check accepts for pictures
    align_axes line up the plot areas of panels that share an edge (a row's tops and bottoms, a column's left
              and right sides), as patchwork does, whatever their tick labels
    palette   the project's colors: a list, {"WT": "#999999", "KO": "#d1495b"}, or the name of one of fignimble's
              (PALETTES: "okabe-ito", "tol-bright", "tol-muted", "tol-vibrant", "colorbrewer-set2", ...). The
              colors panels plot with in turn, offered as swatches in the editor, and color("KO") in panel code
    poster    the poster size this style is for (see Style.poster): ticks and padding grow with the text
    """

    def __init__(self, font: str = "Arial", size: float = 7, linewidth: float = 0.6, labels: Labels | None = None,
                 despine: bool = True, rc: dict | None = None, min_font: float = 5, min_dpi: float = 300,
                 journal: str | None = None, align_axes: bool = True, palette=None, poster: str | None = None):
        self.props = dict(font=font, size=size, linewidth=linewidth, despine=despine, min_font=min_font,
                          min_dpi=min_dpi, journal=journal, align_axes=align_axes, poster=poster,
                          palette=palette if isinstance(palette, str) else
                          (dict(palette) if isinstance(palette, dict) else (list(palette) if palette else None)))
        self.labels = labels or Labels()
        self.rc = dict(rc or {})
        self.site = _site()

    @classmethod
    def journal(cls, name: str, **overrides) -> "Style":
        """A journal's font, text size and label convention, with any setting overridden."""
        j = journals.JOURNALS[name.lower()]
        labels = overrides.pop("labels", None) or Labels.journal(name)
        st = cls(**{"font": j["font"], "size": j["font_size"], "min_font": j["min_font"], "journal": name.lower(),
                    "labels": labels, **overrides})
        st.site = _site()
        return st

    @classmethod
    def poster(cls, name: str = "a0", **overrides) -> "Style":
        """Type sizes for a poster read from a meter or two (see fignimble.journals.POSTERS): body text, the
        smallest text preflight accepts, line widths and panel letters for that size of poster, with any setting
        overridden. Pictures are checked against 150 dpi, what a poster is printed at."""
        key = journals.resolve(name)[0]
        p = journals.POSTERS[key]
        labels = overrides.pop("labels", None) or Labels(size=p["labels"], weight="bold", case="lower")
        st = cls(**{"font": "Arial", "size": p["body"], "linewidth": p["linewidth"], "min_font": p["min_font"],
                    "min_dpi": journals.POSTER_DPI["photo"], "labels": labels, "poster": key, **overrides})
        st.site = _site()
        return st

    def __getattr__(self, k):
        try:
            return self.__dict__["props"][k]
        except KeyError:
            raise AttributeError(k) from None

    def rcparams(self) -> dict:
        p = self.props
        lw, size = p["linewidth"], p["size"]
        rc = {
            "font.family": "sans-serif",
            "font.sans-serif": [p["font"], "Helvetica", "Arial", "Liberation Sans", "DejaVu Sans"],
            "font.size": size,
            "axes.titlesize": size, "axes.labelsize": size, "xtick.labelsize": size, "ytick.labelsize": size,
            "legend.fontsize": size, "figure.titlesize": size,
            "axes.linewidth": lw, "lines.linewidth": max(lw * 1.5, 0.5), "patch.linewidth": lw,
            "xtick.major.width": lw, "ytick.major.width": lw, "xtick.minor.width": lw * 0.75,
            "ytick.minor.width": lw * 0.75, "xtick.major.size": 2.5, "ytick.major.size": 2.5,
            "axes.titlepad": 3, "axes.labelpad": 2, "xtick.major.pad": 1.5, "ytick.major.pad": 1.5,
            "legend.frameon": False,
            "pdf.fonttype": 42, "ps.fonttype": 42, "svg.fonttype": "none",   # real, editable text in exports
            "figure.constrained_layout.use": True,
            "figure.constrained_layout.h_pad": 0.02, "figure.constrained_layout.w_pad": 0.02,
            "savefig.transparent": False,
        }
        if p.get("poster"):                      # ticks and gaps in proportion to poster-sized text
            k = size / 7
            rc.update({"xtick.major.size": 2.5 * k, "ytick.major.size": 2.5 * k, "axes.titlepad": 3 * k,
                       "axes.labelpad": 2 * k, "xtick.major.pad": 1.5 * k, "ytick.major.pad": 1.5 * k})
        if p["despine"]:
            rc.update({"axes.spines.top": False, "axes.spines.right": False})
        if p["palette"]:
            from cycler import cycler
            colors = list(palette_colors(p["palette"]).values())
            rc["axes.prop_cycle"] = cycler(color=colors)
        rc.update(self.rc)
        return rc


class Node:
    """Something a layout arranges: a Panel, an Image, a Spacer, or a Row, Column, Grid or Design of them.

    Compose them as in patchwork: `a | b` side by side, `a / b` one above the other, `a + b` wrapped into a grid,
    with parentheses for nesting: `(a | b | c) / (d | e)`.
    """
    site = None
    made_by_operator = False

    def __or__(self, other):
        return _join(Row, self, other)

    def __truediv__(self, other):
        return _join(Column, self, other)

    def __add__(self, other):
        return _join(Grid, self, other)


def _join(cls, a, b):
    items = list(a.items) if isinstance(a, cls) and a.made_by_operator else [a]
    items.append(b)
    n = cls(*items)
    n.site = None
    n.made_by_operator = True
    return n


class Panel(Node):
    """A panel for a layout: like fig.panel(...) without a position, which the layout gives it."""
    kind = "panel"

    def __init__(self, source, function: str = "draw", args: dict | None = None, label="auto",
                 label_dx: float | None = None, label_dy: float | None = None,
                 label_x: float | None = None, label_y: float | None = None, texts: dict | None = None,
                 colors: dict | None = None, rotation: float = 0, name: str | None = None, text_style: dict | None = None):
        self.props = dict(source=source if callable(source) else str(source), function=function,
                          args=dict(args or {}), label=label, label_dx=label_dx, label_dy=label_dy, label_x=label_x, label_y=label_y,
                          texts=dict(texts or {}), colors=dict(colors or {}), rotation=rotation, name=name,
                          text_style=dict(text_style or {}))
        self.site = _site()
        self.method = "Panel"


class Image(Node):
    """A picture for a layout: like fig.image(...) without a position. It fills its cell (fit="contain")."""
    kind = "image"

    def __init__(self, source: str, fit: str = "contain", crop: tuple | None = None, label="auto",
                 label_dx: float | None = None, label_dy: float | None = None,
                 label_x: float | None = None, label_y: float | None = None, opacity: float = 1.0,
                 rotation: float = 0, flip: str | None = None, name: str | None = None):
        self.props = dict(source=str(source), fit=fit, crop=tuple(crop) if crop else None, label=label,
                          label_dx=label_dx, label_dy=label_dy, label_x=label_x, label_y=label_y, opacity=opacity,
                          rotation=rotation, flip=flip, name=name)
        self.site = _site()
        self.method = "Image"


class Spacer(Node):
    """An empty cell."""
    kind = "spacer"

    def __init__(self):
        self.site = _site()
        self.method = "Spacer"


class Row(Node):
    """Items side by side; `widths` are relative (e.g. [2, 1, 1]); `gap` (mm) overrides the layout's."""
    kind = "row"

    def __init__(self, *items, widths: list | None = None, gap: float | None = None):
        self.items = list(items)
        self.props = dict(widths=list(widths) if widths else None, gap=gap)
        self.site = _site()
        self.method = "Row"


class Column(Node):
    """Items one above the other; `heights` are relative."""
    kind = "column"

    def __init__(self, *items, heights: list | None = None, gap: float | None = None):
        self.items = list(items)
        self.props = dict(heights=list(heights) if heights else None, gap=gap)
        self.site = _site()
        self.method = "Column"


class Grid(Node):
    """Items wrapped into a grid, row by row (or column by column with byrow=False). Without ncol or nrow, the
    grid is as square as it can be, as with patchwork's `+`."""
    kind = "grid"

    def __init__(self, *items, ncol: int | None = None, nrow: int | None = None, widths: list | None = None,
                 heights: list | None = None, byrow: bool = True, gap: float | None = None):
        self.items = list(items)
        self.props = dict(ncol=ncol, nrow=nrow, widths=list(widths) if widths else None,
                          heights=list(heights) if heights else None, byrow=byrow, gap=gap)
        self.site = _site()
        self.method = "Grid"


class Design(Node):
    """A layout drawn as text, as in patchwork: each letter is an area, `#` an empty cell.

        Design(\"\"\"
            AAB
            CDB
        \"\"\", A=a, B=b, C=c, D=d)

    Each letter's area must be a rectangle."""
    kind = "design"

    def __init__(self, design: str, widths: list | None = None, heights: list | None = None,
                 gap: float | None = None, **areas):
        rows = [r.strip() for r in design.strip().splitlines() if r.strip()]
        if not rows or len({len(r) for r in rows}) != 1:
            raise ValueError("every row of a design needs the same number of cells")
        self.rows = rows
        self.areas = areas
        self.items = list(areas.values())
        self.props = dict(design="\n".join(rows), widths=list(widths) if widths else None,
                          heights=list(heights) if heights else None, gap=gap)
        self.site = _site()
        self.method = "Design"


class Element:
    """Something placed on a figure. `kind` is panel, image, figure, text, table, line, rect, ellipse, bracket, qr, ..."""

    def __init__(self, fig: "Figure", kind: str, method: str, props: dict, site):
        self.fig, self.kind, self.method, self.props, self.site = fig, kind, method, props, site
        self.index = len(fig.elements)

    def __getattr__(self, k):
        try:
            return self.__dict__["props"][k]
        except KeyError:
            raise AttributeError(k) from None

    def __repr__(self):
        return f"<fignimble {self.kind} #{self.index} {self.props}>"

    # geometry, for code that places one element next to another (fig.text(..., x=a.right + 2))
    @property
    def box(self) -> tuple[float, float, float, float]:
        p = self.props
        if self.kind in ("line",):
            return (min(p["x1"], p["x2"]), min(p["y1"], p["y2"]), abs(p["x2"] - p["x1"]), abs(p["y2"] - p["y1"]))
        if self.kind == "bracket":
            return (min(p["x1"], p["x2"]), p["y"] - p["tick"], abs(p["x2"] - p["x1"]), p["tick"])
        if self.kind in ("text", "legend", "table"):
            return (p["x"], p["y"], 0.0, 0.0)
        if self.kind == "path":
            xs, ys = [q[0] for q in p["points"]], [q[1] for q in p["points"]]
            return (min(xs), min(ys), max(xs) - min(xs), max(ys) - min(ys))
        if self.kind in ("zoom", "scalebar"):
            return (0.0, 0.0, 0.0, 0.0)
        if self.kind == "qr":
            return (p["x"], p["y"], p["w"], p["w"])
        if self.kind == "figure":
            w, h = p["w"], p["h"]
            if w is None or h is None:
                from .render import nested_size
                W, H = nested_size(p["source"])
                if w is None and h is None:
                    w, h = W, H
                elif w is None:
                    w = h * W / H
                else:
                    h = w * H / W
            return (p["x"], p["y"], w, h)
        if self.kind == "layout":
            return (p["x"], p["y"], p["w"] if p["w"] is not None else self.fig.width - p["x"],
                    p["h"] if p["h"] is not None else self.fig.height - p["y"])
        if self.kind == "inset":
            hx, hy, hw, hh = self.host.box if isinstance(self.host, Element) else (0, 0, 0, 0)
            return (hx + p["x"] * hw, hy + p["y"] * hh, p["w"] * hw, p["h"] * hh)
        h = p.get("h")
        if h is None and self.kind == "image":
            from .render import image_aspect
            h = p["w"] / image_aspect(p["source"], p.get("crop"))
        return (p["x"], p["y"], p["w"], h or 0.0)

    left = property(lambda s: s.box[0])
    top = property(lambda s: s.box[1])
    width = property(lambda s: s.box[2])
    height = property(lambda s: s.box[3])
    right = property(lambda s: s.box[0] + s.box[2])
    bottom = property(lambda s: s.box[1] + s.box[3])
    cx = property(lambda s: s.box[0] + s.box[2] / 2)
    cy = property(lambda s: s.box[1] + s.box[3] / 2)


class Figure:
    """A figure (or a poster): its size (mm), its style, and the elements placed on it.

    width, height  in mm. With `journal` (and `columns`) and no width, the journal's column width.
    style          a Style shared by every panel (font, sizes, labels); `labels` overrides its Labels
    background     page color ("#FFFFFF"), or None for transparent
    poster         a poster size ("a0", "a1", "b1", "24x36", "36x48", "48x36" (turned), "72x48", ...; see
                   fignimble.journals.POSTERS and POSTER_TEMPLATES):
                   the figure is that size (`landscape=True` turns it), its style is Style.poster(poster) unless
                   one is given, and preflight keeps `margin` mm along the edges clear (the preset's by default)
    """

    def __init__(self, width: float | None = None, height: float | None = None, style: Style | None = None,
                 labels: Labels | None = None, background: str | None = "#FFFFFF", journal: str | None = None,
                 columns="double", poster: str | None = None, landscape: bool = False, margin: float | None = None):
        if poster:
            key, turned = journals.resolve(poster)          # "48x36" is the 36 x 48 in poster turned
            landscape = landscape != turned
            poster = key
            pw, ph = journals.poster_size(key, landscape)
            width = pw if width is None else width
            height = ph if height is None else height
            if style is None:
                style = Style.poster(poster)
                style.site = None
            if margin is None:
                margin = journals.poster(poster)["margin"]
        if width is None:
            width = journals.width(journal, columns) if journal else 183.0
        if height is None:
            height = 120.0
        if style is None:
            style = Style.journal(journal) if journal else Style()
            style.site = None                       # made here, not in the user's code
        self.props = dict(width=width, height=height, background=background, journal=journal or style.props.get("journal"),
                          columns=columns, poster=poster.lower() if poster else None, landscape=landscape,
                          margin=margin)
        self.style = style
        self.labels = labels or style.labels
        self.elements: list[Element] = []
        self.guides: list[dict] = []
        self.caption_text: str | None = None
        self.caption_site = None
        self.site = _site()
        _FIGURES.append(self)

    width = property(lambda s: s.props["width"])
    height = property(lambda s: s.props["height"])

    def _add(self, kind, method, props) -> Element:
        e = Element(self, kind, method, props, _site())
        self.elements.append(e)
        return e

    # --- panels: the parts of a figure that get a letter ---

    def panel(self, source, x: float = 0, y: float = 0, w: float = 80, h: float = 60, function: str = "draw",
              args: dict | None = None, label="auto", label_dx: float | None = None, label_dy: float | None = None,
                 label_x: float | None = None, label_y: float | None = None,
              texts: dict | None = None, colors: dict | None = None, rotation: float = 0, group: str | None = None, name: str | None = None, locked: bool = False, hidden: bool = False,
              text_style: dict | None = None) -> Element:
        """A panel drawn by Python code. `source` is a .py file (path relative to the project) whose `function`
        (default `draw`) takes a matplotlib Figure of exactly w x h mm and draws on it, or a function itself:

            def draw(fig, gene="CD3E"):
                ax = fig.add_subplot()
                ...

        `args` are passed to it as keyword arguments, so one panel file can draw several panels. Resizing the
        panel redraws it at the new size, so its text keeps its size. `label` is "auto" (the next letter), a
        string of your own ("a'"), or None (no label); `label_dx`/`label_dy` move this panel's label only, and
        `label_x`/`label_y` (mm, on the figure) unhook it: it stays there when the panel moves.
        `texts` replaces texts the code draws ({"ax0.xlabel": "Time (min)", "ax0.xtick2": "10³"}), which is how
        the editor retypes a text the code doesn't hold as a literal; `text_style` sets such a text's size, color,
        weight and slant ({"ax0.legend1": {"size": 8, "color": "#D55E00", "weight": "bold", "style": "italic"}});
        `colors` likewise recolors lines, bars and points ({"ax0.line1": "#0072B2"}); `rotation` turns the panel
        (90, 180, 270).
        """
        if callable(source):
            src = source
        else:
            src = str(source)
        return self._add("panel", "panel", dict(source=src, x=x, y=y, w=w, h=h, function=function, args=dict(args or {}),
                                                label=label, label_dx=label_dx, label_dy=label_dy, label_x=label_x, label_y=label_y,
                                                texts=dict(texts or {}), colors=dict(colors or {}), rotation=rotation,
                                                text_style=dict(text_style or {}), group=group,
                                                name=name, locked=locked, hidden=hidden))

    def image(self, source: str, x: float = 0, y: float = 0, w: float = 60, h: float | None = None,
              fit: str = "contain", crop: tuple | None = None, label="auto", label_dx: float | None = None,
              label_dy: float | None = None, label_x: float | None = None, label_y: float | None = None,
              opacity: float = 1.0, rotation: float = 0, flip: str | None = None,
              group: str | None = None,
              name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A picture as a panel: PNG, JPEG, TIFF (raster) or PDF, SVG (vector, kept as vector). Without `h`, the
        height follows the picture's aspect ratio. `fit` is "contain" (fit inside w x h), "cover" (fill it,
        cropping) or "stretch". `crop` = (left, top, right, bottom), fractions 0-1 of the picture to cut away.
        `rotation` turns it (degrees counterclockwise; any angle for PNG/TIFF/JPEG, multiples of 90 for PDF/SVG) and
        `flip` mirrors it ("h" or "v")."""
        return self._add("image", "image", dict(source=str(source), x=x, y=y, w=w, h=h, fit=fit,
                                                crop=tuple(crop) if crop else None, label=label, label_dx=label_dx,
                                                label_dy=label_dy, label_x=label_x, label_y=label_y, opacity=opacity,
                                                rotation=rotation, flip=flip,
                                                group=group, name=name, locked=locked, hidden=hidden))

    def figure(self, source: str, x: float = 0, y: float = 0, w: float | None = None, h: float | None = None,
               style: Style | None = None, letters: bool = True, background="figure", label=None,
               label_dx: float | None = None, label_dy: float | None = None, label_x: float | None = None,
               label_y: float | None = None, group: str | None = None, name: str | None = None,
               locked: bool = False, hidden: bool = False) -> Element:
        """Another figure of the project, placed whole: `source` is its figure file ("fig2.py"). It is drawn from
        its code every time, so it is always the figure as it is now, with its panels, letters and annotations.

        Without `w` and `h` it is placed at its own size; with `w` (or `h`) it is scaled to that width (height),
        text and lines with it, keeping its proportions (with both, it fits inside w x h). `style` draws it with
        another Style (a poster's font, say) instead of its own; `letters=False` leaves out its panel letters;
        `background` is its own page color ("figure"), another color, or None (transparent). `label` "auto"
        letters it as a panel of this figure; None (the default): no letter."""
        e = self._add("figure", "figure", dict(source=str(source), x=x, y=y, w=w, h=h, letters=letters,
                                                background=background, label=label, label_dx=label_dx,
                                                label_dy=label_dy, label_x=label_x, label_y=label_y, group=group,
                                                name=name, locked=locked, hidden=hidden))
        e.substyle = style
        return e

    # --- annotations ---

    def text(self, text: str = "", x: float = 0, y: float = 0, size: float | None = None, weight: str = "normal",
             style: str = "normal", color: str = "#000000", align: str = "left", rotation: float = 0,
             font: str | None = None, width: float | None = None, opacity: float = 1.0, source: str | None = None,
             height: float | None = None, fit: str | None = None, group: str | None = None, name: str | None = None,
             locked: bool = False, hidden: bool = False) -> Element:
        """Text. (x, y) is the top-left of the text (top-center or top-right with align "center" / "right").
        Size in pt; default the style's size. `width` (mm) makes it a text box whose lines wrap.
        Markup: **bold**, *italic*, ^{superscript}, _{subscript} (in the style's font), and matplotlib's math:
        "$\\alpha$-actin". A line break is "\\n".

        In a text box (`width`), lines that start with "- " or "• " are bullets, "1. " numbered items and "# " or
        "## " headings, with their lines indented under them; an empty line starts a paragraph.
        `source` takes the text from a file of the project instead (a .md or .txt, written as Markdown: lines of a
        paragraph join, "- " and "* " are bullets). `height` (mm) is the box's height: preflight says when the text
        doesn't fit, and fit="shrink" makes the type smaller until it does (never below the style's minimum)."""
        return self._add("text", "text", dict(text=str(text), x=x, y=y, size=size, weight=weight, style=style,
                                              color=color, align=align, rotation=rotation, font=font, width=width,
                                              opacity=opacity, source=None if source is None else str(source),
                                              height=height, fit=fit,
                                              group=group,
                                              name=name, locked=locked, hidden=hidden))

    def table(self, source: str | None = None, x: float = 0, y: float = 0, w: float | None = None,
              rows: list | None = None, header: bool = True, size: float | None = None, decimals=None,
              align=None, font: str | None = None, color: str = "#000000", grid: bool = False,
              group: str | None = None, name: str | None = None, locked: bool = False,
              hidden: bool = False) -> Element:
        """A table from a .csv, .tsv or .txt file (`source`, relative to the project) or from `rows` ([["Gene",
        "n"], ["CD3E", 12], ...]). (x, y) is its top-left; `w` (mm) widens it (it is never narrower than its text).
        The first row is the header (`header=False`: none). `decimals` rounds numbers with decimals (2, or
        [None, 0, 2, 3] per column); `align` is "lrr"
        (left/center/right per column), else numbers go right and words left. Size in pt; default the style's.
        Rules above and below the header and at the bottom, as in a printed paper; `grid=True` draws a line between
        every row and column and around the table instead (in `color`). Journals want tables as text:
        the editor's Export Table as Word (and `fignimble table`) writes it as a .docx."""
        return self._add("table", "table", dict(source=None if source is None else str(source), x=x, y=y, w=w,
                                                rows=None if rows is None else [list(r) for r in rows],
                                                header=header, size=size, decimals=decimals, align=align, font=font,
                                                color=color, grid=grid, group=group, name=name, locked=locked,
                                                hidden=hidden))

    def line(self, x1: float, y1: float, x2: float, y2: float, width: float = 0.5, color: str = "#000000",
             arrow: str | None = None, dash: str | None = None, head: float = 1.6, curve: float = 0,
             tip: str = "triangle", opacity: float = 1.0, group: str | None = None,
             name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A line from (x1, y1) to (x2, y2). `arrow` is None, "end", "start" or "both"; `head` the arrowhead's
        length (mm); `tip` its shape: "triangle", "open", "stealth", "bar" or "dot"; `curve` bends the line
        (a fraction of its length; + bends to the left of its direction); `dash` None, "dashed" or "dotted";
        `width` in pt."""
        return self._add("line", "line", dict(x1=x1, y1=y1, x2=x2, y2=y2, width=width, color=color, arrow=arrow,
                                              dash=dash, head=head, curve=curve, tip=tip, opacity=opacity, group=group, name=name, locked=locked, hidden=hidden))

    def arrow(self, x1: float, y1: float, x2: float, y2: float, width: float = 0.5, color: str = "#000000",
              arrow: str = "end", dash: str | None = None, head: float = 1.6, curve: float = 0,
              tip: str = "triangle", opacity: float = 1.0, group: str | None = None,
              name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A line with an arrowhead at its end (x2, y2)."""
        e = self.line(x1, y1, x2, y2, width=width, color=color, arrow=arrow, dash=dash, head=head, curve=curve,
                      tip=tip, opacity=opacity, group=group,
                      name=name, locked=locked, hidden=hidden)
        e.method = "arrow"
        e.site = _site()
        return e

    def rect(self, x: float, y: float, w: float, h: float, stroke: str | None = "#000000", fill: str | None = None,
             width: float = 0.5, radius: float = 0, dash: str | None = None, opacity: float = 1.0,
             label=None, label_dx: float | None = None, label_dy: float | None = None,
                 label_x: float | None = None, label_y: float | None = None,
             group: str | None = None, name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A rectangle; `radius` (mm) rounds its corners. stroke/fill None: no outline / no fill. `label` "auto"
        letters it like a panel (a schematic drawn on it, say), or is a letter of its own; None (the default): no
        letter."""
        props = dict(x=x, y=y, w=w, h=h, stroke=stroke, fill=fill, width=width, radius=radius, dash=dash,
                     opacity=opacity, group=group, name=name, locked=locked, hidden=hidden)
        if label is not None:
            props.update(label=label, label_dx=label_dx, label_dy=label_dy, label_x=label_x, label_y=label_y)
        return self._add("rect", "rect", props)

    def ellipse(self, x: float, y: float, w: float, h: float, stroke: str | None = "#000000",
                fill: str | None = None, width: float = 0.5, dash: str | None = None, opacity: float = 1.0,
                group: str | None = None, name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """An ellipse in the box (x, y, w, h)."""
        return self._add("ellipse", "ellipse", dict(x=x, y=y, w=w, h=h, stroke=stroke, fill=fill, width=width,
                                                    dash=dash, opacity=opacity, group=group, name=name, locked=locked, hidden=hidden))

    def bracket(self, x1: float, x2: float, y: float, text: str = "*", tick: float = 1.2, size: float | None = None,
                width: float = 0.5, color: str = "#000000", group: str | None = None,
                name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A significance bracket from x1 to x2 at height y, its ends dropping `tick` mm, with `text` ("*",
        "**", "n.s.", "P = 0.003") centered above it."""
        return self._add("bracket", "bracket", dict(x1=x1, x2=x2, y=y, text=str(text), tick=tick, size=size,
                                                    width=width, color=color, group=group, name=name, locked=locked, hidden=hidden))

    def path(self, points: list, closed: bool = False, smooth: bool = False, stroke: str | None = "#000000",
             fill: str | None = None, width: float = 0.5, dash: str | None = None, arrow: str | None = None,
             tip: str = "triangle", head: float = 1.6, opacity: float = 1.0, group: str | None = None,
             name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A line through `points` [(x, y), ...] (mm): a polygon with closed=True, a curve through them with
        smooth=True. `arrow`, `tip` and `head` as for fig.line."""
        return self._add("path", "path", dict(points=[tuple(map(float, p)) for p in points], closed=closed,
                                              smooth=smooth, stroke=stroke, fill=fill, width=width, dash=dash,
                                              arrow=arrow, tip=tip, head=head, opacity=opacity, group=group,
                                              name=name, locked=locked, hidden=hidden))

    def qr(self, data: str, x: float = 0, y: float = 0, w: float = 30, color: str = "#000000",
           background: str | None = "#FFFFFF", level: str = "M", quiet: int = 4, group: str | None = None,
           name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A QR code for `data` (a link, usually), `w` mm square including its `quiet` border of light modules
        (4, as the standard asks; scanners need it). `level` is how much of it can be damaged and still read:
        "L" 7%, "M" 15%, "Q" 25%, "H" 30% (more makes a denser code). It stays dark on light in dark versions."""
        return self._add("qr", "qr", dict(data=str(data), x=x, y=y, w=w, color=color, background=background,
                                          level=level, quiet=quiet, group=group, name=name, locked=locked,
                                          hidden=hidden))

    def zoom(self, source, target, region: tuple = (0.35, 0.35, 0.25, 0.25), color: str = "#000000",
             width: float = 0.5, dash: str | None = None, lines: bool = True, sync: bool = True,
             group: str | None = None, name: str | None = None, locked: bool = False,
             hidden: bool = False) -> Element:
        """A zoom box: a rectangle on `source` (a panel or picture) around `region` = (x, y, w, h), fractions of
        its box, and lines from it to `target` (the panel or inset showing it magnified). With `sync`, a target
        picture of the same file is cropped to the region, so moving the box on the figure changes what the
        magnified view shows."""
        e = self._add("zoom", "zoom", dict(region=tuple(region), color=color, width=width, dash=dash, lines=lines,
                                            sync=sync, group=group, name=name, locked=locked, hidden=hidden))
        e.source, e.target = source, target
        return e

    def scalebar(self, image, length: float, unit: str = "µm", pixel_size: float = 1.0, corner: str = "lower right",
                 x: float | None = None, y: float | None = None, margin: float = 1.5, color: str = "#FFFFFF",
                 thickness: float = 0.6, size: float | None = None, label: bool = True, group: str | None = None,
                 name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A scale bar on a picture: `length` in `unit`, where one pixel of the original picture is `pixel_size`
        units (e.g. 0.325 µm). It sits in `corner` ("lower right", "lower left", "upper right", "upper left"),
        `margin` mm in, or at (x, y), fractions of the picture's box for the bar's left end. `thickness` in mm."""
        e = self._add("scalebar", "scalebar", dict(length=length, unit=unit, pixel_size=pixel_size, corner=corner,
                                                    x=x, y=y, margin=margin, color=color, thickness=thickness,
                                                    size=size, label=label, group=group, name=name, locked=locked,
                                                    hidden=hidden))
        e.host = image
        return e

    # --- patchwork-style layouts ---

    def layout(self, node: Node, x: float = 0, y: float = 0, w: float | None = None, h: float | None = None,
               gap: float = 4, widths: list | None = None, heights: list | None = None, guides: str | None = None,
               legend: str = "right", align: bool = True, group: str | None = None,
               name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """Arrange panels in the box (x, y, w, h) as `node` says, patchwork style:

            a, b, c = Panel("panels/a.py"), Panel("panels/b.py"), Panel("panels/c.py")
            fig.layout((a | b) / c, x=0, y=0, w=183, h=120, heights=[2, 1])

        `widths`/`heights` (relative) apply to the outermost Row, Column, Grid or Design; `gap` (mm) is the space
        between cells. guides="collect" takes the legends out of the panels and shows each distinct entry once,
        at the `legend` side ("right" or "bottom"). With `align`, the panels' plot areas line up across rows and
        columns. Without w or h, the layout fills the figure from (x, y)."""
        if not isinstance(node, Node):
            raise TypeError("fig.layout needs a Panel, Image or a Row/Column/Grid/Design of them")
        e = self._add("layout", "layout", dict(x=x, y=y, w=w, h=h, gap=gap,
                                                widths=list(widths) if widths else None,
                                                heights=list(heights) if heights else None, guides=guides,
                                                legend=legend, align=align, group=group, name=name, locked=locked, hidden=hidden))
        e.node = node
        return e

    def inset(self, item: Node, within, x: float = 0.55, y: float = 0.05, w: float = 0.4, h: float = 0.4,
              label=None, group: str | None = None, name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A Panel or Image on top of another panel (`within`: what fig.panel/fig.image returned, or a Panel in a
        layout). x, y, w, h are fractions of that panel's box, from its top-left corner."""
        if not isinstance(item, (Panel, Image)):
            raise TypeError("an inset is a Panel or an Image")
        item.props["label"] = label
        e = self._add("inset", "inset", dict(x=x, y=y, w=w, h=h, label=label, group=group, name=name, locked=locked, hidden=hidden))
        e.node, e.host = item, within
        return e

    def legend(self, entries: dict, x: float = 0, y: float = 0, ncol: int = 1, title: str | None = None,
               size: float | None = None, marker: str | None = None, group: str | None = None,
               name: str | None = None, locked: bool = False, hidden: bool = False) -> Element:
        """A legend of its own, shared by several panels: {"label": color or {"color", "marker", "linestyle",
        "kind": "line"|"patch"|"marker"}}. (x, y) is its top-left corner."""
        return self._add("legend", "legend", dict(entries=dict(entries), x=x, y=y, ncol=ncol, title=title,
                                                  size=size, marker=marker, group=group, name=name, locked=locked, hidden=hidden))

    # --- the caption (shown under the figure in the editor's Caption tab, never drawn on it) ---

    def caption(self, text: str):
        """The figure's caption: free text for the paper, kept with the figure but not drawn on it. The editor's
        Caption tab edits it."""
        self.caption_text = str(text)
        self.caption_site = _site()

    # --- layout aids (shown in the editor, never exported) ---

    def guide(self, x: float | None = None, y: float | None = None):
        """A guide line at x (vertical) or y (horizontal), in mm, for the editor's snapping."""
        self.guides.append({"x": x, "y": y, "site": _site()})
