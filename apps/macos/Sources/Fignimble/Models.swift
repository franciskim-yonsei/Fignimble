import AppKit

/// A transparent picture of one element, and where it goes on the figure (mm, from the top-left).
struct Sprite {
    let file: String
    let rect: CGRect
}

/// A text a panel's code draws (an axis label, a tick label, a legend entry), where it is on the figure.
/// A colored mark a panel's code draws (a line, a scatter, a bar), where it is on the figure.
struct PanelMark {
    let key: String
    let color: String
    let box: CGRect
}

struct PanelText {
    let key: String
    let text: String
    let box: CGRect           // mm
    let size: Double          // pt
    let rotation: Double
    var color: String? = nil
    var bold = false
    var italic = false

    /// What the text is, in words: "x-axis label", "legend entry", ...
    var role: String {
        let k = key.split(separator: ".").last.map(String.init) ?? key
        let names: [(String, String)] = [("xlabel", "x-axis label"), ("ylabel", "y-axis label"), ("legendtitle", "legend title"),
                                         ("ltitle", "title"), ("rtitle", "title"), ("title", "title"), ("xtick", "x tick label"),
                                         ("ytick", "y tick label"), ("legend", "legend entry"), ("suptitle", "title"),
                                         ("supxlabel", "x-axis label"), ("supylabel", "y-axis label"), ("text", "text")]
        if let n = names.first(where: { k.hasPrefix($0.0) }) { return n.1 }
        return key.contains("legend") ? "legend entry" : "text"
    }
}

/// A text or a mark (line, bar, points) inside a panel, picked on the canvas for the inspector to change.
struct PanelPart: Equatable {
    let oid: String
    let key: String
    let isText: Bool
}

/// A boundary between two rows or columns of a layout, which can be dragged to resize them.
struct Divider {
    let vertical: Bool        // between columns
    let index: Int
    let at: Double            // mm
    let from: Double
    let to: Double
    let editable: Bool
}

/// Something on the figure as the engine last rendered it: a panel, picture, text, shape, a layout (and the panels
/// and collected legend in it), an inset, or a panel's label.
final class Element {
    let oid: String
    let kind: String          // panel, image, text, line, rect, ellipse, bracket, label
    let method: String
    var box: CGRect           // mm
    let sprite: Sprite?
    let props: [String: Any]
    let group: String?
    let error: String?
    let file: String?
    let line: Int?
    let codeFile: String?
    let codeLine: Int?
    let panelOid: String?     // a label's panel
    let text: String?         // a label's text
    let autoLabel: Bool
    /// A label unhooked from its panel: it stays put when the panel moves, and moves on its own.
    let detached: Bool
    let parentOid: String?    // the layout a panel is in
    let children: [String]
    let dividers: [Divider]
    let hostOid: String?      // the panel an inset is in
    let inner: String?        // what an inset is: panel or image
    let item: [String: Any]   // an inset's panel or picture settings
    let texts: [PanelText]
    let marks: [PanelMark]
    let hidden: Bool
    let locked: Bool
    let points: [CGPoint]     // a path's
    let sourceBox: CGRect?    // a zoom's source picture
    /// A figure placed whole (fig.figure): the figure it is (when it is one of the project's), and its scale.
    let figureName: String?
    let scale: Double?
    /// A text box's height it runs past (mm), and the size it was shrunk to fit (pt).
    let overflow: Double?
    /// a scale bar's pixel size as drawn, and where it came from ("given", "from x.tif's ImageJ metadata", "not set")
    let pixelSize: Double?
    let pixelFrom: String?
    let shrunk: Double?
    /// A table's columns (x, width) and rows (y, height), mm from its top-left corner: where each cell is.
    let gridCols: [(Double, Double)]
    let gridRows: [(Double, Double)]

