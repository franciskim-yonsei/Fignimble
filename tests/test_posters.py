"""Figures placed in figures, posters, text boxes laid out in blocks, QR codes, and what exports record.

    PYTHONPATH=engine .venv/bin/python -m pytest tests/test_posters.py
"""

import hashlib
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

import pymupdf
import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

from fignimble import cli, journals, qr, templates  # noqa: E402
from fignimble.core import Session  # noqa: E402
from fignimble.render import Renderer, is_flowing, read_provenance  # noqa: E402


@pytest.fixture()
def project(tmp_path):
    d = tmp_path / "demo"
    shutil.copytree(ROOT / "tests" / "fixtures" / "demo", d, ignore=shutil.ignore_patterns(".fignimble", "__pycache__"))
    return d


def write(project, name, body):
    (project / name).write_text("from fignimble import *\n\n" + body, encoding="utf-8")


def render(project, name, **kw):
    # absolute: a Renderer works in its project, and a bare name would mean a file in the folder it is in
    res = Renderer(str(project)).render(str(project / name), px_per_mm=2, **kw)
    assert not res.get("error"), res.get("error")
    return res


def element(res, kind, n=0):
    return [e for e in res["elements"] if e["kind"] == kind][n]


def words(pdf: str) -> list[str]:
    with pymupdf.open(pdf) as d:
        return [w[4] for w in d[0].get_text("words")]


# --- figures placed whole ---

def test_figure_placed_whole_and_scaled(project):
    write(project, "poster.py", 'fig = Figure(width=400, height=300)\n'
                                'f = fig.figure("fig1.py", x=10, y=10, w=366)\n'
                                'fig.text("beside", x=f.right + 2, y=f.top)\n')
    res = render(project, "poster.py")
    f = element(res, "figure")
    assert f["natural"] == [183.0, 118.0]
    assert f["scale"] == 2.0 and f["box"] == [10, 10, 366, 236]
    assert f["figure"] == "fig1" and Path(f["code"]["file"]).name == "fig1.py"
    assert element(res, "text")["box"][:2] == [378, 10]            # Element.box of a placed figure
    assert res["labels"] == []                                    # not lettered here unless asked
    # its texts are printed twice as large, and checked at that size: the 7 pt figure's smallest text is now 12 pt
    assert not any("small" in w["message"] for w in res["warnings"])


def test_figure_letters_label_and_style(project):
    write(project, "poster.py", 'fig = Figure(width=400, height=300)\n'
                                'fig.figure("fig1.py", x=10, y=10, w=183, letters=False)\n'
                                'fig.figure("fig1.py", x=200, y=10, w=183, label="auto")\n'
                                'fig.figure("fig1.py", x=10, y=150, w=183, style=Style(font="Arial", size=9))\n')
    r = Renderer(str(project))
    res = r.render(str(project / "poster.py"), px_per_mm=2)
    figs = [e for e in res["elements"] if e["kind"] == "figure"]
    pieces = {pc.oid: pc for pc in r.pieces(r.run_figure(str(project / "poster.py")))[0]}
    sprite_pdfs = [pieces[e["oid"]].meta["pdf"] for e in figs]
    letters = lambda pdf: [w for w in words(pdf) if re.fullmatch(r"[a-f]", w)]    # noqa: E731
    assert letters(sprite_pdfs[0]) == []
    assert letters(sprite_pdfs[1]) == list("abcdef")
    assert sprite_pdfs[2] != sprite_pdfs[1]                        # drawn again with the other style
    assert [l["text"] for l in res["labels"]] == ["a"] and res["labels"][0]["panel"] == figs[1]["oid"]


def test_figure_errors(project):
    write(project, "broken.py", 'fig = Figure(width=100, height=60)\nfig.panel("panels/nope.py", x=0, y=0, w=50, h=50)\n')
    write(project, "poster.py", 'fig = Figure(width=400, height=300)\n'
                                'fig.figure("poster.py", x=10, y=10, w=50)\n'
                                'fig.figure("broken.py", x=100, y=10, w=100)\n'
                                'fig.figure("../elsewhere.py", x=200, y=10, w=100)\n')
    res = render(project, "poster.py")
    a, b, c = [e for e in res["elements"] if e["kind"] == "figure"]
    assert "can't be placed in itself" in a["error"]
    assert "error" not in b                                        # it is drawn, with its failed panel inside
    assert "outside the project" in c["error"]
    errs = [w["message"] for w in res["warnings"] if w["level"] == "error"]
    assert any(m.startswith("figure broken.py: panel a failed: can't read panel code panels/nope.py") for m in errs)


