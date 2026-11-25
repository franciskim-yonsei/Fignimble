"""Engine tests: canvas edits become the right source edits, undo works with edits made in between.

    PYTHONPATH=engine .venv/bin/python -m pytest tests
"""

import shutil
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

from fignimble.core import Session  # noqa: E402
from fignimble import srcedit  # noqa: E402


@pytest.fixture(scope="module")
def session(tmp_path_factory):
    d = tmp_path_factory.mktemp("proj") / "demo"
    # a fixed copy of the demo project (the one in examples/ is for people to play with)
    shutil.copytree(ROOT / "tests" / "fixtures" / "demo", d, ignore=shutil.ignore_patterns(".fignimble", "__pycache__"))
    s = Session(str(d), sys.executable)
    yield s
    s.close()


def fig_text(s):
    return (s.project / "fig1.py").read_text()


def el(res, kind, n=0):
    return [e for e in res["elements"] if e["kind"] == kind][n]


def edit(s, **a):
    r = s.edit({"figure": "fig1", **a})
    assert r["ok"], r.get("error")
    return r


def test_render(session):
    r = session.api_figure({"name": "fig1"})
    assert not r.get("error"), r.get("error")
    assert [l["text"] for l in r["labels"]] == list("abcdef")
    assert all(Path(e["sprite"]["file"]).exists() for e in r["elements"])
    assert session.api_state()["figures"][0]["name"] == "fig1"


def test_move_and_undo(session):
    before = fig_text(session)
    r = edit(session, op="move", oids=["1"], dx=3, dy=-1.5)
    assert 'fig.panel("panels/scatter.py", x=68, y=1.5, w=56, h=52)' in fig_text(session)
    assert el(r["figure"], "panel", 1)["box"][:2] == [68, 1.5]
    r = session.undo({"figure": "fig1"})
    assert r["ok"], r.get("error")
    assert fig_text(session) == before


@pytest.mark.parametrize("newline", [b"\n", b"\r\n"], ids=["lf", "crlf"])
def test_move_undo_redo_preserves_line_endings(tmp_path, newline):
    path = tmp_path / "fig1.py"
    before = newline.join([
        b"from fignimble import Figure",
        b"fig = Figure(width=100, height=80)",
        b"fig.rect(x=5, y=10, w=20, h=15)",
        b"",
    ])
    path.write_bytes(before)
    s = Session(str(tmp_path), sys.executable)
    try:
        edit(s, op="move", oids=["0"], dx=3, dy=0)
        after = before.replace(b"x=5", b"x=8")
        assert path.read_bytes() == after
        r = s.undo({"figure": "fig1"})
        assert r["ok"], r.get("error")
        assert path.read_bytes() == before
        r = s.redo_({"figure": "fig1"})
        assert r["ok"], r.get("error")
        assert path.read_bytes() == after

        # Another editor changed a different line. Undo must retain that edit.
        path.write_bytes(after.replace(b"height=80", b"height=90"))
        r = s.undo({"figure": "fig1"})
        assert r["ok"], r.get("error")
        assert path.read_bytes() == before.replace(b"height=80", b"height=90")
    finally:
        s.close()


@pytest.mark.parametrize("make, files", [
    ("new_figure", ["fig1.py"]),
    ("new_table", ["fig1.py", "tables/fig1.csv"]),
])
def test_new_figure_undo_redo(tmp_path, make, files):
    s = Session(str(tmp_path), sys.executable)
    try:
        r = getattr(s, make)({"name": "fig1"})
        assert r["ok"], r.get("error")
        paths = [tmp_path / name for name in files]
        before = [path.read_bytes() for path in paths]
        r = s.undo({})
        assert r["ok"], r.get("error")
        assert not any(path.exists() for path in paths)
        r = s.redo_({})
        assert r["ok"], r.get("error")
        assert [path.read_bytes() for path in paths] == before
    finally:
        s.close()


def test_import_panel_uses_portable_path_and_is_undoable(tmp_path):
    from fignimble.templates import new_project

    project = tmp_path / "figure project"
    new_project(str(project))
    source = tmp_path / "sample plot.py"
    source.write_text("def draw(fig):\n    fig.add_subplot().plot([0, 1], [1, 2])\n", encoding="utf-8")
    s = Session(str(project), sys.executable)
    try:
        r = s.import_file({"figure": "fig1", "path": str(source), "x": 12.5, "y": 7})
        assert r["ok"], r.get("error")
        panel = el(r["figure"], "panel")
        assert not panel.get("error")
        assert panel["box"][:2] == [12.5, 7]
        assert 'fig.panel("panels/sample plot.py"' in fig_text(s)
        copy = project / "panels" / source.name
        assert copy.read_bytes() == source.read_bytes()
        assert s.undo({"figure": "fig1"})["ok"]
        assert not copy.exists() and source.exists()
        assert s.redo_({"figure": "fig1"})["ok"]
        assert copy.read_bytes() == source.read_bytes()

        broken = tmp_path / "broken.py"
        broken.write_text("def draw(fig)\n", encoding="utf-8")
        r = s.import_file({"figure": "fig1", "path": str(broken)})
        assert not r["ok"] and "broken.py (line 1)" in r["error"]
        assert not (project / "panels" / broken.name).exists()
    finally:
        s.close()


def test_resize_image_keeps_aspect(session):
    edit(session, op="resize", oid="3", box=[4, 63, 40, 30])
    assert 'fig.image("images/cells.png", x=4, y=63, w=40)' in fig_text(session)
    session.undo({"figure": "fig1"})


def test_text_edit_and_new_kwarg(session):
    edit(session, op="set", oid="6", props={"text": "Control", "color": "#d1495b"})
    t = fig_text(session)
    assert 'fig.text("Control", x=30, y=104, size=7, weight="bold", align="center", color="#d1495b")' in t
    session.undo({"figure": "fig1"})


def test_labels_reorder_when_panels_move(session):
    # moving panel b below panel f's row makes it last in reading order
    r = edit(session, op="move", oids=["1"], dx=60, dy=60)
    names = {l["panel"]: l["text"] for l in r["figure"]["labels"]}
    assert names["0"] == "a" and names["2"] == "b" and names["1"] == "e"
    session.undo({"figure": "fig1"})


