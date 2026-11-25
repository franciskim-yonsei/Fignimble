import AppKit

/// A scripted walk through the editor that saves pictures of the window, for checking the UI without a person
/// (after Manimble's ShotsTest): `FIGNIMBLE_SHOTS=/some/folder Fignimble.app/Contents/MacOS/Fignimble <project>`.
/// It edits the project it is given, so give it a copy.
extension MainController {
    /// Where the window is against its screen, and the smallest size its views allow (the window must fit a 13"
    /// MacBook Air): FIGNIMBLE_SHOTS_SCENE=window.
    func windowReport(_ dir: String) {
        Task { @MainActor in
            for _ in 0..<300 where figure == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            let vis = (window.screen ?? NSScreen.main)?.visibleFrame ?? .zero
            let fit = window.contentView?.fittingSize ?? .zero
            var s = "window \(window.frame)\nscreen visible \(vis)\ncontent fitting \(fit)\nminSize \(window.minSize)\n"
            s += "contentMinSize \(window.contentMinSize)\n"
            for v in window.contentView?.subviews ?? [] { s += "  \(type(of: v)) \(v.frame) fitting \(v.fittingSize)\n" }
            // the smallest the window goes: asked for 900 x 560, where does it end up
            window.setFrame(NSRect(x: vis.minX, y: vis.minY, width: 900, height: 560), display: true)
            try? await Task.sleep(nanoseconds: 500_000_000)
            window.contentView?.layoutSubtreeIfNeeded()
            s += "shrunk to \(window.frame)\n"
            for v in window.contentView?.subviews ?? [] { s += "  \(type(of: v)) \(v.frame)\n" }
            try? s.write(toFile: dir + "/window.txt", atomically: true, encoding: .utf8)
            NSApp.terminate(nil)
        }
    }

    /// The sidebar's right-click menu on a figure: copy, paste (a <name>_copy shown), delete (the one next to it
    /// shown), undo (back): FIGNIMBLE_SHOTS_SCENE=figmenu, into figmenu.txt.
    func figureMenuTest(_ dir: String) async {
        for _ in 0..<300 where figure == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
        let names = { self.figures.compactMap { $0["name"] as? String } }
        var s = "start \(names()) current \(current ?? "-")\n"
        let first = names().first ?? ""
        let m = figureListMenu(first)
        s += "menu \(m.items.map(\.title)) enabled \(m.items.filter(\.isEnabled).map(\.title))\n"
        func fire(_ t: String) { if let i = m.items.first(where: { $0.title == t }), let a = i.action { NSApp.sendAction(a, to: i.target, from: i) } }
        fire("Copy"); await waitForQueue()
        let m2 = figureListMenu(first)
        s += "paste enabled \(m2.items.first { $0.title == "Paste" }?.isEnabled ?? false)\n"
        fire("Paste"); await waitForQueue(); await waitForQueue()
        s += "pasted \(names()) current \(current ?? "-")\n"
        let copy = current ?? ""
        let m3 = figureListMenu(copy)
        if let i = m3.items.first(where: { $0.title == "Delete" }) { NSApp.sendAction(i.action!, to: i.target, from: i) }
        await waitForQueue(); await waitForQueue()
        s += "deleted \(names()) current \(current ?? "-")\n"
        undoEdit(nil); await waitForQueue()
        s += "undone \(names())\n"
        try? s.write(toFile: dir + "/figmenu.txt", atomically: true, encoding: .utf8)
        NSApp.terminate(nil)
    }

