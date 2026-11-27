"""Changing figure code the way a person would: the smallest edit to the call that made an element.

Edits are found through Python's syntax tree and applied as text patches, so the rest of the file (comments,
formatting, the agent's and the user's own code) is left exactly as it was. A value that the code computes
(`x=a.right + 2`) is never overwritten: the edit is refused with the line that controls it.
"""

from __future__ import annotations

import ast
import inspect
import json
import re

from . import api


def is_figure_source(text: str) -> bool:
    """A .py file that makes a figure or a table (and is not a panel or a style module)."""
    return "fignimble" in text and ("Figure(" in text or is_table_source(text))


def is_table_source(text: str) -> bool:
    """A .py file that makes a Table (`tab = Table(...)`)."""
    return "fignimble" in text and re.search(r"(?m)^\w+\s*=\s*(fignimble\.)?Table\(", text) is not None


class EditError(Exception):
    pass


REMOVE = object()          # set_args value: delete the keyword argument


class Raw:
    """A set_args value written into the code as it is (`Labels(size=9)`)."""

    def __init__(self, text: str):
        self.text = text


def _params(fn) -> list[str]:
    """The parameters a call can give by position (up to a *args, after which they are keywords only)."""
    out = []
    for name, p in inspect.signature(fn).parameters.items():
        if name in ("self", "cls"):
            continue
        if p.kind is not inspect.Parameter.POSITIONAL_OR_KEYWORD:
            break
        out.append(name)
    return out


# positional parameter names of every call an edit can change, by callee
PARAMS = {name: _params(getattr(api.Figure, name)) for name in
          ("panel", "image", "text", "line", "arrow", "rect", "ellipse", "bracket", "guide", "layout", "inset",
           "legend", "path", "zoom", "scalebar", "table", "caption")}
PARAMS.update({"Figure": _params(api.Figure.__init__), "Table": _params(api.Table.__init__), "Labels": _params(api.Labels.__init__),
               "Style": _params(api.Style.__init__), "Labels.journal": ["name"], "Style.journal": ["name"]})
PARAMS.update({n: _params(getattr(api, n).__init__) for n in ("Panel", "Image", "Spacer", "Row", "Column", "Grid",
                                                             "Design")})
CLASSES = ("Figure", "Labels", "Style")


def callee(call: ast.Call) -> str | None:
    f = call.func
    if isinstance(f, ast.Name):
        return f.id
    if isinstance(f, ast.Attribute):
        v = f.value
        if isinstance(v, ast.Attribute) and isinstance(v.value, ast.Name) and v.value.id == "fignimble":
            return f"{v.attr}.{f.attr}"                 # fignimble.Labels.journal
        if isinstance(v, ast.Name) and v.id in CLASSES:
            return f"{v.id}.{f.attr}"
        if isinstance(v, ast.Name) and v.id == "fignimble":
            return f.attr                               # fignimble.Figure
        return f.attr
    return None


def fmt(v) -> str:
    """A value as Python source: millimeters rounded to 0.01, fractions to 0.0001."""
    if isinstance(v, Raw):
        return v.text
    if v is None or isinstance(v, bool):
        return repr(v)
    if isinstance(v, (int, float)):
        # millimeters to 0.01; fractions (crops, regions, insets) to 0.0001
        r = round(float(v), 4 if abs(v) < 1 else 2)
        if r == int(r):
            return str(int(r))
        return repr(r)
    if isinstance(v, str):
        return json.dumps(v, ensure_ascii=False)
    if isinstance(v, tuple):
        inner = ", ".join(fmt(x) for x in v)
        return f"({inner},)" if len(v) == 1 else f"({inner})"
    if isinstance(v, list):
        return "[" + ", ".join(fmt(x) for x in v) + "]"
    if isinstance(v, dict):
        return "{" + ", ".join(f"{fmt(k)}: {fmt(x)}" for k, x in v.items()) + "}"
    raise EditError(f"can't write {v!r} as code")


def text_literal(text: str) -> str:
    """A string as Python source, as a triple-quoted block when it has several lines (a caption) so it reads as
    written."""
    if "\n" in text and '"""' not in text and "\\" not in text and not text.endswith('"') and "\r" not in text:
        return '"""' + text + '"""'
    return fmt(text)


def is_literal(node: ast.AST) -> bool:
    try:
        ast.literal_eval(node)
        return True
    except (ValueError, TypeError, SyntaxError, MemoryError, RecursionError):
        return False