    init(_ d: [String: Any]) {
        oid = d["oid"] as? String ?? "?"
        kind = d["kind"] as? String ?? "?"
        method = d["method"] as? String ?? kind
        box = Element.rect(d["box"]) ?? .zero
        if let s = d["sprite"] as? [String: Any], let f = s["file"] as? String {
            sprite = Sprite(file: f, rect: CGRect(x: num(s["x"]), y: num(s["y"]), width: num(s["w"]), height: num(s["h"])))
        } else { sprite = nil }
        props = d["props"] as? [String: Any] ?? [:]
        group = d["group"] as? String
        error = d["error"] as? String
        let site = d["site"] as? [String: Any]
        file = site?["file"] as? String
        line = site?["line"] as? Int
        let code = d["code"] as? [String: Any]
        codeFile = code?["file"] as? String
        codeLine = code?["line"] as? Int
        panelOid = d["panel"] as? String
        text = d["text"] as? String
        autoLabel = d["auto"] as? Bool ?? true
        detached = d["detached"] as? Bool ?? false
        parentOid = d["parent"] as? String
        children = d["children"] as? [String] ?? []
        dividers = (d["dividers"] as? [[String: Any]] ?? []).map {
            Divider(vertical: $0["axis"] as? String == "col", index: $0["index"] as? Int ?? 0, at: num($0["at"]),
                    from: num($0["from"]), to: num($0["to"]), editable: $0["editable"] as? Bool ?? false)
        }
        hostOid = d["host"] as? String
        inner = d["inner"] as? String
        item = d["item"] as? [String: Any] ?? [:]
        texts = (d["texts"] as? [[String: Any]] ?? []).compactMap { t in
            guard let k = t["key"] as? String, let b = Element.rect(t["box"]) else { return nil }
            return PanelText(key: k, text: t["text"] as? String ?? "", box: b, size: num(t["size"]), rotation: num(t["rotation"]),
                             color: t["color"] as? String, bold: t["bold"] as? Bool ?? false, italic: t["italic"] as? Bool ?? false)
        }
        marks = (d["marks"] as? [[String: Any]] ?? []).compactMap { m in
            guard let k = m["key"] as? String, let c = m["color"] as? String, let b = Element.rect(m["box"]) else { return nil }
            return PanelMark(key: k, color: c, box: b)
        }
        hidden = d["hidden"] as? Bool ?? false
        locked = d["locked"] as? Bool ?? false
        points = ((d["props"] as? [String: Any])?["points"] as? [[Any]] ?? []).map { CGPoint(x: num($0.first), y: num($0.count > 1 ? $0[1] : 0)) }
        sourceBox = Element.rect(d["sourceBox"])
        figureName = d["figure"] as? String
        scale = (d["scale"] as? NSNumber)?.doubleValue
        overflow = (d["overflow"] as? NSNumber)?.doubleValue
        pixelSize = (d["pixelSize"] as? NSNumber)?.doubleValue
        pixelFrom = d["pixelFrom"] as? String
        shrunk = (d["shrunk"] as? NSNumber)?.doubleValue
        let g = d["grid"] as? [String: Any]
        gridCols = (g?["cols"] as? [[Any]] ?? []).map { (num($0.first), num($0.count > 1 ? $0[1] : 0)) }
        gridRows = (g?["rows"] as? [[Any]] ?? []).map { (num($0.first), num($0.count > 1 ? $0[1] : 0)) }
    }

    /// Whether the canvas lets the pointer take it (hidden and locked elements are reached in the layers list).
    var pickable: Bool { !hidden && !locked }

    /// The layout a panel is in, or itself.
    var top: String { parentOid ?? oid }

    static func rect(_ v: Any?) -> CGRect? {
        guard let a = v as? [Any], a.count == 4 else { return nil }
        return CGRect(x: num(a[0]), y: num(a[1]), width: num(a[2]), height: num(a[3]))
    }