def test_place_move_and_resize_figure(project):
    write(project, "poster.py", 'fig = Figure(width=400, height=300)\n')
    s = Session(str(project), sys.executable)
    try:
        r = s.import_file({"figure": "poster", "path": str(project / "fig2.py"), "x": 10, "y": 20})
        assert r["ok"], r.get("error")
        assert 'fig.figure("fig2.py", x=10, y=20)' in (project / "poster.py").read_text()
        fig = r["figure"]
        oid = element(fig, "figure")["oid"]
        r = s.edit({"figure": "poster", "op": "move", "oids": [oid], "dx": 5, "dy": 0})
        assert 'fig.figure("fig2.py", x=15, y=20)' in (project / "poster.py").read_text()
        r = s.edit({"figure": "poster", "op": "resize", "oid": oid, "box": [15, 20, 366, 220]})
        assert r["ok"], r.get("error")
        assert 'fig.figure("fig2.py", x=15, y=20, w=366)' in (project / "poster.py").read_text()
        assert element(r["figure"], "figure")["scale"] == 2.0
        s.undo({"figure": "poster"})
        assert 'w=366' not in (project / "poster.py").read_text()
        bad = s.import_file({"figure": "poster", "path": str(project / "poster.py"), "x": 0, "y": 0})
        assert not bad["ok"] and "itself" in bad["error"]
        # a figure larger than the room left is placed as wide as the room
        write(project, "small.py", 'fig = Figure(width=100, height=60)\n')
        r = s.import_file({"figure": "small", "path": str(project / "fig1.py"), "x": 5, "y": 5})
        assert 'fig.figure("fig1.py", x=5, y=5, w=90)' in (project / "small.py").read_text()
    finally:
        s.close()


# --- posters ---

def test_poster_presets_and_style(project):
    write(project, "poster.py", 'fig = Figure(poster="a1")\nfig.text("near the edge", x=5, y=100)\n'
                                'fig.rect(-3, -3, 600, 100, stroke=None, fill="#0B3C5D")\n')
    res = render(project, "poster.py")
    assert res["size"] == [594, 841] and res["poster"]["key"] == "a1" and res["poster"]["margin"] == 15
    assert res["style"]["props"]["size"] == 22 and res["style"]["props"]["min_font"] == 14
    assert res["journal"] is None
    msgs = [w["message"] for w in res["warnings"]]
    assert any("near the edge" in m and "15 mm margin" in m for m in msgs)
    assert not any("rect" in m for m in msgs)                      # a background band may run off the edge
    write(project, "land.py", 'fig = Figure(poster="36x48", landscape=True)\n')
    assert render(project, "land.py")["size"] == [1219.2, 914.4]
    with pytest.raises(ValueError):
        journals.poster("b5")


def test_poster_template_and_new_project(project, tmp_path):
    s = Session(str(project), sys.executable)
    try:
        r = s.new_figure({"poster": "a0"})
        assert r["ok"] and r["name"] == "poster"
        res = s.render("poster", 2.0)
        assert not res.get("error") and res["size"] == [841, 1189]
        assert any(e["kind"] == "figure" and e["props"]["source"] == "fig1.py" for e in res["elements"])
        assert any(e["kind"] == "qr" for e in res["elements"])
        assert [e for e in res["elements"] if e["kind"] == "text" and e["props"].get("height")]
    finally:
        s.close()
    d = tmp_path / "talk"
    assert cli.main(["new", str(d), "--poster", "24x36", "--landscape"]) == 0
    res = Renderer(str(d)).render(str(d / "poster.py"), px_per_mm=2)
    assert not res.get("error") and res["size"] == [914.4, 609.6]
    for k in journals.POSTERS:                                    # every preset makes a poster that runs
        compile(templates.poster_py(k, False, ["fig1"]), k, "exec")