class Source:
    """A file's text with its syntax tree, and conversions between (line, column) and offsets."""

    def __init__(self, text: str, path: str = "<figure>"):
        self.text, self.path = text, path
        try:
            self.tree = ast.parse(text)
        except SyntaxError as exc:
            raise EditError(f"{path} has a syntax error at line {exc.lineno}: {exc.msg}") from None
        self.lines = text.splitlines(keepends=True)
        self.starts = [0]
        for ln in self.lines:
            self.starts.append(self.starts[-1] + len(ln))

    def off(self, line: int, col: int) -> int:
        """Offset of (1-based line, UTF-8 byte column)."""
        ln = self.lines[line - 1] if line - 1 < len(self.lines) else ""
        return self.starts[line - 1] + len(ln.encode("utf-8")[:col].decode("utf-8", errors="ignore"))

    def start(self, node) -> int:
        return self.off(node.lineno, node.col_offset)

    def end(self, node) -> int:
        return self.off(node.end_lineno, node.end_col_offset)

    def seg(self, node) -> str:
        return self.text[self.start(node):self.end(node)]

    # --- finding things ---

    def calls(self, name: str | tuple) -> list[ast.Call]:
        names = (name,) if isinstance(name, str) else name
        return [n for n in ast.walk(self.tree) if isinstance(n, ast.Call) and callee(n) in names]

    def find_call(self, site: dict | None, name: str | tuple) -> ast.Call | None:
        """The call at a recorded call site."""
        if not site:
            return None
        cands = self.calls(name)
        pos = site.get("pos")
        if pos:
            for n in cands:
                if (n.lineno, n.end_lineno, n.col_offset, n.end_col_offset) == tuple(pos):
                    return n
        line = site["line"]
        hits = [n for n in cands if n.lineno <= line <= n.end_lineno]
        if not hits:
            return None
        return min(hits, key=lambda n: (n.end_lineno - n.lineno, n.end_col_offset - n.col_offset))

    def statement(self, call: ast.Call) -> ast.stmt:
        """The statement that is this call (`fig.text(...)` or `t = fig.text(...)`)."""
        for node in ast.walk(self.tree):
            for fld in ("body", "orelse", "finalbody"):
                for st in getattr(node, fld, []) or []:
                    if isinstance(st, (ast.Expr, ast.Assign, ast.AnnAssign)) and st.value is call:
                        return st
        raise EditError(f"line {call.lineno} makes this element inside a larger expression; change it in the code")

    def block_of(self, st: ast.stmt) -> list:
        for node in ast.walk(self.tree):
            for fld in ("body", "orelse", "finalbody"):
                body = getattr(node, fld, None)
                if isinstance(body, list) and st in body:
                    return body
        return self.tree.body

    def stmt_span(self, st: ast.stmt) -> tuple[int, int]:
        """Offsets of the whole lines the statement fills (it must have them to itself)."""
        a, b = self.off(st.lineno, 0), self.end(st)
        if self.text[a:self.start(st)].strip():
            raise EditError(f"line {st.lineno} holds more than this statement")
        rest_end = self.starts[st.end_lineno] if st.end_lineno < len(self.starts) else len(self.text)
        tail = self.text[b:rest_end]
        if tail.strip() and not tail.strip().startswith("#"):
            raise EditError(f"line {st.end_lineno} holds more than this statement")
        return a, rest_end

    def indent(self, st: ast.stmt) -> str:
        ln = self.lines[st.lineno - 1]
        return ln[:len(ln) - len(ln.lstrip())]


def apply(text: str, patches: list[tuple[int, int, str]]) -> str:
    """Apply (start, end, replacement) patches, which must not overlap. Insertions at one place keep their order."""
    order = sorted(enumerate(patches), key=lambda ip: (ip[1][0], ip[1][1], ip[0]), reverse=True)
    for _, (a, b, r) in order:
        text = text[:a] + r + text[b:]
    return text


def arg_node(call: ast.Call, name: str, method: str):
    """The node giving parameter `name` in this call (keyword or positional), or None."""
    for kw in call.keywords:
        if kw.arg == name:
            return kw.value
    params = PARAMS.get(method, [])
    if name in params:
        i = params.index(name)
        if i < len(call.args) and not any(isinstance(a, ast.Starred) for a in call.args[:i + 1]):
            return call.args[i]
    return None


def arg_value(src: Source, call: ast.Call, name: str, method: str):
    """(present, literal value or None, literal?)"""
    n = arg_node(call, name, method)
    if n is None:
        return False, None, True
    try:
        return True, ast.literal_eval(n), True
    except (ValueError, TypeError, SyntaxError):
        return True, None, False


