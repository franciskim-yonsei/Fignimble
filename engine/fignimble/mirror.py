"""The engine's half of a remote project (see tunnel.py, which runs on the Mac and starts the engine over ssh).

With FIGNIMBLE_REMOTE set, the engine runs on a server and the app on a Mac that can't read the server's disk, so:

- every response carries, in `files`, the pictures it names from the project's .fignimble cache (sprites and
  thumbnails) that this connection hasn't been sent yet, base64-encoded; the tunnel writes them into its mirror;
- `manifest`, `read_files`, `write_file`, `delete_file` and `upload` let the tunnel keep a mirror of the project's
  files on the Mac and send the server files from there.
"""

from __future__ import annotations

import base64
import os
import shutil
import tempfile
from pathlib import Path
from stat import S_ISREG

from . import templates

MIRROR_MAX = 8 * 1024 * 1024        # bigger files stay on the server
# what the app reads or edits itself: code, tables, notes, pictures (data the panels read stays on the server)
MIRROR_TYPES = {".py", ".csv", ".tsv", ".txt", ".md", ".json", ".toml", ".yaml", ".yml", ".cfg", ".ini", ".xlsx",
                ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".gif", ".bmp", ".webp", ".svg", ".pdf", ".eps"}


SKIP_DIRS = {".fignimble", ".git", "__pycache__", ".venv", "venv", "node_modules", ".ipynb_checkpoints"}


def outside_stats(files) -> list:
    """(path, mtime, size) of each file, (path, None, None) for one that is gone: what changes when they do."""
    out = []
    for f in files:
        try:
            st = os.stat(f)
            out.append((f, st.st_mtime_ns, st.st_size))
        except OSError:
            out.append((f, None, None))
    return out


def enabled() -> bool:
    return bool(os.environ.get("FIGNIMBLE_REMOTE"))


def _stat(p: Path) -> list[int]:
    st = p.stat()
    return [st.st_mtime_ns, st.st_size]


def _is_mirror_filename(name: str) -> bool:
    return not name.startswith(".") and os.path.splitext(name)[1].lower() in MIRROR_TYPES


def mirrored(rel: str) -> bool:
    """Whether a project file (a relative path with /) belongs in the Mac's mirror."""
    parts = rel.split("/")
    return (_is_mirror_filename(parts[-1])
            and not any(d in SKIP_DIRS or d.startswith(".") for d in parts[:-1])
            and not (len(parts) == 1 and parts[0] in templates.AGENT_FILES))


def scan(project: Path) -> dict:
    """{relative path: [mtime_ns, size]} of the files the mirror keeps (on either side)."""
    out = {}
    for dirpath, dirs, files in os.walk(project):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        directory = Path(dirpath)
        relative = directory.relative_to(project).as_posix()
        prefix = "" if relative == "." else relative + "/"
        for name in files:
            if not _is_mirror_filename(name) or (not prefix and name in templates.AGENT_FILES):
                continue
            try:
                st = (directory / name).stat()
            except OSError:
                continue
            if S_ISREG(st.st_mode) and st.st_size <= MIRROR_MAX:
                out[prefix + name] = [st.st_mtime_ns, st.st_size]
    return out


def manifest(project: Path) -> dict:
    return {"files": scan(project)}


class Remote:
    """The remote commands, for one engine process."""

    def __init__(self):
        self.sent: dict[str, tuple] = {}            # cache picture -> (mtime_ns, size) last sent
        self.staging = Path(tempfile.mkdtemp(prefix="fignimble-"))

    def close(self):
        shutil.rmtree(self.staging, ignore_errors=True)

    # --- pictures in responses ---

    def attach(self, result, cache: Path | None) -> dict:
        """The cache pictures `result` names that haven't been sent with this (mtime, size): {path: base64}."""
        if cache is None:
            return {}
        prefix = str(cache) + os.sep
        found: list[str] = []

        def walk(v):
            if isinstance(v, str):
                if v.startswith(prefix):
                    found.append(v)
            elif isinstance(v, dict):
                for x in v.values():
                    walk(x)
            elif isinstance(v, list):
                for x in v:
                    walk(x)
        walk(result)
        out = {}
        for path in dict.fromkeys(found):
            try:
                st = os.stat(path)
                if not os.path.isfile(path):
                    continue
                key = (st.st_mtime_ns, st.st_size)
                if self.sent.get(path) == key:
                    continue
                out[path] = base64.b64encode(Path(path).read_bytes()).decode()
                self.sent[path] = key
            except OSError:
                continue
        return out

    # --- the mirror's commands ---

    def _in(self, project: Path, rel: str) -> Path:
        p = (project / rel).resolve()
        if not str(p).startswith(str(project) + os.sep) or not mirrored(rel):
            raise ValueError(f"{rel} isn't a project file the mirror keeps")
        return p

    def read_files(self, project: Path | None, a: dict) -> dict:
        """{paths: [relative to the project, or absolute in the staging folder]} -> {files: {path: base64 | None}}."""
        out = {}
        for path in a.get("paths") or []:
            try:
                if os.path.isabs(path):
                    p = Path(path).resolve()
                    if not str(p).startswith(str(self.staging.resolve()) + os.sep):
                        raise ValueError
                else:
                    if project is None:
                        raise ValueError
                    p = self._in(project, path)
                out[path] = {"data": base64.b64encode(p.read_bytes()).decode(), "stat": _stat(p)}
            except (OSError, ValueError):
                out[path] = None
        return {"files": out}

    def write_file(self, project: Path, a: dict) -> dict:
        """{path (relative), data (base64), expect: [mtime_ns, size] | null}: written unless the file changed from
        `expect` (null: unless it exists) -> {ok, stat} or {ok: false, conflict: true, stat}."""
        p = self._in(project, a["path"])
        now = _stat(p) if p.exists() else None
        if now != a.get("expect"):
            return {"ok": False, "conflict": True, "stat": now}
        p.parent.mkdir(parents=True, exist_ok=True)
        tmp = p.with_name(f".{p.name}.fignimble-tmp")
        tmp.write_bytes(base64.b64decode(a["data"]))
        os.replace(tmp, p)
        return {"ok": True, "stat": _stat(p)}

    def delete_file(self, project: Path, a: dict) -> dict:
        """{path, expect}: removed unless it changed from `expect`."""
        p = self._in(project, a["path"])
        if not p.exists():
            return {"ok": True}
        if _stat(p) != a.get("expect"):
            return {"ok": False, "conflict": True, "stat": _stat(p)}
        p.unlink()
        return {"ok": True}

    def upload(self, a: dict) -> dict:
        """{name, data}: a file from the Mac (a picture to place) in a folder of its own -> {path}."""
        d = Path(tempfile.mkdtemp(dir=self.staging))
        p = d / Path(a["name"]).name
        p.write_bytes(base64.b64decode(a["data"]))
        return {"path": str(p)}

    def staging_path(self, a: dict) -> dict:
        """{name}: where an export can go before the tunnel fetches it -> {path}."""
        d = Path(tempfile.mkdtemp(dir=self.staging))
        return {"path": str(d / Path(a["name"]).name)}
