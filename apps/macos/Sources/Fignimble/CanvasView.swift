import AppKit
import UniformTypeIdentifiers

/// The tools of the canvas: select and move, or draw a new element by dragging.
enum Tool: String, CaseIterable {
    case select, text, line, arrow, rect, ellipse, bracket, pen, zoom
    var key: String { ["select": "v", "text": "t", "line": "l", "arrow": "a", "rect": "r", "ellipse": "e", "bracket": "b",
                       "pen": "p", "zoom": "z"][rawValue]! }
    var title: String { ["select": "Select", "text": "Text", "line": "Line", "arrow": "Arrow", "rect": "Rectangle",
                         "ellipse": "Ellipse", "bracket": "Significance Bracket", "pen": "Pen (click points; double-click or Return ends; click the first point to close)",
                         "zoom": "Zoom (drag a box; ⌥-click zooms out)"][rawValue]! }
    var symbol: String { ["select": "cursorarrow", "text": "textformat", "line": "line.diagonal", "arrow": "arrow.up.right",
                          "rect": "rectangle", "ellipse": "circle", "bracket": "chevron.up", "pen": "pencil.tip",
                          "zoom": "magnifyingglass"][rawValue]! }
}

/// The figure on its paper: what the engine rendered, one sprite per element, which the user selects, drags,
/// resizes and draws on. Positions are millimeters from the figure's top-left corner, y down, as in the code.
final class CanvasView: NSView, NSTextFieldDelegate {
    weak var main: MainController?
    private(set) var figure: FigureData?
    var selection: [String] = [] {
        didSet {
            guard selection != oldValue else { return }
            if let pt = part, selection != [pt.oid] { part = nil }
            needsDisplay = true; main?.selectionChanged()
        }
    }
    /// A text or mark of the selected panel, picked by clicking it, which the inspector changes.
    var part: PanelPart? { didSet { if part != oldValue { needsDisplay = true; main?.selectionChanged() } } }
    private var hoverPart: PanelPart?
    var tool: Tool = .select { didSet { window?.invalidateCursorRects(for: self); main?.toolChanged() } }
    var showGrid = UserDefaults.standard.bool(forKey: "showGrid") { didSet { UserDefaults.standard.set(showGrid, forKey: "showGrid"); needsDisplay = true; main?.syncAidButtons() } }
    var snapToGrid = UserDefaults.standard.bool(forKey: "snapToGrid") { didSet { UserDefaults.standard.set(snapToGrid, forKey: "snapToGrid") } }
    var smartGuides = UserDefaults.standard.object(forKey: "smartGuides") as? Bool ?? true { didSet { UserDefaults.standard.set(smartGuides, forKey: "smartGuides") } }
    /// Guides shown (and snapped to); hidden, they stay in the code.
    var showGuides = UserDefaults.standard.object(forKey: "showGuides") as? Bool ?? true { didSet { UserDefaults.standard.set(showGuides, forKey: "showGuides"); needsDisplay = true; main?.syncAidButtons() } }
    /// How the canvas shows colors: normal, or as readers with a color vision deficiency see them.
    var vision = "normal" { didSet { applyVision() } }
    var gridStep: Double {
        get { UserDefaults.standard.double(forKey: "gridStep") > 0 ? UserDefaults.standard.double(forKey: "gridStep") : 1 }
        set { UserDefaults.standard.set(newValue, forKey: "gridStep"); needsDisplay = true }
    }
    /// Called with the cursor position (mm) and what the pointer is over, for the rulers and the status line.
    var onCursor: ((CGPoint?) -> Void)?
    var onViewChange: (() -> Void)?

    // view transform: a figure point p (mm) is drawn at offset + p * scale (points)
    private(set) var scale: CGFloat = 3
    private(set) var offset = CGPoint(x: 40, y: 40)
    private var fitMode = true
    private var images: [String: CGImage] = [:]