def test_label_style_goes_to_style_py(session):
    r = edit(session, op="labels", props={"format": "({})", "case": "upper"})
    style = (session.project / "style.py").read_text()
    assert 'Labels.journal("nature", dx=-3, dy=-1, format="({})", case="upper")' in style
    assert r["figure"]["labels"][0]["text"] == "(A)"
    session.undo({"figure": "fig1"})


def test_move_one_label(session):
    edit(session, op="move", oids=["L3"], dx=0, dy=-4)
    assert 'fig.image("images/cells.png", x=4, y=63, w=52, label_dy=-4)' in fig_text(session)
    session.undo({"figure": "fig1"})


def test_delete_duplicate_insert(session):
    before = fig_text(session)
    r = edit(session, op="duplicate", oids=["6"])
    assert fig_text(session).count('"Treated"') == 2
    assert r["select"] == ["7"]
    r = edit(session, op="delete", oids=["7"])
    assert fig_text(session) == before
    r = edit(session, op="insert", kind="rect", props={"x": 10, "y": 10, "w": 20, "h": 10, "stroke": "#ff0000"})
    assert 'fig.rect(x=10, y=10, w=20, h=10, stroke="#ff0000")' in fig_text(session)
    assert r["select"] == ["9"]
    for _ in range(3):
        session.undo({"figure": "fig1"})
    assert fig_text(session) == before


def test_order(session):
    before = fig_text(session)
    edit(session, op="order", oids=["0"], how="front")
    lines = [l for l in fig_text(session).splitlines() if l.startswith("fig.")]
    assert lines[-1].startswith('fig.panel("panels/expression.py", x=4')
    session.undo({"figure": "fig1"})
    assert fig_text(session) == before


def test_reorder_reports_new_selection_for_multiline_element(tmp_path):
    (tmp_path / "fig1.py").write_text(
        'from fignimble import Figure\nfig = Figure(90, 60)\n'
        'fig.rect(\n    5, 5, 30, 25,\n    fill="red",\n)\n\nfig.rect(8, 8, 20, 15, fill="blue")\n',
        encoding="utf-8",
    )
    s = Session(str(tmp_path), sys.executable)
    try:
        r = edit(s, op="order", how="front", oids=["0"])
        assert r["select"] == ["1"]
        assert r["figure"]["elements"][-1]["props"]["fill"] == "red"
    finally:
        s.close()


def test_figure_journal_drives_default_export(tmp_path):
    from PIL import Image

    (tmp_path / "fig1.py").write_text(
        'from fignimble import Figure, Style\n'
        'fig = Figure(width=25.4, height=25.4, style=Style.journal("nature"), journal="cell")\n'
        'fig.rect(5, 5, 15, 15, fill="blue")\n', encoding="utf-8",
    )
    s = Session(str(tmp_path), sys.executable)
    try:
        path = tmp_path / "fig1.tiff"
        r = s.export({"figure": "fig1", "path": str(path), "dpi": 150})
        assert r["resolution"]["journal"] == "cell"
        with Image.open(path) as im:
            assert im.info["dpi"] == (1000, 1000)
            assert im.size == (1000, 1000)
    finally:
        s.close()


def test_align_distribute(session):
    edit(session, op="align", oids=["0", "1", "2"], how="top")
    edit(session, op="align", oids=["3", "4"], how="left", to="figure")
    t = fig_text(session)
    assert 'fig.image("images/schematic.svg", x=0, y=63' in t
    session.undo({"figure": "fig1"})
    session.undo({"figure": "fig1"})


def test_figure_size(session):
    edit(session, op="figure", props={"height": 125})
    assert "fig = Figure(width=183, height=125, style=STYLE)" in fig_text(session)
    session.undo({"figure": "fig1"})


def test_computed_value_refused(session):
    p = session.project / "fig1.py"
    before = p.read_text()
    p.write_text(before.replace('fig.text("Treated", x=30,', 'fig.text("Treated", x=24 + 6,'))
    session.renders.clear()
    r = session.edit({"figure": "fig1", "op": "move", "oids": ["6"], "dx": 1, "dy": 0})
    assert not r["ok"] and "computed" in r["error"]
    p.write_text(before)


def test_undo_after_agent_edit(session):
    """An agent edits another line after our edit: undo keeps the agent's line."""
    edit(session, op="move", oids=["0"], dx=1, dy=0)
    p = session.project / "fig1.py"
    t = p.read_text().replace('fig.arrow(57, 82, 64, 82', 'fig.arrow(55, 82, 64, 82')
    p.write_text(t)
    r = session.undo({"figure": "fig1"})
    assert r["ok"]
    t = p.read_text()
    assert 'fig.panel("panels/expression.py", x=4, y=3' in t and "fig.arrow(55, 82" in t
    p.write_text(t.replace("fig.arrow(55, 82", "fig.arrow(57, 82"))


def test_guides_and_groups(session):
    before = fig_text(session)
    r = edit(session, op="guide_add", x=91.5)
    assert r["figure"]["guides"][0]["x"] == 91.5
    edit(session, op="guide_move", index=0, value=90)
    assert "fig.guide(x=90)" in fig_text(session)
    edit(session, op="group", oids=["6", "7"])
    assert fig_text(session).count('group="g1"') == 2
    edit(session, op="ungroup", oids=["6", "7"])
    edit(session, op="guide_delete", index=0)
    assert fig_text(session) == before


def test_srcedit_remove_and_multiline():
    text = 'fig.panel(\n    "a.py",\n    x=1,\n    y=2,\n    label_dx=3,\n)\n'
    src = srcedit.Source(text)
    call = src.calls("panel")[0]
    new = srcedit.apply(text, srcedit.set_args(src, call, "panel", {"label_dx": srcedit.REMOVE, "x": 5, "w": 9}))
    assert new == 'fig.panel(\n    "a.py",\n    x=5,\n    y=2, w=9,\n)\n'


def fig2_text(s):
    return (s.project / "fig2.py").read_text()


def edit2(s, **a):
    r = s.edit({"figure": "fig2", **a})
    assert r["ok"], r.get("error")
    return r