    var isLabel: Bool { kind == "label" }
    /// A table's cells as text, row by row.
    var tableCells: [[String]] {
        (props["cells"] as? [[Any]] ?? props["rows"] as? [[Any]] ?? []).map { $0.map { c -> String in
            if let n = c as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() { let v = n.doubleValue; return v == v.rounded() && abs(v) < 1e15 ? String(Int(v)) : "\(v)" }
            return c is NSNull ? "" : "\(c)"
        } }
    }
    /// The cell of a table under a figure point (mm): (row, column).
    func tableCell(at p: CGPoint) -> (Int, Int)? {
        let x = Double(p.x - box.minX), y = Double(p.y - box.minY)
        guard let i = gridRows.firstIndex(where: { y >= $0.0 && y < $0.0 + $0.1 }) ?? (y >= 0 && y < Double(box.height) ? (y < (gridRows.first?.0 ?? 0) ? 0 : gridRows.count - 1) : nil),
              let j = gridCols.firstIndex(where: { x >= $0.0 && x < $0.0 + $0.1 }), i >= 0, i < gridRows.count else { return nil }
        return (i, j)
    }
    /// Where a table's cell is on the figure (mm).
    func tableCellBox(_ i: Int, _ j: Int) -> CGRect? {
        guard i < gridRows.count, j < gridCols.count else { return nil }
        return CGRect(x: box.minX + gridCols[j].0, y: box.minY + gridRows[i].0, width: gridCols[j].1, height: gridRows[i].1)
    }
    var isLine: Bool { kind == "line" }
    func p(_ k: String) -> Double? { (props[k] as? NSNumber)?.doubleValue }
    func s(_ k: String) -> String? { props[k] as? String }
    /// The line's two ends (mm), for lines.
    var ends: (CGPoint, CGPoint)? {
        guard isLine, let x1 = p("x1"), let y1 = p("y1"), let x2 = p("x2"), let y2 = p("y2") else { return nil }
        return (CGPoint(x: x1, y: y1), CGPoint(x: x2, y: y2))
    }
    /// What the layers list and messages call it.
    func title(label: String?) -> String {
        switch kind {
        case "panel":
            let src = (s("source") ?? "code") as NSString
            let fn = s("function").flatMap { $0 == "draw" ? nil : ":" + $0 } ?? ""
            return (label.map { "\($0)  " } ?? "") + src.lastPathComponent + fn
        case "image": return (label.map { "\($0)  " } ?? "") + ((s("source") ?? "picture") as NSString).lastPathComponent
        case "text" where role == "title": return "Title  “" + (s("text") ?? "") + "”"
        case "text" where role == "notes": return "Notes  “" + (s("text") ?? "").replacingOccurrences(of: "\n", with: " · ") + "”"
        case "text": return s("source").map { "Text from " + ($0 as NSString).lastPathComponent } ?? "“" + (s("text") ?? "") + "”"
        case "figure": return (label.map { "\($0)  " } ?? "") + "Figure " + ((s("source") ?? "") as NSString).deletingPathExtension
        case "qr": return "QR code"
        case "line": return s("arrow") == nil ? "Line" : "Arrow"
        case "rect": return "Rectangle"
        case "ellipse": return "Ellipse"
        case "bracket": return "Bracket " + (s("text") ?? "")
        case "label": return "Label " + (text ?? "")
        case "layout": return "Layout (\(children.filter { !$0.hasSuffix("legend") }.count) panels)"
        case "legend": return parentOid != nil ? "Collected legend" : "Legend"
        case "inset": return "Inset " + ((item["source"] as? String ?? "") as NSString).lastPathComponent
        case "path": return (props["closed"] as? Bool ?? false) ? "Shape" : "Path"
        case "zoom": return "Zoom box"
        case "scalebar": return "Scale bar \(mm(p("length") ?? 0)) \(s("unit") ?? "")"
        case "table" where role == "cells": return "Cells" + (s("source").map { "  " + ($0 as NSString).lastPathComponent } ?? "")
        case "table": return "Table " + ((s("source") ?? "") as NSString).lastPathComponent
        default: return kind
        }
    }
    /// Which part of a Table it is: "title", "cells" or "notes" (nil on a figure).
    var role: String? { props["role"] as? String }
    var symbol: String {
        switch kind {
        case "panel": return "chart.xyaxis.line"
        case "image": return "photo"
        case "text": return "textformat"
        case "line": return s("arrow") == nil ? "line.diagonal" : "arrow.up.right"
        case "rect": return "rectangle"
        case "ellipse": return "circle"
        case "bracket": return "chevron.up"
        case "layout": return "rectangle.split.2x2"
        case "legend": return "list.bullet.rectangle"
        case "table": return "tablecells"
        case "inset": return "rectangle.inset.filled"
        case "path": return "scribble"
        case "zoom": return "plus.magnifyingglass"
        case "scalebar": return "ruler"
        case "figure": return "rectangle.on.rectangle"
        case "qr": return "qrcode"
        default: return "tag"
        }
    }
}

struct Warning {
    let level: String
    let oid: String?
    let message: String
}

struct Guide {
    let x: Double?
    let y: Double?
}

/// A rendered figure: its size, elements, labels, guides, style and preflight warnings.
final class FigureData {
    let name: String
    let file: String
    let size: CGSize                       // mm
    let elements: [Element]
    let labels: [Element]
    let guides: [Guide]
    let warnings: [Warning]
    let labelStyle: [String: Any]
    let labelPreset: String?
    let style: [String: Any]
    let figureProps: [String: Any]
    let journal: [String: Any]?
    /// A poster's preset (Figure(poster=...)): key, name, landscape, margin (mm), minFont.
    let poster: [String: Any]?
    let error: String?
    let pxPerMM: Double
    let background: String?
    let dark: Bool
    /// The figure's caption (fig.caption(...)): free text, not drawn on the figure.
    let caption: String
    /// The project's palette (Style(palette=...)), resolved: names (or numbers) and colors.
    let palette: [(String, String)]
    /// fignimble's named palettes.
    let palettes: [String: [String]]

