"""Fonts that come with Microsoft Office: Aptos (Office's default since 2023), Aptos Display, Calibri, Cambria, ...

They are not installed system-wide. Office keeps them in its own folders: the fonts it ships inside each app
(macOS: Microsoft PowerPoint.app/Contents/Resources/DFonts) and the "cloud fonts" it downloads when a document uses
them (Aptos Display, Aptos Serif, ...: one folder per family, files with numbers for names). `install()` adds them to
matplotlib's fonts in this process, so `Style(font="Aptos")` draws panels and text in Aptos rather than a
substitute; render.py calls it when it is imported. Nothing is copied or installed.

matplotlib names a font by the family in its file, and Aptos Display's files say "Aptos" (with "Display" for a
style), the same as Aptos itself: a cloud font is added under its folder's name instead, so "Aptos Display" draws in
Aptos Display and "Aptos" stays Aptos. (Manimble has the same module for Pango, engine/manimble/officefonts.py.)
"""
from __future__ import annotations

import dataclasses
import glob
import os
import sys
from pathlib import Path

FONT_SUFFIXES = (".ttf", ".otf", ".ttc")

_installed = False


def font_dirs() -> list[str]:
    """Folders of Office's fonts on this machine: those shipped with its apps, then its downloaded cloud fonts."""
    if sys.platform == "darwin":
        shipped = sorted(glob.glob("/Applications/Microsoft *.app/Contents/Resources/DFonts"))
        cloud = Path.home() / "Library/Group Containers/UBF8T346G9.Office/FontCache/4/CloudFonts"
    elif sys.platform == "win32":
        shipped = []                                    # Office installs its own fonts system-wide on Windows
        cloud = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local"))) / "Microsoft/FontCache/4/CloudFonts"
    else:
        return []
    return shipped + sorted(str(d) for d in cloud.glob("*") if d.is_dir())


def font_files() -> list[str]:
    """Office's font files, each once (every Office app ships the same fonts)."""
    seen, out = set(), []
    for d in font_dirs():
        try:
            names = sorted(os.listdir(d))
        except OSError:
            continue
        for n in names:
            if n.lower().endswith(FONT_SUFFIXES):
                # a cloud font's file names are numbers, unique only within its family's folder
                key = f"{Path(d).name}/{n}".lower() if cloud_family(os.path.join(d, n)) else n.lower()
                if key not in seen:
                    seen.add(key)
                    out.append(os.path.join(d, n))
    return out


def cloud_family(path: str) -> str | None:
    """The family a cloud font file belongs to (its folder's name), or None for another file."""
    p = Path(path)
    return p.parent.name if p.parent.parent.name == "CloudFonts" else None


def install() -> list[str]:
    """Add Office's fonts to matplotlib in this process (idempotent): the files added."""
    global _installed
    if _installed:
        return []
    _installed = True
    try:
        from matplotlib import font_manager as fm
    except Exception:                                   # noqa: BLE001
        return []
    known = {e.fname for e in fm.fontManager.ttflist}
    done = []
    for f in font_files():
        if f in known:
            continue
        try:
            entry = fm.ttfFontProperty(fm.get_font(f))
        except Exception:                               # noqa: BLE001
            continue
        family = cloud_family(f)
        if family and family != entry.name and family.lower().startswith(entry.name.lower()):
            entry = dataclasses.replace(entry, name=family)          # Aptos Display, not a second "Aptos"
        fm.fontManager.ttflist.append(entry)
        done.append(f)
    if done:
        fm.fontManager._findfont_cached.cache_clear()
    return done


def family_of(path: str) -> str | None:
    """The family matplotlib knows a font file by (Aptos Display for its cloud files), or None."""
    from matplotlib import font_manager as fm
    return next((e.name for e in fm.fontManager.ttflist if e.fname == path), None)