def test_layout_render_and_alignment(session):
    r = session.render("fig2")
    assert not r.get("error"), r.get("error")
    kinds = {e["oid"]: e["kind"] for e in r["elements"]}
    assert kinds["0"] == "layout" and kinds["0.legend"] == "legend" and kinds["1"] == "inset"
    assert [l["text"] for l in r["labels"]] == list("abcd")
    el = {e["oid"]: e for e in r["elements"]}
    # c and d share a row: their plot areas get the same top and bottom margins; a and c the same left margin
    c, d, a = el["0.2"]["aligned"], el["0.3"]["aligned"], el["0.0"]["aligned"]
    assert c[1] == d[1] and c[3] == d[3] and a[0] == c[0]
    assert el["0.3"]["margins"][0] > el["0.2"]["margins"][0]          # d's wide tick labels
    # the gene legend of a and d is shown once
    assert el["0.legend"]["box"][2] > 30


def test_layout_edits(session):
    before = fig2_text(session)
    edit2(session, op="move", oids=["0.1"], dx=2, dy=0)                # a panel in a layout moves the layout
    assert "x=6, y=3, w=177, h=104" in fig2_text(session)
    session.undo({"figure": "fig2"})
    r = session.render("fig2")
    col = next(i for i, d in enumerate(el2(r, "0")["dividers"]) if d["axis"] == "col")
    edit2(session, op="divider", oid="0", index=col, delta=10)
    assert "widths=[" in fig2_text(session) and "widths=[1, 1, 1.2]" not in fig2_text(session)
    session.undo({"figure": "fig2"})
    edit2(session, op="set", oid="0.3", props={"function": "zoom"})
    assert 'counts = Panel("panels/counts.py", function="zoom")' in fig2_text(session)
    session.undo({"figure": "fig2"})
    edit2(session, op="move", oids=["1"], dx=-5.16, dy=0)               # the inset, in fractions of its panel
    assert 'within=counts, x=0.44' in fig2_text(session) or 'within=counts, x=0.45' in fig2_text(session)
    session.undo({"figure": "fig2"})
    r = session.edit({"figure": "fig2", "op": "delete", "oids": ["0.2"]})
    assert not r["ok"] and "part of a layout" in r["error"]
    edit2(session, op="delete", oids=["0.legend"])                      # stops collecting the legends
    assert 'guides="collect"' not in fig2_text(session)
    session.undo({"figure": "fig2"})
    assert fig2_text(session) == before


def el2(r, oid):
    return next(e for e in r["elements"] if e["oid"] == oid)


def test_dark(session):
    r = session.render("fig1", dark=True)
    assert r["dark"] and r["background"] == "#000000"
    assert session.render("fig1", dark=False)["background"] == "#FFFFFF"


def test_make_and_release_layout(session):
    before = fig_text(session)
    r = edit(session, op="make_layout", oids=["0", "1", "2"], gap=5)
    t = fig_text(session)
    assert 'expression = Panel("panels/expression.py")' in t and 'scatter = Panel("panels/scatter.py")' in t
    assert "fig.layout(Grid(expression, scatter, heatmap, ncol=3), x=4, y=3, w=178, h=52, gap=5)" in t
    lay = next(e for e in r["figure"]["elements"] if e["kind"] == "layout")
    assert len(lay["children"]) == 3
    assert [l["text"] for l in r["figure"]["labels"]][:3] == ["a", "b", "c"]
    edit(session, op="release_layout", oid=lay["oid"])
    t = fig_text(session)
    assert "Panel(" not in t and "layout(" not in t and t.count("fig.panel(") == 4
    session.undo({"figure": "fig1"})
    session.undo({"figure": "fig1"})
    assert fig_text(session) == before


def fig3_text(s):
    return (s.project / "fig3.py").read_text()


def edit3(s, **a):
    r = s.edit({"figure": "fig3", **a})
    assert r["ok"], r.get("error")
    return r


def test_new_elements_render(session):
    r = session.render("fig3")
    assert not r.get("error"), r.get("error")
    el = {e["oid"]: e for e in r["elements"]}
    assert not any(e.get("error") for e in r["elements"]), [e.get("error") for e in r["elements"]]
    assert el["2"]["kind"] == "zoom" and el["2"]["target"] == "1"
    assert el["3"]["kind"] == "scalebar" and el["3"]["host"] == "0"
    assert el["10"]["hidden"] and "sprite" not in el["10"]
    keys = {t["key"]: t["text"] for t in el["4"]["texts"]}
    assert keys["ax0.xlabel"] == "tSNE 1" and keys["ax0.legend0"] == "cluster 1"
    assert el["5"]["box"][3] > 5                          # the long text wrapped onto two lines


def test_retype_panel_text(session):
    before = (session.project / "panels" / "scatter.py").read_text()
    # a literal in the panel's code: the code changes
    edit3(session, op="panel_text", oid="4", key="ax0.legend0", text="T cells", old="cluster 1")
    assert (session.project / "panels" / "scatter.py").read_text() == before  # f-string label: not a literal
    assert 'texts={"ax0.xlabel": "tSNE 1", "ax0.ylabel": "tSNE 2", "ax0.legend0": "T cells"}' in fig3_text(session)
    session.undo({"figure": "fig3"})
    edit3(session, op="panel_text", oid="4", key="ax0.title", text="Clusters", old=None)
    r = session.render("fig3")
    assert any(t["text"] == "Clusters" for t in next(e for e in r["elements"] if e["oid"] == "4")["texts"])
    session.undo({"figure": "fig3"})
    r = session.edit({"figure": "fig1", "op": "panel_text", "oid": "0", "key": "ax0.xlabel", "text": "Time (min)",
                      "old": "Time (h)"})
    assert r["ok"], r.get("error")
    assert 'ax.set_xlabel("Time (min)")' in (session.project / "panels" / "expression.py").read_text()
    session.undo({"figure": "fig1"})


def test_crop_rotate_flip(session):
    before = fig3_text(session)
    edit3(session, op="crop", oid="0", box=[14, 3, 70, 60])             # cut 10 mm off the left
    t = fig3_text(session)
    assert 'fig.image("images/cells.png", x=14, y=3, w=70, crop=(0.125, 0, 0, 0))' in t
    edit3(session, op="crop", oid="0", box=[4, 3, 80, 60])              # and back
    assert 'cells = fig.image("images/cells.png", x=4, y=3, w=80)' in fig3_text(session)
    edit3(session, op="rotate", oids=["9"], by=-15)
    assert 'fig.image("images/cells.png", x=160, y=60, w=21, flip="h")' in fig3_text(session)
    edit3(session, op="flip", oids=["9"], axis="h")
    assert 'fig.image("images/cells.png", x=160, y=60, w=21)' in fig3_text(session)
    for _ in range(4):
        session.undo({"figure": "fig3"})
    assert fig3_text(session) == before


