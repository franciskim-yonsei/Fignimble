"""Tables of the paper (`Table`): a file of their own, numbered apart from figures, laid out for you, edited on the
canvas through their Table(...) call, and exported as Word, Excel, CSV or TSV with their title and notes."""

import re
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

from fignimble import tables  # noqa: E402
from fignimble.core import Session  # noqa: E402
from fignimble.render import Renderer, is_figure_source, is_table_source  # noqa: E402

TABLE = '''from fignimble import *
from style import STYLE

tab = Table("tables/genes.csv", style=STYLE, title="Differentially expressed genes",
            decimals=[None, 0, 2, 3], notes="FDR, Benjamini-Hochberg.")
'''
FROM_CODE = '''from fignimble import *

def genes():
    return [["Gene", "n"], ["CD3E", 12], ["GAPDH", 40]]

tab = Table(genes, title="From code")
'''


@pytest.fixture()
def project(tmp_path):
    (tmp_path / "tables").mkdir()
    (tmp_path / "tables" / "genes.csv").write_text("Gene,n,log2FC,FDR\nCD3E,12,1.23456,0.0012\nIL7R,7,2.1,0.00001\n")
    (tmp_path / "style.py").write_text('from fignimble import *\nSTYLE = Style.journal("nature")\n')
    (tmp_path / "table1.py").write_text(TABLE)
    (tmp_path / "table2.py").write_text(FROM_CODE)
    (tmp_path / "fig1.py").write_text('from fignimble import *\nfrom style import STYLE\n\n'
                                      'fig = Figure(width=120, height=60, style=STYLE)\n'
                                      'fig.figure("table1.py", x=5, y=5, w=80)\n')
    return tmp_path


@pytest.fixture()
def session(project):
    s = Session(str(project), sys.executable)
    yield s
    s.close()


def text(project, name="table1.py"):
    return (project / name).read_text()


def test_table_files_are_told_apart():
    assert is_table_source(TABLE) and is_figure_source(TABLE)
    assert not is_table_source('from fignimble import *\nfig = Figure(width=90)\nfig.table("t.csv")\n')


def test_layout_and_checks(project):
    r = Renderer(str(project)).render(str(project / "table1.py"), sprites=False)
    assert not r.get("error"), r.get("error")
    assert r["figureProps"]["kind"] == "table" and not r["labels"]
    title, cells, notes = r["elements"]
    assert [e["props"]["role"] for e in r["elements"]] == ["title", "cells", "notes"]
    # one under the other, the page sized to them
    assert title["box"][1] + title["box"][3] < cells["box"][1] < cells["box"][1] + cells["box"][3] < notes["box"][1]
    assert r["size"][0] == pytest.approx(cells["box"][2] + 6, abs=0.05)
    assert r["size"][1] == pytest.approx(notes["box"][1] + notes["box"][3] + 3, abs=0.05)
    msgs = [w["message"] for w in r["warnings"]]
    assert any("shows as 0.000" in m for m in msgs)
    assert not any("mm wide" in m for m in msgs)          # a table's width isn't a figure's column width


def test_rows_from_a_function(project):
    r = Renderer(str(project)).render(str(project / "table2.py"), sprites=False)
    cells = r["elements"][1]
    assert cells["fromCode"] == "genes" and cells["props"]["cells"][2] == ["GAPDH", 40]


def test_canvas_edits_go_to_the_table_call(session, project):
    def edit(**a):
        res = session.edit({"figure": "table1", **a})
        assert res["ok"], res.get("error")
        return res

    edit(op="set", oid="0", props={"text": "Genes up in treated cells"})
    assert 'title="Genes up in treated cells"' in text(project)
    edit(op="set", oid="1", props={"grid": True})
    assert "grid=True" in text(project)
    before = text(project)
    assert edit(op="move", oids=["1", "2"], dx=5, dy=5).get("unchanged")      # it lays itself out
    assert text(project) == before
    edit(op="resize", oid="1", box=[3, 10, 90, 20])
    assert "w=90" in text(project) and "x=" not in text(project)
    edit(op="table_rows", oid="1", rows=[["Gene", "n"], ["CD3E", "13"]])
    assert (project / "tables" / "genes.csv").read_text().splitlines()[1] == "CD3E,13"
    edit(op="delete", oids=["2"])
    assert "notes=" not in text(project)
    assert not session.edit({"figure": "table1", "op": "delete", "oids": ["1"]})["ok"]
    res = session.edit({"figure": "table1", "op": "insert", "kind": "text", "props": {"text": "x", "x": 1, "y": 1}})
    assert not res["ok"] and "place it in a figure" in res["error"]
    assert not session.copy({"figure": "table1", "oids": ["1"]})["ok"]
    res = session.edit({"figure": "table2", "op": "table_rows", "oid": "1", "rows": [["a"]]})
    assert not res["ok"] and "genes()" in res["error"]


