"""PowerPoint: a figure as a picture on a 16:9 slide (.pptx), or a poster on a slide its own size, written without
python-pptx.

The picture is a PNG at the export's resolution, with the same figure as SVG alongside, which PowerPoint 2019 and
365 show instead (so it stays sharp when scaled); Keynote and Google Slides use the PNG. The figure is placed at its
real size when it fits the slide, else scaled down to fit, and centered. Its caption, when it has one, is the
picture's alt text.
"""

from __future__ import annotations

import zipfile
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr

EMU_PER_MM = 36000
SLIDE_W, SLIDE_H = 12192000, 6858000          # 13.333 x 7.5 in
MARGIN = 228600                               # 0.25 in

_P = "http://schemas.openxmlformats.org/presentationml/2006/main"
_A = "http://schemas.openxmlformats.org/drawingml/2006/main"
_R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
_REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
_PKG = "http://schemas.openxmlformats.org/package/2006/relationships"
_NS = f'xmlns:a="{_A}" xmlns:r="{_R}" xmlns:p="{_P}"'

_CT = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="png" ContentType="image/png"/>
<Default Extension="svg" ContentType="image/svg+xml"/>
<Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
<Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>
<Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>
<Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
<Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>
<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
</Types>"""


def _rels(*rels) -> str:
    body = "".join(f'<Relationship Id="{i}" Type="{t}" Target="{target}"/>' for i, t, target in rels)
    return f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<Relationships xmlns="{_PKG}">{body}</Relationships>'


_SPTREE_EMPTY = ('<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>'
                 '<p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/>'
                 '<a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld>')

_MASTER = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<p:sldMaster {_NS}>'
           '<p:cSld><p:bg><p:bgRef idx="1001"><a:schemeClr val="bg1"/></p:bgRef></p:bg>'
           + _SPTREE_EMPTY[len('<p:cSld>'):]
           + '<p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" '
             'accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>'
             '<p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst>'
             '<p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>')

_LAYOUT = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<p:sldLayout {_NS} type="blank" preserve="1">'
           '<p:cSld name="Blank">' + _SPTREE_EMPTY[len('<p:cSld>'):]
           + '<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>')


def _theme() -> str:
    colors = [("dk1", '<a:sysClr val="windowText" lastClr="000000"/>'), ("lt1", '<a:sysClr val="window" lastClr="FFFFFF"/>'),
              ("dk2", '<a:srgbClr val="44546A"/>'), ("lt2", '<a:srgbClr val="E7E6E6"/>'),
              ("accent1", '<a:srgbClr val="0072B2"/>'), ("accent2", '<a:srgbClr val="D55E00"/>'),
              ("accent3", '<a:srgbClr val="009E73"/>'), ("accent4", '<a:srgbClr val="CC79A7"/>'),
              ("accent5", '<a:srgbClr val="E69F00"/>'), ("accent6", '<a:srgbClr val="56B4E9"/>'),
              ("hlink", '<a:srgbClr val="0563C1"/>'), ("folHlink", '<a:srgbClr val="954F72"/>')]
    clr = "".join(f"<a:{k}>{v}</a:{k}>" for k, v in colors)
    font = '<a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/>'
    fill = '<a:solidFill><a:schemeClr val="phClr"/></a:solidFill>'
    line = '<a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln>'
    return (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<a:theme xmlns:a="{_A}" name="Fignimble">'
            f'<a:themeElements><a:clrScheme name="Fignimble">{clr}</a:clrScheme>'
            f'<a:fontScheme name="Fignimble"><a:majorFont>{font}</a:majorFont><a:minorFont>{font}</a:minorFont></a:fontScheme>'
            f'<a:fmtScheme name="Fignimble"><a:fillStyleLst>{fill * 3}</a:fillStyleLst><a:lnStyleLst>{line * 3}</a:lnStyleLst>'
            '<a:effectStyleLst>' + '<a:effectStyle><a:effectLst/></a:effectStyle>' * 3 + '</a:effectStyleLst>'
            f'<a:bgFillStyleLst>{fill * 3}</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>')


MAX_SLIDE = 51206400                          # 56 in: PowerPoint's largest slide side
MIN_SLIDE = 914400                            # 1 in


def _presentation(sw: int = SLIDE_W, sh: int = SLIDE_H) -> str:
    return (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<p:presentation {_NS} saveSubsetFonts="1">'
            '<p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst>'
            '<p:sldIdLst><p:sldId id="256" r:id="rId2"/></p:sldIdLst>'
            f'<p:sldSz cx="{sw}" cy="{sh}"/><p:notesSz cx="6858000" cy="9144000"/>'
            '<p:defaultTextStyle/></p:presentation>')


def slide_size(width_mm: float, height_mm: float) -> tuple[int, int]:
    """A slide the size of a poster (EMU), scaled down to PowerPoint's largest (56 in a side) when it is larger."""
    w, h = width_mm * EMU_PER_MM, height_mm * EMU_PER_MM
    k = min(1.0, MAX_SLIDE / w, MAX_SLIDE / h)
    return max(MIN_SLIDE, int(w * k)), max(MIN_SLIDE, int(h * k))


