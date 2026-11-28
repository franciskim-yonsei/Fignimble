# Installation

## Download

1. Download `Fignimble-<version>-macOS.dmg` from the
   [latest release](https://github.com/pachterlab/Fignimble/releases/latest). It runs on Apple Silicon and Intel Macs
   with macOS 13 or later, and is signed and notarized by Apple.
2. Open the disk image and drag **Fignimble** to **Applications**.
3. Open Fignimble.

## Python

Fignimble draws figures with Python. The first time it opens, it looks for a Python that has matplotlib, PyMuPDF and
Pillow. If it finds none, it offers two choices:

**Set Up Python** (recommended)
:   Downloads Python 3.11 with matplotlib, PyMuPDF, Pillow, NumPy and BioCartoon into
    `~/Library/Application Support/Fignimble`. It takes about 260 MB and usually under a minute, needs no
    administrator password, and changes nothing else on your Mac.

**Choose a Python…**
:   Uses a Python of your own (3.10 or later), such as a conda or venv environment, that has the packages installed:
    `pip install matplotlib "pymupdf<2" pillow numpy`. Pick the environment's `python3` itself (for example
    `…/envs/myenv/bin/python3`), not `ipython` or a wrapper script.

### Packages your panels need

Panels can import anything: pandas, seaborn, statsmodels, scanpy. List the packages a project's panels need, beyond
matplotlib and NumPy, in a `requirements.txt` in the project folder:

```text
pandas
seaborn>=0.13
statsmodels
```

When the project opens, Fignimble checks them against its Python and offers to install the missing ones with pip.
It does the same for packages Fignimble itself needs. **Fignimble ▸ Check Packages…** checks again.

## The `fignimble` command

The `fignimble` command renders, checks and exports figures from a terminal, and lets a coding agent look at its own
work. It is ready in Fignimble's terminal. To use it in Terminal, VS Code or an agent running elsewhere, choose
**Fignimble ▸ Install Command Line Tool…**: it puts the command in `~/.local/bin` and offers to add that folder to
your PATH. See [the fignimble command](../reference/cli.md).

## Optional

- **Ghostscript** for EPS export, CMYK conversion and text as outlines: `brew install ghostscript`.
- **BioCartoon** icon search: Fignimble's Python setup includes it. With a Python of your own,
  `pip install biocartoon`, or let the BioCartoon pane install it.

## From source

On macOS 13 or later with Xcode or its command line tools, and Python 3.10 or later:

```bash
git clone https://github.com/pachterlab/Fignimble
cd Fignimble
./install_fignimble.sh          # Python in ~/Library/Application Support/Fignimble/python, the app in
                                # ~/Applications, the fignimble command in ~/.local/bin
open ~/Applications/Fignimble.app --args "$PWD/examples/demo"
```

See [how Fignimble works](../developers/architecture.md) for building and testing.
