"""Tables: read from CSV/TSV (or given as rows), formatted the same way on the figure and in Word.

Journals want tables as editable text, not pictures, so a table on a figure can also be written as a .docx with a
real Word table (header in bold, rules above and below the header and at the bottom, as in a printed paper).
"""

from __future__ import annotations

import csv
import io
import zipfile
from pathlib import Path
from xml.sax.saxutils import escape


def read_rows(source: str | Path) -> list[list[str]]:
    """The rows of a .csv, .tsv or .txt file (the delimiter is guessed)."""
    text = Path(source).read_text(encoding="utf-8-sig", errors="replace")
    return parse_delimited(text, Path(source).suffix)[0]


def parse_delimited(text: str, suffix: str) -> tuple[list[list[str]], str]:
    """Use the same row and delimiter interpretation for rendering and inline editing."""
    if suffix.lower() == ".tsv":
        delim = "\t"
    else:
        try:
            delim = csv.Sniffer().sniff(text[:4096], delimiters=",\t;|").delimiter
        except csv.Error:
            delim = ","
    # rows of empty cells stay (a table still to be filled in); only blank lines go
    return [r for r in csv.reader(io.StringIO(text), delimiter=delim) if r], delim


def write_delimited(rows: list, delimiter: str = ",") -> str:
    """The rows as CSV (or TSV with a tab) text."""
    buf = io.StringIO()
    csv.writer(buf, delimiter=delimiter, lineterminator="\n").writerows([["" if c is None else c for c in r] for r in rows])
    return buf.getvalue()


def _number(s) -> float | None:
    if isinstance(s, (int, float)) and not isinstance(s, bool):
        return float(s)
    try:
        return float(str(s).replace(",", "").replace("−", "-").strip())
    except ValueError:
        return None


def _numeric(s: str) -> bool:
    """A number, or a bound on one ("< 0.001", "≥ 10"), which lines up with the numbers of its column."""
    return _number(s) is not None or _number(str(s).strip().lstrip("<>≤≥~±").strip()) is not None


def cells(rows: list, header: bool = True, decimals=None) -> tuple[list[list[str]], list[str]]:
    """The rows as the strings shown, padded to one width, and each column's alignment: "r" when every body cell
    is a number, else "l". `decimals` rounds numbers that have decimals (whole numbers stay whole): one number
    for every column, or a list with one per column (None leaves a column as written)."""
    n = max((len(r) for r in rows), default=0)
    places = list(decimals) if isinstance(decimals, (list, tuple)) else [decimals] * n
    places += [None] * (n - len(places))
    out = []
    for i, r in enumerate(rows):
        row = []
        for j, c in enumerate(list(r) + [""] * (n - len(r))):
            v = _number(c)
            whole = isinstance(c, int) or (isinstance(c, str) and not any(ch in c for ch in ".eE"))
            if places[j] is not None and v is not None and not whole and not (header and i == 0):
                row.append(f"{v:.{int(places[j])}f}")
            else:
                row.append("" if c is None else str(c))
        out.append(row)
    body = out[1:] if header else out
    align = ["r" if body and all(_numeric(r[j]) or not r[j].strip() for r in body)
             and any(r[j].strip() for r in body) else "l" for j in range(n)]
    return out, align


def check(rows: list, header: bool = True, decimals=None) -> list[dict]:
    """What a copy editor would query in a table: numbers that round to zero as shown, columns whose numbers are
    shown with different numbers of decimals, a header cell left empty above a column with values."""
    shown, _ = cells(rows, header, decimals)
    if not shown:
        return []
    head = shown[0] if header else None
    body = shown[1:] if header else shown
    out = []
    n = max(len(r) for r in shown)

    def name(j):
        return f"column “{head[j]}”" if head and head[j].strip() else f"column {j + 1}"

    for j in range(n):
        col = [(i, r[j]) for i, r in enumerate(body) if j < len(r) and r[j].strip()]
        nums = [(i, c) for i, c in col if _number(c) is not None]
        if not nums or len(nums) < len(col):
            continue
        zero = [c for i, c in nums if _number(c) == 0 and _number(rows[i + (1 if header else 0)][j]) not in (0, None)]
        if zero:
            out.append({"level": "warning", "message": f"{name(j)}: a value shows as {zero[0]} but isn't zero; give "
                                                        "it more decimals, or write it as < 0.001 or in E notation"})
        places = {len(c.split(".")[1]) if "." in c and "e" not in c.lower() else 0 for _, c in nums}
        if len(places) > 1 and any(p > 0 for p in places):
            out.append({"level": "info", "message": f"{name(j)} shows numbers with {min(places)} to {max(places)} "
                                                    "decimals; decimals= rounds them alike"})
        if head is not None and j < len(head) and not head[j].strip():
            out.append({"level": "info", "message": f"column {j + 1} has no header"})
    return out


def resolve_align(align, auto: list[str]) -> list[str]:
    """`align` as given ("lrr", ["l", "c"], "center") over the automatic one."""
    if not align:
        return auto
    if isinstance(align, str) and align in ("left", "center", "right"):
        return [align[0]] * len(auto)
    given = [str(a)[0].lower() for a in align]
    return [given[j] if j < len(given) and given[j] in "lcr" else auto[j] for j in range(len(auto))]