    func runShotsIfAsked() {
        guard let dir = ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS"] else { return }
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true)
        Task { @MainActor in
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "blank" { await blankShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "window" { windowReport(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "figmenu" { await figureMenuTest(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "remote" { await remoteShots(dir); return }
            // with FIGNIMBLE_TEST_SETUP the Python is set up first, which takes a while
            let wait = ProcessInfo.processInfo.environment["FIGNIMBLE_TEST_SETUP"] != nil ? 3000 : 300
            for _ in 0..<wait where figure == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
            try? await Task.sleep(nanoseconds: 800_000_000)
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "layout" { await layoutShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "features" { await featureShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "code" { await codeShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "colors" { await colorShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "parts" { await partShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "more" { await moreShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "size" { await sizeShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "journals" { await journalShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "audit" { await auditEditing(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "fresh" { await freshShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "bcinfo" { await bioCartoonInfoShots(dir); return }
            if ProcessInfo.processInfo.environment["FIGNIMBLE_SHOTS_SCENE"] == "v04" { await v04Shots(dir); return }
            shot(dir, "01-figure")
            canvas.selection = ["1"]
            await settle(); shot(dir, "02-panel-selected")
            edit(["op": "move", "oids": ["1"], "dx": 6, "dy": 4])
            await settle(); shot(dir, "03-moved")
            undoEdit(nil)
            await settle()
            canvas.selection = ["L0"]
            await settle(); shot(dir, "04-label-selected")
            edit(["op": "move", "oids": ["L0"], "dx": -2.5, "dy": -1, "labels": "all"])
            await settle(); shot(dir, "05-labels-moved")
            undoEdit(nil)
            await settle()
            // a real drag with the mouse: panel c 8 mm to the right (snapping and the move edit)
            if let c = figure?.element("2") {
                let start = CGPoint(x: c.box.midX, y: c.box.midY)
                mouse(.leftMouseDown, start)
                for i in 1...8 { mouse(.leftMouseDragged, CGPoint(x: start.x + CGFloat(i), y: start.y)) }
                shot(dir, "06a-dragging")
                mouse(.leftMouseUp, CGPoint(x: start.x + 8, y: start.y))
                await settle(); shot(dir, "06b-dragged")
                Engine.note("after drag: panel c at \(figure?.element("2")?.box.minX ?? -1)")
                undoEdit(nil); await settle()
            }
            canvas.selection = ["0", "1", "2"]
            await settle(); shot(dir, "06-multi")
            canvas.selection = ["6"]
            await settle(); shot(dir, "07-text")
            showProblems(nil)
            await settle(); shot(dir, "08-preflight")
            showTerminal(nil)
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            shot(dir, "09-terminal")
            canvas.selection = []
            await settle(); shot(dir, "10-nothing-selected")
            NSApp.terminate(nil)
        }
    }

    /// A patchwork layout (open the project at its figure file): a panel in it, a gap dragged, the dark version.
    private func layoutShots(_ dir: String) async {
        shot(dir, "L1-layout")
        canvas.selection = ["0.3"]
        await settle(); shot(dir, "L2-child")
        if let l = figure?.element("0"), let k = l.dividers.firstIndex(where: { $0.vertical && $0.editable }) {
            let d = l.dividers[k]
            let start = CGPoint(x: d.at, y: (d.from + d.to) / 2)
            mouse(.leftMouseDown, start)
            for i in 1...12 { mouse(.leftMouseDragged, CGPoint(x: start.x + CGFloat(i), y: start.y)) }
            shot(dir, "L3-dragging-gap")
            mouse(.leftMouseUp, CGPoint(x: start.x + 12, y: start.y))
            await settle(); shot(dir, "L4-gap-dragged")
            Engine.note("layout widths now \(figure?.element("0")?.props["widths"] ?? "none")")
            undoEdit(nil); await settle()
        }
        canvas.selection = ["0"]
        toggleDarkPreview(nil)
        await settle(); try? await Task.sleep(nanoseconds: 1_000_000_000); shot(dir, "L5-dark-version")
        toggleDarkPreview(nil)
        await settle()
        NSApp.terminate(nil)
    }

    /// Figure 3 (open the project at fig3.py): retyping a panel's text, cropping, the pen, copy and paste, layers.
    private func featureShots(_ dir: String) async {
        func src() -> String { (try? String(contentsOfFile: figure?.file ?? "", encoding: .utf8)) ?? "" }
        shot(dir, "F1-figure")
        canvas.selection = ["4"]
        await settle(); shot(dir, "F2-panel-texts")
        if let t = figure?.element("4")?.texts.first(where: { $0.key == "ax0.xlabel" }) {
            mouse(.leftMouseDown, CGPoint(x: t.box.midX, y: t.box.midY), clicks: 2)
            shot(dir, "F3-retyping")
            canvas.typeInEditor("UMAP 1")
            await settle(); shot(dir, "F4-retyped")
            Engine.note("retyped: \(src().contains("\"ax0.xlabel\": \"UMAP 1\""))")
        }
        canvas.selection = ["0"]
        canvas.beginCrop("0")
        if let b = figure?.element("0")?.box {
            let h = CGPoint(x: b.maxX, y: b.midY)
            mouse(.leftMouseDown, h)
            for i in 1...15 { mouse(.leftMouseDragged, CGPoint(x: h.x - CGFloat(i), y: h.y)) }
            shot(dir, "F5-cropping")
            mouse(.leftMouseUp, CGPoint(x: h.x - 15, y: h.y))
            await settle(); canvas.endCrop(); shot(dir, "F6-cropped")
            Engine.note("cropped: \(src().components(separatedBy: "\n").first { $0.hasPrefix("cells =") } ?? "?")")
        }
        canvas.selection = []
        canvas.tool = .pen
        for (i, q) in [CGPoint(x: 20, y: 85), CGPoint(x: 35, y: 78), CGPoint(x: 50, y: 88)].enumerated() {
            mouse(.leftMouseDown, q, clicks: i == 2 ? 2 : 1); mouse(.leftMouseUp, q)
        }
        await settle(); shot(dir, "F7-pen")
        Engine.note("pen: \(src().components(separatedBy: "\n").first { $0.hasPrefix("fig.path([(20") } ?? "none")")
        canvas.selection = ["6"]
        copy(nil); await settle()
        paste(nil); await settle()
        Engine.note("arrows after paste: \(src().components(separatedBy: "fig.arrow(").count - 1)")
        setLayer("5", locked: true); await settle()
        setLayer("8", hidden: true); await settle()
        canvas.selection = ["5"]
        showHistory(nil)
        await settle(); shot(dir, "F8-history-layers")
        Engine.note("locked: \(src().contains("width=78, locked=True")), hidden: \(src().contains("rotation=90, hidden=True"))")
        NSApp.terminate(nil)
    }

    /// The Code tab following the selection (open the project at fig1.py).
    private func codeShots(_ dir: String) async {
        await settle()
        shot(dir, "C1-figure-code")
        canvas.selection = ["1"]
        await settle(); shot(dir, "C2-panel-code")
        Engine.note("panel: \(code.path ?? "-") line \(code.textView.selectedRange().location)")
        canvas.selection = ["6"]
        await settle(); shot(dir, "C3-text-line")
        Engine.note("text: \(code.path ?? "-")")
        canvas.selection = []
        await settle()
        Engine.note("none: \(code.path ?? "-")")
        NSApp.terminate(nil)
    }

    /// Colors (open the project at fig1.py): a panel's colors, the color-vision preview, recoloring, a palette.
    private func colorShots(_ dir: String) async {
        canvas.selection = ["5"]
        await settle(); shot(dir, "K1-panel-colors")
        canvas.vision = "deuteranopia"
        await settle(); shot(dir, "K2-deuteranopia")
        canvas.vision = "normal"
        edit(["op": "panel_color", "oid": "5", "keys": ["ax0.patch2", "ax0.patch3"], "color": "#0072B2", "old": "#D1495B"])
        await settle(); shot(dir, "K3-recolored")
        canvas.selection = []
        edit(["op": "style", "props": ["palette": "okabe-ito"]])
        await settle(); shot(dir, "K4-okabe-ito")
        Engine.note("warnings now: \(figure?.warnings.map(\.message) ?? [])")
        NSApp.terminate(nil)
    }

    /// Wait for the engine work queued so far, then a moment for drawing.
    /// Texts and marks of a panel picked by clicking, the figure copied as a picture, a scrolled canvas, BioCartoon.
    private func partShots(_ dir: String) async {
        // scrolled far down: the figure must stay under the tool bar and the rulers
        for _ in 0..<12 {
            if let e = CGEvent(scrollWheelEvent2Source: nil, units: .pixel, wheelCount: 2, wheel1: -10, wheel2: 0, wheel3: 0).flatMap(NSEvent.init(cgEvent:)) {
                canvas.scrollWheel(with: e)
            }
        }
        await settle(); shot(dir, "P0-scrolled")
        canvas.fit(); await settle()
        // one click on the scatter panel's x-axis label picks the panel and the label
        guard let sc = figure?.element("1"), let xl = sc.texts.first(where: { $0.key == "ax0.xlabel" }) else { Engine.note("parts: no label"); NSApp.terminate(nil); return }
        mouse(.leftMouseDown, CGPoint(x: xl.box.midX, y: xl.box.midY)); mouse(.leftMouseUp, CGPoint(x: xl.box.midX, y: xl.box.midY))
        await settle(); shot(dir, "P1-text-picked")
        Engine.note("parts: selection \(canvas.selection) part \(String(describing: canvas.part))")
        edit(["op": "panel_text_style", "oid": "1", "key": "ax0.xlabel", "text": xl.text, "props": ["size": 9, "color": "#D55E00", "weight": "bold"]])
        await settle(); shot(dir, "P2-text-restyled")
        Engine.note("parts: after restyle \(figure?.element("1")?.texts.first { $0.key == "ax0.xlabel" }.map { "\($0.size) \($0.color ?? "-") bold \($0.bold)" } ?? "-") part \(String(describing: canvas.part))")
        undoEdit(nil); await settle()
        // a bar of the bars panel
        if let bars = figure?.element("5"), let mk = bars.marks.first(where: { $0.key == "ax0.patch1" }) {
            canvas.selection = []
            mouse(.leftMouseDown, CGPoint(x: mk.box.midX, y: mk.box.midY)); mouse(.leftMouseUp, CGPoint(x: mk.box.midX, y: mk.box.midY))
            await settle(); shot(dir, "P3-mark-picked")
            Engine.note("parts: mark \(String(describing: canvas.part))")
        }
        canvas.part = nil; canvas.selection = []
        // the whole figure on the pasteboard
        copyFigure(nil)
        await settle()
        let types = NSPasteboard.general.types?.map(\.rawValue) ?? []
        Engine.note("copy figure: \(types) pdf \(NSPasteboard.general.data(forType: .pdf)?.count ?? 0) png \(NSPasteboard.general.data(forType: .png)?.count ?? 0)")
        if let d = NSPasteboard.general.data(forType: .png) { try? d.write(to: URL(fileURLWithPath: dir).appendingPathComponent("P4-copied.png")) }
        // BioCartoon: the search in the inspector's place, an icon dropped on the figure
        showBioCartoon(nil)
        for _ in 0..<300 where (biocartoonPane?.resultCount ?? 0) == 0 && biocartoonPane?.offersInstall == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
        try? await Task.sleep(nanoseconds: 2_000_000_000)
        shot(dir, "P5-biocartoon")
        Engine.note("biocartoon: exe \(biocartoonPane?.exe ?? "-") results \(biocartoonPane?.resultCount ?? 0) offers \(biocartoonPane?.offersInstall ?? "-")")
        var vf = BCFilter(); vf.fileTypes = ["svg"]
        for (q, filter) in [("mitochondria", BCFilter()), ("neuron", vf)] {
            guard let exe = biocartoonPane?.exe, let icon = try? await BioCartoonCLI.search(exe, query: q, filter: filter, sort: "relevance", offset: 0, limit: 1).first,
                  let data = try? JSONEncoder().encode(icon) else { Engine.note("biocartoon: no \(q)"); continue }
            let n = figure?.elements.count ?? 0
            _ = biocartoonDropped(data, at: CGPoint(x: q == "neuron" ? 150 : 100, y: 90))
            for _ in 0..<200 where (figure?.elements.count ?? 0) == n { try? await Task.sleep(nanoseconds: 100_000_000) }
            await settle()
            if let e = figure?.elements.last {
                let src = e.s("source") ?? "-"
                Engine.note("biocartoon: \(icon.id) svg \(icon.svg_key != nil) -> \(e.kind) \(src) box \(e.box) error \(e.error ?? "none") origin \(BioCartoonCLI.origin(ofImageAt: (project ?? "") + "/" + src)?.id ?? "-")")
                canvas.selection = [e.oid]
                Engine.note("biocartoon: menu \(contextMenu().items.map(\.title))")
            }
        }
        canvas.selection = []
        await settle(); shot(dir, "P6-icons-placed")
        hideBioCartoon()
        await settle(); shot(dir, "P7-inspector-back")
        NSApp.terminate(nil)
    }

    /// The export dialog, no panel letters, a table and its Word file, a panel from an SSH server
    /// (FIGNIMBLE_SSH: a stand-in for ssh), Update from Server.
    private func moreShots(_ dir: String) async {
        exportFigure(nil)
        try? await Task.sleep(nanoseconds: 1_500_000_000)
        // the save panel itself is drawn by another process: the options Fignimble adds to it
        if let sheet = window.attachedSheet, let v = (sheet as? NSSavePanel)?.accessoryView, let rep = v.bitmapImageRepForCachingDisplay(in: v.bounds) {
            v.cacheDisplay(in: v.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("M1-export-dialog.png"))
            window.endSheet(sheet)
        }
        labelsNone(nil)
        await settle(); shot(dir, "M2-no-letters")
        Engine.note("more: labels after No Letters \(figure?.labels.count ?? -1)")
        labelsNone(nil); await settle()
        Engine.note("more: labels back \(figure?.labels.count ?? -1)")
        // a table from a CSV, then as Word
        let csv = URL(fileURLWithPath: dir).appendingPathComponent("genes.csv")
        try? "Gene,Cells,Fold change\nCD3E,1204,2.4512\nLYZ,2950,1.10\n".write(to: csv, atomically: true, encoding: .utf8)
        importFiles([csv], at: CGPoint(x: 100, y: 88))
        await settle()
        if let t = figure?.elements.last, t.kind == "table" {
            canvas.selection = [t.oid]
            await settle(); shot(dir, "M3-table")
            let out = dir + "/genes.docx"
            let r = (try? await engine.json("export_table", ["figure": current ?? "", "oid": t.oid, "path": out])) ?? [:]
            Engine.note("more: table \(t.title(label: nil)) box \(t.box) docx \(r["ok"] ?? "-") \(FileManager.default.fileExists(atPath: out)) menu \(contextMenu().items.map(\.title).filter { $0.contains("Word") })")
        } else { Engine.note("more: no table") }
        // the SSH dialog lists a folder on the "server"
        canvas.selection = []
        let d = RemoteDialog(main: self)
        var fetched: (String, String)?
        d.onImport = { h, p in fetched = (h, p) }
        d.begin()
        try? await Task.sleep(nanoseconds: 800_000_000)
        if let host = d.panel.contentView?.firstDescendant(NSComboBox.self) { host.stringValue = "lab-server" }
        if let field = d.panel.contentView?.descendants(NSTextField.self).first(where: { $0.placeholderString?.hasPrefix("folder") == true }) { field.stringValue = "~/analysis" }
        d.list()
        try? await Task.sleep(nanoseconds: 2_500_000_000)
        if let v = d.panel.contentView, let rep = v.bitmapImageRepForCachingDisplay(in: v.bounds) {
            v.cacheDisplay(in: v.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("M4-ssh-dialog.png"))
        }
        if let table = d.panel.contentView?.firstDescendant(NSTableView.self), let i = (0..<table.numberOfRows).first(where: { r in
            (table.view(atColumn: 0, row: r, makeIfNecessary: true)?.descendants(NSTextField.self).first?.stringValue ?? "").hasSuffix(".py") }) {
            table.selectRowIndexes([i], byExtendingSelection: false)
            d.perform(Selector(("doImport")))
        }
        Engine.note("more: dialog chose \(fetched.map { "\($0.0):\($0.1)" } ?? "nothing")")
        window.attachedSheet.map { window.endSheet($0) }
        if let (h, p) = fetched, let name = current {
            let r = (try? await engine.json("import_remote", ["figure": name, "host": h, "path": p, "x": 60.0, "y": 60.0, "pxPerMM": lastPx])) ?? [:]
            if let fig = r["figure"] as? [String: Any] { apply(fig) }
            if let sel = r["select"] as? [String] { canvas.selection = sel }
            await settle(); shot(dir, "M5-remote-panel")
            let e = canvas.selection.first.flatMap { figure?.element($0) }
            Engine.note("more: import \(r["ok"] ?? "-") \(r["error"] ?? "") origin \(e.flatMap { remoteOrigin(of: $0) } ?? "-") error \(e?.error ?? "none") menu \(contextMenu().items.map(\.title).filter { $0.contains("Server") })")
        }
        NSApp.terminate(nil)
    }

    /// How wide the window has to be: its frame, the screen's, the narrowest the tool bar allows; then 1280 x 780.
    private func sizeShots(_ dir: String) async {
        func report(_ what: String) {
            let bar = window.contentView?.descendants(NSStackView.self).first { $0.arrangedSubviews.contains { $0 is NSSegmentedControl } }
            Engine.note("size \(what): window \(Int(window.frame.width))x\(Int(window.frame.height)), screen visible \(Int(window.screen?.visibleFrame.width ?? 0))x\(Int(window.screen?.visibleFrame.height ?? 0)), "
                + "tool bar needs \(Int(bar?.fittingSize.width ?? -1)), navigator \(Int(navigator.frame.width)), inspector \(Int(rightHost.frame.width)), canvas \(Int(canvas.frame.width))")
        }
        report("at launch")
        window.setFrame(NSRect(x: 0, y: 0, width: 1280, height: 780), display: true)
        await settle(); report("asked 1280x780"); shot(dir, "S1-1280")
        window.setFrame(NSRect(x: 0, y: 0, width: 1100, height: 700), display: true)
        await settle(); report("asked 1100x700"); shot(dir, "S2-1100")
        NSApp.terminate(nil)
    }

    /// Started with no project: the empty window, File ▸ Open Recent, then a recent project opened from it.
    /// A project on an SSH server (FIGNIMBLE_SSH: a stand-in whose home holds proj/): the Open dialog, the project
    /// opened, an edit that lands on the server. FIGNIMBLE_SHOTS_PYTHON: the Python "there".
    private func remoteShots(_ dir: String) async {
        for _ in 0..<100 where python == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
        let env = ProcessInfo.processInfo.environment
        let d = RemoteDialog(main: self, mode: .project)
        var chosen: RemoteProject?
        d.onOpenProject = { chosen = $0 }
        d.begin()
        try? await Task.sleep(nanoseconds: 800_000_000)
        if let host = d.panel.contentView?.firstDescendant(NSComboBox.self) { host.stringValue = "lab-server" }
        let fields = d.panel.contentView?.descendants(NSTextField.self) ?? []
        fields.first(where: { $0.placeholderString?.hasPrefix("folder") == true })?.stringValue = "~/proj"
        fields.first(where: { $0.placeholderString?.hasPrefix("python3") == true })?.stringValue = env["FIGNIMBLE_SHOTS_PYTHON"] ?? "python3"
        d.list()
        try? await Task.sleep(nanoseconds: 2_500_000_000)
        if let v = d.panel.contentView, let rep = v.bitmapImageRepForCachingDisplay(in: v.bounds) {
            v.cacheDisplay(in: v.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("R1-open-dialog.png"))
        }
        d.perform(Selector(("doImport")))
        guard let r = chosen else { Engine.note("remote: the dialog chose nothing"); NSApp.terminate(nil); return }
        Engine.note("remote: chose \(r.host):\(r.path) with \(r.python), mirror \(r.mirror)")
        openRemote(r)
        for _ in 0..<600 where figure == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
        await settle(); shot(dir, "R2-remote-project")
        Engine.note("remote: title \(window.title) project \(project ?? "-") figure \(figure?.file ?? "-") elements \(figure?.elements.count ?? 0) sprites on disk \(figure?.elements.filter { $0.sprite.map { FileManager.default.fileExists(atPath: $0.file) } ?? false }.count ?? 0)")
        if let p = figure?.elements.first(where: { $0.kind == "panel" }) {
            canvas.selection = [p.oid]
            nudge(dx: 2, dy: 0)
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            await settle(); shot(dir, "R3-after-move")
            Engine.note("remote: moved \(p.oid) from \(p.box) to \(figure?.element(p.oid)?.box ?? .zero)")
        }
        Engine.note("remote: recent \(MainController.recentProjects.first ?? "-")")
        NSApp.terminate(nil)
    }

    private func blankShots(_ dir: String) async {
        try? await Task.sleep(nanoseconds: 4_000_000_000)
        shot(dir, "B1-empty")
        let file = NSApp.mainMenu?.items.first { $0.title == "File" }?.submenu
        let recent = file?.items.first { $0.title == "Open Recent" }
        if let m = recent?.submenu { m.delegate?.menuNeedsUpdate?(m) }
        Engine.note("blank: project \(project ?? "none") figure \(figure?.name ?? "none"); Open Recent: \(recent?.submenu?.items.map(\.title) ?? ["missing"])")
        if let first = recent?.submenu?.items.first, first.action != nil {
            openRecent(first)
            for _ in 0..<100 where figure == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
            await settle(); shot(dir, "B2-opened-recent")
            Engine.note("blank: opened \(project ?? "none") figure \(figure?.name ?? "none")")
        }
        NSApp.terminate(nil)
    }

    /// The journal menus: the export dialog with PLOS Computational Biology chosen, Journal Convention, figure widths.
    private func journalShots(_ dir: String) async {
        exportFigure(nil)
        try? await Task.sleep(nanoseconds: 1_500_000_000)
        if let sheet = window.attachedSheet, let acc = (sheet as? NSSavePanel)?.accessoryView,
           let pop = acc.descendants(NSPopUpButton.self).first(where: { $0.itemArray.contains { $0.representedObject as? String == "plos-computational-biology" } }),
           let i = pop.itemArray.firstIndex(where: { $0.representedObject as? String == "plos-computational-biology" }) {
            Engine.note("journals: export menu \(pop.numberOfItems) items, \(pop.itemArray.filter { $0.representedObject != nil }.count) journals")
            pop.selectItem(at: i)
            if let a = pop.action { NSApp.sendAction(a, to: pop.target, from: pop) }
            try? await Task.sleep(nanoseconds: 500_000_000)
            if let rep = acc.bitmapImageRepForCachingDisplay(in: acc.bounds) {
                acc.cacheDisplay(in: acc.bounds, to: rep)
                try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: dir).appendingPathComponent("J1-export-plos.png"))
            }
            Engine.note("journals: resolution for PLOS TIFF? " + (acc.descendants(NSComboBox.self).first?.stringValue ?? "-"))
            window.endSheet(sheet)
        } else { Engine.note("journals: no export menu") }
        let labels = NSApp.mainMenu?.items.first { $0.title == "Labels" }?.submenu
        if let jc = labels?.items.first(where: { $0.title == "Journal Convention" })?.submenu {
            jc.delegate?.menuNeedsUpdate?(jc)
            Engine.note("journals: Journal Convention \(jc.items.filter { $0.action != nil }.count) journals, headers \(jc.items.filter { !$0.isEnabled && !$0.isSeparatorItem }.map(\.title))")
        }
        canvas.selection = []
        await settle()
        if let w = inspector.descendants(NSPopUpButton.self).first(where: { $0.itemTitle(at: 0) == "Width for a journal…" }) {
            Engine.note("journals: width menu \(w.itemArray.filter { $0.representedObject != nil }.count) widths")
        }
        NSApp.terminate(nil)
    }

    /// Select All, Copy, Cut, Paste, Undo and Delete through the menu bar's shortcuts, with each place one types in
    /// focused in turn: the canvas, the code pane, the terminal, an inspector field, the BioCartoon search, the SSH
    /// dialog; and copying from one into another. Lines "audit PASS/FAIL ..." in the log.
    private func auditEditing(_ dir: String) async {
        var fails = 0
        func check(_ what: String, _ ok: Bool, _ detail: String = "") {
            if !ok { fails += 1 }
            Engine.note("audit \(ok ? "PASS" : "FAIL") \(what)\(detail.isEmpty ? "" : ": " + detail)")
        }
        // what the menu bar does with a shortcut: its action to the first responder that takes it, up the chain to the
        // window and its delegate (sent here directly, so it doesn't matter which app is in front while this runs)
        let actions: [String: Selector] = ["a": #selector(NSResponder.selectAll(_:)), "c": #selector(NSText.copy(_:)),
                                           "v": #selector(NSText.paste(_:)), "x": #selector(NSText.cut(_:)),
                                           "z": #selector(MainController.undoEdit(_:)), "Z": #selector(MainController.redoEdit(_:)),
                                           "d": #selector(MainController.duplicate(_:))]
        func key(_ ch: String, _ mods: NSEvent.ModifierFlags = [.command], in win: NSWindow? = nil) {
            let window = win ?? self.window
            if ch == "\u{1b}" {
                if let e = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                                            windowNumber: window.windowNumber, context: nil, characters: ch, charactersIgnoringModifiers: ch,
                                            isARepeat: false, keyCode: 53) { window.firstResponder?.keyDown(with: e) }
                return
            }
            guard let sel = actions[mods.contains(.shift) ? ch.uppercased() : ch] else { return }
            var r: NSResponder? = window.firstResponder
            while let x = r, !x.responds(to: sel) { r = x.nextResponder }
            if let x = r { _ = x.perform(sel, with: nil) }
            else if window.responds(to: sel) { window.perform(sel, with: nil) }
            else if let d = window.delegate as? NSObject, d.responds(to: sel) { d.perform(sel, with: nil) }
        }
        func pbString() -> String { NSPasteboard.general.string(forType: .string) ?? "" }
        func setPB(_ s: String) { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s, forType: .string) }
        func enabled(_ title: String) -> Bool {
            guard let item = NSApp.mainMenu?.items.first(where: { $0.title == "Edit" })?.submenu?.items.first(where: { $0.title.hasPrefix(title) }),
                  let sel = item.action else { return false }
            var r: NSResponder? = window.firstResponder
            while let x = r, !x.responds(to: sel) { r = x.nextResponder }
            let target: AnyObject? = r ?? (window.delegate as AnyObject?)
            return (target as? NSMenuItemValidation)?.validateMenuItem(item) ?? (target != nil)
        }
        // the shortcuts themselves
        let edit = NSApp.mainMenu?.items.first { $0.title == "Edit" }?.submenu
        let shortcuts = (edit?.items ?? []).filter { !$0.keyEquivalent.isEmpty }.map { i -> String in
            let m = i.keyEquivalentModifierMask
            return "\(m.contains(.option) ? "⌥" : "")\(m.contains(.shift) ? "⇧" : "")\(m.contains(.command) ? "⌘" : "")\(i.keyEquivalent.uppercased()) \(i.title)"
        }
        for want in ["⌘Z Undo", "⇧⌘Z Redo", "⌘X Cut", "⌘C Copy", "⌘V Paste", "⌘A Select All", "⌘D Duplicate"] {
            check("Edit menu has \(want)", shortcuts.contains { $0.hasPrefix(want) })
        }
        let allKeys = (NSApp.mainMenu?.items ?? []).flatMap { m -> [String] in
            func walk(_ menu: NSMenu?) -> [String] {
                (menu?.items ?? []).flatMap { i in i.submenu != nil ? walk(i.submenu) :
                    (i.keyEquivalent.isEmpty ? [] : ["\(i.keyEquivalentModifierMask.rawValue)-\(i.keyEquivalent)"]) }
            }
            return walk(m.submenu)
        }
        let clashes = Dictionary(grouping: allKeys, by: { $0 }).filter { $0.value.count > 1 }.map(\.key)
        check("no two menu items share a shortcut", clashes.isEmpty, clashes.joined(separator: ", "))
        NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil)
        await settle()

        // --- the canvas ---
        window.makeFirstResponder(canvas); canvas.selection = []
        let top = figure?.elements.filter { $0.parentOid == nil && !$0.hidden && !$0.locked } ?? []
        check("canvas, nothing selected: Copy enabled (copies the figure), Cut and Delete not", enabled("Copy") && !enabled("Cut") && !enabled("Delete"))
        key("a")
        check("canvas ⌘A selects every element", Set(canvas.selection) == Set(top.map(\.oid)), "\(canvas.selection.count) of \(top.count)")
        let all = figure?.elements ?? []
        check("⌘A leaves out locked, hidden and layout parts", !canvas.selection.contains { oid in all.first { $0.oid == oid }.map { $0.locked || $0.hidden || $0.parentOid != nil } ?? false })
        canvas.selection = [top.first { $0.kind == "text" }?.oid ?? top[0].oid]
        key("c"); await settle()
        check("canvas ⌘C puts the element's code on the clipboard", pbString().contains("fig."), String(pbString().prefix(60)))
        let n0 = figure?.elements.count ?? 0
        window.makeFirstResponder(canvas); key("v"); await settle()
        check("canvas ⌘V pastes it", (figure?.elements.count ?? 0) == n0 + 1, "\(n0) -> \(figure?.elements.count ?? 0)")
        window.makeFirstResponder(canvas); key("z"); await settle()
        check("⌘Z undoes the paste", (figure?.elements.count ?? 0) == n0)
        window.makeFirstResponder(canvas); key("z", [.command, .shift]); await settle()
        check("⇧⌘Z redoes it", (figure?.elements.count ?? 0) == n0 + 1)
        if let last = figure?.elements.last { canvas.selection = [last.oid] }
        window.makeFirstResponder(canvas); key("x"); await settle()
        check("canvas ⌘X cuts it (code on the clipboard, element gone)", (figure?.elements.count ?? 0) == n0 && pbString().contains("fig."))
        canvas.selection = [top[0].oid]
        window.makeFirstResponder(canvas); key("\u{1b}", [])
        check("Esc deselects", canvas.selection.isEmpty)
        window.makeFirstResponder(canvas); key("d"); await settle()
        check("⌘D with nothing selected does nothing", (figure?.elements.count ?? 0) == n0)
        canvas.selection = [top[0].oid]; window.makeFirstResponder(canvas); key("d"); await settle()
        check("⌘D duplicates", (figure?.elements.count ?? 0) == n0 + 1)
        window.makeFirstResponder(canvas); key("z"); await settle()

        // --- the code pane ---
        showCodePane(nil); await settle()
        // ⌥⌘E toggles: if the drawer was already showing the code, that closed it
        if code.textView?.isHiddenOrHasHiddenAncestor == true { showCodePane(nil); await settle() }
        if let tv = code.textView {
            // the file it shows may still be loading on a first run (nothing cached): wait until it holds still
            for _ in 0..<50 {
                let before = tv.string
                try? await Task.sleep(nanoseconds: 200_000_000)
                if !before.isEmpty && before == tv.string { break }
            }
            window.makeFirstResponder(tv)
            let text = tv.string
            key("a")
            check("code pane ⌘A selects all its text", tv.selectedRange() == NSRange(location: 0, length: (text as NSString).length))
            key("c")
            check("code pane ⌘C copies it", pbString() == text,
                  "clipboard \(pbString().prefix(60).debugDescription) (\(pbString().count)) vs text \(text.prefix(60).debugDescription) (\(text.count)), responder \(String(describing: type(of: window.firstResponder ?? NSResponder())))")
            tv.setSelectedRange(NSRange(location: 0, length: 0))
            setPB("# pasted by the audit\n")
            key("v")
            check("code pane ⌘V pastes", tv.string.hasPrefix("# pasted by the audit"))
            key("z")
            check("code pane ⌘Z undoes the typing, not a figure edit", tv.string == text, "")
            check("code pane: nothing left unsaved", !code.dirty)
        } else { check("code pane exists", false) }

        // --- an inspector field ---
        canvas.selection = []; await settle()
        if let field = inspector.descendants(Field.self).first(where: { !$0.stringValue.isEmpty }) {
            window.makeFirstResponder(field)
            key("a")
            let ed = field.currentEditor() as? NSTextView
            check("inspector field ⌘A selects its text", ed?.selectedRange().length == (field.stringValue as NSString).length, field.stringValue)
            key("c")
            check("inspector field ⌘C copies it", pbString() == field.stringValue)
            window.makeFirstResponder(canvas)
        } else { check("inspector field found", false) }

        // --- the terminal ---
        showTerminal(nil)
        try? await Task.sleep(nanoseconds: 3_000_000_000)
        if let s = terminal.activeSession {
            window.makeFirstResponder(s.terminalView)
            setPB("echo FIGNIMBLE_AUDIT_$((40+2))")
            key("v")
            try? await Task.sleep(nanoseconds: 500_000_000)
            check("terminal ⌘V pastes text", s.screenText.contains("echo FIGNIMBLE_AUDIT_$((40+2))"))
            s.terminalView.send(txt: "\r")
            try? await Task.sleep(nanoseconds: 1_000_000_000)
            check("the pasted command runs", s.screenText.contains("FIGNIMBLE_AUDIT_42"))
            key("a"); key("c")
            check("terminal ⌘A, ⌘C copies what it shows", pbString().contains("FIGNIMBLE_AUDIT_42"))
            // from the canvas into the terminal: an element's code
            canvas.selection = [top[0].oid]; window.makeFirstResponder(canvas); key("c"); await settle()
            let code1 = pbString().components(separatedBy: "\n").first ?? ""
            window.makeFirstResponder(s.terminalView)
            s.terminalView.send(txt: "# ")
            key("v")
            try? await Task.sleep(nanoseconds: 500_000_000)
            check("canvas ⌘C, terminal ⌘V: the element's code in the terminal", !code1.isEmpty && s.screenText.contains(String(code1.prefix(30))), String(code1.prefix(40)))
            s.terminalView.send(data: [0x15])            // ^U: drop the line, run nothing
            let before = figure?.elements.count ?? 0
            key("z")
            await settle()
            check("⌘Z in the terminal leaves the figure alone", (figure?.elements.count ?? 0) == before)
            window.makeFirstResponder(canvas)
        } else { check("terminal started", false) }

        // --- the BioCartoon search ---
        showBioCartoon(nil); try? await Task.sleep(nanoseconds: 1_500_000_000)
        if let search = biocartoonPane?.view.descendants(NSSearchField.self).first {
            window.makeFirstResponder(search)
            setPB("ribosome")
            key("a"); key("v")
            check("BioCartoon search ⌘V pastes", search.stringValue == "ribosome", search.stringValue)
            key("a"); key("c")
            check("BioCartoon search ⌘A, ⌘C copies", pbString() == "ribosome")
        } else { check("BioCartoon search field found", false) }
        hideBioCartoon()

        // --- the SSH dialog ---
        let d = RemoteDialog(main: self); d.begin()
        try? await Task.sleep(nanoseconds: 500_000_000)
        if let host = d.panel.contentView?.firstDescendant(NSComboBox.self) {
            d.panel.makeFirstResponder(host)
            setPB("me@cluster.example.org")
            key("a", in: d.panel); key("v", in: d.panel)
            check("SSH dialog ⌘V pastes into the server field", host.stringValue == "me@cluster.example.org", host.stringValue)
        } else { check("SSH dialog server field", false) }
        window.attachedSheet.map { window.endSheet($0) }

        // --- Copy Figure, with nothing selected ---
        canvas.selection = []; window.makeFirstResponder(canvas); key("c"); await settle()
        check("⌘C with nothing selected: the figure as PDF and PNG", NSPasteboard.general.data(forType: .pdf) != nil && NSPasteboard.general.data(forType: .png) != nil)
        Engine.note("audit done: \(fails) failed")
        NSApp.terminate(nil)
    }

    /// A BioCartoon icon (SVG, then PNG) placed and selected: the inspector shows its title, license and credit.
    private func bioCartoonInfoShots(_ dir: String) async {
        showBioCartoon(nil)
        for _ in 0..<300 where biocartoonPane?.exe == nil { try? await Task.sleep(nanoseconds: 100_000_000) }
        var svg = BCFilter(); svg.fileTypes = ["svg"]
        var png = BCFilter(); png.fileTypes = ["png"]
        var placed: [String] = []
        for (q, filter) in [("C57 mouse", svg), ("mitochondria", png)] {
            guard let exe = biocartoonPane?.exe, let icon = try? await BioCartoonCLI.search(exe, query: q, filter: filter, sort: "relevance", offset: 0, limit: 1).first,
                  let data = try? JSONEncoder().encode(icon) else { Engine.note("bcinfo: no \(q)"); continue }
            let n = figure?.elements.count ?? 0
            _ = biocartoonDropped(data, at: CGPoint(x: 60 + Double(placed.count) * 60, y: 80))
            for _ in 0..<200 where (figure?.elements.count ?? 0) == n { try? await Task.sleep(nanoseconds: 100_000_000) }
            await settle()
            if let e = figure?.elements.last { placed.append(e.oid) }
        }
        hideBioCartoon()
        for (i, oid) in placed.enumerated() {
            canvas.selection = [oid]
            await settle()
            let e = figure?.element(oid)
            Engine.note("bcinfo: \(e?.s("source") ?? "-") origin \(e?.codeFile.flatMap { Inspector.bioCartoonOrigin($0) }?.license ?? "none")")
            if let sv = inspector.descendants(NSScrollView.self).first, let doc = sv.documentView {
                doc.scroll(NSPoint(x: 0, y: doc.isFlipped ? doc.bounds.height : 0))     // to the BioCartoon section
            }
            await settle()
            shot(dir, "bcinfo-\(i)")
        }
        NSApp.terminate(nil)
    }

    /// From a project with its code showing to a new project, and to an empty folder: nothing of the first is left.
    private func freshShots(_ dir: String) async {
        func report(_ what: String) {
            let first = code.textView.string.split(separator: "\n").prefix(3).joined(separator: " | ")
            Engine.note("fresh \(what): project \(project ?? "-") figure \(figure?.file ?? "-") elements \(figure?.elements.count ?? -1) "
                        + "code \(code.path ?? "-") text [\(first)] selection \(canvas.selection) figures \(figures.count)")
        }
        showCodePane(nil)
        canvas.selection = ["1"]; await settle()
        report("demo"); shot(dir, "fresh-1-demo")
        for (name, template) in [("new", true), ("empty", false)] {
            let d = dir + "/" + name
            try? FileManager.default.removeItem(atPath: d)
            if template { _ = try? await engine.json("new_project", ["dir": d, "journal": "nature"]) }
            else { try? FileManager.default.createDirectory(atPath: d, withIntermediateDirectories: true) }
            openProject(at: URL(fileURLWithPath: d))
            for _ in 0..<100 where project != d { try? await Task.sleep(nanoseconds: 100_000_000) }
            await settle(); await settle()
            report(name); shot(dir, "fresh-\(name)")
        }
        NSApp.terminate(nil)
    }

    /// The caption tab, a label unlinked from its panel, grid and guides, a new table typed into:
    /// FIGNIMBLE_SHOTS_SCENE=v04 (open the project at fig1.py).
    private func v04Shots(_ dir: String) async {
        func log(_ t: String) { Engine.note("v04: " + t); try? (t + "\n").data(using: .utf8).map { d in
            if let h = FileHandle(forWritingAtPath: dir + "/v04.log") { h.seekToEndOfFile(); h.write(d); h.closeFile() }
            else { FileManager.default.createFile(atPath: dir + "/v04.log", contents: d) } } }
        showCaption(nil)
        captionPane.text.string = "Figure 1 | Gene expression in treated cells. a, Expression over time."
        captionPane.commit()
        await settle(); shot(dir, "V1-caption")
        log("caption=\(figure?.caption ?? "-")")
        // unlink label a by double-clicking it, then move its panel: the label stays
        guard let lab = figure?.labels.first, let panel = lab.panelOid else { log("no label"); return }
        let c = CGPoint(x: lab.box.midX, y: lab.box.midY)
        mouse(.leftMouseDown, c); mouse(.leftMouseUp, c)
        mouse(.leftMouseDown, c, clicks: 2); mouse(.leftMouseUp, c, clicks: 2)
        await settle()
        log("detached=\(figure?.label(of: panel)?.detached ?? false)")
        canvas.selection = [panel]
        edit(["op": "move", "oids": [panel], "dx": 8, "dy": 6])
        await settle(); shot(dir, "V2-label-unlinked")
        log("label box before \(lab.box) after \(figure?.label(of: panel)?.box ?? .zero)")
        // grid lines and guides
        canvas.showGrid = true
        addVerticalGuide(nil); addHorizontalGuide(nil)
        await settle(); shot(dir, "V3-grid-guides")
        log("guides=\(figure?.guides.count ?? 0)")
        canvas.showGrid = false
        clearGuides(nil)
        await settle()
        // a panel letter selected: its font, size and color
        if let l = figure?.labels.dropFirst().first { canvas.selection = [l.oid] }
        await settle(); shot(dir, "V3b-label-inspector")
        // a new 5-row table, typed into on the figure: double-click a cell, type, Tab, type
        let r = try? await engine.json("new_table", ["name": "table", "rows": 5, "cols": 3])
        if let n = r?["name"] as? String { await refreshState(); await loadFigure(n) }
        await settle()
        if let t = figure?.elements.first(where: { $0.kind == "table" }) {
            canvas.selection = [t.oid]
            log("rows shown=\(t.gridRows.count) cells=\(t.tableCells)")
            await settle(); shot(dir, "V4a-new-table")
            if let b = t.tableCellBox(1, 0) {
                let c = CGPoint(x: b.midX, y: b.midY)
                mouse(.leftMouseDown, c); mouse(.leftMouseUp, c)
                mouse(.leftMouseDown, c, clicks: 2); mouse(.leftMouseUp, c, clicks: 2)
                for word in ["CD3E", "2.4", "0.001", "GAPDH"] {
                    if let ed = window.firstResponder as? NSTextView {
                        ed.string = word
                        ed.doCommand(by: #selector(NSResponder.insertTab(_:)))
                    } else { log("no cell editor") }
                }
                window.makeFirstResponder(canvas)
            }
        }
        await settle(); await settle(); shot(dir, "V4-table")
        log("cells now=\(figure?.elements.first(where: { $0.kind == "table" })?.tableCells ?? [])")
        log("table figure=\(figure?.isTableFigure ?? false) size=\(figure?.size ?? .zero)")
        // a table embedded in a figure (Insert ▸ Table…): a grid of cells, typed into on the figure
        await loadFigure("fig1"); await settle()
        let e = try? await engine.json("insert_table", ["figure": "fig1", "rows": 3, "cols": 3, "x": 120, "y": 70, "pxPerMM": lastPx])
        if let fig = e?["figure"] as? [String: Any] { apply(fig) }
        await settle()
        if let t = figure?.elements.last(where: { $0.kind == "table" }) {
            canvas.selection = [t.oid]
            await settle()
            if let b = t.tableCellBox(0, 0) {
                let c = CGPoint(x: b.midX, y: b.midY)
                mouse(.leftMouseDown, c); mouse(.leftMouseUp, c)
                mouse(.leftMouseDown, c, clicks: 2); mouse(.leftMouseUp, c, clicks: 2)
                for word in ["Gene", "n", "p", "CD3E", "12", "0.01"] {
                    if let ed = window.firstResponder as? NSTextView {
                        ed.string = word
                        ed.doCommand(by: #selector(NSResponder.insertTab(_:)))
                    } else { log("no cell editor") }
                }
                window.makeFirstResponder(canvas)
            }
        }
        await settle(); await settle(); shot(dir, "V5-embedded-table")
        log("embedded cells=\(figure?.elements.last(where: { $0.kind == "table" })?.tableCells ?? [])")
        NSApp.terminate(nil)
    }

    private func settle() async {
        try? await Task.sleep(nanoseconds: 300_000_000)
        await waitForQueue()
        try? await Task.sleep(nanoseconds: 300_000_000)
    }

    /// A mouse event at a figure point (mm), sent to the canvas.
    private func mouse(_ type: NSEvent.EventType, _ p: CGPoint, clicks: Int = 1) {
        let w = canvas.convert(canvas.toView(p), to: nil)
        guard let e = NSEvent.mouseEvent(with: type, location: w, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                                         windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: clicks, pressure: 1) else { return }
        switch type {
        case .leftMouseDown: canvas.mouseDown(with: e)
        case .leftMouseDragged: canvas.mouseDragged(with: e)
        default: canvas.mouseUp(with: e)
        }
    }

    private func shot(_ dir: String, _ name: String) {
        guard let v = window.contentView, let rep = v.bitmapImageRepForCachingDisplay(in: v.bounds) else { return }
        v.cacheDisplay(in: v.bounds, to: rep)
        try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: dir).appendingPathComponent(name + ".png"))
        Engine.note("shot \(name)")
    }
}

private extension NSView {
    func descendants<T: NSView>(_ type: T.Type) -> [T] {
        subviews.flatMap { ($0 as? T).map { [$0] } ?? [] } + subviews.flatMap { $0.descendants(type) }
    }
    func firstDescendant<T: NSView>(_ type: T.Type) -> T? { descendants(type).first }
}
