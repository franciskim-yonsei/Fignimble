"""Figure sizes, label conventions and resolutions of journals: computational biology's especially, and the main
chemistry and materials journals (ACS, RSC, Elsevier, Wiley).

Widths are in millimeters. `dpi` is the least resolution, as printed, asked of raster figures and of the pictures
inside vector ones: "photo" for photographs and micrographs alone (halftones), "combination" for pictures with text
or line art, "line" for line art alone (plots, diagrams, text). Export never goes below them.

`checked` says where the numbers come from: "guidelines" (the journal's own author guidelines, November 2026),
"secondary" (summaries of them; the journal's site refused automated reading), or "publisher" (the publisher's
usual values, not checked for this journal). Journals revise their rules: check before submitting, and correct an
entry here when it is wrong.
"""

# what most journals ask when no journal is named (and the usual production standard)
DEFAULT_DPI = {"photo": 300, "combination": 600, "line": 1200}

_NATURE = {"font": "Arial", "font_size": 7, "min_font": 5, "dpi": {"photo": 300, "combination": 600, "line": 1200},
           "labels": {"case": "lower", "weight": "bold", "size": 8, "format": "{}"}}
_CELL = {"publisher": "Cell Press", "widths": {"single": 85, "one-and-a-half": 114, "double": 174}, "font": "Arial",
         "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 500, "line": 1000},
         "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "secondary",
         "source": "https://www.cell.com/figureguidelines"}
_PLOS = {"publisher": "PLOS", "widths": {"column": 132, "full": 190}, "font": "Arial", "font_size": 8, "min_font": 8,
         # 300-600 dpi; PLOS resizes figures above 600 dpi, so line art stays at 600
         "dpi": {"photo": 300, "combination": 600, "line": 600}, "max_dpi": 600,
         "labels": {"case": "upper", "weight": "bold", "size": 10, "format": "{}"}, "checked": "guidelines",
         "note": "TIFF or EPS only; Arial, Times or Symbol at 8-12 pt; 6.68-19.05 cm wide, at most 22.23 cm high"}