# --- Word ---

_CT = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
</Types>"""
_RELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
</Relationships>"""
_CORE = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
<dc:title>{title}</dc:title><dc:creator>Fignimble</dc:creator></cp:coreProperties>"""
_W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def write_docx(rows: list, path: str | Path, header: bool = True, decimals=None, align=None,
               font: str = "Arial", size: float = 7, title: str | None = None, grid: bool = False,
               notes: str | None = None) -> str:
    """Write the table to a Word file: a real table (editable in Word), the figure's font and size, the header
    bold, a rule above and below the header and one at the bottom (`grid`: lines between every row and column and
    around the table). `title` goes above it as a paragraph, `notes` below it, a paragraph per line."""
    shown, auto = cells(rows, header, decimals)
    al = resolve_align(align, auto)
    half = max(2, int(round(size * 2)))                     # Word sizes are in half points
    rpr = f'<w:rPr><w:rFonts w:ascii="{escape(font)}" w:hAnsi="{escape(font)}" w:cs="{escape(font)}"/><w:sz w:val="{half}"/></w:rPr>'
    rprb = rpr.replace("<w:sz ", "<w:b/><w:sz ")             # the schema's order: rFonts, b, sz

    def para(text, bold=False, jc="left"):
        runs = []
        for k, line in enumerate(str(text).split("\n")):
            if k:
                runs.append("<w:r><w:br/></w:r>")
            runs.append(f'<w:r>{rprb if bold else rpr}<w:t xml:space="preserve">{escape(line)}</w:t></w:r>')
        return f'<w:p><w:pPr><w:spacing w:before="0" w:after="0"/><w:jc w:val="{jc}"/></w:pPr>{"".join(runs)}</w:p>'

    jcs = {"l": "left", "c": "center", "r": "right"}
    body = []
    if title:
        body.append(para(title, bold=True))
    rule = '<w:{side} w:val="single" w:sz="{sz}" w:space="0" w:color="000000"/>'
    tbl = ['<w:tbl><w:tblPr><w:tblW w:w="0" w:type="auto"/><w:tblBorders>'
           + rule.format(side="top", sz=8)
           + (rule.format(side="left", sz=8) if grid else "")
           + rule.format(side="bottom", sz=8)
           + (rule.format(side="right", sz=8) + rule.format(side="insideH", sz=4)
              + rule.format(side="insideV", sz=4) if grid else "")
           + '</w:tblBorders><w:tblLayout w:type="autofit"/>'
           '<w:tblCellMar><w:left w:w="80" w:type="dxa"/><w:right w:w="80" w:type="dxa"/></w:tblCellMar></w:tblPr>']
    # column widths (twentieths of a point) from the longest text in each, which Word's autofit then refines
    widths = [int(max(len(r[j]) for r in shown) * size * 0.6 * 20 + 2 * 80 + 120) for j in range(len(al))]
    tbl.append("<w:tblGrid>" + "".join(f'<w:gridCol w:w="{w}"/>' for w in widths) + "</w:tblGrid>")
    for i, r in enumerate(shown):
        head = header and i == 0
        tr = ["<w:tr>" + ("<w:trPr><w:tblHeader/></w:trPr>" if head else "")]
        for j, c in enumerate(r):
            borders = ("<w:tcBorders>" + rule.format(side="bottom", sz=4) + "</w:tcBorders>") if head and not grid else ""
            tr.append(f'<w:tc><w:tcPr><w:tcW w:w="{widths[j]}" w:type="dxa"/>{borders}</w:tcPr>'
                      f"{para(c, bold=head, jc=jcs[al[j]])}</w:tc>")
        tbl.append("".join(tr) + "</w:tr>")
    tbl.append("</w:tbl>")
    body.append("".join(tbl))
    for line in (notes or "").splitlines():
        body.append(para(line))
    body.append('<w:p/><w:sectPr><w:pgSz w:w="11906" w:h="16838"/>'
                '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>')
    doc = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<w:document xmlns:w="{_W}"><w:body>'
           + "".join(body) + "</w:body></w:document>")
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", _CT)
        z.writestr("_rels/.rels", _RELS)
        z.writestr("docProps/core.xml", _CORE.format(title=escape(title or path.stem)))
        z.writestr("word/document.xml", doc)
    return str(path.resolve())


# --- Excel ---

_XCT = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
</Types>"""
_XRELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
</Relationships>"""
_XWB = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets><sheet name="{name}" sheetId="1" r:id="rId1"/></sheets></workbook>"""
_XWBRELS = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>"""
_XSTYLES = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<fonts count="2"><font><sz val="11"/><name val="{font}"/></font><font><b/><sz val="11"/><name val="{font}"/></font></fonts>
<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
<borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border>
<border><left/><right/><top style="thin"/><bottom style="thin"/><diagonal/></border></borders>
<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
<xf numFmtId="0" fontId="1" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1"/></cellXfs>
<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>"""