def set_args(src: Source, call: ast.Call, method: str, updates: dict, what: str = "this element") -> list:
    """Patches that make the call's arguments `updates` ({name: value or REMOVE}). Refuses to overwrite a value
    the code computes."""
    patches, new = [], []
    for name, value in updates.items():
        node = arg_node(call, name, method)
        if node is not None:
            if not is_literal(node):
                raise EditError(f"{what}'s {name} is computed by the code ({src.seg(node)}, "
                                f"{_short(src.path)} line {node.lineno}); change it there or ask the agent")
            if value is REMOVE:
                kw = next((k for k in call.keywords if k.value is node), None)
                if kw is None:
                    patches.append((src.start(node), src.end(node), "None"))
                else:
                    patches.append(_remove_kw(src, call, kw))
            else:
                patches.append((src.start(node), src.end(node), fmt(value)))
        elif value is not REMOVE:
            new.append(f"{name}={fmt(value)}")
    if new:
        parts = list(call.args) + [k.value for k in call.keywords]
        if any(k.arg is None for k in call.keywords):            # **kwargs: put new ones before it
            first = next(k for k in call.keywords if k.arg is None)
            at = src.off(first.lineno, first.col_offset)
            patches.append((at, at, ", ".join(new) + ", "))
        elif parts:
            last = max(parts, key=lambda n: (n.end_lineno, n.end_col_offset))
            at = src.end(last)
            patches.append((at, at, ", " + ", ".join(new)))
        else:
            at = src.end(call) - 1                               # before the closing parenthesis
            patches.append((at, at, ", ".join(new)))
    return patches


def _remove_kw(src: Source, call: ast.Call, kw: ast.keyword) -> tuple:
    parts = sorted(list(call.args) + [k.value for k in call.keywords], key=lambda n: (n.lineno, n.col_offset))
    i = parts.index(kw.value)
    kstart = src.off(kw.lineno, kw.col_offset)
    if i > 0:
        return (src.end(parts[i - 1]), src.end(kw.value), "")
    if len(parts) > 1:
        nxt = parts[1]
        nstart = src.start(nxt)
        if isinstance(nxt, ast.AST):
            k2 = next((k for k in call.keywords if k.value is nxt), None)
            if k2 is not None:
                nstart = src.off(k2.lineno, k2.col_offset)
        return (kstart, nstart, "")
    return (kstart, src.end(kw.value), "")


def _short(path: str) -> str:
    return path.rsplit("/", 1)[-1]


def call_text(target: str, method: str, args: list, kwargs: dict) -> str:
    """`fig.text("Hi", x=3, y=4)`"""
    parts = [fmt(a) for a in args] + [f"{k}={fmt(v)}" for k, v in kwargs.items()]
    return f"{target}.{method}({', '.join(parts)})"


def figure_var(src: Source) -> str:
    """The name the figure file gives its Figure (`fig = Figure(...)`)."""
    for st in src.tree.body:
        if isinstance(st, ast.Assign) and isinstance(st.value, ast.Call) and callee(st.value) in ("Figure",) \
                and isinstance(st.targets[0], ast.Name):
            return st.targets[0].id
    return "fig"


def names_used_after(src: Source, st: ast.stmt) -> list[tuple[str, int]]:
    """Names this statement assigns that later code reads: [(name, line)]."""
    targets = []
    if isinstance(st, ast.Assign):
        for t in st.targets:
            targets += [n.id for n in ast.walk(t) if isinstance(n, ast.Name)]
    if not targets:
        return []
    out = []
    for n in ast.walk(src.tree):
        if isinstance(n, ast.Name) and n.id in targets and isinstance(n.ctx, ast.Load) and n.lineno > st.end_lineno:
            out.append((n.id, n.lineno))
    return out


def class_ref(src: Source, name: str) -> str:
    """How this file can name a fignimble class: `Labels` after `from fignimble import *`, else `fignimble.Labels`."""
    if re.search(r"^\s*from\s+fignimble\s+import\s+\*", src.text, re.M) or \
            re.search(rf"^\s*from\s+fignimble\s+import\s+[^\n]*\b{name}\b", src.text, re.M):
        return name
    return f"fignimble.{name}"


def ensure_import(src: Source, ref: str) -> list:
    """A patch adding `import fignimble` when `ref` needs it and the file lacks it."""
    if not ref.startswith("fignimble.") or re.search(r"^\s*import\s+fignimble\b", src.text, re.M):
        return []
    return [(0, 0, "import fignimble\n")]