_OUP = {"publisher": "Oxford University Press", "widths": {"single": 86, "double": 178}, "font": "Arial",
        "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 600, "line": 1200},
        "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "publisher"}
_BMC = {"publisher": "BMC (Springer Nature)", "widths": {"single": 85, "double": 170}, "font": "Arial", "font_size": 7,
        "min_font": 6, "dpi": {"photo": 300, "combination": 600, "line": 1200},
        "labels": {"case": "lower", "weight": "bold", "size": 8, "format": "{}"}, "checked": "publisher",
        "source": "https://www.biomedcentral.com/getpublished/writing-resources/data-presentation"}
_AAAS = {"publisher": "AAAS", "widths": {"single": 57, "double": 121, "triple": 184}, "font": "Helvetica",
         "font_size": 7, "min_font": 5, "dpi": {"photo": 300, "combination": 600, "line": 1200},
         "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}}

# --- chemistry and materials ---
_ACS_GUIDE = "https://researcher-resources.acs.org/publish/author_guidelines?coden="
# one column up to 3.33 in (240 pt), two 4.167-7 in (300-504 pt), at most 9.167 in deep with the caption; Helvetica
# or Arial, no smaller than 4.5 pt printed; 300 dpi color, 600 dpi grayscale, 1200 dpi black-and-white line art
_ACS = {"publisher": "American Chemical Society", "widths": {"single": 84.6, "double": 177.8}, "font": "Arial",
        "font_size": 7, "min_font": 4.5, "dpi": {"photo": 300, "combination": 600, "line": 1200},
        "labels": {"case": "lower", "weight": "bold", "size": 8, "format": "({})"},
        "note": "one column up to 3.33 in, two 4.17-7 in, at most 9.17 in deep with the caption"}
# one column 8.3 cm, two 17.1 cm, at most 23.3 cm long; TIFF at 600 dpi or more; Helvetica or Arial
_RSC = {"publisher": "Royal Society of Chemistry", "widths": {"single": 83, "double": 171}, "font": "Arial",
        "font_size": 7, "min_font": 5, "dpi": {"photo": 600, "combination": 600, "line": 600},
        "labels": {"case": "lower", "weight": "normal", "size": 8, "format": "({})"}, "checked": "secondary",
        "note": "one column 8.3 cm, two 17.1 cm, at most 23.3 cm long; at least 600 dpi",
        "source": "https://www.rsc.org/journals-books-databases/author-and-reviewer-hub/authors-information/prepare-and-format/figures-graphics-images"}
# 90, 140 or 190 mm wide; 300 dpi halftones, 500 combinations, 1000 line art; 7 pt text, sub- and superscripts at 6
_ELSEVIER = {"publisher": "Elsevier", "widths": {"single": 90, "one-and-a-half": 140, "double": 190}, "font": "Arial",
             "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 500, "line": 1000},
             "labels": {"case": "lower", "weight": "normal", "size": 8, "format": "({})"}, "checked": "guidelines",
             "source": "https://www.elsevier.com/authors/policies-and-guidelines/artwork-and-media-instructions/artwork-sizing"}
# 80-180 mm wide at 300-600 dpi: 300 for photographs, 600 for line art and combinations. Wiley-VCH's materials and
# chemistry journals letter panels a), b), ...
_WILEY = {"publisher": "Wiley", "widths": {"single": 85, "double": 180}, "font": "Arial", "font_size": 7, "min_font": 5,
          "dpi": {"photo": 300, "combination": 600, "line": 600},
          "labels": {"case": "lower", "weight": "bold", "size": 8, "format": "{})"}, "checked": "publisher",
          "note": "80-180 mm wide, 300 dpi for photographs and 600 dpi for line art",
          "source": "https://authors.wiley.com/author-resources/Journal-Authors/Prepare/manuscript-preparation-guidelines.html/figure-preparation.html"}


def _j(base: dict, name: str, **kw) -> dict:
    return {**base, "name": name, **kw}


JOURNALS = {
    # --- Nature Portfolio ---
    "nature": _j(_NATURE, "Nature", publisher="Nature Portfolio", widths={"single": 89, "one-and-a-half": 120, "double": 183},
                 checked="guidelines", source="https://www.nature.com/nature/for-authors/final-submission"),
    "nature-methods": _j(_NATURE, "Nature Methods", publisher="Nature Portfolio", widths={"single": 89, "double": 183},
                         checked="publisher", source="https://research-figure-guide.nature.com/figures/preparing-figures-our-specifications/"),
    "nature-biotechnology": _j(_NATURE, "Nature Biotechnology", publisher="Nature Portfolio", widths={"single": 89, "double": 183},
                               checked="publisher", source="https://research-figure-guide.nature.com/figures/preparing-figures-our-specifications/"),
    "nature-genetics": _j(_NATURE, "Nature Genetics", publisher="Nature Portfolio", widths={"single": 89, "double": 183},
                          checked="publisher", source="https://research-figure-guide.nature.com/figures/preparing-figures-our-specifications/"),
    "nature-communications": _j(_NATURE, "Nature Communications", publisher="Nature Portfolio",
                                widths={"single": 88, "double": 180}, checked="guidelines",
                                labels={"case": "lower", "weight": "bold", "size": 7, "format": "{}"},
                                source="https://www.nature.com/ncomms/submit/how-to-submit"),
    "communications-biology": _j(_NATURE, "Communications Biology", publisher="Nature Portfolio",
                                 widths={"single": 88, "double": 180}, checked="publisher",
                                 labels={"case": "lower", "weight": "bold", "size": 7, "format": "{}"}),
    "scientific-reports": _j(_NATURE, "Scientific Reports", publisher="Nature Portfolio",
                             widths={"single": 88, "double": 180}, checked="publisher",
                             labels={"case": "lower", "weight": "bold", "size": 7, "format": "{}"}),
    # --- Cell Press ---
    "cell": _j(_CELL, "Cell"),
    "cell-systems": _j(_CELL, "Cell Systems"),
    "cell-reports": _j(_CELL, "Cell Reports"),
    "cell-genomics": _j(_CELL, "Cell Genomics"),
    "patterns": _j(_CELL, "Patterns"),
    "iscience": _j(_CELL, "iScience"),
    # --- AAAS ---
    "science": _j(_AAAS, "Science", checked="publisher"),
    "science-advances": _j(_AAAS, "Science Advances", checked="publisher"),
    # --- PNAS ---
    "pnas": {"name": "PNAS", "publisher": "National Academy of Sciences", "widths": {"single": 87, "one-and-a-half": 114, "double": 178},
             "font": "Arial", "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 600, "line": 1200},
             "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "secondary",
             "note": "8.7, 11.4 or 17.8 cm wide, at most 22.5 cm high"},
    # --- PLOS ---
    "plos-computational-biology": _j(_PLOS, "PLOS Computational Biology", source="https://journals.plos.org/ploscompbiol/s/figures"),
    "plos-biology": _j(_PLOS, "PLOS Biology", source="https://journals.plos.org/plosbiology/s/figures"),
    "plos-genetics": _j(_PLOS, "PLOS Genetics", source="https://journals.plos.org/plosgenetics/s/figures"),
    "plos-one": _j(_PLOS, "PLOS ONE", source="https://journals.plos.org/plosone/s/figures"),
    # --- Oxford University Press ---
    "bioinformatics": _j(_OUP, "Bioinformatics", checked="secondary", dpi={"photo": 350, "combination": 600, "line": 1200},
                         source="https://academic.oup.com/bioinformatics/pages/author-guidelines"),
    "bioinformatics-advances": _j(_OUP, "Bioinformatics Advances"),
    "briefings-in-bioinformatics": _j(_OUP, "Briefings in Bioinformatics"),
    "nar": _j(_OUP, "Nucleic Acids Research", source="https://academic.oup.com/nar/pages/general_instructions"),
    "nar-genomics-bioinformatics": _j(_OUP, "NAR Genomics and Bioinformatics"),
    "gigascience": _j(_OUP, "GigaScience", source="https://academic.oup.com/gigascience/pages/instructions_to_authors"),
    "mbe": _j(_OUP, "Molecular Biology and Evolution"),
    "gbe": _j(_OUP, "Genome Biology and Evolution"),
    # --- BMC ---
    "genome-biology": _j(_BMC, "Genome Biology"),
    "genome-medicine": _j(_BMC, "Genome Medicine"),
    "bmc-bioinformatics": _j(_BMC, "BMC Bioinformatics"),
    "bmc-genomics": _j(_BMC, "BMC Genomics"),
    "bmc-biology": _j(_BMC, "BMC Biology"),
    "algorithms-for-molecular-biology": _j(_BMC, "Algorithms for Molecular Biology"),
    "microbiome": _j(_BMC, "Microbiome"),
    # --- others ---
    "genome-research": {"name": "Genome Research", "publisher": "Cold Spring Harbor Laboratory Press",
                        "widths": {"single": 85, "double": 178}, "font": "Arial", "font_size": 7, "min_font": 6,
                        # 300 for halftones and 600-900 for combinations are the journal's; line art as at its sister journals
                        "dpi": {"photo": 300, "combination": 600, "line": 1200},
                        "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "secondary",
                        "source": "https://genome.cshlp.org/ifora_digartsubm"},
    "msb": {"name": "Molecular Systems Biology", "publisher": "EMBO Press", "widths": {"single": 85, "double": 174},
            "font": "Arial", "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 600, "line": 1200},
            "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "publisher"},
    "elife": {"name": "eLife", "publisher": "eLife", "widths": {"at least": 100, "full page": 200}, "font": "Arial",
              "font_size": 7, "min_font": 6, "dpi": {"photo": 300, "combination": 300, "line": 300},
              "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "{}"}, "checked": "guidelines",
              "note": "at least 300 dpi, uploaded at least 10 cm wide (20 cm for a full-page figure)",
              "source": "https://reviewer.elifesciences.org/author-guide/full"},
    "frontiers": {"name": "Frontiers journals", "publisher": "Frontiers", "widths": {"single": 85, "double": 180},
                  "font": "Arial", "font_size": 8, "min_font": 6, "dpi": {"photo": 300, "combination": 300, "line": 300},
                  "labels": {"case": "upper", "weight": "bold", "size": 9, "format": "({})"}, "checked": "guidelines",
                  "source": "https://www.frontiersin.org/guidelines/author-guidelines"},
    "ieee-tcbb": {"name": "IEEE/ACM TCBB", "publisher": "IEEE", "widths": {"single": 88.9, "double": 181.9},
                  "font": "Times New Roman", "font_size": 8, "min_font": 8, "dpi": {"photo": 300, "combination": 600, "line": 600},
                  "labels": {"case": "lower", "weight": "normal", "size": 8, "format": "({})"}, "checked": "guidelines",
                  "note": "one column 3.5 in, two 7.16 in, at most 7.16 x 8.8 in",
                  "source": "https://journals.ieeeauthorcenter.ieee.org/create-your-ieee-journal-article/create-graphics-for-your-article/resolution-and-size/"},
    # --- American Chemical Society ---
    "jacs": _j(_ACS, "Journal of the American Chemical Society", checked="guidelines", source=_ACS_GUIDE + "jacsat"),
    "acs-nano": _j(_ACS, "ACS Nano", checked="publisher", source=_ACS_GUIDE + "ancac3"),
    "nano-letters": _j(_ACS, "Nano Letters", checked="publisher", source=_ACS_GUIDE + "nalefd"),
    "chemistry-of-materials": _j(_ACS, "Chemistry of Materials", checked="publisher", source=_ACS_GUIDE + "cmatex"),
    "acs-ami": _j(_ACS, "ACS Applied Materials & Interfaces", checked="publisher", source=_ACS_GUIDE + "aamick"),
    "acs-catalysis": _j(_ACS, "ACS Catalysis", checked="publisher", source=_ACS_GUIDE + "accacs"),
    "acs-energy-letters": _j(_ACS, "ACS Energy Letters", checked="publisher", source=_ACS_GUIDE + "aelccp"),
    "analytical-chemistry": _j(_ACS, "Analytical Chemistry", checked="publisher", source=_ACS_GUIDE + "ancham"),
    "jpcc": _j(_ACS, "The Journal of Physical Chemistry C", checked="publisher", source=_ACS_GUIDE + "jpccck"),
    "langmuir": _j(_ACS, "Langmuir", checked="publisher", source=_ACS_GUIDE + "langd5"),
    # --- Royal Society of Chemistry ---
    "chemical-science": _j(_RSC, "Chemical Science"),
    "chemcomm": _j(_RSC, "Chemical Communications"),
    "jmca": _j(_RSC, "Journal of Materials Chemistry A"),
    "nanoscale": _j(_RSC, "Nanoscale"),
    "ees": _j(_RSC, "Energy & Environmental Science"),
    "pccp": _j(_RSC, "Physical Chemistry Chemical Physics"),
    "rsc-advances": _j(_RSC, "RSC Advances"),
    "dalton": _j(_RSC, "Dalton Transactions"),
    # --- Elsevier ---
    "materials-today": _j(_ELSEVIER, "Materials Today"),
    "acta-materialia": _j(_ELSEVIER, "Acta Materialia"),
    "carbon": _j(_ELSEVIER, "Carbon"),
    "nano-energy": _j(_ELSEVIER, "Nano Energy"),
    "journal-of-power-sources": _j(_ELSEVIER, "Journal of Power Sources"),
    "chemical-engineering-journal": _j(_ELSEVIER, "Chemical Engineering Journal"),
    "applied-catalysis-b": _j(_ELSEVIER, "Applied Catalysis B: Environment and Energy"),
    # --- Wiley ---
    "advanced-materials": _j(_WILEY, "Advanced Materials"),
    "advanced-functional-materials": _j(_WILEY, "Advanced Functional Materials"),
    "advanced-energy-materials": _j(_WILEY, "Advanced Energy Materials"),
    "advanced-science": _j(_WILEY, "Advanced Science"),
    "small": _j(_WILEY, "Small"),
    "angewandte": _j(_WILEY, "Angewandte Chemie"),
}

# the order journals are offered in: by publisher, as listed above
PUBLISHERS = list(dict.fromkeys(j["publisher"] for j in JOURNALS.values()))


def dpi(journal: str | None) -> dict:
    """The resolutions (dpi) `journal` asks for: {photo, combination, line}."""
    j = JOURNALS.get((journal or "").lower())
    return dict(j["dpi"]) if j else dict(DEFAULT_DPI)


def width(journal: str, columns="double") -> float:
    """The figure width (mm) of `journal` for `columns` ("single", "double", 1, 2, ...). A journal without that
    name of width gives its narrowest for "single" and widest for "double"."""
    j = JOURNALS[journal.lower()]
    if isinstance(columns, int):
        columns = {1: "single", 2: "double", 3: "triple"}.get(columns, "double")
    ws = j["widths"]
    if columns in ws:
        return float(ws[columns])
    if columns in ("single", "double"):
        return float((min if columns == "single" else max)(ws.values()))
    raise ValueError(f"{j['name']} has no {columns!r} width; it has {', '.join(ws)}")


# --- posters ---
#
# Sizes (mm, portrait: width x height) of the usual conference posters, and type sizes for reading them from a meter or
# two: `body` for body text, `min_font` the smallest the preflight check accepts (captions, tick labels of the
# figures placed on it, at the size they are printed), `title` and `heading` for the template, `labels` for panel
# letters. `margin` (mm) is kept clear of anything that must print: poster printers trim, and some can't print to the
# edge. Posters are printed at 150-300 dpi at full size, so pictures need far fewer pixels per inch than in a journal.
POSTER_DPI = {"photo": 150, "combination": 200, "line": 300}
POSTER_MAX_DPI = 300            # rasters above this, at a poster's size, are hundreds of megapixels for nothing

_HUGE = {"body": 32, "min_font": 22, "title": 120, "heading": 54, "labels": 42, "linewidth": 1.8, "margin": 25}
_BIG = {"body": 28, "min_font": 18, "title": 96, "heading": 44, "labels": 36, "linewidth": 1.5, "margin": 20}
_MID = {"body": 22, "min_font": 14, "title": 72, "heading": 34, "labels": 28, "linewidth": 1.2, "margin": 15}
_SMALL = {"body": 16, "min_font": 10, "title": 54, "heading": 26, "labels": 20, "linewidth": 1.0, "margin": 12}
# keys are width x height as the poster stands upright; "48x36" (the other way round) means the same poster turned
POSTERS = {
    "a0": {"name": "A0", "size": (841, 1189), **_BIG},
    "a1": {"name": "A1", "size": (594, 841), **_MID},
    "a2": {"name": "A2", "size": (420, 594), **_SMALL},
    "b1": {"name": "B1", "size": (707, 1000), **_MID},
    "24x36": {"name": "24 x 36 in", "size": (609.6, 914.4), **_MID},
    "36x48": {"name": "36 x 48 in", "size": (914.4, 1219.2), **_BIG},
    "42x56": {"name": "42 x 56 in", "size": (1066.8, 1422.4), **_BIG},
    "46x46": {"name": "46 x 46 in", "size": (1168.4, 1168.4), **_BIG},
    "36x72": {"name": "36 x 72 in", "size": (914.4, 1828.8), **_BIG},
    "48x72": {"name": "48 x 72 in", "size": (1219.2, 1828.8), **_HUGE},
}

# Starting points for New Figure and `fignimble new --poster`: a size the way it is usually hung, and where it is
# typical. Conferences change their boards from year to year: check the instructions of the one you go to.
POSTER_TEMPLATES = [
    {"key": "a0", "poster": "a0", "landscape": False, "name": "A0 portrait",
     "use": "the usual size at European meetings (ISMB/ECCB recommends it)"},
    {"key": "48x36", "poster": "36x48", "landscape": True, "name": "48 x 36 in landscape",
     "use": "the most common size at US meetings"},
    {"key": "36x48", "poster": "36x48", "landscape": False, "name": "36 x 48 in portrait",
     "use": "US meetings with portrait boards"},
    {"key": "72x48", "poster": "48x72", "landscape": True, "name": "72 x 48 in landscape",
     "use": "a whole 6 x 4 ft board, as at SfN"},
    {"key": "72x36", "poster": "36x72", "landscape": True, "name": "72 x 36 in landscape",
     "use": "a wide poster on a 4 x 8 ft board, as ASHG has recommended"},
    {"key": "46x46", "poster": "46x46", "landscape": False, "name": "46 x 46 in square",
     "use": "square limits, as at ISMB 2024"},
    {"key": "42x56", "poster": "42x56", "landscape": False, "name": "42 x 56 in portrait",
     "use": "large portrait boards"},
    {"key": "24x36", "poster": "24x36", "landscape": False, "name": "24 x 36 in portrait",
     "use": "workshops and machine learning meetings (NeurIPS 2024-25)"},
    {"key": "a1", "poster": "a1", "landscape": False, "name": "A1 portrait", "use": "workshops and smaller meetings"},
    {"key": "a0-landscape", "poster": "a0", "landscape": True, "name": "A0 landscape",
     "use": "European meetings with landscape boards"},
    {"key": "b1", "poster": "b1", "landscape": False, "name": "B1 portrait", "use": "common in Japan"},
    {"key": "a2", "poster": "a2", "landscape": False, "name": "A2 portrait", "use": "small sessions and retreats"},
]


def _key(name) -> str:
    return str(name).lower().replace(" ", "").replace("×", "x").replace("in", "")


def resolve(name: str) -> tuple[str, bool]:
    """(preset key, turned) of a poster name: a preset (a0, 36x48), a template (48x36, a0-landscape), or a size
    written the other way round, which means the poster turned."""
    k = _key(name)
    if k in POSTERS:
        return k, False
    t = next((t for t in POSTER_TEMPLATES if t["key"] == k), None)
    if t:
        return t["poster"], t["landscape"]
    if "x" in k:
        a, b = k.split("x", 1)
        if f"{b}x{a}" in POSTERS:
            return f"{b}x{a}", True
    raise ValueError(f"no poster size {name!r}; there are {', '.join(POSTERS)} "
                     f"(and turned: {', '.join(t['key'] for t in POSTER_TEMPLATES if t['landscape'])})")


def poster(name: str) -> dict:
    """A poster preset by name (see resolve)."""
    return POSTERS[resolve(name)[0]]


def poster_size(name: str, landscape: bool = False) -> tuple[float, float]:
    """(width, height) mm of a poster; `landscape` turns it (again, for a name that is already turned)."""
    key, turned = resolve(name)
    w, h = POSTERS[key]["size"]
    return (float(h), float(w)) if landscape != turned else (float(w), float(h))
