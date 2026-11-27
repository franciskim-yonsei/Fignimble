"""Scale bars from picture metadata, the grayscale check, source data, chemistry and materials presets, and nothing
written into the app bundle (issues #4-#6).

    PYTHONPATH=engine .venv/bin/python -m pytest tests/test_issues.py
"""

import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

import numpy as np
import pytest
from PIL import Image
from PIL.TiffImagePlugin import ImageFileDirectory_v2

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

from fignimble import api, journals, sourcedata  # noqa: E402
from fignimble.render import Renderer, _clashes, pixel_size_of  # noqa: E402


@pytest.fixture()
def project(tmp_path):
    d = tmp_path / "demo"
    shutil.copytree(ROOT / "tests" / "fixtures" / "demo", d, ignore=shutil.ignore_patterns(".fignimble", "__pycache__"))
    return d


def write(project, name, body):
    (project / name).write_text("from fignimble import *\n\n" + body, encoding="utf-8")


def render(project, name):
    res = Renderer(str(project)).render(str(project / name), px_per_mm=2)
    assert not res.get("error"), res.get("error")
    return res


def tiff(path, description=None, xres=None, unit=None):
    ifd = ImageFileDirectory_v2()
    if description:
        ifd[270] = description
    if xres:
        ifd[282] = ifd[283] = xres
    if unit:
        ifd[296] = unit
    Image.fromarray((np.arange(300 * 200) % 255).astype("uint8").reshape(200, 300)).save(path, tiffinfo=ifd)


# --- scale bars ---

def test_pixel_size_from_metadata(tmp_path):
    tiff(tmp_path / "ij.tif", "ImageJ=1.54f\nunit=\\u00B5m\n", xres=4.0)                  # 4 px per µm
    tiff(tmp_path / "ome.tif", '<OME><Pixels PhysicalSizeX="0.65" PhysicalSizeXUnit="nm"/></OME>')
    tiff(tmp_path / "cm.tif", xres=30769.23, unit=3)                                       # px per cm: 0.325 µm
    tiff(tmp_path / "print.tif", xres=300.0, unit=2)                                       # 300 dpi: for print
    Image.new("L", (10, 10)).save(tmp_path / "plain.png")
    assert pixel_size_of(tmp_path / "ij.tif") == (pytest.approx(0.25e-6), "ImageJ")
    assert pixel_size_of(tmp_path / "ome.tif") == (pytest.approx(0.65e-9), "OME-TIFF")
    assert pixel_size_of(tmp_path / "cm.tif") == (pytest.approx(0.325e-6, rel=1e-4), "TIFF resolution")
    assert pixel_size_of(tmp_path / "print.tif") is None
    assert pixel_size_of(tmp_path / "plain.png") is None


def test_scalebar_uses_metadata_and_warns(project):
    tiff(project / "images" / "ij.tif", "ImageJ=1.54f\nunit=micron\n", xres=4.0)
    write(project, "bars.py", 'fig = Figure(width=183, height=60)\n'
          'a = fig.image("images/ij.tif", x=2, y=2, w=60)\n'
          'fig.scalebar(a, length=15, unit="µm")\n'                        # from the metadata: 60 px of 300
          'b = fig.image("images/cells.png", x=62, y=2, w=55)\n'
          'fig.scalebar(b, length=50)\n'                                    # no metadata, no pixel size
          'c = fig.image("images/ij.tif", x=122, y=2, w=60)\n'
          'fig.scalebar(c, length=1500, unit="nm", pixel_size=325)\n')    # given, and disagreeing
    res = render(project, "bars.py")
    bars = [e for e in res["elements"] if e["kind"] == "scalebar"]
    assert bars[0]["pixelSize"] == pytest.approx(0.25) and "ImageJ" in bars[0]["pixelFrom"]
    assert bars[0]["box"][2] == pytest.approx(60 * 60 / 300, rel=0.02)    # 15 µm = 60 px of 300 across 60 mm
    assert bars[1]["pixelFrom"] == "not set"
    msgs = [w["message"] for w in res["warnings"]]
    assert any("the scale bar on" in m and "isn't given" in m and "cells.png" in m for m in msgs)
    assert any("pixel_size=325 nm" in m and "says one pixel is 250 nm" in m for m in msgs)


# --- grayscale ---

def test_grayscale_clashes():
    okabe = list(api.PALETTES["okabe-ito"])
    found = _clashes(okabe)
    assert found and all(k == ["grayscale"] for _, _, k in found)                  # color-blind safe, not gray-safe
    assert _clashes(["#FF0000", "#00AA00"]) == [("#00AA00", "#FF0000", ["deuteranopia", "grayscale"])]
    assert _clashes(["#000000", "#E69F00"]) == []                                  # far apart in lightness


# --- source data ---

