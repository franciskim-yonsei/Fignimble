"""Exercise the Windows mirror across an SSH-like stream using a POSIX-path server fixture."""
import json
import os
import queue
import subprocess
import sys
import threading
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent

FAKE = '''
import base64, json, sys
if len(sys.argv) > 2 and sys.argv[-1].startswith("cd "):
    print("/home/example/project\\nfig1.py\\nextra/")
    sys.exit(0)
source = b"# figure source\\n"
revision = 10
root = "/home/example/project"
image = root + "/.fignimble/sprites/test.png"
print(json.dumps({"event": "fignimble-remote-ready"}), flush=True)
for line in sys.stdin:
    req = json.loads(line)
    cmd = req["cmd"]
    if cmd == "open":
        assert req["project"] == "~/project"
        result = {"project": root}
    elif cmd == "manifest":
        result = {"files": {"fig1.py": [revision, len(source)]}}
    elif cmd == "read_files":
        result = {"files": {p: {"data": base64.b64encode(source if p == "fig1.py" else b"%PDF-test").decode(),
                  "stat": [revision, len(source)]} for p in req["paths"]}}
    elif cmd == "figure":
        result = {"file": root + "/fig1.py", "sprite": image, "source": source.decode()}
    elif cmd == "write_file":
        assert req["path"] == "fig1.py"
        assert req["expect"] == [revision, len(source)]
        source = base64.b64decode(req["data"])
        revision += 1
        result = {"ok": True, "stat": [revision, len(source)]}
    elif cmd == "write_source":
        assert req["file"] == root + "/fig1.py"
        source = req["text"].encode()
        revision += 1
        result = {"ok": True}
    elif cmd == "staging_path":
        result = {"path": "/tmp/fignimble/export.pdf"}
    elif cmd == "export":
        assert req["path"] == "/tmp/fignimble/export.pdf"
        result = {"path": req["path"]}
    else:
        raise ValueError(cmd)
    print(json.dumps({"id": req["id"], "result": result,
          "files": {image: base64.b64encode(b"sprite").decode()} if cmd == "figure" else {}}), flush=True)
'''


@pytest.mark.skipif(os.name != "nt", reason="exercises a native Windows mirror path")
def test_windows_tunnel_local_paths_and_edits(tmp_path, monkeypatch):
    monkeypatch.syspath_prepend(str(ROOT / "engine"))
    from fignimble.tunnel import TunnelError, _map, _mirror_path

    for name in ("../escape.py", "nested/CON.py", "nested/a.py:stream", "nested/a?.py", "bad\\path.py"):
        with pytest.raises(TunnelError):
            _mirror_path(str(tmp_path), name)
    assert _map(str(tmp_path / "file.py").lower(), tmp_path.as_posix(), "/remote") == "/remote/file.py"
    fake = tmp_path / "fake ssh.py"
    fake.write_text(FAKE, encoding="utf-8")
    project = tmp_path / "local mirror"
    env = dict(os.environ, PYTHONPATH=str(ROOT / "engine"), PYTHONUTF8="1",
               FIGNIMBLE_SSH=f'"{Path(sys.executable).as_posix()}" "{fake.as_posix()}"')
    with (tmp_path / "tunnel.log").open("w+", encoding="utf-8") as log:
        p = subprocess.Popen([sys.executable, "-m", "fignimble.tunnel", "--host=fixture", "--path=~/project",
                             "--mirror=" + str(project)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                             stderr=log, text=True, encoding="utf-8", env=env)
        number = 0
        lines = queue.Queue()

        def read():
            for text in p.stdout:
                lines.put(text)
            lines.put(None)

        threading.Thread(target=read, daemon=True).start()

        def call(cmd, **args):
            nonlocal number
            number += 1
            p.stdin.write(json.dumps(dict(id=number, cmd=cmd, **args)) + "\n")
            p.stdin.flush()
            while True:
                try:
                    line = lines.get(timeout=12)
                except queue.Empty:
                    log.seek(0)
                    pytest.fail(f"Tunnel did not answer {cmd}: {log.read()}")
                if line is None:
                    log.seek(0)
                    pytest.fail(log.read())
                reply = json.loads(line)
                if reply.get("id") == number:
                    assert "error" not in reply, reply
                    return reply["result"]

        try:
            assert call("remote_list", host="fixture")["entries"][-1] == {"name": "fig1.py", "dir": False}
            assert Path(call("open", project=str(project))["project"]) == project
            path = project / "fig1.py"
            assert path.read_text() == "# figure source\n"
            figure = call("figure")
            assert Path(figure["file"]) == path
            assert Path(figure["sprite"]).read_bytes() == b"sprite"
            call("write_source", file=str(path), text="# app edit\n")
            assert path.read_text() == "# app edit\n"
            path.write_text("# local mirror edit\n", encoding="utf-8")
            assert call("figure")["source"].splitlines() == ["# local mirror edit"]
            pdf = tmp_path / "figure export.pdf"
            assert Path(call("export", path=str(pdf))["path"]) == pdf
            assert pdf.read_bytes() == b"%PDF-test"
        finally:
            p.stdin.close()
            try:
                p.wait(timeout=10)
            except subprocess.TimeoutExpired:
                p.kill()
                p.wait()
