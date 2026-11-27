"""Microsoft Office's fonts (Aptos, Aptos Display, Calibri, ...) found where Office keeps them and added to matplotlib,
Aptos Display under its own name.

    PYTHONPATH=engine .venv/bin/python -m pytest tests/test_officefonts.py
"""

import shutil
import sys
from pathlib import Path
from unittest import mock

import pymupdf
import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "engine"))

from fignimble import officefonts  # noqa: E402
from fignimble.render import Renderer  # noqa: E402

HAVE_APTOS = any(Path(f).name == "Aptos.ttf" for f in officefonts.font_files())
HAVE_DISPLAY = any(officefonts.cloud_family(f) == "Aptos Display" for f in officefonts.font_files())


def test_files_once_each_and_cloud_families(tmp_path):
    ppt, word, cloud = tmp_path / "PowerPoint/DFonts", tmp_path / "Word/DFonts", tmp_path / "CloudFonts"
    for d in (ppt, word, cloud / "Aptos Display", cloud / "Aptos Serif"):
        d.mkdir(parents=True)
    for d in (ppt, word):
        (d / "Aptos.ttf").write_bytes(b"")
        (d / "notes.txt").write_bytes(b"")
    (cloud / "Aptos Display" / "301.ttf").write_bytes(b"")
    (cloud / "Aptos Serif" / "301.ttf").write_bytes(b"")              # the same number, another family
    dirs = [str(ppt), str(word), str(cloud / "Aptos Display"), str(cloud / "Aptos Serif")]
    with mock.patch.object(officefonts, "font_dirs", return_value=dirs):
        files = officefonts.font_files()
    assert [Path(f).name for f in files] == ["Aptos.ttf", "301.ttf", "301.ttf"]
    assert [officefonts.cloud_family(f) for f in files] == [None, "Aptos Display", "Aptos Serif"]


@pytest.mark.skipif(not HAVE_APTOS, reason="Microsoft Office (with Aptos) isn't installed")
def test_matplotlib_knows_them():
    from matplotlib import font_manager as fm
    path = fm.findfont(fm.FontProperties(family="Aptos"), fallback_to_default=False)
    assert Path(path).name == "Aptos.ttf"
    if HAVE_DISPLAY:
        display = fm.findfont(fm.FontProperties(family="Aptos Display"), fallback_to_default=False)
        assert officefonts.cloud_family(display) == "Aptos Display"
        assert officefonts.family_of(display) == "Aptos Display"
        bold = fm.findfont(fm.FontProperties(family="Aptos", weight="bold"), fallback_to_default=False)
        assert Path(bold).name == "Aptos-Bold.ttf"                   # Aptos stays Aptos, not Aptos Display


@pytest.mark.skipif(not HAVE_APTOS, reason="Microsoft Office (with Aptos) isn't installed")
@pytest.mark.parametrize("font,ps", [("Aptos", "Aptos"), ("Aptos Display", "AptosDisplay")])
def test_exported_in_aptos(tmp_path, font, ps):
    if font == "Aptos Display" and not HAVE_DISPLAY:
        pytest.skip("Office hasn't downloaded Aptos Display")
    d = tmp_path / "demo"
    shutil.copytree(ROOT / "tests" / "fixtures" / "demo", d, ignore=shutil.ignore_patterns(".fignimble", "__pycache__"))
    (d / "f.py").write_text("from fignimble import *\n\n"
                            f'fig = Figure(width=120, height=60, style=Style(font="{font}", size=7))\n'
                            'fig.panel("panels/expression.py", x=4, y=3, w=70, h=55)\n'
                            'fig.text("**Bold** and *italic*, 10^{3} cells", x=78, y=10, size=10)\n', encoding="utf-8")
    r = Renderer(str(d))
    res = r.export(str(d / "f.py"), str(tmp_path / "f.pdf"))
    fonts = {f[3].split("+")[-1] for f in pymupdf.open(tmp_path / "f.pdf")[0].get_fonts()}
    assert fonts and all(f.split("-")[0] == ps for f in fonts), fonts       # panel, text, bold, italic and math
    assert not any("not the style's" in w["message"] for w in res.get("warnings", []) if isinstance(w, dict))