    init(_ d: [String: Any]) {
        name = d["figure"] as? String ?? ""
        file = d["file"] as? String ?? ""
        let sz = d["size"] as? [Any] ?? [183, 120]
        size = CGSize(width: num(sz.first), height: num(sz.count > 1 ? sz[1] : 120))
        elements = (d["elements"] as? [[String: Any]] ?? []).map(Element.init)
        labels = (d["labels"] as? [[String: Any]] ?? []).map(Element.init)
        guides = (d["guides"] as? [[String: Any]] ?? []).map { Guide(x: ($0["x"] as? NSNumber)?.doubleValue, y: ($0["y"] as? NSNumber)?.doubleValue) }
        warnings = (d["warnings"] as? [[String: Any]] ?? []).map {
            Warning(level: $0["level"] as? String ?? "warning", oid: $0["oid"] as? String, message: $0["message"] as? String ?? "")
        }
        let ls = d["labelStyle"] as? [String: Any] ?? [:]
        labelStyle = ls["props"] as? [String: Any] ?? [:]
        labelPreset = ls["preset"] as? String
        style = (d["style"] as? [String: Any])?["props"] as? [String: Any] ?? [:]
        figureProps = d["figureProps"] as? [String: Any] ?? [:]
        journal = d["journal"] as? [String: Any]
        poster = d["poster"] as? [String: Any]
        error = d["error"] as? String
        pxPerMM = (d["pxPerMM"] as? NSNumber)?.doubleValue ?? 12
        background = d["background"] as? String
        dark = d["dark"] as? Bool ?? false
        caption = ((d["caption"] as? [String: Any])?["text"] as? String) ?? ""
        palette = ((d["style"] as? [String: Any])?["palette"] as? [[Any]] ?? []).compactMap { p in
            guard p.count == 2, let c = p[1] as? String else { return nil }
            return ("\(p[0])", c)
        }
        palettes = (d["palettes"] as? [String: [String]]) ?? [:]
    }

    var all: [Element] { elements + labels }
    func element(_ oid: String) -> Element? { all.first { $0.oid == oid } }
    func label(of panel: String) -> Element? { labels.first { $0.panelOid == panel } }
    func labelText(_ oid: String) -> String? { label(of: oid)?.text }
    /// A table of the paper (a Table file: its title, cells and notes, laid out for it).
    var isTable: Bool { figureProps["kind"] as? String == "table" }
    /// A table on a page of its own, a Table or an older figure holding only a table: it also exports as Word,
    /// Excel, CSV or TSV.
    var isTableFigure: Bool { isTable || (elements.count == 1 && elements[0].kind == "table") }
    /// The table of a table page: its cells.
    var tableCells: Element? { isTableFigure ? elements.first { $0.kind == "table" } : nil }
}

func num(_ v: Any?) -> Double { (v as? NSNumber)?.doubleValue ?? Double(v as? String ?? "") ?? 0 }

/// "12.5" for 12.5, "12" for 12.0: millimeters for fields.
func mm(_ v: Double) -> String {
    let r = (v * 100).rounded() / 100
    return r == r.rounded() ? String(Int(r)) : String(format: "%g", r)
}

extension NSColor {
    convenience init?(hex: String?) {
        guard var h = hex?.trimmingCharacters(in: .whitespaces), h.hasPrefix("#") else { return nil }
        h.removeFirst()
        guard h.count == 6, let v = Int(h, radix: 16) else { return nil }
        self.init(srgbRed: CGFloat((v >> 16) & 255) / 255, green: CGFloat((v >> 8) & 255) / 255, blue: CGFloat(v & 255) / 255, alpha: 1)
    }
    var hex: String {
        let c = usingColorSpace(.sRGB) ?? self
        return String(format: "#%02X%02X%02X", Int((c.redComponent * 255).rounded()), Int((c.greenComponent * 255).rounded()),
                      Int((c.blueComponent * 255).rounded()))
    }
}

/// Split view with a wider, easy-to-grab divider that still draws as a hairline. (From Manimble.)
final class GrabSplitView: NSSplitView, NSSplitViewDelegate {
    var collapsesLastPane = false { didSet { delegate = self } }
    var onResize: (() -> Void)?
    override var dividerThickness: CGFloat { 9 }
    func splitView(_ splitView: NSSplitView, canCollapseSubview subview: NSView) -> Bool {
        collapsesLastPane && subview === arrangedSubviews.last
    }
    func splitViewDidResizeSubviews(_ notification: Notification) { onResize?() }
    override func drawDivider(in rect: NSRect) {
        NSColor.separatorColor.setFill()
        if isVertical { NSRect(x: rect.midX - 0.5, y: rect.minY, width: 1, height: rect.height).fill() }
        else { NSRect(x: rect.minX, y: rect.midY - 0.5, width: rect.width, height: 1).fill() }
    }
}

/// A view whose y axis points down, like the figure's.
class FlippedView: NSView { override var isFlipped: Bool { true } }