def _col(j: int) -> str:
    s = ""
    j += 1
    while j:
        j, r = divmod(j - 1, 26)
        s = chr(65 + r) + s
    return s


def write_xlsx(rows: list, path: str | Path, header: bool = True, decimals=None, font: str = "Arial",
               title: str | None = None, notes: str | None = None) -> str:
    """Write the table to an Excel workbook: numbers as numbers (rounded as on the figure), the header bold with
    rules above and below it; `notes` in the rows after it, below an empty one."""
    shown, _ = cells(rows, header, decimals)
    if notes:
        shown = shown + [[""]] + [[line] for line in notes.splitlines()]
    out = []
    for i, r in enumerate(shown):
        head = header and i == 0
        cs = []
        for j, c in enumerate(r):
            ref = f"{_col(j)}{i + 1}"
            st = ' s="1"' if head else ""
            v = None if head else _number(c)
            if v is not None and c.strip():
                cs.append(f'<c r="{ref}"{st}><v>{repr(v) if v != int(v) else int(v)}</v></c>')
            elif c != "":
                cs.append(f'<c r="{ref}"{st} t="inlineStr"><is><t xml:space="preserve">{escape(c)}</t></is></c>')
        out.append(f'<row r="{i + 1}">{"".join(cs)}</row>')
    n = max((len(r) for r in shown), default=1)
    widths = [min(60, max(8, max((len(r[j]) for r in shown if j < len(r) and (not notes or len(r) > 1)),
                                 default=0) + 2)) for j in range(n)]
    cols = "<cols>" + "".join(f'<col min="{j + 1}" max="{j + 1}" width="{w}" customWidth="1"/>'
                              for j, w in enumerate(widths)) + "</cols>"
    sheet = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
             '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
             f'{cols}<sheetData>{"".join(out)}</sheetData></worksheet>')
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    name = escape((title or path.stem)[:31].translate({ord(ch): " " for ch in "[]:*?/\\"}) or "Table")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", _XCT)
        z.writestr("_rels/.rels", _XRELS)
        z.writestr("docProps/core.xml", _CORE.format(title=escape(title or path.stem)))
        z.writestr("xl/workbook.xml", _XWB.format(name=name))
        z.writestr("xl/_rels/workbook.xml.rels", _XWBRELS)
        z.writestr("xl/styles.xml", _XSTYLES.format(font=escape(font)))
        z.writestr("xl/worksheets/sheet1.xml", sheet)
    return str(path.resolve())


def write_table(rows: list, path: str | Path, fmt: str | None = None, header: bool = True, decimals=None,
                align=None, font: str = "Arial", size: float = 7, title: str | None = None,
                grid: bool = False, notes: str | None = None) -> str:
    """The table as a file people edit: .docx (a Word table), .xlsx (an Excel sheet), .csv or .tsv (the cells as
    shown, rounded as on the figure; without the title and notes)."""
    fmt = (fmt or Path(path).suffix.lstrip(".")).lower()
    if fmt == "docx":
        return write_docx(rows, path, header=header, decimals=decimals, align=align, font=font, size=size, title=title,
                          grid=grid, notes=notes)
    if fmt == "xlsx":
        return write_xlsx(rows, path, header=header, decimals=decimals, font=font, title=title, notes=notes)
    if fmt in ("csv", "tsv"):
        shown, _ = cells(rows, header, decimals)
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(write_delimited(shown, "\t" if fmt == "tsv" else ","), encoding="utf-8")
        return str(path.resolve())
    raise ValueError(f"a table can't be written as {fmt}: use docx, xlsx, csv or tsv")


TABLE_FORMATS = ("docx", "xlsx", "csv", "tsv")


def write_rendered(data: dict, path: str | Path, fmt: str | None = None, oid=None, title: str | None = None) -> str:
    """Write a table of a render (`Renderer.render`'s result) as a file people edit. Without `oid`: the figure's only
    table. A Table's title and notes come with it (`title` replaces the title)."""
    found = [e for e in data["elements"] if e["kind"] == "table"]
    if oid is not None:
        found = [e for e in data["elements"] if str(e["oid"]) == str(oid) or e["props"].get("name") == oid]
        if not found:
            raise ValueError(f"there is no element {oid}")
        if found[0]["kind"] != "table":
            raise ValueError("only a table can be exported as a Word, Excel, CSV or TSV file")
    if not found:
        raise ValueError("this figure has no table")
    if len(found) > 1:
        raise ValueError("this figure has more than one table; say which (its element number, as fignimble info "
                         "shows it, or its name)")
    p = found[0]["props"]
    if p.get("cells") is None:
        raise ValueError("the table didn't draw; fix it first")
    parts = {e["props"].get("role"): e["props"].get("text") for e in data["elements"] if e.get("method") == "Table"}
    st = data["style"]["props"]
    return write_table(p["cells"], path, fmt, header=p.get("header", True), decimals=p.get("decimals"),
                       align=p.get("align"), font=p.get("font") or st.get("font") or "Arial",
                       size=p.get("size") or st.get("size") or 7, title=title or parts.get("title"),
                       grid=bool(p.get("grid")), notes=parts.get("notes"))