def test_move_zoom_scalebar_path(session):
    before = fig3_text(session)
    edit3(session, op="move", oids=["2"], dx=8, dy=0)                    # 8 mm of an 80 mm picture
    assert "region=(0.65, 0.1, 0.25, 0.25)" in fig3_text(session)
    r = edit3(session, op="move", oids=["3"], dx=-40, dy=0)
    assert "scalebar(cells, length=50, unit=\"µm\", pixel_size=0.325, x=" in fig3_text(session)
    edit3(session, op="move", oids=["7"], dx=1, dy=1)
    assert "[(131, 61), (141, 73)" in fig3_text(session)
    for _ in range(3):
        session.undo({"figure": "fig3"})
    assert fig3_text(session) == before


def test_lock_hide_paste_style(session):
    before = fig3_text(session)
    edit3(session, op="set", oid="5", props={"locked": True})
    assert "width=78, locked=True)" in fig3_text(session)
    edit3(session, op="set", oid="5", props={"locked": False})
    edit3(session, op="paste_style", oids=["6", "7"], props={"color": "#123456", "stroke": "#123456", "width": 2,
                                                              "size": 9})
    t = fig3_text(session)
    assert 'fig.arrow(96, 80, 120, 66, width=2, curve=0.25, tip="stealth", color="#123456")' in t
    assert 'stroke="#123456"' in t
    for _ in range(3):
        session.undo({"figure": "fig3"})
    assert fig3_text(session) == before


def test_copy_paste_between_figures(session):
    before1, before3 = fig_text(session), fig3_text(session)
    c = session.copy({"figure": "fig3", "oids": ["6", "5"]})
    r = session.paste({"figure": "fig1", "code": c["code"], "dx": 0, "dy": 0})
    assert r["ok"], r.get("error")
    t = fig_text(session)
    assert "fig.arrow(96, 80, 120, 66" in t and "GFP" in t and len(r["select"]) == 2
    session.undo({"figure": "fig1"})
    c = session.copy({"figure": "fig3", "oids": ["2"]})                  # a zoom needs its pictures
    r = session.paste({"figure": "fig1", "code": c["code"]})
    assert not r["ok"] and "cells" in r["error"]
    c = session.copy({"figure": "fig2", "oids": ["0.1"]})               # a layout brings its Panel lines
    r = session.paste({"figure": "fig2", "code": c["code"], "dx": 0, "dy": 0})
    assert r["ok"], r.get("error")
    assert "expression2 = Panel(" in fig2_text(session)
    session.undo({"figure": "fig2"})
    assert fig_text(session) == before1 and fig3_text(session) == before3


def test_undo_to_and_exports(session, tmp_path):
    n = len(session.history)
    edit3(session, op="move", oids=["5"], dx=1, dy=0)
    edit3(session, op="move", oids=["5"], dx=1, dy=0)
    assert session.api_state()["history"][-1] == "Move"
    session.undo_to({"n": n, "figure": "fig3"})
    assert len(session.history) == n and len(session.redo) == 2
    session.undo_to({"n": n + 2, "figure": "fig3"})
    assert len(session.history) == n + 2
    session.undo_to({"n": n, "figure": "fig3"})
    for fmt, extra in (("eps", {}), ("pdf", {"cmyk": True, "outline": True}), ("tiff", {"cmyk": True})):
        out = tmp_path / f"f.{fmt}"
        r = session.export({"figure": "fig3", "path": str(out), "format": fmt, "dpi": 150, **extra})
        assert r["ok"] and out.stat().st_size > 1000
    from PIL import Image
    assert Image.open(tmp_path / "f.tiff").mode == "CMYK"


def test_add_scalebar_and_zoom(session):
    before = fig_text(session)
    r = edit(session, op="add_scalebar", oid="3", props={"length": 20, "pixel_size": 0.5})
    t = fig_text(session)
    assert 'cells = fig.image("images/cells.png"' in t and "fig.scalebar(cells, length=20, unit=\"µm\", pixel_size=0.5)" in t
    assert any(e["kind"] == "scalebar" for e in r["figure"]["elements"])
    r = edit(session, op="add_zoom", oids=["3"])
    t = fig_text(session)
    assert 'cells_zoom = fig.inset(Image("images/cells.png"), within=cells' in t and "fig.zoom(cells, cells_zoom" in t
    z = next(e for e in r["figure"]["elements"] if e["kind"] == "zoom")
    assert not z.get("error"), z.get("error")
    session.undo({"figure": "fig1"})
    session.undo({"figure": "fig1"})
    assert fig_text(session) == before


def test_panel_text_only_for_panels(session):
    r = session.edit({"figure": "fig1", "op": "panel_text", "oid": "3", "key": "ax0.xlabel", "text": "x", "old": ""})
    assert not r["ok"] and "only the texts of a panel" in r["error"]


def test_palettes_and_color_vision(session):
    r = session.render("fig1")
    assert any("deuteranopia" in w["message"] for w in r["warnings"])          # matplotlib's default red and green
    edit(session, op="style", props={"palette": "okabe-ito"})
    assert 'palette="okabe-ito"' in (session.project / "style.py").read_text()
    r = session.render("fig1")
    assert r["style"]["palette"][0] == ["0", "#E69F00"]
    assert not any("look alike" in w["message"] and w["oid"] == "0" for w in r["warnings"])
    session.undo({"figure": "fig1"})


def test_recolor_panel_marks(session):
    r = session.render("fig1")
    bars = next(e for e in r["elements"] if e["oid"] == "5")
    red = [m["key"] for m in bars["marks"] if m["color"] == "#D1495B"]
    assert red == ["ax0.patch2", "ax0.patch3"]
    # the color is written twice in the code (two bars): an override for those marks
    edit(session, op="panel_color", oid="5", keys=red, color="#0072B2", old="#D1495B")
    t = fig_text(session)
    assert 'colors={"ax0.patch2": "#0072B2", "ax0.patch3": "#0072B2"}' in t
    r = session.render("fig1")
    assert sum(m["color"] == "#0072B2" for m in next(e for e in r["elements"] if e["oid"] == "5")["marks"]) == 2
    session.undo({"figure": "fig1"})
    # written once in the panel's code: the code changes
    r = session.edit({"figure": "fig3", "op": "panel_color", "oid": "4", "keys": ["ax0.coll1"], "color": "#000000",
                      "old": "#D1495B"})
    assert r["ok"], r.get("error")
    assert '"#000000", "#edae49"' in (session.project / "panels" / "scatter.py").read_text()
    session.undo({"figure": "fig3"})


