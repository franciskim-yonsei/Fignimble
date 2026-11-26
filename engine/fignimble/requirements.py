"""The packages Fignimble and a project's panels need that the Python running them lacks, and installing them.

Fignimble's own are engine/requirements.txt. A project lists what its panels import beyond those in a
requirements.txt of its own (pip's format: "pandas", "seaborn>=0.13", comments after #). The app asks when it opens a
project, offers to install what is missing into the Python it runs (with pip), and restarts the renderer.
"""

from __future__ import annotations

import re
import subprocess
import sys
from importlib import metadata
from pathlib import Path

ENGINE_REQUIREMENTS = Path(__file__).resolve().parent.parent / "requirements.txt"

try:
    from packaging.requirements import InvalidRequirement, Requirement
except ImportError:                               # pip carries its own copy
    try:
        from pip._vendor.packaging.requirements import InvalidRequirement, Requirement
    except ImportError:                           # no pip either: only whether a package is there at all
        Requirement = None
        InvalidRequirement = ValueError


def parse(text: str) -> list[str]:
    """The requirement lines of a requirements.txt (no comments, options or blank lines)."""
    out = []
    for line in text.splitlines():
        line = line.split("#", 1)[0].strip()
        if line and not line.startswith("-"):
            out.append(line)
    return out


def _unmet(line: str) -> str | None:
    """Why `line` isn't met by this Python ("not installed", "1.2 installed"), or None when it is."""
    if Requirement is None:
        name = re.split(r"[<>=!~;\[ ]", line, maxsplit=1)[0]
        spec = None
    else:
        try:
            req = Requirement(line)
        except InvalidRequirement:
            return None                           # not ours to judge; pip would say
        if req.marker is not None and not req.marker.evaluate():
            return None
        name, spec = req.name, req.specifier
    try:
        have = metadata.version(name)
    except metadata.PackageNotFoundError:
        return "not installed"
    if spec and not spec.contains(have, prereleases=True):
        return f"{have} installed"
    return None


def missing(project: str | None = None) -> dict:
    """{python, engine: [{requirement, why}], project: [...], file}: what Fignimble and the project need that this
    Python lacks. `file` is the project's requirements.txt, if it has one."""
    def check(path: Path) -> list[dict]:
        try:
            lines = parse(path.read_text(encoding="utf-8"))
        except OSError:
            return []
        return [{"requirement": ln, "why": why} for ln in lines if (why := _unmet(ln))]

    out = {"python": sys.executable, "engine": check(ENGINE_REQUIREMENTS), "project": [], "file": None}
    if project:
        f = Path(project) / "requirements.txt"
        if f.exists():
            out["file"] = str(f)
            have = {r["requirement"] for r in out["engine"]}
            out["project"] = [r for r in check(f) if r["requirement"] not in have]
    return out


def install(requirements: list[str], timeout: float = 900) -> dict:
    """pip install `requirements` into this Python: {ok, output (its last lines)}."""
    if not requirements:
        return {"ok": True, "output": ""}
    cmd = [sys.executable, "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "--", *requirements]
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        if r.returncode and "No module named pip" in r.stderr:     # a Python without pip: give it one, once
            subprocess.run([sys.executable, "-m", "ensurepip", "--upgrade"], capture_output=True, text=True,
                           timeout=timeout)
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return {"ok": False, "output": f"pip took longer than {timeout:.0f} s and was stopped"}
    text = (r.stdout + r.stderr).strip().splitlines()
    return {"ok": r.returncode == 0, "output": "\n".join(text[-12:])}