def test_listed_apart_and_new_table(session, project):
    kinds = {f["name"]: f["kind"] for f in session.figures()}
    assert kinds == {"fig1": "figure", "table1": "table", "table2": "table"}
    made = session.new_table({"name": "table3", "rows": 3, "cols": 2})
    assert made["ok"] and is_table_source(text(project, "table3.py"))
    r = session.render("table3")
    assert not r.get("error"), r.get("error")


def test_table_embedded_in_a_figure(session, project):
    res = session.insert_table({"figure": "fig1", "rows": 3, "cols": 2, "x": 10, "y": 30})
    assert res["ok"], res.get("error")
    assert 'fig.table("tables/fig1_table1.csv", x=10, y=30, grid=True)' in text(project, "fig1.py")
    assert (project / "tables" / "fig1_table1.csv").read_text() == ",\n,\n,\n"
    session.undo({"figure": "fig1"})
    assert "fig.table" not in text(project, "fig1.py") and not (project / "tables" / "fig1_table1.csv").exists()


def test_exports_with_title_and_notes(session, project, tmp_path):
    docx = tmp_path / "out" / "table1.docx"
    assert session.export_table({"figure": "table1", "path": str(docx)})["ok"]
    words = re.findall(r"<w:t[^>]*>([^<]+)", zipfile.ZipFile(docx).read("word/document.xml").decode())
    assert words[0] == "Differentially expressed genes" and words[-1] == "FDR, Benjamini-Hochberg."
    assert "0.001" in words and "1.23" in words
    xlsx = tmp_path / "out" / "table1.xlsx"
    assert session.export_table({"figure": "table1", "path": str(xlsx)})["ok"]
    assert "Benjamini" in zipfile.ZipFile(xlsx).read("xl/worksheets/sheet1.xml").decode()
    out = subprocess.run([str(ROOT / "bin" / "fignimble"), "export", "table2.py", str(tmp_path / "t2.csv")],
                         cwd=project, capture_output=True, text=True, env={"FIGNIMBLE_PYTHON": sys.executable,
                                                                           "PATH": "/usr/bin:/bin"})
    assert out.returncode == 0, out.stdout + out.stderr
    assert (tmp_path / "t2.csv").read_text() == "Gene,n\nCD3E,12\nGAPDH,40\n"


def test_check():
    rows = [["p", "x"], ["0.00001", "1.5"], ["0.2", "2.25"]]
    msgs = [w["message"] for w in tables.check(rows, decimals=[3, None])]
    assert any("0.000" in m for m in msgs) and any("1 to 2 decimals" in m for m in msgs)
    assert tables.check([["a", "b"], ["1.00", "x"], ["2.00", "y"]]) == []


def test_rows_from_a_dataframe(tmp_path):
    """A function may return a pandas DataFrame: its column names, then its values (a named index becomes a column,
    missing values empty cells). A stand-in with the same attributes, so the test doesn't need pandas."""
    from fignimble.render import table_rows

    class Values(list):
        def tolist(self):
            return [list(r) for r in self]

    class Frame:
        def __init__(self, columns, rows, index_name=None):
            self.columns, self.values = columns, Values(rows)
            self.index = type("Index", (), {"names": [index_name]})()
            self._index_name = index_name

        def reset_index(self):
            return Frame([self._index_name] + self.columns, [["a", *r] for r in self.values])

    rows = table_rows({"rows": None, "source": lambda: Frame(["n", "x"], [[1, 1.5], [2, float("nan")]], "gene")},
                      tmp_path)
    assert rows == [["gene", "n", "x"], ["a", 1, 1.5], ["a", 2, ""]]
    assert table_rows({"rows": None, "source": lambda: Frame(["n"], [[3]])}, tmp_path) == [["n"], [3]]