def test_restyle_panel_text(session):
    expr = session.project / "panels" / "expression.py"
    before = expr.read_text()
    r = session.render("fig1")
    xl = next(t for t in next(e for e in r["elements"] if e["oid"] == "0")["texts"] if t["key"] == "ax0.xlabel")
    assert xl["color"] and xl["bold"] is False and xl["italic"] is False
    # a literal of one ax.set_xlabel(...) in the panel's code: that call gets the settings
    edit(session, op="panel_text_style", oid="0", key="ax0.xlabel", text="Time (h)",
         props={"size": 8, "color": "#D55E00", "weight": "bold"})
    assert 'ax.set_xlabel("Time (h)", fontsize=8, color="#D55E00", fontweight="bold")' in expr.read_text()
    r = session.render("fig1")
    xl = next(t for t in next(e for e in r["elements"] if e["oid"] == "0")["texts"] if t["key"] == "ax0.xlabel")
    assert xl["size"] == 8 and xl["color"] == "#D55E00" and xl["bold"]
    session.undo({"figure": "fig1"})
    assert expr.read_text() == before
    # a text the code doesn't hold as a literal (a legend entry from an f-string): the panel's text_style
    edit3(session, op="panel_text_style", oid="4", key="ax0.legend0", text="cluster 1",
          props={"color": "#0072B2", "style": "italic"})
    assert '"ax0.legend0": {"color": "#0072B2", "style": "italic"}' in fig3_text(session)
    r = session.render("fig3")
    lg = next(t for t in next(e for e in r["elements"] if e["oid"] == "4")["texts"] if t["key"] == "ax0.legend0")
    assert lg["color"] == "#0072B2" and lg["italic"]
    # a text the panel already retypes (texts=) is restyled beside it
    edit3(session, op="panel_text_style", oid="4", key="ax0.xlabel", text="tSNE 1", props={"size": 9})
    assert '"ax0.xlabel": {"size": 9}' in fig3_text(session)
    # Reset drops both the retyping and the restyling of that text
    edit3(session, op="panel_text_reset", oid="4", key="ax0.xlabel")
    t = fig3_text(session)
    assert '"ax0.xlabel"' not in t and '"ax0.legend0": {"color"' in t
    session.undo({"figure": "fig3"})
    session.undo({"figure": "fig3"})
    session.undo({"figure": "fig3"})
    assert "text_style" not in fig3_text(session)


def test_export_meets_journal_resolution(session, tmp_path):
    from PIL import Image
    # a raster export below what the journal asks is raised to it, and says so
    r = session.export({"figure": "fig1", "path": str(tmp_path / "f.tiff"), "dpi": 300, "journal": "cell"})
    rz = r["resolution"]
    assert rz["kind"] == "combination" and rz["dpi"] == 500 and rz["ok"] and "raised" in rz["notes"][0]
    with Image.open(tmp_path / "f.tiff") as im:
        assert round(im.info["dpi"][0]) == 500 and im.mode == "RGB"
        assert abs(im.size[0] / (183 / 25.4) - 500) < 1
    # a figure with no pictures is line art
    r = session.export({"figure": "fig1", "path": str(tmp_path / "f.jpg"), "dpi": 600})
    assert r["resolution"]["dpi"] == 600
    # PDF: the pictures inside are measured, and one printed too large for its pixels is reported
    r = session.export({"figure": "fig1", "path": str(tmp_path / "f.pdf")})
    assert r["resolution"]["ok"] and r["resolution"]["pictures"] >= 300
    r = session.export({"figure": "fig3", "path": str(tmp_path / "f3.pdf")})
    assert not r["resolution"]["ok"] and any("dpi as printed" in w for w in r["warnings"])
    if shutil.which("gs") or Path("/usr/local/bin/gs").exists() or Path("/opt/homebrew/bin/gs").exists():
        r = session.export({"figure": "fig1", "path": str(tmp_path / "f.eps")})
        assert r["resolution"]["ok"] and r["resolution"]["pictures"] >= 1200 * 0.98


def test_tables(session, tmp_path):
    import zipfile
    from fignimble import tables
    csv = tmp_path / "genes.csv"
    csv.write_text("Gene,Cells,Fold change,p\nCD3E,1204,2.4512,0.0012\nLYZ,2950,1.10,0.048\n")
    # a dropped CSV is copied into tables/ and becomes a table
    r = session.import_file({"figure": "fig1", "path": str(csv), "x": 100, "y": 90})
    assert r["ok"], r.get("error")
    assert 'fig.table("tables/genes.csv", x=100, y=90)' in fig_text(session)
    t = next(e for e in r["figure"]["elements"] if e["kind"] == "table")
    assert t["box"][2] > 20 and t["box"][3] > 5 and "sprite" in t
    edit(session, op="set", oid=t["oid"], props={"decimals": [None, None, 2, 3]})
    assert "decimals=[None, None, 2, 3]" in fig_text(session)
    edit(session, op="resize", oid=t["oid"], box=[100, 90, 70, 10])
    r = session.render("fig1")
    assert abs(next(e for e in r["elements"] if e["oid"] == t["oid"])["box"][2] - 70) < 0.01
    shown, align = tables.cells(tables.read_rows(csv), True, [None, None, 2, 3])
    assert shown[1] == ["CD3E", "1204", "2.45", "0.001"] and align == ["l", "r", "r", "r"]
    # as a Word file: a real table in the figure's font, header bold, numbers right
    out = session.export_table({"figure": "fig1", "oid": t["oid"], "path": str(tmp_path / "t.docx")})
    assert out["ok"], out.get("error")
    x = zipfile.ZipFile(out["path"]).read("word/document.xml").decode()
    assert x.count("<w:tr>") == 3 and x.count("<w:tc>") == 12 and "<w:tblGrid>" in x
    assert "2.45" in x and "0.001" in x and 'w:ascii="Arial"' in x and "<w:b/>" in x and 'w:val="right"' in x
    assert session.export_table({"figure": "fig1", "oid": "0", "path": str(tmp_path / "x.docx")})["ok"] is False
    for _ in range(3):
        session.undo({"figure": "fig1"})


