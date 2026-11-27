"""Test fixture for the Windows figure list. Hold one render until the test releases it."""

import json
import sys
import time
from pathlib import Path


for line in sys.stdin:
    request = json.loads(line)
    reply = {"id": request["id"]}
    command = request["cmd"]
    if command == "open":
        project = Path(request["project"])
        reply["result"] = {"ok": True}
    elif command == "state":
        if (project / "hold-state").exists():
            (project / "started-state").touch()
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline and (project / "hold-state").exists():
                time.sleep(0.025)
        reply["result"] = {
            "figures": [{"name": name} for name in ("first", "slow", "skipped", "last", "failed")],
            "canUndo": False,
            "canRedo": False,
        }
    elif command == "figure":
        name = request["name"]
        with (project / "renders").open("a", encoding="utf-8") as log:
            log.write(name + "\n")
        if name == "slow":
            (project / "started-slow").touch()
            deadline = time.monotonic() + 20
            while time.monotonic() < deadline and not (project / "finish-slow").exists():
                time.sleep(0.025)
        if name == "failed":
            reply["error"] = "Figure not found"
        else:
            reply["result"] = {
                "figure": name, "file": str(project / (name + ".py")), "size": [100, 80],
                "pxPerMM": request["pxPerMM"],
            }
    elif command == "write_source":
        path = Path(request["file"])
        if request["expected"] != path.read_text(encoding="utf-8") or request["check_syntax"] is not True:
            reply["error"] = "Source changed outside the editor."
        else:
            path.write_text(request["text"], encoding="utf-8")
            reply["result"] = {"ok": True}
    else:
        reply["error"] = f"Unexpected command: {command}"
    print(json.dumps(reply), flush=True)
