"""The project marker, fignimble.toml: new projects get one, the project root is found from inside it, and older
projects (figures but no marker) are told apart from empty folders.

    PYTHONPATH=engine python -m pytest tests/test_marker.py
"""

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

import fignimble  # noqa: E402
from fignimble import cli, templates  # noqa: E402
from fignimble.core import Session  # noqa: E402


def test_new_project_has_marker_and_default_folder(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    r = templates.new_project(journal="nature")
    d = Path(r["project"])
    assert d.name == "fignimble"
    assert templates.read_marker(d) == {"fignimble": fignimble.__version__}
    assert templates.marker_state(d)["marker"] and not templates.marker_state(d)["newer"]


def test_poster_project_has_marker(tmp_path):
    r = templates.new_project(str(tmp_path / "p"), poster="a0")
    assert templates.read_marker(r["project"]) is not None


def test_cli_new_without_folder(tmp_path, monkeypatch, capsys):
    monkeypatch.chdir(tmp_path)
    assert cli.main(["new"]) == 0
    assert (tmp_path / "fignimble" / "fignimble.toml").is_file()
    assert (tmp_path / "fignimble" / "fig1.py").is_file()


def test_find_project_walks_up(tmp_path):
    d = Path(templates.new_project(str(tmp_path / "paper" / "fignimble"))["project"])
    (d / "panels" / "a.py").write_text("def draw(fig):\n    pass\n")
    assert templates.find_project(d / "panels" / "a.py") == d
    assert templates.find_project(d / "panels") == d
    assert templates.find_project(tmp_path / "paper") is None
    assert templates.project_root(d / "panels") == d                 # opening panels/ opens the project
    assert templates.project_root(d / "panels" / "a.py") == d
    assert templates.project_root(tmp_path / "paper") == tmp_path / "paper"


def test_cli_check_from_inside_project(tmp_path, monkeypatch):
    d = Path(templates.new_project(str(tmp_path / "fignimble"))["project"])
    monkeypatch.chdir(d / "panels")
    assert cli._here() == d
    assert [p.name for p in cli._figures(cli._here())] == ["fig1.py"]


def test_old_project_and_empty_folder(tmp_path):
    old = tmp_path / "old"
    old.mkdir()
    (old / "fig1.py").write_text(templates.figure_py(183, 100))
    st = templates.marker_state(old)
    assert st == {**st, "marker": False, "looksLikeProject": True}
    empty = tmp_path / "empty"
    empty.mkdir()
    assert templates.marker_state(empty)["looksLikeProject"] is False
    templates.write_marker(old)
    assert templates.marker_state(old)["marker"]


def test_marker_versions(tmp_path):
    (tmp_path / "fignimble.toml").write_text('[project]\nfignimble = "99.0.0"\n')
    st = templates.marker_state(tmp_path)
    assert st["madeWith"] == "99.0.0" and st["newer"]
    (tmp_path / "fignimble.toml").write_text("not = [toml")
    assert templates.read_marker(tmp_path) == {}                     # mangled, but still a marker
    assert templates.marker_state(tmp_path)["marker"]


def test_write_marker_keeps_existing(tmp_path):
    (tmp_path / "fignimble.toml").write_text('[project]\nfignimble = "0.1.0"\n')
    templates.write_marker(tmp_path)
    assert templates.read_marker(tmp_path) == {"fignimble": "0.1.0"}


def test_first_figure_in_empty_folder_marks_it(tmp_path):
    s = Session(str(tmp_path), sys.executable)
    try:
        assert s.new_figure({"name": "fig1"})["ok"]
        assert templates.read_marker(tmp_path) is not None
    finally:
        s.close()


def test_new_figure_in_unmarked_project_does_not_mark_it(tmp_path):
    (tmp_path / "fig1.py").write_text(templates.figure_py(183, 100))
    s = Session(str(tmp_path), sys.executable)
    try:
        assert s.new_figure({"name": "fig2"})["ok"]
        assert templates.read_marker(tmp_path) is None               # the app asks instead
    finally:
        s.close()


# --- files outside the project that a figure reads are watched like its own ---

from fignimble.render import Renderer  # noqa: E402
from PIL import Image as PILImage  # noqa: E402
import json  # noqa: E402
import time  # noqa: E402


def _outside_setup(tmp_path):
    ana = tmp_path / "analysis"
    ana.mkdir()
    PILImage.new("RGB", (40, 30), "red").save(ana / "umap.png")
    (ana / "labels.py").write_text('XLABEL = "first"\n')
    (ana / "plots.py").write_text("import labels\n\ndef umap(fig):\n    ax = fig.add_subplot()\n"
                                  "    ax.plot([0, 1], [0, 1])\n    ax.set_xlabel(labels.XLABEL)\n")
    d = Path(templates.new_project(str(tmp_path / "fignimble"))["project"])
    with open(d / "fig1.py", "a") as f:
        f.write('fig.image("../analysis/umap.png", x=4, y=3, w=40)\n'
                'fig.panel("../analysis/plots.py", x=60, y=3, w=60, h=50, function="umap")\n')
    return d, ana


def _bump(path, text=None):
    if text is not None:
        path.write_text(text)
    st = path.stat()
    os.utime(path, ns=(st.st_atime_ns, st.st_mtime_ns + 2_000_000_000))


def test_renderer_watches_and_reloads_outside_files(tmp_path, monkeypatch):
    d, ana = _outside_setup(tmp_path)
    monkeypatch.chdir(tmp_path)
    r = Renderer(str(d))
    res = r.render(str(d / "fig1.py"), sprites=False)
    out = set(res["outside"])
    assert {str((ana / n).resolve()) for n in ("umap.png", "plots.py", "labels.py")} <= out
    assert "first" in json.dumps(res["elements"])
    before = r.fingerprint()
    _bump(ana / "labels.py", 'XLABEL = "second"\n')      # a module the outside panel code imports
    assert r.fingerprint() != before
    res = r.render(str(d / "fig1.py"), sprites=False)
    assert "second" in json.dumps(res["elements"]) and "first" not in json.dumps(res["elements"])
    # a fresh process (fignimble export) knows them from the cache
    assert set(Renderer(str(d)).outside_files()) == out


def test_session_redraws_when_outside_picture_changes(tmp_path):
    d, ana = _outside_setup(tmp_path)
    s = Session(str(d), sys.executable)
    try:
        s.render("fig1")
        sig = s._sig
        assert s.fingerprint() == sig                      # watching them is not itself a change
        assert s.render("fig1") is s.render("fig1")        # and the render stays cached
        PILImage.new("RGB", (40, 30), "blue").save(ana / "umap.png")
        _bump(ana / "umap.png")
        assert s.fingerprint() != sig                      # the watcher sees it and the app redraws
        first = s.renders["fig1"][2]
        assert s.render("fig1") is not first
    finally:
        s.close()