def test_no_panel_letters(session):
    edit(session, op="labels", props={"numbering": "none"})
    assert session.render("fig1")["labels"] == []
    # a panel's own label (fig3's inset has label="b") goes too
    assert session.render("fig3")["labels"] == []
    session.undo({"figure": "fig1"})
    assert len(session.render("fig1")["labels"]) == 6
    assert "b" in [l["text"] for l in session.render("fig3")["labels"]]


def test_pdf_resolution_is_that_of_panel_rasters(session, tmp_path):
    import pymupdf

    def heatmap_dpi(path, box):
        x, y, w, h = (v * 72 / 25.4 for v in box)
        with pymupdf.open(path) as d:
            return max((i["width"] / ((i["bbox"][2] - i["bbox"][0]) / 72) for i in d[0].get_image_info()
                        if x - 1 <= i["bbox"][0] and i["bbox"][2] <= x + w + 1 and y - 1 <= i["bbox"][1] <= y + h), default=0)
    box = next(e for e in session.render("fig1")["elements"] if e["oid"] == "2")["box"]       # the heatmap panel
    r = session.export({"figure": "fig1", "path": str(tmp_path / "hi.pdf"), "dpi": 1200})
    assert heatmap_dpi(tmp_path / "hi.pdf", box) > 1150
    r = session.export({"figure": "fig1", "path": str(tmp_path / "lo.pdf"), "dpi": 150})
    assert "raised to 300" in r["resolution"]["notes"][0]
    assert 290 < heatmap_dpi(tmp_path / "lo.pdf", box) < 320


def test_panel_from_ssh_server(session, tmp_path, monkeypatch):
    # a stand-in for ssh: runs the remote command in a shell here, with a home folder of its own
    home = tmp_path / "server home"
    (home / "analysis" / "old stuff").mkdir(parents=True)
    (home / "analysis" / "umap panel.py").write_text("def draw(fig):\n    ax = fig.add_subplot()\n    ax.plot([0, 1], [1, 0])\n")
    (home / "analysis" / "data.csv").write_text("x\n1\n")
    fake = tmp_path / "fake_ssh"
    fake.write_text(f'#!/bin/bash\nwhile [ "$1" != "--" ]; do shift; done; shift\nexport HOME="{home}"\ncd "$HOME"\n'
                    'exec /bin/bash -c "$*"\n')
    fake.chmod(0o755)
    monkeypatch.setenv("FIGNIMBLE_SSH", str(fake))
    r = session.remote_list({"host": "lab-server"})
    assert r["ok"], r.get("error")
    assert r["path"] == str(home) and {"name": "analysis", "dir": True} in r["entries"]
    r = session.remote_list({"host": "lab-server", "path": "~/analysis"})
    assert [e["name"] for e in r["entries"]] == ["old stuff", "umap panel.py"]       # folders, then .py files
    r = session.import_remote({"figure": "fig1", "host": "lab-server", "path": "~/analysis/umap panel.py", "x": 10, "y": 70})
    assert r["ok"], r.get("error")
    text = Path(r["file"]).read_text()
    assert text.startswith("# Fetched from lab-server:~/analysis/umap panel.py by Fignimble")
    assert 'fig.panel("panels/umap panel.py", x=10, y=70' in fig_text(session)
    assert not any(e.get("error") for e in r["figure"]["elements"])
    # changed on the server: Update from Server takes the new code (one undo step)
    (home / "analysis" / "umap panel.py").write_text("def draw(fig):\n    fig.add_subplot().bar([0, 1], [2, 3])\n")
    r = session.update_remote({"file": "panels/umap panel.py", "figure": "fig1"})
    assert r["ok"], r.get("error")
    assert "bar(" in Path(session.project / "panels" / "umap panel.py").read_text()
    session.undo({"figure": "fig1"})
    assert "plot(" in Path(session.project / "panels" / "umap panel.py").read_text()
    bad = session.import_remote({"figure": "fig1", "host": "lab-server", "path": "~/analysis/missing.py", "x": 0, "y": 0})
    assert not bad["ok"] and "couldn't fetch" in bad["error"]
    assert not session.remote_list({"host": "-oProxyCommand=x"})["ok"]
    session.undo({"figure": "fig1"})


def test_undo_takes_away_files_made_for_an_insert(session, tmp_path):
    csv = tmp_path / "counts.csv"
    csv.write_text("a,b\n1,2\n")
    r = session.import_file({"figure": "fig1", "path": str(csv), "x": 5, "y": 90})
    assert r["ok"], r.get("error")
    copied = session.project / "tables" / "counts.csv"
    assert copied.exists()
    session.undo({"figure": "fig1"})
    assert not copied.exists() and "counts.csv" not in fig_text(session)
    session.redo_({"figure": "fig1"})
    assert copied.exists() and "counts.csv" in fig_text(session)
    session.undo({"figure": "fig1"})
    # a picture made just for the insert (a BioCartoon icon, already in images/) goes with it too
    icon = session.project / "images" / "icon_test.png"
    shutil.copy(session.project / "images" / "cells.png", icon)
    r = session.import_file({"figure": "fig1", "path": str(icon), "x": 5, "y": 5, "w": 10, "created": True})
    assert r["ok"], r.get("error")
    session.undo({"figure": "fig1"})
    assert not icon.exists()
    # a picture of the project's own is never removed
    r = session.import_file({"figure": "fig1", "path": str(session.project / "images" / "cells.png"), "x": 5, "y": 5})
    session.undo({"figure": "fig1"})
    assert (session.project / "images" / "cells.png").exists()


def test_journal_table_is_complete():
    from fignimble import api, journals
    assert len(journals.JOURNALS) >= 40
    for k, j in journals.JOURNALS.items():
        for field in ("name", "publisher", "widths", "font", "font_size", "min_font", "dpi", "labels", "checked"):
            assert field in j, (k, field)
        assert set(j["dpi"]) == {"photo", "combination", "line"}, k
        assert j["dpi"]["photo"] <= j["dpi"]["combination"] <= j["dpi"]["line"], k
        assert j["checked"] in ("guidelines", "secondary", "publisher"), k
        assert 0 < journals.width(k, "single") <= journals.width(k, "double") <= 200, k
        api.Style.journal(k)                         # every one makes a style


