# Pictures and BioCartoon icons

```python
cells = fig.image("images/cells.png", x=4, y=63, w=52)              # h follows the picture's aspect
fig.image("images/schematic.svg", x=65, y=63, w=56, h=47)          # PDF and SVG stay vector
```

Pictures are PNG, TIFF, JPEG, PDF or SVG. **Insert ▸ Picture…** (<kbd>⇧</kbd><kbd>⌘</kbd><kbd>I</kbd>), dropping a
file, or pasting a picture from another app copies it into `images/` and places it. Pictures are lettered like
panels; give one `label=None` for no letter, or a letter of its own.

**Replace…** in the inspector swaps the file and keeps the place.

## Cropping, turning and mirroring

Double-click a picture (or **Object ▸ Crop Picture**, <kbd>⌥</kbd><kbd>⌘</kbd><kbd>K</kbd>) and drag its handles to
crop; drag outward to uncrop. In code, `crop=(left, top, right, bottom)` in fractions of the picture.

<img class="fg-shot" src="../../assets/crop.jpg" alt="Cropping a picture on the canvas">

`rotation=` turns a picture any angle (PDF and SVG in quarter turns), and `flip="h"` or `flip="v"` mirrors it; PDF
and SVG pictures stay vector when mirrored. The inspector and the **Object** menu have both.

## Zoom boxes and scale bars

```python
detail = fig.inset(Image("images/cells.png"), within=cells, x=0.6, y=0.6, w=0.38, h=0.38)
fig.zoom(cells, detail, region=(0.2, 0.2, 0.25, 0.25))       # a box and lines; the inset shows what's in the box
fig.scalebar(cells, length=50, unit="µm", pixel_size=0.325)  # sized from the picture's real pixels
fig.scalebar(stack, length=10, unit="µm")                     # the pixel size from the picture's metadata
```

**Insert ▸ Zoom Box** on one picture makes the magnified inset too; drag the box and the inset follows.
**Insert ▸ Scale Bar on Picture…** asks for the bar's length and the size of one pixel of the original picture.

Leave the pixel size out and Fignimble reads it from the picture: an OME-TIFF's physical pixel size, an ImageJ TIFF's
calibration, or a TIFF, PNG or JPEG resolution fine enough to be the microscope's (pixels under 2.5 µm; 300 dpi is a
print setting, not a pixel size). The inspector shows where it came from. Preflight warns when a scale bar has no
pixel size at all, since a bar drawn from a guess is wrong without looking wrong, and when a pixel size you typed
disagrees with the picture's metadata, as it does when a picture was resized after it was taken. The metadata
converts between µm, nm, mm, cm, m and Å, so `unit="nm"` works on a picture calibrated in microns.

## BioCartoon icons

**Insert ▸ BioCartoon Icons…** (<kbd>⇧</kbd><kbd>⌘</kbd><kbd>B</kbd>) or the **BioCartoon** toolbar button opens a
search of more than 15,000 open-licensed science icons from [BioCartoon](https://biocartoon.com) in place of the
inspector: cells, organelles, organisms, lab equipment. Filter by collection, category and license, then drag an
icon onto the figure or double-click it. It is saved into `images/` (as SVG when it is published as one, so it stays
vector) and placed without a panel letter.

<img class="fg-shot" src="../../assets/biocartoon.jpg" alt="The BioCartoon search pane beside the figure">

Select a placed icon and the inspector shows where it comes from: its title, author, collection, license, category
and ID, and the full credit. It says whether the license asks for credit in the legend, and has buttons to copy the
credit and to open the icon's page and its license. The record travels inside the picture file, so it is there
offline and in a colleague's copy of the project.

<img class="fg-figure" src="../../assets/biocartoon-credit.png" alt="The BioCartoon section of the inspector for a selected mouse icon" width="360">

## Schematics

A schematic made of shapes, text and icons can carry its own panel letter: give its background rectangle
`label="auto"`, and it is lettered with the panels in reading order.

```python
fig.rect(3, 75, 24, 26, stroke=None, fill="#EBEBEB", label="auto")
fig.image("images/mouse.svg", x=5, y=84, w=10, label=None, flip="h")
fig.text("Pre", x=15, y=77, align="center")
```