def place(width_mm: float, height_mm: float, sw: int = SLIDE_W, sh: int = SLIDE_H,
          margin: int = MARGIN) -> tuple[int, int, int, int]:
    """Where the figure goes on the slide (EMU): real size when it fits inside the margins, else scaled to fit."""
    w, h = width_mm * EMU_PER_MM, height_mm * EMU_PER_MM
    k = min(1.0, (sw - 2 * margin) / w, (sh - 2 * margin) / h)
    w, h = int(w * k), int(h * k)
    return (sw - w) // 2, (sh - h) // 2, w, h


def write_pptx(path: str | Path, png: bytes, width_mm: float, height_mm: float, svg: str | None = None,
               title: str = "Figure", alt: str | None = None, slide: tuple | None = None) -> str:
    """Write a one-slide presentation holding the figure (PNG, and SVG when given). `slide` (w, h mm): a slide
    of that size, the figure filling it (a poster, which conferences often want as PowerPoint), instead of 16:9."""
    sw, sh = slide_size(*slide) if slide else (SLIDE_W, SLIDE_H)
    x, y, w, h = place(width_mm, height_mm, sw, sh, 0 if slide else MARGIN)
    ext = ""
    if svg is not None:
        ext = ('<a:extLst><a:ext uri="{96DAC541-7B7A-43D3-8B79-37D633B846F1}">'
               '<asvg:svgBlip xmlns:asvg="http://schemas.microsoft.com/office/drawing/2016/SVG/main" r:embed="rId3"/>'
               '</a:ext></a:extLst>')
    descr = f" descr={quoteattr(alt)}" if alt else ""
    pic = (f'<p:pic><p:nvPicPr><p:cNvPr id="2" name={quoteattr(title)}{descr}/>'
           '<p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>'
           f'<p:blipFill><a:blip r:embed="rId2">{ext}</a:blip><a:stretch><a:fillRect/></a:stretch></p:blipFill>'
           f'<p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm>'
           '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>')
    slide = (f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<p:sld {_NS}>'
             + _SPTREE_EMPTY.replace("</p:spTree>", pic + "</p:spTree>")
             + '<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>')
    slide_rels = [("rId1", f"{_REL}/slideLayout", "../slideLayouts/slideLayout1.xml"),
                  ("rId2", f"{_REL}/image", "../media/image1.png")]
    if svg is not None:
        slide_rels.append(("rId3", f"{_REL}/image", "../media/image2.svg"))
    core = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
            f'xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>{escape(title)}</dc:title>'
            '<dc:creator>Fignimble</dc:creator></cp:coreProperties>')
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr("[Content_Types].xml", _CT)
        z.writestr("_rels/.rels", _rels(("rId1", f"{_REL}/officeDocument", "ppt/presentation.xml"),
                                        ("rId2", f"{_PKG}/metadata/core-properties", "docProps/core.xml")))
        z.writestr("docProps/core.xml", core)
        z.writestr("ppt/presentation.xml", _presentation(sw, sh))
        z.writestr("ppt/_rels/presentation.xml.rels", _rels(
            ("rId1", f"{_REL}/slideMaster", "slideMasters/slideMaster1.xml"),
            ("rId2", f"{_REL}/slide", "slides/slide1.xml"),
            ("rId3", f"{_REL}/theme", "theme/theme1.xml")))
        z.writestr("ppt/slideMasters/slideMaster1.xml", _MASTER)
        z.writestr("ppt/slideMasters/_rels/slideMaster1.xml.rels", _rels(
            ("rId1", f"{_REL}/slideLayout", "../slideLayouts/slideLayout1.xml"),
            ("rId2", f"{_REL}/theme", "../theme/theme1.xml")))
        z.writestr("ppt/slideLayouts/slideLayout1.xml", _LAYOUT)
        z.writestr("ppt/slideLayouts/_rels/slideLayout1.xml.rels", _rels(
            ("rId1", f"{_REL}/slideMaster", "../slideMasters/slideMaster1.xml")))
        z.writestr("ppt/slides/slide1.xml", slide)
        z.writestr("ppt/slides/_rels/slide1.xml.rels", _rels(*slide_rels))
        z.writestr("ppt/theme/theme1.xml", _theme())
        z.writestr("ppt/media/image1.png", png)
        if svg is not None:
            z.writestr("ppt/media/image2.svg", svg)
    return str(path.resolve())