def test_new_project_is_empty(tmp_path):
    from fignimble import templates
    d = tmp_path / "fresh"
    templates.new_project(str(d), "nature")
    assert sorted(p.name for p in (d / "panels").iterdir()) == []
    s = Session(str(d), sys.executable)
    try:
        r = s.api_figure({"name": "fig1"})
        assert not r.get("error"), r.get("error")
        assert r["elements"] == [] and r["labels"] == []
        assert s.api_state()["panels"] == []
    finally:
        s.close()


def _render(tmp_path, figure: str, files: dict | None = None) -> dict:
    """Render a small project made of `figure` (fig.py) and `files`, in this process."""
    import os
    from fignimble.render import Renderer
    for name, text in {"fig.py": "from fignimble import *\n" + figure, **(files or {})}.items():
        p = tmp_path / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text) if isinstance(text, str) else p.write_bytes(text)
    cwd = os.getcwd()
    try:
        return Renderer(str(tmp_path)).render(str(tmp_path / "fig.py"), sprites=False)
    finally:
        os.chdir(cwd)


def _pdf(tmp_path) -> str:
    """fig.py of the project in tmp_path exported as PDF."""
    import os
    from fignimble.render import Renderer
    cwd = os.getcwd()
    try:
        Renderer(str(tmp_path)).export(str(tmp_path / "fig.py"), str(tmp_path / "out.pdf"), "pdf")
    finally:
        os.chdir(cwd)
    return str(tmp_path / "out.pdf")


PANEL = "def draw(fig):\n    fig.add_subplot().plot([0, 1], [0, 1])\n"


def test_failed_picture_keeps_its_letter(tmp_path):
    r = _render(tmp_path, 'fig = Figure(width=120, height=40)\n'
                          'fig.panel("p.py", x=0, y=0, w=35, h=35)\n'
                          'fig.image("missing.png", x=40, y=0, w=35)\n'
                          'fig.panel("p.py", x=80, y=0, w=35, h=35)\n', {"p.py": PANEL})
    assert {l["panel"]: l["text"] for l in r["labels"]} == {"0": "a", "1": "b", "2": "c"}
    assert any(w["level"] == "error" for w in r["warnings"])