def test_poster_templates(project):
    """Every template makes a poster of its size, hung its way, that renders without warnings of its own."""
    r = Renderer(str(project))
    for t in journals.POSTER_TEMPLATES:
        f = project / f"p_{t['key'].replace('-', '_')}.py"
        f.write_text(templates.poster_py(t["key"], False, ["fig1"]), encoding="utf-8")
        res = r.render(str(f), px_per_mm=1)
        assert not res.get("error"), (t["key"], res.get("error"))
        W, H = journals.poster_size(t["poster"], t["landscape"])
        assert res["size"] == [W, H] and res["poster"]["landscape"] == t["landscape"]
        assert (W > H) == t["landscape"] or W == H
        own = [w["message"] for w in res["warnings"] if not w["message"].startswith("figure fig1.py")]
        assert own == [], (t["key"], own)
    # sizes written the other way round are the poster turned, in code and on the command line
    assert journals.resolve("48x36") == ("36x48", True) and journals.resolve("48 × 36 in") == ("36x48", True)
    assert journals.resolve("a0-landscape") == ("a0", True)
    write(project, "turned.py", 'fig = Figure(poster="72x48")\n')
    assert render(project, "turned.py")["size"] == [1828.8, 1219.2]
    with pytest.raises(ValueError):
        journals.resolve("50x50")


# --- text boxes ---

def test_flowing_text(project):
    (project / "intro.md").write_text("# Intro\n\nA paragraph written\nover two lines.\n\n- one\n- two\n  - under two\n"
                                      "1. first\n", encoding="utf-8")
    long = " ".join(["word"] * 150)
    write(project, "t.py", f'fig = Figure(width=200, height=200)\n'
                           f'fig.text(source="intro.md", x=5, y=5, width=80)\n'
                           f'fig.text("{long}", x=100, y=5, width=80, height=20)\n'
                           f'fig.text("{long}", x=100, y=60, width=80, height=20, fit="shrink")\n'
                           f'fig.text("A **multi-panel** figure", x=5, y=150)\n')
    r = Renderer(str(project))
    res = r.render(str(project / "t.py"), px_per_mm=2)
    src, over, shrunk, bold = [e for e in res["elements"] if e["kind"] == "text"]
    assert Path(src["code"]["file"]).name == "intro.md"
    pieces = {pc.oid: pc for pc in r.pieces(r.run_figure(str(project / "t.py")))[0]}
    w = words(pieces[src["oid"]].meta["pdf"])
    assert w[:2] == ["Intro", "A"] and "over" in w and w.count("•") == 2 and "–" in w and "1." in w
    assert over["overflow"] > 0 and "shrunk" not in over
    assert shrunk["shrunk"] < 7 and shrunk.get("overflow") is None
    assert any("runs" in m["message"] and "past its 20 mm box" in m["message"] for m in res["warnings"])
    assert "multi-panel" in " ".join(words(pieces[bold["oid"]].meta["pdf"]))   # a hyphen, not a minus sign
    assert is_flowing("- a\n- b") and is_flowing("# Head\ntext") and not is_flowing("a - b\n*p < 0.05")


# --- QR codes ---

def test_qr_modules():
    m = qr.modules("https://github.com/pachterlab/Fignimble", "M")
    assert len(m) == 29                                           # version 3
    for x0, y0 in ((0, 0), (22, 0), (0, 22)):                     # finder patterns
        assert all(m[y0][x0 + i] for i in range(7)) and not m[y0 + 1][x0 + 1] and m[y0 + 3][x0 + 3]
    assert m[len(m) - 8][8]                                       # the module that is always dark
    # checked module for module against the qrcode package when written; kept as a fingerprint
    digest = hashlib.sha256("".join("1" if v else "0" for row in m for v in row).encode()).hexdigest()[:16]
    assert digest == QR_DIGEST
    assert len(qr.modules("x" * 2000, "L")) == 4 * 33 + 17                   # version 33 holds 2071 bytes at L
    with pytest.raises(ValueError):
        qr.modules("x" * 3000, "H")


QR_DIGEST = "0b16b547e8680b55"


def test_qr_element(project):
    write(project, "q.py", 'fig = Figure(width=100, height=100)\nfig.qr("https://example.org", x=10, y=10, w=40)\n'
                           'fig.qr("x" * 400, x=60, y=60, w=20)\n')
    s = Session(str(project), sys.executable)
    try:
        res = s.render("q", 2.0)
        a, b = [e for e in res["elements"] if e["kind"] == "qr"]
        assert a["box"] == [10, 10, 40, 40] and a["module"] == round(40 / 33, 3)      # 25 modules + 2 x 4
        assert any("QR code's squares" in w["message"] and w["oid"] == b["oid"] for w in res["warnings"])
        r = s.edit({"figure": "q", "op": "resize", "oid": a["oid"], "box": [10, 10, 50, 30]})
        assert 'fig.qr("https://example.org", x=10, y=10, w=50)' in (project / "q.py").read_text()
        assert element(r["figure"], "qr")["box"] == [10, 10, 50, 50]
        r = s.edit({"figure": "q", "op": "insert", "kind": "qr", "props": {"data": "https://doi.org/x", "x": 5, "y": 70, "w": 25}})
        assert 'fig.qr("https://doi.org/x", x=5, y=70, w=25)' in (project / "q.py").read_text()
    finally:
        s.close()