def test_source_data(project, tmp_path):
    (project / "panels" / "own.py").write_text(
        "import numpy as np\n\n\n"
        "def draw(fig):\n    fig.add_subplot().plot([0, 1, 2], [3, 4, 5])\n\n\n"
        "def source_data():\n    return [['dose', 'response'], [0, 0.1], [1, 0.5]]\n\n\n"
        "def misc(fig):\n    ax1, ax2 = fig.subplots(1, 2)\n"
        "    ax1.errorbar([0, 1, 2], [1, 4, 9], yerr=0.5, fmt='o', label='treated')\n"
        "    ax1.fill_between([0, 1, 2], [0, 1, 2], [1, 4, 9], alpha=0.2)\n"
        "    ax2.pie([1, 3])\n", encoding="utf-8")
    write(project, "sd.py", 'from style import STYLE\nfig = Figure(width=183, height=150, style=STYLE)\n'
          'fig.panel("panels/own.py", x=4, y=3, w=56, h=52)\n'
          'fig.panel("panels/own.py", x=65, y=3, w=110, h=52, function="misc")\n'
          'fig.figure("fig1.py", x=4, y=60, w=170)\n')
    out = tmp_path / "sd.xlsx"
    res = sourcedata.export(Renderer(str(project)), str(project / "sd.py"), str(out))
    hows = {p["panel"]: p["how"] for p in res["panels"]}
    assert hows["a"] == "from its source_data()" and hows["b"] == "read from what it draws"
    assert res["sheets"][0] == "About" and any(s.startswith("fig1.py") for s in res["sheets"])
    with zipfile.ZipFile(out) as z:
        assert z.testzip() is None
        names = z.namelist()
        xml = {n: z.read(n).decode() for n in names if n.startswith("xl/worksheets/")}
        wb = z.read("xl/workbook.xml").decode()
    assert len(xml) == len(res["sheets"]) == wb.count("<sheet ")
    misc = xml[f"xl/worksheets/sheet{res['sheets'].index('Panel b') + 1}.xml"]
    assert "y error low" in misc and "pie" in misc and "shaded area" in misc
    # fig1's bars are named by their tick labels, its heatmap's rows and columns too
    sheet = lambda name: xml[f"xl/worksheets/sheet{res['sheets'].index(name) + 1}.xml"]   # noqa: E731
    bars = next(sheet(n) for n in res["sheets"] if n.startswith("fig1.py") and "Fold change" in sheet(n))
    assert re.search(r">WT<.*>KO<", bars)
    assert ">S1<" in next(sheet(n) for n in res["sheets"] if "heatmap" in sheet(n))


def test_source_data_cli(project, tmp_path):
    env = {**os.environ, "PYTHONPATH": str(ROOT / "engine")}
    r = subprocess.run([sys.executable, "-m", "fignimble.cli", "source-data", "fig1.py"], cwd=project, env=env,
                       capture_output=True, text=True)
    assert r.returncode == 0, r.stderr
    assert (project / "fig1_source_data.xlsx").exists() and "Panel" in r.stdout


# --- journals ---

def test_chemistry_presets():
    for key in ("jacs", "chemical-science", "acta-materialia", "advanced-materials"):
        assert key in journals.JOURNALS
    assert journals.width("jacs", "single") == pytest.approx(84.6)
    assert journals.width("acta-materialia", "one-and-a-half") == 140
    assert journals.dpi("materials-today") == {"photo": 300, "combination": 500, "line": 1000}
    assert api.Labels.journal("advanced-materials").props["format"] == "{})"
    assert api.Style.journal("jacs").props["min_font"] == 4.5


# --- the app bundle ---

def test_nothing_writes_bytecode():
    # the app, the command and the engine's entry points all keep Python from writing __pycache__ (issue #4)
    assert 'env["PYTHONDONTWRITEBYTECODE"] = "1"' in (ROOT / "apps/macos/Sources/Fignimble/Engine.swift").read_text()
    assert "PYTHONDONTWRITEBYTECODE=1" in (ROOT / "bin/fignimble").read_text()
    for name in ("rpc.py", "cli.py", "tunnel.py"):
        assert "sys.dont_write_bytecode = True" in (ROOT / "engine/fignimble" / name).read_text()
    build = (ROOT / "apps/macos/build_app.sh").read_text()
    assert "CFBundleIconFile" in build and (ROOT / "apps/macos/assets/Fignimble.icns").exists()


def test_source_data_of_a_table(tmp_path):
    # a Table file's cells are its data: source data points to exporting the table instead
    d = tmp_path / "demo"
    shutil.copytree(ROOT / "examples" / "demo", d, ignore=shutil.ignore_patterns(".fignimble", "__pycache__"))
    with pytest.raises(ValueError, match="is a table"):
        sourcedata.export(Renderer(str(d)), str(d / "table1.py"), str(tmp_path / "t.xlsx"))