def test_vector_picture_mirrors(tmp_path):
    import pymupdf
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="50">'
           '<rect x="0" y="0" width="30" height="50" fill="black"/></svg>')
    r = _render(tmp_path, 'fig = Figure(width=60, height=30)\n'
                          'fig.image("s.svg", x=0, y=0, w=60, flip="h", label=None)\n', {"s.svg": svg})
    assert not any(w["level"] == "error" for w in r["warnings"]), r["warnings"]
    page = pymupdf.open(_pdf(tmp_path))[0]
    pix = page.get_pixmap(dpi=72)
    left, right = pix.pixel(5, pix.height // 2), pix.pixel(pix.width - 5, pix.height // 2)
    assert sum(left[:3]) > 600 and sum(right[:3]) < 100           # the black bar moved to the right


def test_superscripts_are_not_small_text(tmp_path):
    ok = ('def draw(fig):\n    ax = fig.add_subplot()\n    ax.set_xticks([0, 1])\n'
          '    ax.set_xticklabels(["Agg$^{+}$", "CD8$_{a}$"])\n')
    small = 'def draw(fig):\n    ax = fig.add_subplot()\n    ax.set_xlabel("tiny", fontsize=4.5)\n'
    r = _render(tmp_path, 'fig = Figure(width=90, height=40, style=Style(min_font=5))\n'
                          'fig.panel("ok.py", x=2, y=2, w=40, h=35)\n'
                          'fig.panel("small.py", x=48, y=2, w=40, h=35)\n', {"ok.py": ok, "small.py": small})
    small_text = [w["message"] for w in r["warnings"] if "small" in w["message"] or "below" in w["message"]]
    assert small_text == ["panel b: text as small as 4.5 pt (minimum 5 pt)"], small_text


def test_panel_math_in_the_style_font(tmp_path):
    import pymupdf
    panel = 'def draw(fig):\n    fig.add_subplot().set_xlabel("10$^{3}$ cells")\n'
    r = _render(tmp_path, 'fig = Figure(width=50, height=40, style=Style(font="Arial"))\n'
                          'fig.panel("p.py", x=2, y=2, w=45, h=35)\n', {"p.py": panel})
    fonts = {f[3].split("+")[-1] for f in pymupdf.open(_pdf(tmp_path))[0].get_fonts(full=True)}
    assert not any("DejaVu" in f for f in fonts), fonts


def test_rectangle_carries_a_letter(tmp_path):
    r = _render(tmp_path, 'fig = Figure(width=120, height=40)\n'
                          'fig.panel("p.py", x=0, y=0, w=35, h=35)\n'
                          'fig.rect(40, 0, 35, 35, fill="#eeeeee", label="auto")\n'
                          'fig.rect(40, 5, 10, 10)\n'
                          'fig.panel("p.py", x=80, y=0, w=35, h=35)\n', {"p.py": PANEL})
    assert {l["panel"]: l["text"] for l in r["labels"]} == {"0": "a", "1": "b", "3": "c"}


def test_cached_runs_once(tmp_path):
    import os
    from fignimble import api
    (tmp_path / "data.csv").write_text("1\n")
    calls = tmp_path / "calls.txt"
    cwd = os.getcwd()
    os.chdir(tmp_path)
    try:
        @api.cached("data.csv")
        def load(n):
            with open(calls, "a") as f:
                f.write("x")
            return int(open("data.csv").read()) * n
        assert load(2) == 2 and load(2) == 2
        api._CACHE.clear()                       # a new process: from disk
        assert load(2) == 2 and calls.read_text() == "x"
        (tmp_path / "data.csv").write_text("5\n")
        assert load(2) == 10 and calls.read_text() == "xx"
    finally:
        os.chdir(cwd)


def test_panel_warnings_are_kept_not_printed(tmp_path, capfd):
    panel = 'import warnings\ndef draw(fig):\n    warnings.warn("old way", FutureWarning)\n    fig.add_subplot()\n'
    r = _render(tmp_path, 'fig = Figure(width=50, height=40)\nfig.panel("p.py", x=2, y=2, w=45, h=35)\n',
                {"p.py": panel})
    assert r["elements"][0]["pyWarnings"] == ["FutureWarning: old way"]
    assert "old way" not in capfd.readouterr().err


def test_project_requirements(tmp_path):
    from fignimble import requirements
    (tmp_path / "requirements.txt").write_text("matplotlib  # there\nno-such-package-fignimble-test>=1\n")
    got = requirements.missing(str(tmp_path))
    assert got["project"] == [{"requirement": "no-such-package-fignimble-test>=1", "why": "not installed"}]
    assert got["file"].endswith("requirements.txt")
    assert requirements.parse("# c\n-r other.txt\npandas>=2 # x\n\n") == ["pandas>=2"]


def test_svg_style_sheet_is_drawn(tmp_path):
    import pymupdf
    svg = ('<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20"><style type="text/css">'
           '.st0{fill:#FF0000;} .st1{fill:none;stroke:#0000FF;}</style>'
           '<rect class="st0" x="0" y="0" width="20" height="20"/>'
           '<rect class="st1" x="20" y="0" width="20" height="20" fill="#00FF00"/></svg>')
    r = _render(tmp_path, 'fig = Figure(width=40, height=20)\nfig.image("s.svg", x=0, y=0, w=40, label=None)\n',
                {"s.svg": svg})
    assert not r["warnings"], r["warnings"]
    pix = pymupdf.open(_pdf(tmp_path))[0].get_pixmap(dpi=72)
    assert pix.pixel(pix.width // 4, pix.height // 2)[:3] == (255, 0, 0)          # the sheet's red, not black
    assert pix.pixel(pix.width * 3 // 4, pix.height // 2)[:3] == (255, 255, 255)  # fill:none beats fill="#00FF00"


def test_label_unlinked_from_panel(session):
    s = session
    r = s.render("fig1")
    lab = r["labels"][0]
    panel = lab["panel"]
    edit(s, op="label_detach", oid=lab["oid"], detach=True)
    assert "label_x=" in fig_text(s)
    # the panel moves, its unlinked label stays
    r = edit(s, op="move", oids=[panel], dx=5, dy=0)
    l2 = next(l for l in r["figure"]["labels"] if l["oid"] == lab["oid"])
    assert l2["detached"] and l2["box"][:2] == lab["box"][:2]
    # the label moves alone
    r = edit(s, op="move", oids=[lab["oid"]], dx=2, dy=1)
    l3 = next(l for l in r["figure"]["labels"] if l["oid"] == lab["oid"])
    assert abs(l3["box"][0] - lab["box"][0] - 2) < 0.01 and abs(l3["box"][1] - lab["box"][1] - 1) < 0.01
    # linked again it keeps its place, and follows the panel
    r = edit(s, op="label_detach", oid=lab["oid"], detach=False)
    l4 = next(l for l in r["figure"]["labels"] if l["oid"] == lab["oid"])
    assert not l4["detached"] and "label_x=" not in fig_text(s)
    r = edit(s, op="move", oids=[panel], dx=-5, dy=0)
    l5 = next(l for l in r["figure"]["labels"] if l["oid"] == lab["oid"])
    assert abs(l5["box"][0] - (l4["box"][0] - 5)) < 0.01
    for _ in range(5):
        s.undo({"figure": "fig1"})


def test_caption(session):
    s = session
    r = edit(s, op="caption", text="Figure 1 | Expression.\nSecond line.")
    assert r["figure"]["caption"]["text"] == "Figure 1 | Expression.\nSecond line."
    assert 'fig.caption("""Figure 1' in fig_text(s)
    r = edit(s, op="caption", text="Shorter")
    assert r["figure"]["caption"]["text"] == "Shorter" and fig_text(s).count("fig.caption(") == 1
    r = edit(s, op="caption", text="")
    assert r["figure"]["caption"]["text"] is None and "fig.caption(" not in fig_text(s)
    for _ in range(3):
        s.undo({"figure": "fig1"})


def test_new_table_cells_and_exports(session, tmp_path):
    import zipfile
    s = session
    t = s.new_table({"name": "Table 1", "rows": 3, "cols": 2})
    assert t["ok"]
    name = t["name"]
    r = s.render(name)
    tb = next(e for e in r["elements"] if e["kind"] == "table")
    assert tb["props"]["cells"] == [["", ""]] * 3                # no placeholder text
    assert tb["props"]["grid"] is True                           # lines between all the cells
    assert r["size"][0] >= tb["box"][0] + tb["box"][2]          # the figure is the table's size
    rows = [["Gene", "n"], ["CD3E", "12"], ["GAPDH", "300"], ["ACTB", "7"]]
    r = s.edit({"figure": name, "op": "table_rows", "oid": tb["oid"], "rows": rows})
    assert r["ok"], r.get("error")
    assert (s.project / "tables" / f"{name}.csv").read_text().splitlines()[3] == "ACTB,7"
    r = s.edit({"figure": name, "op": "fit_figure", "margin": 3})
    tb = next(e for e in r["figure"]["elements"] if e["kind"] == "table")
    assert abs(r["figure"]["size"][1] - (tb["box"][1] + tb["box"][3] + 3)) < 0.2
    for fmt in ("docx", "xlsx", "csv", "tsv", "png", "pptx"):
        res = s.export({"figure": name, "path": str(tmp_path / f"t.{fmt}"), "dpi": 300})
        assert res["ok"] and (tmp_path / f"t.{fmt}").exists(), fmt
    assert (tmp_path / "t.tsv").read_text().splitlines()[1] == "CD3E\t12"
    with zipfile.ZipFile(tmp_path / "t.docx") as z:
        assert "w:insideV" in z.read("word/document.xml").decode()
    with zipfile.ZipFile(tmp_path / "t.xlsx") as z:
        assert "<v>300</v>" in z.read("xl/worksheets/sheet1.xml").decode()
    with zipfile.ZipFile(tmp_path / "t.pptx") as z:
        names = z.namelist()
        assert "ppt/media/image1.png" in names and "ppt/media/image2.svg" in names
        assert "svgBlip" in z.read("ppt/slides/slide1.xml").decode()


def test_clear_guides(session):
    s = session
    edit(s, op="guide_add", x=40)
    edit(s, op="guide_add", y=30)
    r = edit(s, op="guide_clear")
    assert r["figure"]["guides"] == [] and ".guide(" not in fig_text(s)
    for _ in range(3):
        s.undo({"figure": "fig1"})