    // what a drag is doing
    private enum Drag {
        case none
        case move(start: CGPoint, oids: [String], allLabels: Bool, base: CGRect)
        case resize(oid: String, handle: Int, start: CGRect)
        case endpoint(oid: String, which: Int)
        case marquee(start: CGPoint, additive: Bool)
        case create(kind: Tool, start: CGPoint)
        case guide(index: Int?, vertical: Bool)
        case pan(start: CGPoint, origin: CGPoint)
        case divider(layout: String, k: Int)          // k: which of the layout's dividers
    }
    /// What a move drags along: the layouts of the panels selected in them, the panels in those, insets in moved
    /// panels (labels follow their panels).
    private var moving: Set<String> = []
    private var drag: Drag = .none
    private var moved = false
    private var downPoint = CGPoint.zero                 // mm
    private var current = CGPoint.zero                   // mm
    private var liveDelta = CGPoint.zero                 // a move in progress (mm)
    private var liveBox: CGRect?                         // a resize in progress
    private var liveEnds: (CGPoint, CGPoint)?            // a line end being dragged
    private var liveGuide: Double?
    private var liveDivider: Double?
    private var snapLines: [(vertical: Bool, at: Double)] = []
    private var hover: String?
    /// After a drag, until the engine's render arrives: the moved elements stay where they were dropped.
    private var pending: (oids: Set<String>, delta: CGPoint, box: (String, CGRect)?, ends: (String, (CGPoint, CGPoint))?)?
    private var spaceDown = false
    private var zoomDrag = false
    private var editor: NSTextField?
    private var editing: (oid: String?, at: CGPoint, size: Double, key: String?, old: String?)?
    /// The picture being cropped: its handles crop instead of scaling.
    private(set) var cropping: String?
    /// Points of the path the pen is drawing.
    private var penPoints: [CGPoint] = []
    /// A path point being dragged: (oid, index).
    private var pointDrag: (String, Int)?
    private var spacingHints: [(vertical: Bool, from: CGPoint, to: CGPoint)] = []
    /// Cells typed into a table on the canvas whose edit hasn't come back rendered yet (so typing on doesn't lose them).
    private var tableDraft: (oid: String, rows: [[String]])? { didSet { tableDraftGeneration += 1 } }
    private(set) var tableDraftGeneration = 0
    /// The edit of the cells typed up to `gen` has come back: the figure shows them now.
    func clearTableDraft(_ gen: Int) { if gen == tableDraftGeneration { tableDraft = nil } }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        // since macOS 14 a view may draw outside its bounds: a figure scrolled up would cover the tool bar and rulers
        clipsToBounds = true
        registerForDraggedTypes([.fileURL, BioCartoonPane.dragType])
        let area = NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect, .cursorUpdate],
                                  owner: self, userInfo: nil)
        addTrackingArea(area)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    // MARK: data

    func setFigure(_ f: FigureData?) {
        let sameFigure = f?.name == figure?.name
        figure = f
        if let d = tableDraft, !sameFigure || f?.element(d.oid) == nil { tableDraft = nil }
        pending = nil
        // keep the selection where its elements still exist
        if let f = f { selection = selection.filter { f.element($0) != nil } } else { selection = [] }
        if let pt = part, partText(pt) == nil && partMark(pt) == nil { part = nil }
        // drop pictures no longer used
        let used = Set(f?.all.compactMap { $0.sprite?.file } ?? [])
        images = images.filter { used.contains($0.key) }
        if !sameFigure || fitMode { fitMode = true; fit() }
        needsDisplay = true
    }

    private func image(_ file: String) -> CGImage? {
        if vision != "normal" { return visionImage(file) }
        if let i = images[file] { return i }
        guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: file) as CFURL, nil),
              let i = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
        images[file] = i
        return i
    }

    /// The resolution (pixels per mm) sprites should have at this zoom.
    var wantedPxPerMM: Double {
        let px = Double(scale * (window?.backingScaleFactor ?? 2))
        // a poster seen whole needs only a few pixels per millimeter (the engine also caps each piece's picture)
        return [2.0, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64].first { $0 >= px * 0.9 } ?? 64
    }

    // MARK: zoom and pan

    func fit() {
        guard let f = figure, bounds.width > 40, bounds.height > 40 else { return }
        let m: CGFloat = 36
        scale = max(0.2, min((bounds.width - 2 * m) / f.size.width, (bounds.height - 2 * m) / f.size.height))
        offset = CGPoint(x: (bounds.width - f.size.width * scale) / 2, y: (bounds.height - f.size.height * scale) / 2)
        fitMode = true
        needsDisplay = true
        onViewChange?()
    }

    func zoom(by factor: CGFloat, around p: CGPoint? = nil) {
        let c = p ?? CGPoint(x: bounds.midX, y: bounds.midY)
        let m = toMM(c)
        scale = max(0.3, min(scale * factor, 80))
        offset = CGPoint(x: c.x - m.x * scale, y: c.y - m.y * scale)
        fitMode = false
        needsDisplay = true
        onViewChange?()
    }

    /// 1 mm on the figure is 1 mm on this screen.
    func actualSize() {
        var ptPerMM: CGFloat = 72 / 25.4
        if let screen = window?.screen, let n = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber {
            let mmSize = CGDisplayScreenSize(CGDirectDisplayID(n.uint32Value))
            if mmSize.width > 0 { ptPerMM = screen.frame.width / mmSize.width }
        }
        zoom(by: ptPerMM / scale)
    }

    override func setFrameSize(_ s: NSSize) {
        super.setFrameSize(s)
        if fitMode { fit() }
    }

    override func scrollWheel(with e: NSEvent) {
        if e.modifierFlags.contains(.command) || e.modifierFlags.contains(.control) {
            zoom(by: 1 + e.scrollingDeltaY * (e.hasPreciseScrollingDeltas ? 0.01 : 0.1), around: convert(e.locationInWindow, from: nil))
            return
        }
        offset.x += e.scrollingDeltaX * (e.hasPreciseScrollingDeltas ? 1 : 8)
        offset.y += e.scrollingDeltaY * (e.hasPreciseScrollingDeltas ? 1 : 8)
        fitMode = false
        needsDisplay = true
        onViewChange?()
    }

    override func magnify(with e: NSEvent) { zoom(by: 1 + e.magnification, around: convert(e.locationInWindow, from: nil)) }

    func toView(_ p: CGPoint) -> CGPoint { CGPoint(x: offset.x + p.x * scale, y: offset.y + p.y * scale) }
    func toView(_ r: CGRect) -> CGRect { CGRect(x: offset.x + r.minX * scale, y: offset.y + r.minY * scale, width: r.width * scale, height: r.height * scale) }
    func toMM(_ p: CGPoint) -> CGPoint { CGPoint(x: (p.x - offset.x) / scale, y: (p.y - offset.y) / scale) }
    /// The middle of the part of the figure in view (mm), to put something new where the user is looking.
    func visibleFigureCenter() -> CGPoint? {
        guard let f = main?.figure, scale > 0 else { return nil }
        let c = toMM(CGPoint(x: bounds.midX, y: bounds.midY))
        return CGPoint(x: min(max(c.x, 0), f.size.width), y: min(max(c.y, 0), f.size.height))
    }
    var paperRect: CGRect { toView(CGRect(origin: .zero, size: figure?.size ?? .zero)) }

    // MARK: drawing

    override func draw(_ dirty: NSRect) {
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        Theme.canvasBottom.setFill(); bounds.fill()
        guard let f = figure else { return }
        let paper = paperRect
        // the paper
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: 2), blur: 10, color: NSColor(white: 0, alpha: 0.25).cgColor)
        (NSColor(hex: f.background) ?? .white).setFill()
        ctx.fill(paper)
        ctx.restoreGState()
        if f.background == nil { drawChecker(ctx, paper) }
        if showGrid { drawGrid(ctx, f) }

        // the elements, then the labels (drawn last, as in the figure)
        ctx.saveGState()
        ctx.interpolationQuality = .high
        for e in f.all where !(editing?.oid == e.oid && editing?.key == nil) && !e.hidden {
            guard let s = e.sprite, let img = image(s.file) else { continue }
            var r = s.rect
            let croppingThis = cropping == e.oid && (liveBox != nil || pending?.box?.0 == e.oid)
            if let t = transformed(e), !croppingThis { r = t(r) }
            let v = toView(r)
            ctx.saveGState()
            if croppingThis, let b = liveBox ?? pending?.box?.1 { ctx.clip(to: toView(b)) }
            ctx.translateBy(x: v.minX, y: v.maxY)
            ctx.scaleBy(x: 1, y: -1)
            ctx.draw(img, in: CGRect(origin: .zero, size: v.size))
            ctx.restoreGState()
        }
        ctx.restoreGState()

        // guides
        let guideColor = Theme.dynamic(light: NSColor(srgbRed: 0, green: 0.6, blue: 0.85, alpha: 0.75), dark: NSColor(srgbRed: 0.3, green: 0.85, blue: 1, alpha: 0.75))
        guideColor.setStroke()
        for (i, g) in f.guides.enumerated() where showGuides {
            let path = NSBezierPath(); path.lineWidth = 1
            var gx = g.x, gy = g.y
            if case .guide(let idx, _) = drag, idx == i, let lg = liveGuide { if gx != nil { gx = lg } else { gy = lg } }
            if let x = gx { let vx = toView(CGPoint(x: x, y: 0)).x.rounded() + 0.5; path.move(to: CGPoint(x: vx, y: 0)); path.line(to: CGPoint(x: vx, y: bounds.height)) }
            if let y = gy { let vy = toView(CGPoint(x: 0, y: y)).y.rounded() + 0.5; path.move(to: CGPoint(x: 0, y: vy)); path.line(to: CGPoint(x: bounds.width, y: vy)) }
            path.stroke()
        }
        if case .guide(nil, let vertical) = drag, let lg = liveGuide {
            let path = NSBezierPath(); path.lineWidth = 1
            if vertical { let vx = toView(CGPoint(x: lg, y: 0)).x; path.move(to: CGPoint(x: vx, y: 0)); path.line(to: CGPoint(x: vx, y: bounds.height)) }
            else { let vy = toView(CGPoint(x: 0, y: lg)).y; path.move(to: CGPoint(x: 0, y: vy)); path.line(to: CGPoint(x: bounds.width, y: vy)) }
            path.stroke()
        }

        // elements that failed
        for e in f.elements where e.error != nil {
            Theme.warm.setStroke()
            let p = NSBezierPath(rect: toView(e.box).insetBy(dx: -1, dy: -1)); p.lineWidth = 1.5; p.stroke()
        }

        // hover
        if let h = hover, !selection.contains(h), let e = f.element(h), case .none = drag {
            Theme.accent.withAlphaComponent(0.5).setStroke()
            outline(e).stroke()
        }
        drawSelection(f)

        // smart guides
        if !snapLines.isEmpty {
            Theme.warm.setStroke()
            for s in snapLines {
                let p = NSBezierPath(); p.lineWidth = 1; p.setLineDash([4, 3], count: 2, phase: 0)
                if s.vertical { let x = toView(CGPoint(x: s.at, y: 0)).x; p.move(to: CGPoint(x: x, y: paper.minY - 12)); p.line(to: CGPoint(x: x, y: paper.maxY + 12)) }
                else { let y = toView(CGPoint(x: 0, y: s.at)).y; p.move(to: CGPoint(x: paper.minX - 12, y: y)); p.line(to: CGPoint(x: paper.maxX + 12, y: y)) }
                p.stroke()
            }
        }

        // a marquee or a new element being drawn
        switch drag {
        case .marquee(let s, _):
            let r = toView(rect(s, current))
            Theme.accent.withAlphaComponent(0.08).setFill(); r.fill()
            Theme.accent.setStroke(); let p = NSBezierPath(rect: r); p.lineWidth = 1; p.stroke()
        case .create(let kind, let s) where moved:
            Theme.accent.setStroke()
            let p: NSBezierPath
            let a = toView(s), b = toView(constrained(s, current))
            switch kind {
            case .ellipse: p = NSBezierPath(ovalIn: toView(rect(s, current)))
            case .line, .arrow: p = NSBezierPath(); p.move(to: a); p.line(to: b)
            case .bracket:
                p = NSBezierPath(); p.move(to: CGPoint(x: a.x, y: a.y + 4)); p.line(to: a); p.line(to: CGPoint(x: b.x, y: a.y)); p.line(to: CGPoint(x: b.x, y: a.y + 4))
            default: p = NSBezierPath(rect: toView(rect(s, current)))
            }
            p.lineWidth = 1.5; p.stroke()
        default: break
        }
        drawExtras(f)
        drawReadout()
    }

    private func drawExtras(_ f: FigureData) {
        // the path the pen is drawing
        if tool == .pen, !penPoints.isEmpty {
            let p = NSBezierPath(); p.lineWidth = 1.5
            p.move(to: toView(penPoints[0]))
            for q in penPoints.dropFirst() { p.line(to: toView(q)) }
            p.line(to: toView(current))
            Theme.accent.setStroke(); p.stroke()
            for q in penPoints { handle(at: toView(q)) }
        }
        // a picture being cropped
        if let c = cropping, let e = f.element(c) {
            let b = liveBox ?? e.box
            Theme.warm.setStroke()
            let p = NSBezierPath(rect: toView(b)); p.lineWidth = 1.5; p.setLineDash([6, 3], count: 2, phase: 0); p.stroke()
            for h in handlePoints(toView(b)) { handle(at: h) }
            let s = NSAttributedString(string: "Crop: drag the handles; Return or Esc when done", attributes: [.font: Theme.font(10, .medium), .foregroundColor: NSColor.white])
            let r = CGRect(x: toView(b).minX, y: toView(b).minY - 20, width: s.size().width + 12, height: 17)
            Theme.warm.setFill(); NSBezierPath(roundedRect: r, xRadius: 4, yRadius: 4).fill()
            s.draw(at: CGPoint(x: r.minX + 6, y: r.minY + 2))
        }
        // texts a selected panel draws, which a click picks and double-click retypes
        if selection.count == 1, let e = f.element(selection[0]), !e.texts.isEmpty, case .none = drag {
            Theme.accent.withAlphaComponent(0.35).setStroke()
            for t in e.texts where t.key != part?.key {
                let p = NSBezierPath(rect: toView(t.box).insetBy(dx: -1, dy: -1)); p.lineWidth = 0.75
                p.setLineDash([1.5, 2], count: 2, phase: 0); p.stroke()
            }
        }
        // the text or mark under the pointer, and the one picked
        if case .none = drag {
            for (pt, picked) in [(hoverPart, false), (part, true)] {
                guard let pt, picked || pt != part, let r = partBox(pt) else { continue }
                let v = toView(r).insetBy(dx: -1.5, dy: -1.5)
                if picked { Theme.accent.withAlphaComponent(0.1).setFill(); v.fill() }
                Theme.accent.withAlphaComponent(picked ? 1 : 0.6).setStroke()
                let p = NSBezierPath(rect: v); p.lineWidth = picked ? 1.5 : 1; p.stroke()
            }
        }
        // a path's points, to drag
        if selection.count == 1, let e = f.element(selection[0]), e.kind == "path", cropping == nil {
            for (i, q) in e.points.enumerated() {
                var q2 = q
                if let (o, k) = pointDrag, o == e.oid, k == i, moved { q2 = current }
                let v = toView(q2)
                let r = CGRect(x: v.x - 4, y: v.y - 4, width: 8, height: 8)
                NSColor.white.setFill(); NSBezierPath(ovalIn: r).fill()
                Theme.accent.setStroke(); let b = NSBezierPath(ovalIn: r); b.lineWidth = 1.2; b.stroke()
            }
        }
        // equal spacing
        if !spacingHints.isEmpty {
            Theme.warm.setStroke()
            for h in spacingHints {
                let a = toView(h.from), b = toView(h.to)
                let p = NSBezierPath(); p.lineWidth = 1
                p.move(to: a); p.line(to: b)
                if h.vertical {
                    p.move(to: CGPoint(x: a.x - 4, y: a.y)); p.line(to: CGPoint(x: a.x + 4, y: a.y))
                    p.move(to: CGPoint(x: b.x - 4, y: b.y)); p.line(to: CGPoint(x: b.x + 4, y: b.y))
                } else {
                    p.move(to: CGPoint(x: a.x, y: a.y - 4)); p.line(to: CGPoint(x: a.x, y: a.y + 4))
                    p.move(to: CGPoint(x: b.x, y: b.y - 4)); p.line(to: CGPoint(x: b.x, y: b.y + 4))
                }
                p.stroke()
            }
        }
    }

    private func drawChecker(_ ctx: CGContext, _ r: CGRect) {
        ctx.saveGState(); ctx.clip(to: r)
        NSColor(white: 0.9, alpha: 1).setFill()
        let s: CGFloat = 8
        var y = r.minY, row = 0
        while y < r.maxY {
            var x = r.minX + (row % 2 == 0 ? 0 : s)
            while x < r.maxX { NSRect(x: x, y: y, width: s, height: s).fill(); x += 2 * s }
            y += s; row += 1
        }
        ctx.restoreGState()
    }

    private func drawGrid(_ ctx: CGContext, _ f: FigureData) {
        var step = gridStep
        while step * Double(scale) < 6 { step *= 5 }
        let major = step * 5
        ctx.saveGState(); ctx.clip(to: paperRect)
        for vertical in [true, false] {
            var v = 0.0
            let end = Double(vertical ? f.size.width : f.size.height)
            while v <= end + 1e-6 {
                let isMajor = abs((v / major).rounded() * major - v) < 1e-6
                NSColor(srgbRed: 0, green: 0.5, blue: 0.8, alpha: isMajor ? 0.18 : 0.08).setStroke()
                let p = NSBezierPath(); p.lineWidth = 0.5
                if vertical { let x = toView(CGPoint(x: v, y: 0)).x; p.move(to: CGPoint(x: x, y: paperRect.minY)); p.line(to: CGPoint(x: x, y: paperRect.maxY)) }
                else { let y = toView(CGPoint(x: 0, y: v)).y; p.move(to: CGPoint(x: paperRect.minX, y: y)); p.line(to: CGPoint(x: paperRect.maxX, y: y)) }
                p.stroke()
                v += step
            }
        }
        ctx.restoreGState()
    }

    /// How an element is shown while a drag (or its pending edit) changes it: a map of sprite rects.
    private func transformed(_ e: Element) -> ((CGRect) -> CGRect)? {
        var delta: CGPoint?
        var box: CGRect?
        var ends: (CGPoint, CGPoint)?
        if case .move(_, let oids, let allLabels, _) = drag, moved {
            _ = oids
            if moving.contains(e.oid) || (allLabels && e.isLabel && !e.detached) { delta = liveDelta }
            else if !allLabels, !e.detached, let po = e.panelOid, moving.contains(po) { delta = liveDelta }   // a label goes with its panel (unless unlinked)
        } else if case .resize(let oid, _, _) = drag, oid == e.oid, moved { box = liveBox }
        else if case .endpoint(let oid, _) = drag, oid == e.oid, moved { ends = liveEnds }
        else if let p = pending {
            if p.oids.contains(e.oid) || (!e.detached && (e.panelOid.map { p.oids.contains($0) } ?? false)) { delta = p.delta }
            if let (o, b) = p.box, o == e.oid { box = b }
            if let (o, en) = p.ends, o == e.oid { ends = en }
        }
        if let d = delta { return { $0.offsetBy(dx: d.x, dy: d.y) } }
        if let nb = box {
            let ob = e.box
            let sx = ob.width > 0 ? nb.width / ob.width : 1, sy = ob.height > 0 ? nb.height / ob.height : 1
            return { r in CGRect(x: nb.minX + (r.minX - ob.minX) * sx, y: nb.minY + (r.minY - ob.minY) * sy,
                                 width: r.width * sx, height: r.height * sy) }
        }
        if let (a, b) = ends, let (oa, ob) = e.ends {
            let o0 = CGPoint(x: min(oa.x, ob.x), y: min(oa.y, ob.y)), n0 = CGPoint(x: min(a.x, b.x), y: min(a.y, b.y))
            let ow = abs(ob.x - oa.x), oh = abs(ob.y - oa.y), nw = abs(b.x - a.x), nh = abs(b.y - a.y)
            let sx = ow > 0.01 ? nw / ow : 1, sy = oh > 0.01 ? nh / oh : 1
            return { r in CGRect(x: n0.x + (r.minX - o0.x) * sx, y: n0.y + (r.minY - o0.y) * sy, width: r.width * (ow > 0.01 ? sx : 1), height: r.height * (oh > 0.01 ? sy : 1)) }
        }
        return nil
    }

    private func outline(_ e: Element) -> NSBezierPath {
        if let (a, b) = e.ends {
            let p = NSBezierPath(); p.move(to: toView(a)); p.line(to: toView(b)); p.lineWidth = 1; return p
        }
        let p = NSBezierPath(rect: toView(e.box).insetBy(dx: -1.5, dy: -1.5)); p.lineWidth = 1; return p
    }

    /// The box an element shows right now (moved, resized) in mm.
    func liveBox(of e: Element) -> CGRect {
        if let t = transformed(e) { return t(e.box) }
        return e.box
    }

    private func drawLayout(_ l: Element) {
        let shift: CGPoint = {
            if case .move = drag, moved, moving.contains(l.oid) { return liveDelta }
            if let p = pending, p.oids.contains(l.oid) { return p.delta }
            return .zero
        }()
        let frame = toView(l.box.offsetBy(dx: shift.x, dy: shift.y)).insetBy(dx: -3, dy: -3)
        Theme.accent.withAlphaComponent(0.6).setStroke()
        let p = NSBezierPath(rect: frame); p.lineWidth = 1; p.setLineDash([5, 3], count: 2, phase: 0); p.stroke()
        for (k, d) in l.dividers.enumerated() {
            var at = d.at
            if case .divider(let lay, let dk) = drag, lay == l.oid, dk == k, let delta = liveDivider { at += delta }
            let a = toView(CGPoint(x: at + (d.vertical ? shift.x : shift.y), y: at + (d.vertical ? shift.x : shift.y)))
            let lo = toView(CGPoint(x: d.from + shift.x, y: d.from + shift.y)), hi = toView(CGPoint(x: d.to + shift.x, y: d.to + shift.y))
            let line = NSBezierPath(); line.lineWidth = 1
            if d.vertical { line.move(to: CGPoint(x: a.x, y: lo.y)); line.line(to: CGPoint(x: a.x, y: hi.y)) }
            else { line.move(to: CGPoint(x: lo.x, y: a.y)); line.line(to: CGPoint(x: hi.x, y: a.y)) }
            (d.editable ? Theme.accent : Theme.ink(0.25)).withAlphaComponent(0.45).setStroke()
            line.setLineDash([2, 3], count: 2, phase: 0); line.stroke()
            guard d.editable else { continue }
            let mid = d.vertical ? CGPoint(x: a.x, y: (lo.y + hi.y) / 2) : CGPoint(x: (lo.x + hi.x) / 2, y: a.y)
            let grip = d.vertical ? CGRect(x: mid.x - 3, y: mid.y - 14, width: 6, height: 28) : CGRect(x: mid.x - 14, y: mid.y - 3, width: 28, height: 6)
            NSColor.white.setFill(); Theme.accent.setStroke()
            let g = NSBezierPath(roundedRect: grip, xRadius: 3, yRadius: 3); g.lineWidth = 1.2; g.fill(); g.stroke()
        }
    }

    private func drawSelection(_ f: FigureData) {
        let sel = selection.compactMap { f.element($0) }
        guard !sel.isEmpty else { return }
        for l in Set(sel.map(\.top)).compactMap({ f.element($0) }) where l.kind == "layout" { drawLayout(l) }
        Theme.accent.setStroke()
        for e in sel {
            if let (a, b) = liveEnds.flatMap({ en -> (CGPoint, CGPoint)? in if case .endpoint(let o, _) = drag, o == e.oid { return en }; return nil }) ?? e.ends.map({ en in
                if let p = pending, let (o, pe) = p.ends, o == e.oid { return pe }
                if case .move = drag, moved, moving.contains(e.oid) { return (en.0 + liveDelta, en.1 + liveDelta) }
                if let p = pending, p.oids.contains(e.oid) { return (en.0 + p.delta, en.1 + p.delta) }
                return en }) {
                let p = NSBezierPath(); p.move(to: toView(a)); p.line(to: toView(b)); p.lineWidth = 1; p.stroke()
                if sel.count == 1 { for q in [a, b] { handle(at: toView(q)) } }
                continue
            }
            let r = toView(liveBox(of: e)).insetBy(dx: -1.5, dy: -1.5)
            let p = NSBezierPath(rect: r); p.lineWidth = e.isLabel ? 1 : 1.2
            if e.isLabel { p.setLineDash([3, 2], count: 2, phase: 0) }
            p.stroke()
        }
        if sel.count == 1, let e = sel.first, !e.isLabel, e.ends == nil {
            let r = toView(liveBox(of: e)).insetBy(dx: -1.5, dy: -1.5)
            for (i, h) in handlePoints(r).enumerated() where handleAllowed(e, i) { handle(at: h) }
        } else if sel.count > 1 {
            let u = sel.map { liveBox(of: $0) }.reduce(CGRect.null) { $0.union($1) }
            Theme.accent.withAlphaComponent(0.5).setStroke()
            let p = NSBezierPath(rect: toView(u).insetBy(dx: -4, dy: -4)); p.lineWidth = 1; p.setLineDash([2, 3], count: 2, phase: 0); p.stroke()
        }
    }

    private func handle(at p: CGPoint) {
        let r = CGRect(x: p.x - 3.5, y: p.y - 3.5, width: 7, height: 7)
        NSColor.white.setFill(); r.fill()
        Theme.accent.setStroke(); let b = NSBezierPath(rect: r); b.lineWidth = 1.2; b.stroke()
    }

    /// Handle positions: TL, T, TR, R, BR, B, BL, L.
    private func handlePoints(_ r: CGRect) -> [CGPoint] {
        [CGPoint(x: r.minX, y: r.minY), CGPoint(x: r.midX, y: r.minY), CGPoint(x: r.maxX, y: r.minY), CGPoint(x: r.maxX, y: r.midY),
         CGPoint(x: r.maxX, y: r.maxY), CGPoint(x: r.midX, y: r.maxY), CGPoint(x: r.minX, y: r.maxY), CGPoint(x: r.minX, y: r.midY)]
    }

    /// Text scales from its corners only; a picture that keeps its aspect ratio has corner and side handles that
    /// keep it, which is what the engine does with them.
    private func handleAllowed(_ e: Element, _ i: Int) -> Bool {
        if e.parentOid != nil { return false }              // a layout sizes its panels
        switch e.kind {
        case "text": return i % 2 == 0 || i == 3 || i == 7    // corners scale, sides set the wrap width
        case "path", "zoom": return true
        case "bracket": return i == 3 || i == 7
        case "panel", "image", "rect", "ellipse", "layout", "inset", "figure", "qr": return true
        default: return false
        }
    }

    private func drawReadout() {
        var text: String?
        switch drag {
        case .move(_, _, _, let base) where moved:
            text = "x \(mm(base.minX + liveDelta.x))  y \(mm(base.minY + liveDelta.y)) mm"
        case .resize where moved:
            if let b = liveBox { text = "w \(mm(b.width))  h \(mm(b.height)) mm" }
        case .endpoint where moved:
            if let (a, b) = liveEnds { text = "\(mm(hypot(b.x - a.x, b.y - a.y))) mm" }
        case .create(_, let s) where moved:
            let r = rect(s, current); text = "w \(mm(r.width))  h \(mm(r.height)) mm"
        case .guide where liveGuide != nil:
            text = "\(mm(liveGuide!)) mm"
        default: break
        }
        guard let t = text else { return }
        let attrs: [NSAttributedString.Key: Any] = [.font: Theme.mono(10), .foregroundColor: NSColor.white]
        let s = NSAttributedString(string: t, attributes: attrs)
        let p = toView(current)
        let r = CGRect(x: p.x + 14, y: p.y + 14, width: s.size().width + 10, height: s.size().height + 4)
        NSColor(white: 0.1, alpha: 0.8).setFill(); NSBezierPath(roundedRect: r, xRadius: 4, yRadius: 4).fill()
        s.draw(at: CGPoint(x: r.minX + 5, y: r.minY + 2))
    }

    // MARK: hit testing

    /// The element under a point (mm), topmost first; labels are above everything.
    func hit(_ p: CGPoint) -> Element? {
        guard let f = figure else { return nil }
        let slop = 4 / scale
        for e in f.all.reversed() where e.kind != "layout" && e.pickable {
            if e.kind == "path", e.points.count > 1 {
                let pts = e.points + ((e.props["closed"] as? Bool ?? false) ? [e.points[0]] : [])
                if zip(pts, pts.dropFirst()).contains(where: { distance(p, $0, $1) <= slop + 0.3 }) { return e }
                if (e.props["closed"] as? Bool ?? false) && e.box.contains(p) && !(e.props["fill"] is NSNull) && e.props["fill"] != nil { return e }
                continue
            }
            if let (a, b) = e.ends {
                if distance(p, a, b) <= slop + 0.3 { return e }
                continue
            }
            let r = e.box.insetBy(dx: -(e.kind == "text" || e.isLabel ? slop : 1 / scale), dy: -(e.kind == "text" || e.isLabel ? slop : 1 / scale))
            if r.contains(p) { return e }
        }
        // a layout itself, in the gaps between its panels
        return f.elements.reversed().first { $0.kind == "layout" && $0.pickable && $0.box.contains(p) }
    }

    /// The selection with what moves along with it (see `moving`).
    func movingSet(_ oids: [String]) -> Set<String> {
        guard let f = figure else { return Set(oids) }
        var out = Set(oids.compactMap { f.element($0)?.isLabel == true ? $0 : f.element($0)?.top })
        for e in f.elements where e.parentOid.map({ out.contains($0) }) == true { out.insert(e.oid) }
        var grew = true
        while grew {
            grew = false
            for e in f.elements where e.hostOid.map({ out.contains($0) }) == true && !out.contains(e.oid) { out.insert(e.oid); grew = true }
        }
        return out
    }

    /// The layout whose gaps can be dragged now: the one selected, or the one a selected panel is in.
    private var activeLayout: Element? {
        guard let f = figure, selection.count == 1, let e = f.element(selection[0]) else { return nil }
        let l = f.element(e.top)
        return l?.kind == "layout" ? l : nil
    }

    private func hitDivider(_ v: CGPoint) -> (Element, Int)? {
        guard let l = activeLayout else { return nil }
        for (k, d) in l.dividers.enumerated() where d.editable {
            let a = toView(CGPoint(x: d.at, y: d.at))
            let lo = toView(CGPoint(x: d.from, y: d.from)), hi = toView(CGPoint(x: d.to, y: d.to))
            if d.vertical, abs(a.x - v.x) < 5, v.y >= lo.y, v.y <= hi.y { return (l, k) }
            if !d.vertical, abs(a.y - v.y) < 5, v.x >= lo.x, v.x <= hi.x { return (l, k) }
        }
        return nil
    }

    private func distance(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y
        let l2 = dx * dx + dy * dy
        let t = l2 > 0 ? max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / l2)) : 0
        return hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy))
    }

    private func hitHandle(_ v: CGPoint) -> Int? {
        guard selection.count == 1, let e = figure?.element(selection[0]), !e.isLabel else { return nil }
        if let (a, b) = e.ends {
            for (i, q) in [a, b].enumerated() where hypot(toView(q).x - v.x, toView(q).y - v.y) < 7 { return 100 + i }
            return nil
        }
        let r = toView(e.box).insetBy(dx: -1.5, dy: -1.5)
        for (i, h) in handlePoints(r).enumerated() where handleAllowed(e, i) && abs(h.x - v.x) < 6 && abs(h.y - v.y) < 6 { return i }
        return nil
    }

    private func hitGuide(_ v: CGPoint) -> Int? {
        guard let f = figure, showGuides else { return nil }
        for (i, g) in f.guides.enumerated() {
            if let x = g.x, abs(toView(CGPoint(x: x, y: 0)).x - v.x) < 3 { return i }
            if let y = g.y, abs(toView(CGPoint(x: 0, y: y)).y - v.y) < 3 { return i }
        }
        return nil
    }

    /// The members of an element's group, or just it.
    func withGroup(_ oid: String) -> [String] {
        guard let f = figure, let e = f.element(oid), let g = e.group else { return [oid] }
        return f.elements.filter { $0.group == g }.map(\.oid)
    }

    // MARK: mouse

    override func mouseDown(with e: NSEvent) {
        window?.makeFirstResponder(self)
        commitEditor()
        let v = convert(e.locationInWindow, from: nil)
        let p = toMM(v)
        downPoint = p; current = p; moved = false; snapLines = []
        guard let f = figure else { return }
        if spaceDown || e.buttonNumber == 2 { drag = .pan(start: v, origin: offset); return }
        if tool == .pen { penClick(snapPoint(p), double: e.clickCount == 2); return }
        if tool == .zoom {
            if e.modifierFlags.contains(.option) { zoom(by: 0.67, around: v); return }
            drag = .marquee(start: p, additive: false); zoomDrag = true; return
        }
        if let c = cropping {
            if let h = cropHandle(v) { drag = .resize(oid: c, handle: h, start: f.element(c)!.box); return }
            endCrop()
        }
        if e.clickCount == 2, tool == .select, let el = hit(p), el.kind == "table", let (i, j) = el.tableCell(at: p) {
            beginCellEditor(el, i, j); return
        }
        if e.clickCount == 2, tool == .select, let el = hit(p) {
            if let t = el.texts.first(where: { $0.box.insetBy(dx: -0.4, dy: -0.4).contains(p) }) {
                beginEditor(oid: el.oid, at: t.box.origin, size: t.size, text: t.text, key: t.key)
                return
            }
            // the smallest mark under the pointer (a bar before the line that spans the plot)
            if let mk = el.marks.filter({ $0.box.insetBy(dx: -0.5, dy: -0.5).contains(p) })
                .min(by: { $0.box.width * $0.box.height < $1.box.width * $1.box.height }) {
                main?.recolorMark(el.oid, mk)
                return
            }
            doubleClick(el); return
        }
        if selection.count == 1, let pe = f.element(selection[0]), pe.kind == "path",
           let k = pe.points.firstIndex(where: { hypot(toView($0).x - v.x, toView($0).y - v.y) < 6 }) {
            pointDrag = (pe.oid, k); drag = .none; return
        }
        if tool != .select {
            if tool == .text { beginEditor(oid: nil, at: p, size: (f.style["size"] as? NSNumber)?.doubleValue ?? 7, text: ""); return }
            drag = .create(kind: tool, start: snapPoint(p)); return
        }
        if let h = hitHandle(v), let oid = selection.first {
            drag = h >= 100 ? .endpoint(oid: oid, which: h - 100) : .resize(oid: oid, handle: h, start: f.element(oid)!.box)
            return
        }
        if let (l, k) = hitDivider(v) { drag = .divider(layout: l.oid, k: k); return }
        if let el = hit(p) {
            let ids = withGroup(el.oid)
            if e.modifierFlags.contains(.shift) {
                if selection.contains(el.oid) { selection.removeAll { ids.contains($0) } } else { selection += ids.filter { !selection.contains($0) } }
                if !selection.contains(el.oid) { return }
            } else if !selection.contains(el.oid) {
                selection = ids
            }
            // a text or mark of a panel is picked by the same click (the panel is still what a drag moves)
            part = e.modifierFlags.contains(.shift) || selection != [el.oid] ? nil : partHit(el, p, marks: true)
            let oids = selection
            // dragging a label moves every label (they share one offset); ⌥ moves this one only, as does dragging a
            // label unlinked from its panel
            let allLabels = el.isLabel && !el.detached && !e.modifierFlags.contains(.option)
            moving = movingSet(oids)
            let base = (allLabels ? f.labels.filter { !$0.detached } : f.all.filter { moving.contains($0.oid) }).map(\.box).reduce(CGRect.null) { $0.union($1) }
            drag = .move(start: p, oids: oids, allLabels: allLabels, base: base)
            if e.modifierFlags.contains(.option), !el.isLabel { drag = .move(start: p, oids: oids, allLabels: false, base: base) }
            return
        }
        if let g = hitGuide(v) { drag = .guide(index: g, vertical: f.guides[g].x != nil); return }
        if !e.modifierFlags.contains(.shift) { selection = [] }
        drag = .marquee(start: p, additive: e.modifierFlags.contains(.shift))
    }

    override func mouseDragged(with e: NSEvent) {
        let v = convert(e.locationInWindow, from: nil)
        let p = toMM(v)
        current = p
        if !moved && hypot((p.x - downPoint.x) * scale, (p.y - downPoint.y) * scale) < 3 { return }
        moved = true
        let free = e.modifierFlags.contains(.command)
        let shift = e.modifierFlags.contains(.shift)
        if pointDrag != nil { current = free ? p : snapPoint(p); needsDisplay = true; return }
        switch drag {
        case .pan(let start, let origin):
            offset = CGPoint(x: origin.x + v.x - start.x, y: origin.y + v.y - start.y); fitMode = false; onViewChange?()
        case .move(let start, _, let allLabels, let base):
            var d = CGPoint(x: p.x - start.x, y: p.y - start.y)
            if shift { if abs(d.x) > abs(d.y) { d.y = 0 } else { d.x = 0 } }
            liveDelta = allLabels ? round(d, 0.1) : snapMove(base, d, free: free)
        case .resize(let oid, let h, let start):
            liveBox = resized(oid, start, h, p, keepAspect: cropping == nil && (shift || keepsAspect(oid, handle: h)), free: free)
        case .endpoint(let oid, let which):
            guard let el = figure?.element(oid), let (a, b) = el.ends else { break }
            var q = free ? p : snapPoint(p)
            let other = which == 0 ? b : a
            if shift { q = angleSnap(other, q) }
            liveEnds = which == 0 ? (q, b) : (a, q)
        case .marquee(_, _): break
        case .create(let kind, let s):
            current = free ? p : snapPoint(p)
            if shift && (kind == .line || kind == .arrow) { current = angleSnap(s, current) }
        case .guide(_, let vertical):
            liveGuide = Double((vertical ? p.x : p.y) * 10).rounded() / 10
        case .divider(let lay, let k):
            let vertical = figure?.element(lay)?.dividers[k].vertical ?? true
            liveDivider = Double(((vertical ? p.x - downPoint.x : p.y - downPoint.y) * 10).rounded() / 10)
        case .none: break
        }
        needsDisplay = true
        onCursor?(p)
    }

    override func mouseUp(with e: NSEvent) { endDrag(keepTool: e.modifierFlags.contains(.option)) }

    /// A drag ended: send what it did to the engine.
    private func endDrag(keepTool: Bool) {
        defer { drag = .none; snapLines = []; spacingHints = []; liveBox = nil; liveEnds = nil; liveGuide = nil; liveDivider = nil
                zoomDrag = false; pointDrag = nil; needsDisplay = true }
        guard let f = figure else { return }
        if let (oid, k) = pointDrag, moved, let pe = f.element(oid) {
            var pts = pe.points
            pts[k] = current
            main?.edit(["op": "points", "oid": oid, "points": pts.map { [r1($0.x), r1($0.y)] }])
            return
        }
        if zoomDrag, case .marquee(let s, _) = drag {
            let r = rect(s, current)
            if r.width > 1 && r.height > 1 { zoom(to: r) } else { zoom(by: 1.5, around: toView(s)) }
            return
        }
        switch drag {
        case .divider(let lay, let k) where moved:
            if let d = liveDivider, abs(d) > 0.05 { main?.edit(["op": "divider", "oid": lay, "index": k, "delta": d]) }
        case .move(_, let oids, let allLabels, _) where moved:
            let d = liveDelta
            guard abs(d.x) > 0.001 || abs(d.y) > 0.001 else { return }
            if allLabels {
                let lab = f.labels.filter { !$0.detached }.map(\.oid)
                pending = (Set(lab), d, nil, nil)
                main?.edit(["op": "move", "oids": [lab.first ?? ""], "dx": d.x, "dy": d.y, "labels": "all"])
            } else {
                pending = (moving, d, nil, nil)
                main?.edit(["op": "move", "oids": oids, "dx": d.x, "dy": d.y])
            }
        case .resize(let oid, let h, _) where moved:
            guard let b = liveBox else { return }
            pending = ([], .zero, (oid, b), nil)
            if cropping == oid {
                main?.edit(["op": "crop", "oid": oid, "box": [r1(b.minX), r1(b.minY), r1(b.width), r1(b.height)]])
            } else if let el = f.element(oid), el.kind == "text", h == 3 || h == 7 {
                // a side handle of text sets the width its lines wrap at
                var props: [String: Any] = ["width": r1(b.width)]
                if h == 7, (el.s("align") ?? "left") == "left" { props["x"] = r1(b.minX) }
                main?.edit(["op": "set", "oid": oid, "props": props])
            } else {
                main?.edit(["op": "resize", "oid": oid, "box": [b.minX, b.minY, b.width, b.height]])
            }
        case .endpoint(let oid, _) where moved:
            guard let (a, b) = liveEnds else { return }
            pending = ([], .zero, nil, (oid, (a, b)))
            main?.edit(["op": "endpoints", "oid": oid, "x1": r1(a.x), "y1": r1(a.y), "x2": r1(b.x), "y2": r1(b.y)])
        case .marquee(let s, _):
            let r = rect(s, current)
            guard r.width > 0.3 || r.height > 0.3 else { return }
            var ids = selection
            for el in f.elements where r.intersects(el.box.insetBy(dx: -0.01, dy: -0.01)) {
                for o in withGroup(el.oid) where !ids.contains(o) { ids.append(o) }
            }
            selection = ids
        case .create(let kind, let s):
            create(kind, s, moved ? current : s)
            if !keepTool { tool = .select }
        case .guide(let index, let vertical):
            guard moved, let g = liveGuide else { return }
            let outside = g < -2 || g > Double(vertical ? f.size.width : f.size.height) + 2
            if let i = index {
                main?.edit(outside ? ["op": "guide_delete", "index": i] : ["op": "guide_move", "index": i, "value": g])
            } else if !outside {
                main?.edit(vertical ? ["op": "guide_add", "x": g] : ["op": "guide_add", "y": g])
            }
        default: break
        }
    }

    override func mouseMoved(with e: NSEvent) {
        let p = toMM(convert(e.locationInWindow, from: nil))
        if tool == .pen, !penPoints.isEmpty { current = snapPoint(p); needsDisplay = true }
        let hel = tool == .select ? hit(p) : nil
        let h = hel?.oid
        if h != hover { hover = h; needsDisplay = true }
        // texts of any panel; marks only of the selected one (a line's box can cover the whole plot)
        let hp = hel.flatMap { partHit($0, p, marks: selection == [$0.oid]) }
        if hp != hoverPart { hoverPart = hp; needsDisplay = true }
        if tool == .select, let (l, k) = hitDivider(convert(e.locationInWindow, from: nil)) {
            (l.dividers[k].vertical ? NSCursor.resizeLeftRight : NSCursor.resizeUpDown).set()
        } else if tool == .select { NSCursor.arrow.set() }
        onCursor?(p)
        main?.hint(for: h.flatMap { figure?.element($0) })
    }
    override func mouseExited(with e: NSEvent) { hover = nil; hoverPart = nil; needsDisplay = true; onCursor?(nil) }

    /// The text (or, with `marks`, the smallest mark) of a panel under the point.
    func partHit(_ el: Element, _ p: CGPoint, marks: Bool) -> PanelPart? {
        if let t = el.texts.first(where: { $0.box.insetBy(dx: -0.4, dy: -0.4).contains(p) }) {
            return PanelPart(oid: el.oid, key: t.key, isText: true)
        }
        guard marks, let mk = el.marks.filter({ $0.box.insetBy(dx: -0.5, dy: -0.5).contains(p) })
            .min(by: { $0.box.width * $0.box.height < $1.box.width * $1.box.height }) else { return nil }
        return PanelPart(oid: el.oid, key: mk.key, isText: false)
    }
    func partText(_ pt: PanelPart) -> PanelText? { pt.isText ? figure?.element(pt.oid)?.texts.first { $0.key == pt.key } : nil }
    func partMark(_ pt: PanelPart) -> PanelMark? { pt.isText ? nil : figure?.element(pt.oid)?.marks.first { $0.key == pt.key } }
    private func partBox(_ pt: PanelPart) -> CGRect? { partText(pt)?.box ?? partMark(pt)?.box }

    /// Retype the picked text in place.
    func editPartText() {
        guard let pt = part, let t = partText(pt) else { return }
        beginEditor(oid: pt.oid, at: t.box.origin, size: t.size, text: t.text, key: t.key)
    }

    override func cursorUpdate(with e: NSEvent) {
        if tool != .select { NSCursor.crosshair.set() } else { NSCursor.arrow.set() }
    }

    override func rightMouseDown(with e: NSEvent) {
        let p = toMM(convert(e.locationInWindow, from: nil))
        if let el = hit(p), !selection.contains(el.oid) { selection = withGroup(el.oid) }
        if let m = main?.contextMenu() { NSMenu.popUpContextMenu(m, with: e, for: self) }
    }

    // MARK: snapping

    private func round(_ d: CGPoint, _ step: Double) -> CGPoint {
        CGPoint(x: (d.x / step).rounded() * step, y: (d.y / step).rounded() * step)
    }
    private func r1(_ v: CGFloat) -> Double { Double((v * 100).rounded() / 100) }

    /// Lines things snap to: the figure's edges and middle, the guides, and other elements' edges and middles.
    private func targets(excluding: Set<String>) -> (xs: [Double], ys: [Double]) {
        guard let f = figure else { return ([], []) }
        var xs: [Double] = [0, Double(f.size.width) / 2, Double(f.size.width)]
        var ys: [Double] = [0, Double(f.size.height) / 2, Double(f.size.height)]
        if showGuides { xs += f.guides.compactMap(\.x); ys += f.guides.compactMap(\.y) }
        for e in f.elements where !excluding.contains(e.oid) {
            let b = e.box
            xs += [b.minX, b.midX, b.maxX].map(Double.init)
            ys += [b.minY, b.midY, b.maxY].map(Double.init)
        }
        return (xs, ys)
    }

    private func snapMove(_ base: CGRect, _ d: CGPoint, free: Bool) -> CGPoint {
        snapLines = []
        var out = round(CGPoint(x: base.minX + d.x, y: base.minY + d.y), 0.1)
        out = CGPoint(x: out.x - base.minX, y: out.y - base.minY)
        if free { return out }
        if snapToGrid {
            let g = gridStep
            return CGPoint(x: ((base.minX + d.x) / g).rounded() * g - base.minX, y: ((base.minY + d.y) / g).rounded() * g - base.minY)
        }
        guard smartGuides else { return out }
        let t = targets(excluding: Set(selection).union(moving))
        let tol = Double(6 / scale)
        let moved = base.offsetBy(dx: d.x, dy: d.y)
        if let (shift, at) = best([moved.minX, moved.midX, moved.maxX].map(Double.init), t.xs, tol) {
            out.x = d.x + shift; snapLines.append((true, at))
        }
        if let (shift, at) = best([moved.minY, moved.midY, moved.maxY].map(Double.init), t.ys, tol) {
            out.y = d.y + shift; snapLines.append((false, at))
        }
        spacingHints = []
        if let (dx, hints) = equalSpacing(base.offsetBy(dx: out.x, dy: out.y), horizontal: true, tol: tol), !snapLines.contains(where: { $0.vertical }) {
            out.x += dx; spacingHints += hints
        }
        if let (dy, hints) = equalSpacing(base.offsetBy(dx: out.x, dy: out.y), horizontal: false, tol: tol), !snapLines.contains(where: { !$0.vertical }) {
            out.y += dy; spacingHints += hints
        }
        return out
    }

    /// Equal spacing (Illustrator's smart guides): when the gap between the moved box and its neighbor in a row
    /// (or column) is nearly the gap between two other elements of that row, the shift that makes it equal, and the
    /// gaps to mark.
    private func equalSpacing(_ b: CGRect, horizontal: Bool, tol: Double) -> (CGFloat, [(vertical: Bool, from: CGPoint, to: CGPoint)])? {
        guard let f = figure else { return nil }
        let others = f.elements.filter { !moving.contains($0.oid) && !selection.contains($0.oid) && $0.pickable && $0.kind != "layout"
            && $0.parentOid == nil && $0.box.width > 0.5 && $0.box.height > 0.5 }.map(\.box)
        // the band the box shares with its neighbors
        let row = others.filter { horizontal ? ($0.maxY > b.minY && $0.minY < b.maxY) : ($0.maxX > b.minX && $0.minX < b.maxX) }
        let sorted = row.sorted { horizontal ? $0.minX < $1.minX : $0.minY < $1.minY }
        var gaps: [(CGFloat, CGRect, CGRect)] = []
        for (a, c) in zip(sorted, sorted.dropFirst()) {
            let g = horizontal ? c.minX - a.maxX : c.minY - a.maxY
            if g > 0.5 { gaps.append((g, a, c)) }
        }
        guard !gaps.isEmpty else { return nil }
        let before = sorted.filter { horizontal ? $0.maxX <= b.minX + 0.01 : $0.maxY <= b.minY + 0.01 }.max { horizontal ? $0.maxX < $1.maxX : $0.maxY < $1.maxY }
        let after = sorted.filter { horizontal ? $0.minX >= b.maxX - 0.01 : $0.minY >= b.maxY - 0.01 }.min { horizontal ? $0.minX < $1.minX : $0.minY < $1.minY }
        for (g, a, c) in gaps {
            if let n = before {
                let mine = horizontal ? b.minX - n.maxX : b.minY - n.maxY
                if abs(mine - g) <= tol, mine > 0 {
                    let shift = g - mine
                    return (shift, [mark(a, c, horizontal), mark(n, b.offsetBy(dx: horizontal ? shift : 0, dy: horizontal ? 0 : shift), horizontal)])
                }
            }
            if let n = after {
                let mine = horizontal ? n.minX - b.maxX : n.minY - b.maxY
                if abs(mine - g) <= tol, mine > 0 {
                    let shift = mine - g
                    return (shift, [mark(a, c, horizontal), mark(b.offsetBy(dx: horizontal ? shift : 0, dy: horizontal ? 0 : shift), n, horizontal)])
                }
            }
        }
        return nil
    }

    private func mark(_ a: CGRect, _ c: CGRect, _ horizontal: Bool) -> (vertical: Bool, from: CGPoint, to: CGPoint) {
        if horizontal {
            let y = (max(a.minY, c.minY) + min(a.maxY, c.maxY)) / 2
            return (false, CGPoint(x: a.maxX, y: y), CGPoint(x: c.minX, y: y))
        }
        let x = (max(a.minX, c.minX) + min(a.maxX, c.maxX)) / 2
        return (true, CGPoint(x: x, y: a.maxY), CGPoint(x: x, y: c.minY))
    }

    /// The smallest shift (within tolerance) that puts one of `edges` on one of `targets`.
    private func best(_ edges: [Double], _ targets: [Double], _ tol: Double) -> (Double, Double)? {
        var found: (Double, Double)?
        for e in edges { for t in targets where abs(t - e) <= tol && (found == nil || abs(t - e) < abs(found!.0)) { found = (t - e, t) } }
        return found
    }

    private func snapPoint(_ p: CGPoint) -> CGPoint {
        snapLines = []
        if snapToGrid { let g = gridStep; return CGPoint(x: (p.x / g).rounded() * g, y: (p.y / g).rounded() * g) }
        var q = round(p, 0.1)
        guard smartGuides else { return q }
        let t = targets(excluding: Set(selection))
        let tol = Double(6 / scale)
        if let (s, at) = best([Double(p.x)], t.xs, tol) { q.x = p.x + s; snapLines.append((true, at)) }
        if let (s, at) = best([Double(p.y)], t.ys, tol) { q.y = p.y + s; snapLines.append((false, at)) }
        return q
    }

    private func angleSnap(_ from: CGPoint, _ to: CGPoint) -> CGPoint {
        let dx = to.x - from.x, dy = to.y - from.y
        let a = (atan2(dy, dx) / (.pi / 4)).rounded() * (.pi / 4)
        let l = hypot(dx, dy)
        return CGPoint(x: from.x + cos(a) * l, y: from.y + sin(a) * l)
    }

    private func keepsAspect(_ oid: String, handle: Int) -> Bool {
        guard let e = figure?.element(oid) else { return false }
        if e.kind == "text" { return handle % 2 == 0 }          // corners scale it; sides set its wrap width
        if e.kind == "qr" { return true }
        if e.kind == "figure" { return e.props["h"] is NSNull || e.props["w"] is NSNull }
        return e.kind == "image" && e.props["h"] is NSNull
    }

    private func resized(_ oid: String, _ s: CGRect, _ h: Int, _ p0: CGPoint, keepAspect: Bool, free: Bool) -> CGRect {
        let p = free ? p0 : snapPoint(p0)
        var x0 = s.minX, y0 = s.minY, x1 = s.maxX, y1 = s.maxY
        if [0, 6, 7].contains(h) { x0 = min(p.x, x1 - 1) }
        if [2, 3, 4].contains(h) { x1 = max(p.x, x0 + 1) }
        if [0, 1, 2].contains(h) { y0 = min(p.y, y1 - 1) }
        if [4, 5, 6].contains(h) { y1 = max(p.y, y0 + 1) }
        var r = CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0)
        if keepAspect, s.height > 0 {
            let a = s.width / s.height
            if [1, 5].contains(h) { r.size.width = r.height * a; r.origin.x = s.midX - r.width / 2 }
            else if [3, 7].contains(h) { r.size.height = r.width / a; r.origin.y = s.midY - r.height / 2 }
            else {
                if r.width / r.height > a { r.size.height = r.width / a } else { r.size.width = r.height * a }
                if [0, 6].contains(h) { r.origin.x = s.maxX - r.width }
                if [0, 2].contains(h) { r.origin.y = s.maxY - r.height }
            }
        }
        return CGRect(x: (r.minX * 10).rounded() / 10, y: (r.minY * 10).rounded() / 10,
                      width: (r.width * 10).rounded() / 10, height: (r.height * 10).rounded() / 10)
    }

    private func rect(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }
    private func constrained(_ s: CGPoint, _ p: CGPoint) -> CGPoint { p }

    // MARK: making elements

    private func create(_ kind: Tool, _ s: CGPoint, _ e: CGPoint) {
        var r = rect(s, e)
        let click = r.width < 0.5 && r.height < 0.5
        switch kind {
        case .rect, .ellipse:
            if click { r = CGRect(x: s.x, y: s.y, width: 20, height: 12) }
            main?.edit(["op": "insert", "kind": kind.rawValue, "props": ["x": r1(r.minX), "y": r1(r.minY), "w": r1(r.width), "h": r1(r.height)]])
        case .line, .arrow:
            let end = click ? CGPoint(x: s.x + 15, y: s.y) : e
            main?.edit(["op": "insert", "kind": kind.rawValue, "props": ["x1": r1(s.x), "y1": r1(s.y), "x2": r1(end.x), "y2": r1(end.y)]])
        case .bracket:
            let x2 = click ? s.x + 15 : e.x
            main?.edit(["op": "insert", "kind": "bracket", "props": ["x1": r1(min(s.x, x2)), "x2": r1(max(s.x, x2)), "y": r1(s.y), "text": "*"]])
        default: break
        }
    }

    // MARK: typing on the canvas

    private func doubleClick(_ el: Element) {
        switch el.kind {
        case "text" where el.s("source") != nil:
            main?.showCode(of: el)                              // its text is in a file: edit it there
        case "figure":
            // a figure placed whole opens as itself; edits there show here
            if let n = el.figureName { main?.show(figure: n) } else { main?.showCode(of: el) }
        case "qr":
            main?.promptText("QR code link", el.s("data") ?? "") { [weak self] t in self?.main?.edit(["op": "set", "oid": el.oid, "props": ["data": t]]) }
        case "text":
            beginEditor(oid: el.oid, at: CGPoint(x: el.p("x") ?? el.box.minX, y: el.box.minY),
                        size: el.p("size") ?? (figure?.style["size"] as? NSNumber)?.doubleValue ?? 7, text: el.s("text") ?? "", align: el.s("align"))
        case "label":
            // double-click unlinks a label from its panel (each then moves on its own), or links it again;
            // ⌥-double-click retypes it
            if NSEvent.modifierFlags.contains(.option) {
                beginEditor(oid: el.oid, at: el.box.origin, size: (figure?.labelStyle["size"] as? NSNumber)?.doubleValue ?? 8, text: el.text ?? "")
            } else {
                main?.toggleLabelLink(el.oid)
            }
        case "bracket":
            main?.promptText("Bracket text", el.s("text") ?? "") { [weak self] t in self?.main?.edit(["op": "set", "oid": el.oid, "props": ["text": t]]) }
        case "image" where el.parentOid == nil:
            beginCrop(el.oid)
        case "inset" where el.inner == "image":
            beginCrop(el.oid)
        case "panel", "image", "inset":
            main?.showCode(of: el)
        default: break
        }
    }

    private func beginEditor(oid: String?, at p: CGPoint, size: Double, text: String, align: String? = nil, key: String? = nil) {
        commitEditor()
        let font = NSFont.systemFont(ofSize: max(9, CGFloat(size) * 25.4 / 72 * scale))
        let f = NSTextField(string: text)
        f.font = font; f.isBordered = true; f.focusRingType = .none; f.delegate = self
        f.backgroundColor = NSColor.white.withAlphaComponent(0.95); f.textColor = .black; f.drawsBackground = true
        f.usesSingleLineMode = false; f.cell?.wraps = false; f.cell?.isScrollable = true
        let w = max(80, NSAttributedString(string: text + "  ", attributes: [.font: font]).size().width + 20)
        var origin = toView(p)
        if align == "center" { origin.x -= w / 2 } else if align == "right" { origin.x -= w }
        f.frame = CGRect(x: origin.x - 3, y: origin.y - 3, width: w, height: font.boundingRectForFont.height + 8)
        addSubview(f)
        window?.makeFirstResponder(f)
        editor = f; editing = (oid, p, size, key, key != nil ? text : nil)
        needsDisplay = true
    }

    /// A table's cells as last typed: the canvas's draft, else the figure's.
    func tableDraftRows(_ e: Element) -> [[String]] {
        if let d = tableDraft, d.oid == e.oid { return d.rows }
        let r = e.tableCells
        return r.isEmpty ? [[""]] : r
    }

    /// Type into a cell of a table, in place (Tab: the next cell, ⇧Tab the one before, Return: done).
    func beginCellEditor(_ el: Element, _ i: Int, _ j: Int) {
        guard let b = el.tableCellBox(i, j) else { return }
        let rows = tableDraftRows(el)
        let text = i < rows.count && j < rows[i].count ? rows[i][j] : ""
        let size = el.p("size") ?? (figure?.style["size"] as? NSNumber)?.doubleValue ?? 7
        beginEditor(oid: el.oid, at: CGPoint(x: b.minX, y: b.minY + max(0, (b.height - size * 25.4 / 72 * 1.25) / 2)), size: size, text: text, key: "cell:\(i):\(j)")
        if let f = editor { f.frame.size.width = max(f.frame.width, toView(CGPoint(x: b.maxX, y: 0)).x - toView(CGPoint(x: b.minX, y: 0)).x + 6) }
    }

    func commitEditor() {
        guard let f = editor, let ed = editing else { return }
        let text = f.stringValue
        editor = nil; editing = nil
        f.removeFromSuperview()
        needsDisplay = true
        if let oid = ed.oid, let key = ed.key, key.hasPrefix("cell:"), let el = figure?.element(oid) {
            let ij = key.split(separator: ":").compactMap { Int($0) }
            guard ij.count == 2 else { return }
            var rows = tableDraftRows(el)
            let width = max(rows.map(\.count).max() ?? 1, ij[1] + 1)
            while rows.count <= ij[0] { rows.append(Array(repeating: "", count: width)) }
            for k in rows.indices where rows[k].count < width { rows[k] += Array(repeating: "", count: width - rows[k].count) }
            guard rows[ij[0]][ij[1]] != text else { return }
            rows[ij[0]][ij[1]] = text
            tableDraft = (oid, rows)
            main?.applyTableCells(oid, rows)
            return
        }
        if let oid = ed.oid, let key = ed.key {
            if text != ed.old { main?.edit(["op": "panel_text", "oid": oid, "key": key, "text": text, "old": ed.old ?? ""]) }
        } else if let oid = ed.oid {
            if let e = figure?.element(oid) {
                let old = e.isLabel ? (e.text ?? "") : (e.s("text") ?? "")
                if text != old { main?.edit(["op": "set", "oid": oid, "props": ["text": text]]) }
            }
        } else if !text.trimmingCharacters(in: .whitespaces).isEmpty {
            main?.edit(["op": "insert", "kind": "text", "props": ["text": text, "x": r1(ed.at.x), "y": r1(ed.at.y)]])
            tool = .select
        } else { tool = .select }
    }

    private func cancelEditor() {
        editor?.removeFromSuperview(); editor = nil; editing = nil; needsDisplay = true
        window?.makeFirstResponder(self)
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy s: Selector) -> Bool {
        if s == #selector(NSResponder.insertNewline(_:)) { commitEditor(); window?.makeFirstResponder(self); return true }
        if s == #selector(NSResponder.cancelOperation(_:)) { cancelEditor(); return true }
        // in a table's cell, Tab and ⇧Tab go to the next and previous cell
        if s == #selector(NSResponder.insertTab(_:)) || s == #selector(NSResponder.insertBacktab(_:)),
           let ed = editing, let key = ed.key, key.hasPrefix("cell:"), let oid = ed.oid, let el = figure?.element(oid) {
            let ij = key.split(separator: ":").compactMap { Int($0) }
            let nr = el.gridRows.count, nc = max(1, el.gridCols.count)
            guard ij.count == 2, nr > 0 else { return false }
            var k = ij[0] * nc + ij[1] + (s == #selector(NSResponder.insertTab(_:)) ? 1 : -1)
            k = (k + nr * nc) % (nr * nc)
            commitEditor()
            beginCellEditor(el, k / nc, k % nc)
            return true
        }
        return false
    }
    func controlTextDidEndEditing(_ obj: Notification) { if editor != nil { commitEditor() } }

    // MARK: keys

    override func keyDown(with e: NSEvent) {
        let mods = e.modifierFlags.intersection([.command, .control, .option])
        if e.keyCode == 49 { spaceDown = true; NSCursor.openHand.set(); return }             // space: pan
        if mods.isEmpty, let ch = e.charactersIgnoringModifiers?.lowercased(), let t = Tool.allCases.first(where: { $0.key == ch }) {
            tool = t; return
        }
        let step = e.modifierFlags.contains(.shift) ? 5.0 : (e.modifierFlags.contains(.option) ? 0.1 : 0.5)
        switch e.keyCode {
        case 123: main?.nudge(dx: -step, dy: 0)
        case 124: main?.nudge(dx: step, dy: 0)
        case 125: main?.nudge(dx: 0, dy: step)
        case 126: main?.nudge(dx: 0, dy: -step)
        case 51, 117:
            // with a text of a panel picked, Delete empties that text rather than delete the panel
            if let pt = part {
                if let t = partText(pt) { main?.edit(["op": "panel_text", "oid": pt.oid, "key": pt.key, "text": "", "old": t.text]) }
                else { main?.toast("Press Esc, then Delete, to delete the panel") }
            } else { main?.deleteSelected(nil) }
        case 53:
            if part != nil { part = nil }
            else if tool == .pen && !penPoints.isEmpty { penPoints = []; needsDisplay = true }
            else if cropping != nil { endCrop() }
            else if tool != .select { tool = .select } else { selection = [] }
        case 36:
            if tool == .pen { finishPen(closed: false) }
            else if cropping != nil { endCrop() }
            else if let pt = part, pt.isText { editPartText() }
            else if let oid = selection.first, let el = figure?.element(oid) { doubleClick(el) }
        default: super.keyDown(with: e)
        }
    }
    override func keyUp(with e: NSEvent) {
        if e.keyCode == 49 { spaceDown = false; NSCursor.arrow.set() } else { super.keyUp(with: e) }
    }

    /// The selection moved by the arrow keys: shown at once, written when the engine gets it.
    func showNudge(_ oids: [String], _ d: CGPoint) { pending = (movingSet(oids), d, nil, nil); needsDisplay = true }
    func clearPending() { pending = nil; needsDisplay = true }

    /// For scripted tests: type into the text editor on the canvas and press Return.
    func typeInEditor(_ text: String) { editor?.stringValue = text; commitEditor() }
    var isEditingText: Bool { editor != nil }

    // MARK: color vision

    /// A sprite as a reader with a color vision deficiency sees it (converted once per sprite and mode).
    private func visionImage(_ file: String) -> CGImage? {
        let key = file + "|" + vision
        if let i = visionImages[key] { return i }
        guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: file) as CFURL, nil),
              let i = CGImageSourceCreateImageAtIndex(src, 0, nil), let f = visionFilter() else { return nil }
        f.setValue(CIImage(cgImage: i), forKey: kCIInputImageKey)
        guard let out = f.outputImage,
              let cg = CanvasView.ciContext.createCGImage(out, from: CGRect(x: 0, y: 0, width: i.width, height: i.height)) else { return nil }
        visionImages[key] = cg
        return cg
    }
    private var visionImages: [String: CGImage] = [:]
    private static let ciContext = CIContext()

    private func applyVision() {
        visionImages = [:]
        needsDisplay = true
    }

    /// Core Image works in linear light, where these matrices (Machado et al. 2009) apply.
    private func visionFilter() -> CIFilter? {
        let rows: [[CGFloat]]
        switch vision {
        case "deuteranopia": rows = [[0.367322, 0.860646, -0.227968], [0.280085, 0.672501, 0.047413], [-0.011820, 0.042940, 0.968881]]
        case "protanopia": rows = [[0.152286, 1.052583, -0.204868], [0.114503, 0.786281, 0.099216], [-0.003882, -0.048116, 1.051998]]
        case "tritanopia": rows = [[1.255528, -0.076749, -0.178779], [-0.078411, 0.930809, 0.147602], [0.004733, 0.691367, 0.303900]]
        case "grayscale": rows = [[0.2126, 0.7152, 0.0722], [0.2126, 0.7152, 0.0722], [0.2126, 0.7152, 0.0722]]
        default: return nil
        }
        guard let f = CIFilter(name: "CIColorMatrix") else { return nil }
        f.setDefaults()
        f.setValue(CIVector(x: rows[0][0], y: rows[0][1], z: rows[0][2], w: 0), forKey: "inputRVector")
        f.setValue(CIVector(x: rows[1][0], y: rows[1][1], z: rows[1][2], w: 0), forKey: "inputGVector")
        f.setValue(CIVector(x: rows[2][0], y: rows[2][1], z: rows[2][2], w: 0), forKey: "inputBVector")
        return f
    }

    // MARK: cropping

    func beginCrop(_ oid: String) {
        guard let e = figure?.element(oid), e.kind == "image" || e.kind == "inset" else { return }
        cropping = oid
        selection = [oid]
        main?.toast("Crop: drag the handles in (or out to uncrop); Return when done")
        needsDisplay = true
    }
    func endCrop() { cropping = nil; needsDisplay = true }

    private func cropHandle(_ v: CGPoint) -> Int? {
        guard let c = cropping, let e = figure?.element(c) else { return nil }
        for (i, h) in handlePoints(toView(e.box)).enumerated() where abs(h.x - v.x) < 7 && abs(h.y - v.y) < 7 { return i }
        return nil
    }

    // MARK: the pen

    private func penClick(_ p: CGPoint, double: Bool) {
        if let first = penPoints.first, penPoints.count > 2, hypot((p.x - first.x) * scale, (p.y - first.y) * scale) < 8 {
            finishPen(closed: true); return
        }
        if double { finishPen(closed: false); return }
        penPoints.append(p)
        needsDisplay = true
    }

    private func finishPen(closed: Bool) {
        let pts = penPoints
        penPoints = []
        needsDisplay = true
        guard pts.count >= 2 else { return }
        var props: [String: Any] = ["points": pts.map { [r1($0.x), r1($0.y)] }]
        if closed { props["closed"] = true }
        main?.edit(["op": "insert", "kind": "path", "props": props])
        tool = .select
    }

    // MARK: zooming to a box

    func zoom(to r: CGRect) {
        let m: CGFloat = 20
        scale = max(0.3, min((bounds.width - 2 * m) / r.width, (bounds.height - 2 * m) / r.height, 80))
        offset = CGPoint(x: bounds.midX - r.midX * scale, y: bounds.midY - r.midY * scale)
        fitMode = false
        needsDisplay = true
        onViewChange?()
    }

    // MARK: guides from the rulers

    func beginRulerGuide(vertical: Bool) { drag = .guide(index: nil, vertical: vertical); moved = false }
    func rulerGuideDragged(_ windowPoint: NSPoint) {
        let p = toMM(convert(windowPoint, from: nil))
        current = p; moved = true
        if case .guide(_, let vertical) = drag { liveGuide = Double((vertical ? p.x : p.y) * 10).rounded() / 10 }
        needsDisplay = true
    }
    func rulerGuideEnded() { endDrag(keepTool: false) }

    // MARK: dropping files

    override func draggingEntered(_ s: NSDraggingInfo) -> NSDragOperation { figure == nil ? [] : .copy }
    override func performDragOperation(_ s: NSDraggingInfo) -> Bool {
        if let d = s.draggingPasteboard.data(forType: BioCartoonPane.dragType) {
            return main?.biocartoonDropped(d, at: toMM(convert(s.draggingLocation, from: nil))) ?? false
        }
        guard let urls = s.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
              !urls.isEmpty else { return false }
        let p = toMM(convert(s.draggingLocation, from: nil))
        main?.importFiles(urls, at: p)
        return true
    }
}

extension CGPoint {
    static func + (a: CGPoint, b: CGPoint) -> CGPoint { CGPoint(x: a.x + b.x, y: a.y + b.y) }
}