def test_table_moves(project):
    write(project, "t.py", 'fig = Figure(width=100, height=60)\nfig.table(rows=[["a", "b"], [1, 2]], x=10, y=10)\n')
    s = Session(str(project), sys.executable)
    try:
        oid = element(s.render("t", 2.0), "table")["oid"]
        s.edit({"figure": "t", "op": "move", "oids": [oid], "dx": 5, "dy": 3})
        assert "x=15, y=13" in (project / "t.py").read_text()
    finally:
        s.close()


# --- exports ---

def test_poster_exports(project, tmp_path):
    write(project, "poster.py", 'fig = Figure(poster="a2")\nfig.figure("fig1.py", x=20, y=20, w=366)\n'
                                'fig.text("Hello", x=20, y=300, size=40)\n')
    r = Renderer(str(project))
    res = r.export(str(project / "poster.py"), str(tmp_path / "p.pdf"), bleed=3, marks=True)
    with pymupdf.open(tmp_path / "p.pdf") as d:
        pg = d[0]
        MM = 72 / 25.4
        assert abs(pg.rect.width - (420 + 2 * 13) * MM) < 0.1
        assert abs(pg.trimbox.x0 - 13 * MM) < 0.1 and abs(pg.trimbox.width - 420 * MM) < 0.1
        assert abs(pg.bleedbox.x0 - 10 * MM) < 0.1
        assert "Made with Fignimble" in d.metadata["subject"]
    assert res["resolution"]["need"] == journals.POSTER_DPI
    with pytest.raises(ValueError):
        r.export(str(project / "poster.py"), str(tmp_path / "p.png"), bleed=3)
    png = r.export(str(project / "poster.py"), str(tmp_path / "p.png"), dpi=600)
    assert png["resolution"]["dpi"] == 300 and "lowered to 300 dpi" in png["resolution"]["notes"][0]
    r.export(str(project / "poster.py"), str(tmp_path / "p.pptx"), dpi=100)
    with zipfile.ZipFile(tmp_path / "p.pptx") as z:
        pres = z.read("ppt/presentation.xml").decode()
    assert '<p:sldSz cx="15120000" cy="21384000"/>' in pres            # 420 x 594 mm
    # a figure (not a poster) still goes on a 16:9 slide
    Renderer(str(project)).export(str(project / "fig1.py"), str(tmp_path / "f.pptx"), dpi=100)
    with zipfile.ZipFile(tmp_path / "f.pptx") as z:
        assert '<p:sldSz cx="12192000" cy="6858000"/>' in z.read("ppt/presentation.xml").decode()


def test_provenance(project, tmp_path, capsys):
    write(project, "poster.py", 'fig = Figure(width=400, height=300)\nfig.figure("fig1.py", x=20, y=20, w=183)\n')
    git = shutil.which("git")
    if git:
        for args in (["init", "-q"], ["add", "-A"], ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "x"]):
            subprocess.run([git, *args], cwd=project, check=True)
    r = Renderer(str(project))
    for ext in ("pdf", "png", "svg", "tif"):
        r.export(str(project / "poster.py"), str(tmp_path / f"p.{ext}"), dpi=150)
        prov = read_provenance(str(tmp_path / f"p.{ext}"))
        assert prov["figure"] == "poster.py"
        assert {"poster.py", "fig1.py", "panels/expression.py", "images/cells.png", "style.py"} <= set(prov["files"])
        if git:
            assert len(prov["git"]) == 40 and prov["uncommitted"] == []
    assert cli.main(["provenance", str(tmp_path / "p.pdf"), "--project", str(project)]) == 0
    assert "matches the project" in capsys.readouterr().out
    (project / "panels" / "expression.py").write_text("# changed\n", encoding="utf-8")
    assert cli.main(["provenance", str(tmp_path / "p.pdf"), "--project", str(project)]) == 1
    out = capsys.readouterr().out
    assert "changed  panels/expression.py" in out and "1 file differs" in out
