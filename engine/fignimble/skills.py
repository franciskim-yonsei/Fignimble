"""Install Fignimble's agent skills into an agent's skills directory.

The skills live in `skills/` at the repository root; the app carries a copy in `Contents/Resources/skills`, next to
`Contents/Resources/engine`. `fignimble install-skills` copies them into, by default, `~/.claude/skills`, where
Claude Code finds them in every project (`--agent codex`: `~/.codex/skills`; `--dest DIR`: any other agent's).
Only directories this command made (they hold a marker file) are ever replaced or removed.
"""

from __future__ import annotations

import os
import re
import shutil
from dataclasses import dataclass
from pathlib import Path

# engine/fignimble/skills.py -> the checkout's skills/; Resources/engine/fignimble/skills.py -> Resources/skills
SKILLS_DIR = Path(__file__).resolve().parents[2] / "skills"

AGENT_DIRS = {"claude": Path("~/.claude/skills"), "codex": Path("~/.codex/skills")}
PROJECT_DIRS = {"claude": Path(".claude/skills"), "codex": Path(".codex/skills")}
MARKER = ".fignimble-skill"


@dataclass(frozen=True)
class Skill:
    name: str
    description: str
    path: Path                                   # the skill's folder


def _description(text: str) -> str:
    """The `description` of a SKILL.md's front matter, on one line."""
    m = re.match(r"---\n(.*?)\n---", text, re.S)
    if not m:
        return ""
    lines = m.group(1).splitlines()
    for i, line in enumerate(lines):
        if line.startswith("description:"):
            value = line.split(":", 1)[1].strip()
            if value and value not in (">-", ">", "|", "|-"):
                return value
            folded = []
            for cont in lines[i + 1:]:
                if cont and not cont.startswith(" "):
                    break
                folded.append(cont.strip())
            return " ".join(p for p in folded if p)
    return ""


def available(source: Path | None = None) -> list[Skill]:
    root = source or SKILLS_DIR
    return [Skill(p.parent.name, _description(p.read_text(encoding="utf-8")), p.parent)
            for p in sorted(root.glob("*/SKILL.md"))]


def select(skills: list[Skill], names: list[str] | None) -> list[Skill]:
    if not names:
        return skills
    by_name = {s.name: s for s in skills}
    wanted = [n if n in by_name else f"fignimble-{n}" for n in names]          # "poster" for "fignimble-poster"
    unknown = [n for n, w in zip(names, wanted) if w not in by_name]
    if unknown:
        raise ValueError(f"unknown skill(s): {', '.join(unknown)}; choose from {', '.join(by_name)}")
    return [by_name[w] for w in dict.fromkeys(wanted)]


def target_dir(agent: str = "claude", project: str | os.PathLike | None = None,
               dest: str | os.PathLike | None = None) -> Path:
    if dest:
        return Path(dest).expanduser().resolve()
    if project is not None:
        return (Path(project).expanduser() / PROJECT_DIRS[agent]).resolve()
    return AGENT_DIRS[agent].expanduser()


def _files(folder: Path) -> dict[str, bytes]:
    return {str(p.relative_to(folder)): p.read_bytes() for p in sorted(folder.rglob("*"))
            if p.is_file() and p.name != MARKER and "__pycache__" not in p.parts}


def install(skills: list[Skill], directory: Path, overwrite: bool = False) -> list[tuple[Skill, str]]:
    """Copy each skill's folder to directory/<name>/: installed, updated, unchanged, or skipped (a folder of that name
    this command didn't make, unless `overwrite`)."""
    out = []
    for s in skills:
        dest = directory / s.name
        if dest.is_symlink() or (dest.exists() and not (dest / MARKER).exists()):
            if not overwrite:
                out.append((s, "skipped"))
                continue
            dest.unlink() if (dest.is_symlink() or dest.is_file()) else shutil.rmtree(dest)
        new = _files(s.path)
        if dest.is_dir() and _files(dest) == new:
            out.append((s, "unchanged"))
            continue
        outcome = "updated" if dest.exists() else "installed"
        if dest.exists():
            shutil.rmtree(dest)
        for rel, data in new.items():
            (dest / rel).parent.mkdir(parents=True, exist_ok=True)
            (dest / rel).write_bytes(data)
        (dest / MARKER).write_text("Installed by `fignimble install-skills`; "
                                   "`fignimble install-skills --uninstall` removes it.\n", encoding="utf-8")
        out.append((s, outcome))
    return out


def uninstall(skills: list[Skill], directory: Path) -> list[tuple[Skill, str]]:
    """Remove each skill this command installed: removed, absent, or kept (not made by this command)."""
    out = []
    for s in skills:
        dest = directory / s.name
        if not dest.exists() and not dest.is_symlink():
            out.append((s, "absent"))
        elif dest.is_symlink() or not (dest / MARKER).exists():
            out.append((s, "kept"))
        else:
            shutil.rmtree(dest)
            out.append((s, "removed"))
    return out


def main(names, agent="claude", project=None, dest=None, overwrite=False, list_only=False,
         remove=False) -> int:
    import textwrap
    shipped = available()
    if not shipped:
        print("This Fignimble has no skills to install; get them from "
              "https://github.com/pachterlab/Fignimble/tree/main/skills")
        return 1
    try:
        chosen = select(shipped, names)
    except ValueError as e:
        print(e)
        return 2
    directory = target_dir(agent, project, dest)
    if list_only:
        for s in chosen:
            state = "installed" if (directory / s.name / "SKILL.md").is_file() else "not installed"
            print(f"{s.name} [{state}]")
            print(textwrap.fill(s.description, 100, initial_indent="    ", subsequent_indent="    "))
        print(f"\nSkills directory: {directory}")
        return 0
    if remove:
        res = uninstall(chosen, directory)
        for s, o in res:
            if o == "removed":
                print(f"removed {s.name}")
            elif o == "kept":
                print(f"kept {s.name}: {directory / s.name} was not installed by this command")
        if not any(o == "removed" for _, o in res):
            print(f"No Fignimble skills to remove in {directory}.")
        return 0
    res = install(chosen, directory, overwrite)
    for s, o in res:
        print(f"skipped {s.name}: {directory / s.name} exists and was not installed by this command "
              f"(--overwrite replaces it)" if o == "skipped" else f"{o} {s.name}")
    print(f"Skills directory: {directory}")
    if any(o in ("installed", "updated") for _, o in res):
        print("Start a new agent session to load them.")
    return 1 if any(o == "skipped" for _, o in res) else 0
