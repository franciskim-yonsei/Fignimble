import AppKit

/// A text field that reports its value when Return is pressed or it loses focus, if it changed.
final class Field: NSTextField, NSTextFieldDelegate {
    var onCommit: ((String) -> Void)?
    private var initial = ""
    convenience init(_ value: String, width: CGFloat = 56, commit: @escaping (String) -> Void) {
        self.init(string: value)
        initial = value
        onCommit = commit
        delegate = self
        font = Theme.font(11); controlSize = .small; bezelStyle = .roundedBezel
        widthAnchor.constraint(equalToConstant: width).isActive = true
    }
    private func fire() {
        guard stringValue != initial else { return }
        initial = stringValue
        onCommit?(stringValue)
    }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy s: Selector) -> Bool {
        if s == #selector(NSResponder.insertNewline(_:)) { fire(); window?.makeFirstResponder(nil); return true }
        return false
    }
    func controlTextDidEndEditing(_ obj: Notification) { fire() }
}

/// Buttons, popups and checkboxes that call a closure.
final class Action: NSObject {
    let run: (Any?) -> Void
    init(_ run: @escaping (Any?) -> Void) { self.run = run }
    @objc func fire(_ sender: Any?) { run(sender) }
}

/// The panel on the right: what is selected, and its settings; with nothing selected, the figure's.
final class Inspector: NSView {
    weak var main: MainController?
    private let stack = NSStackView()
    private let scroll = NSScrollView()
    private var actions: [Action] = []

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        Theme.layerColors(self) { $0.layer?.backgroundColor = Theme.sidebarBackground.cgColor }
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 14, bottom: 16, right: 14)
        let doc = FlippedView()
        doc.translatesAutoresizingMaskIntoConstraints = false
        stack.translatesAutoresizingMaskIntoConstraints = false
        doc.addSubview(stack)
        scroll.documentView = doc
        scroll.drawsBackground = false; scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true
        scroll.translatesAutoresizingMaskIntoConstraints = false
        addSubview(scroll)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor), scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.topAnchor.constraint(equalTo: topAnchor), scroll.bottomAnchor.constraint(equalTo: bottomAnchor),
            doc.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor), doc.trailingAnchor.constraint(equalTo: scroll.contentView.trailingAnchor),
            doc.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            stack.leadingAnchor.constraint(equalTo: doc.leadingAnchor), stack.trailingAnchor.constraint(equalTo: doc.trailingAnchor),
            stack.topAnchor.constraint(equalTo: doc.topAnchor), stack.bottomAnchor.constraint(equalTo: doc.bottomAnchor),
            widthAnchor.constraint(greaterThanOrEqualToConstant: 240),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }

    // MARK: building blocks

    private func clear() { stack.arrangedSubviews.forEach { $0.removeFromSuperview() }; actions = [] }
    private func add(_ v: NSView) { stack.addArrangedSubview(v) }
    private func section(_ title: String) {
        if !stack.arrangedSubviews.isEmpty { let h = hairline(); add(h); h.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -28).isActive = true }
        // a long title (a text's whole content) is cut short, so it can't widen the inspector and the window
        let t = title.count > 38 ? String(title.prefix(37)) + "…" : title
        let l = sectionLabel(t)
        l.lineBreakMode = .byTruncatingTail
        l.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        add(l)
    }
    private func note(_ s: String) {
        let l = NSTextField(wrappingLabelWithString: s)
        l.font = Theme.font(10.5); l.textColor = .secondaryLabelColor
        l.preferredMaxLayoutWidth = 220
        add(l)
    }
    private func row(_ items: [(String, NSView)]) {
        let r = NSStackView(); r.spacing = 6; r.alignment = .centerY
        for (label, v) in items {
            if !label.isEmpty { let l = fieldLabel(label); l.setContentHuggingPriority(.required, for: .horizontal); r.addArrangedSubview(l) }
            r.addArrangedSubview(v)
        }
        add(r)
    }
    private func action(_ f: @escaping (Any?) -> Void) -> Action { let a = Action(f); actions.append(a); return a }
    private func button(_ title: String, symbol: String? = nil, _ f: @escaping () -> Void) -> NSButton {
        let a = action { _ in f() }
        let b = NSButton(title: title, target: a, action: #selector(Action.fire(_:)))
        if let s = symbol { b.image = NSImage(systemSymbolName: s, accessibilityDescription: title); b.imagePosition = title.isEmpty ? .imageOnly : .imageLeading }
        b.bezelStyle = .rounded; b.controlSize = .small; b.font = Theme.font(11)
        b.toolTip = title.isEmpty ? nil : title
        return b
    }
    /// What a picture inserted from BioCartoon says about itself (the catalog record written into the file on
    /// insert), read once per version of the file.
    private static var origins: [String: (Date?, BCIcon?)] = [:]
    static func bioCartoonOrigin(_ path: String) -> BCIcon? {
        let date = (try? FileManager.default.attributesOfItem(atPath: path)[.modificationDate]) as? Date
        if let (d, icon) = origins[path], d == date { return icon }
        let icon = BioCartoonCLI.origin(ofImageAt: path)
        origins[path] = (date, icon)
        return icon
    }

    /// The license's page: Creative Commons ones from their SPDX name (CC-BY-4.0, CC-BY-SA-3.0, CC0-1.0).
    static func licenseURL(_ license: String) -> URL? {
        let l = license.uppercased()
        if l.hasPrefix("CC0") { return URL(string: "https://creativecommons.org/publicdomain/zero/1.0/") }
        guard let r = l.range(of: #"^CC-(BY(-NC)?(-SA|-ND)?)-(\d\.\d)$"#, options: .regularExpression) else { return nil }
        let parts = String(l[r]).dropFirst(3).split(separator: "-")
        return URL(string: "https://creativecommons.org/licenses/\(parts.dropLast().joined(separator: "-").lowercased())/\(parts.last!)/")
    }

    /// Selected picture from BioCartoon: where it comes from, its license and the credit the legend needs.
    private func bioCartoonSection(_ icon: BCIcon) {
        guard let m = main else { return }
        section("BioCartoon")
        func fact(_ name: String, _ value: String?) {
            guard let v = value?.trimmingCharacters(in: .whitespaces), !v.isEmpty else { return }
            let t = NSTextField(wrappingLabelWithString: v)
            t.font = Theme.font(11); t.isSelectable = true; t.preferredMaxLayoutWidth = 160
            row([(name, t)])
        }
        fact("Title", icon.title)
        fact("Author", icon.author)
        fact("Collection", icon.source_name ?? icon.source)
        fact("License", icon.license)
        fact("Category", icon.taxa?.map { $0.subcategory.isEmpty ? $0.category : "\($0.category) › \($0.subcategory)" }
            .joined(separator: ", ") ?? icon.category)
        fact("Tags", icon.tags?.joined(separator: ", "))
        fact("ID", icon.id)
        let credit = icon.credit ?? icon.title
        let c = NSTextField(wrappingLabelWithString: credit)
        c.font = Theme.font(10.5); c.textColor = .secondaryLabelColor; c.isSelectable = true; c.preferredMaxLayoutWidth = 220
        add(c)
        let lic = (icon.license ?? "").uppercased()
        if lic.hasPrefix("CC-BY") { note("The license asks for this credit, in the figure legend or the acknowledgements.") }
        else if lic.hasPrefix("CC0") { note("Public domain: no credit needed, though it is welcome.") }
        else if lic.isEmpty { note("No license recorded: check the source before publishing.") }
        var buttons: [(String, NSView)] = [("", button("Copy Credit", symbol: "doc.on.doc") {
            BioCartoonCLI.copy(credit); m.toast("Copied the credit")
        })]
        if let u = icon.url, u.hasPrefix("http"), let url = URL(string: u) {
            buttons.append(("", button("Open Page", symbol: "safari") { NSWorkspace.shared.open(url) }))
        }
        if let url = Inspector.licenseURL(icon.license ?? "") {
            buttons.append(("", button("License", symbol: "checkmark.seal") { NSWorkspace.shared.open(url) }))
        }
        row(buttons)
    }

    private func iconButton(_ symbol: String, _ tip: String, _ f: @escaping () -> Void) -> NSButton {
        let b = button("", symbol: symbol, f); b.toolTip = tip; b.bezelStyle = .texturedRounded
        return b
    }
    private func popup(_ items: [String], _ selected: String?, width: CGFloat = 120, _ f: @escaping (Int, String) -> Void) -> NSPopUpButton {
        let p = NSPopUpButton(frame: .zero, pullsDown: false)
        p.addItems(withTitles: items); p.controlSize = .small; p.font = Theme.font(11)
        if let s = selected, items.contains(s) { p.selectItem(withTitle: s) }
        let a = action { s in if let p = s as? NSPopUpButton { f(p.indexOfSelectedItem, p.titleOfSelectedItem ?? "") } }
        p.target = a; p.action = #selector(Action.fire(_:))
        p.widthAnchor.constraint(equalToConstant: width).isActive = true
        return p
    }
    private func check(_ title: String, _ on: Bool, _ f: @escaping (Bool) -> Void) -> NSButton {
        let a = action { s in if let b = s as? NSButton { f(b.state == .on) } }
        let b = NSButton(checkboxWithTitle: title, target: a, action: #selector(Action.fire(_:)))
        b.state = on ? .on : .off; b.font = Theme.font(11); b.controlSize = .small
        return b
    }
    /// A color well, and the project's palette as swatches beside it (the color picker's own eyedropper picks any
    /// color on the screen).
    private func color(_ hex: String?, _ f: @escaping (String) -> Void) -> NSView {
        let w = NSColorWell(frame: NSRect(x: 0, y: 0, width: 44, height: 22))
        if #available(macOS 13.0, *) { w.colorWellStyle = .minimal }
        w.color = NSColor(hex: hex) ?? .black
        let hexField = Field(hex?.uppercased() ?? "", width: 62) { t in
            var v = t.trimmingCharacters(in: .whitespaces)
            if !v.hasPrefix("#") { v = "#" + v }
            if NSColor(hex: v) != nil { f(v.uppercased()) }
        }
        hexField.font = Theme.mono(10)
        // the color picker reports every step of a drag: write the color once it settles
        var pending: DispatchWorkItem?
        let a = action { s in
            guard let w = s as? NSColorWell else { return }
            let c = w.color.hex
            hexField.stringValue = c
            pending?.cancel()
            let job = DispatchWorkItem { f(c) }
            pending = job
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.45, execute: job)
        }
        w.target = a; w.action = #selector(Action.fire(_:))
        w.widthAnchor.constraint(equalToConstant: 44).isActive = true
        w.heightAnchor.constraint(equalToConstant: 20).isActive = true
        let palette = main?.figure?.palette ?? []
        let top = NSStackView(views: [w, hexField]); top.spacing = 3
        guard !palette.isEmpty else { return top }
        // the palette's swatches under the well, so the row stays narrow
        let column = NSStackView(); column.orientation = .vertical; column.alignment = .leading; column.spacing = 3
        column.addArrangedSubview(top)
        let row = NSStackView(); row.spacing = 3
        column.addArrangedSubview(row)
        for (name, c) in palette.prefix(8) {
            let b = SwatchButton(color: NSColor(hex: c) ?? .gray)
            b.toolTip = "\(name) \(c)"
            let act = action { _ in f(c) }
            b.target = act; b.action = #selector(Action.fire(_:))
            row.addArrangedSubview(b)
        }
        return column
    }
    /// The fonts installed on this Mac, to pick from or type (empty, when `blank` says what that means: the
    /// style's font).
    private func fontPicker(_ value: String?, blank: String? = nil, width: CGFloat = 170, _ f: @escaping (String?) -> Void) -> FontPicker {
        FontPicker(value, blank: blank, width: width, commit: f)
    }
    private func numberField(_ v: Double?, width: CGFloat = 52, _ f: @escaping (Double) -> Void) -> Field {
        Field(v.map(mm) ?? "", width: width) { s in if let d = Double(s.replacingOccurrences(of: ",", with: ".")) { f(d) } }
    }

    // MARK: content

    func update() {
        clear()
        guard let m = main, let f = m.figure else {
            add(sectionLabel("Fignimble"))
            guard let m = main, m.project == nil else { note("No figure."); return }
            note("Open a project or make one. A project is a folder of figures, each a Python file.")
            row([("", button("New Project…", symbol: "plus") { m.newProject(nil) }), ("", button("Open Project…", symbol: "folder") { m.openProject(nil) })])
            let recent = MainController.recentProjects.prefix(6)
            if !recent.isEmpty {
                section("Recent")
                for p in recent {
                    let b = button((p as NSString).lastPathComponent, symbol: "clock") {
                        let it = NSMenuItem(); it.representedObject = p; m.openRecent(it)
                    }
                    b.toolTip = p; b.bezelStyle = .recessed
                    add(b)
                }
            }
            return
        }
        if let err = f.error {
            section("Figure")
            note("The figure's code doesn't run:")
            let l = NSTextField(wrappingLabelWithString: err); l.font = Theme.mono(10); l.textColor = .systemRed; l.isSelectable = true
            l.preferredMaxLayoutWidth = 230; add(l)
            return
        }
        let sel = m.canvas.selection.compactMap { f.element($0) }
        if sel.isEmpty { figureSection(f) }
        else if sel.count == 1 {
            if let pt = m.canvas.part, pt.oid == sel[0].oid { partSection(f, sel[0], pt) }
            elementSection(f, sel[0])
        }
        else { multiSection(f, sel) }
    }

    private func figureSection(_ f: FigureData) {
        guard let m = main else { return }
        if f.isTable { tableSection(f); return }
        section("Figure · \(f.name)")
        pageSizeRow(f)
        if let p = f.poster { posterSection(f, p) }
        // a journal's widths, the journals by publisher
        let widthMenu = NSPopUpButton(frame: .zero, pullsDown: true)
        widthMenu.controlSize = .small; widthMenu.font = Theme.font(11)
        widthMenu.addItem(withTitle: "Width for a journal…")
        let pick = action { s in if let w = (s as? NSMenuItem)?.representedObject as? Double { m.edit(["op": "figure", "props": ["width": w]]) } }
        if let menu = widthMenu.menu {
            m.fillJournalMenu(menu) { k in
                (m.journals[k]?["widths"] as? [String: Any] ?? [:]).sorted { num($0.value) < num($1.value) }.map { n, w in
                    let it = NSMenuItem(title: "\(m.journalName(k)): \(n) (\(mm(num(w))) mm)", action: #selector(Action.fire(_:)), keyEquivalent: "")
                    it.target = pick; it.representedObject = num(w); return it
                }
            }
        }
        widthMenu.widthAnchor.constraint(equalToConstant: 220).isActive = true
        if f.poster == nil { add(widthMenu) }
        row([("Background", color(f.background ?? "#FFFFFF") { m.edit(["op": "figure", "props": ["background": $0]]) })])
        add(check("Transparent background", f.background == nil) { on in m.edit(["op": "figure", "props": ["background": on ? (NSNull() as Any) : "#FFFFFF"]]) })

        section("Style (every panel)")
        let st = f.style
        row([("Font", fontPicker(st["font"] as? String ?? "Arial") { if let v = $0 { m.edit(["op": "style", "props": ["font": v]]) } })])
        row([("Text", numberField(num(st["size"])) { m.edit(["op": "style", "props": ["size": $0]]) }), ("pt   Lines", numberField(num(st["linewidth"])) { m.edit(["op": "style", "props": ["linewidth": $0]]) }), ("pt", NSView())])
        add(check("Hide top and right axis lines", st["despine"] as? Bool ?? true) { m.edit(["op": "style", "props": ["despine": $0]]) })
        row([("Min text", numberField(num(st["min_font"]), width: 40) { m.edit(["op": "style", "props": ["min_font": $0]]) }), ("pt  Min dpi", numberField(num(st["min_dpi"]), width: 48) { m.edit(["op": "style", "props": ["min_dpi": $0]]) })])
        let presets: [(String, String)] = [("okabe-ito", "Okabe–Ito (color-blind safe)"), ("tol-bright", "Paul Tol bright (color-blind safe)"),
                                           ("tol-muted", "Paul Tol muted (color-blind safe)"), ("tol-vibrant", "Paul Tol vibrant (color-blind safe)"),
                                           ("colorbrewer-set2", "ColorBrewer Set2"), ("colorbrewer-dark2", "ColorBrewer Dark2"),
                                           ("tableau", "Tableau (matplotlib's)")]
        let named = st["palette"] as? String
        row([("Palette", popup(["Custom or none"] + presets.map(\.1), presets.first { $0.0 == named }?.1 ?? "Custom or none", width: 190) { i, _ in
            if i > 0 { m.edit(["op": "style", "props": ["palette": presets[i - 1].0]]) } })])
        if !f.palette.isEmpty {
            let sw = NSStackView(); sw.spacing = 3
            for (n, c) in f.palette.prefix(10) { let b = SwatchButton(color: NSColor(hex: c) ?? .gray); b.toolTip = "\(n) \(c)"; sw.addArrangedSubview(b) }
            row([("", sw)])
        }
        let pal = named ?? f.palette.map { $0.0.allSatisfy(\.isNumber) ? $0.1 : "\($0.0)=\($0.1)" }.joined(separator: ", ")
        row([("Colors", Field(pal, width: 160) { t in
            let items = t.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
            if items.isEmpty { m.edit(["op": "style", "props": ["palette": NSNull()]]); return }
            if items.count == 1, f.palettes[items[0].lowercased()] != nil { m.edit(["op": "style", "props": ["palette": items[0].lowercased()]]); return }
            if items.allSatisfy({ $0.contains("=") }) {
                var d: [String: String] = [:]
                for it in items { let kv = it.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }; if kv.count == 2 { d[kv[0]] = kv[1] } }
                m.edit(["op": "style", "props": ["palette": d]])
            } else { m.edit(["op": "style", "props": ["palette": items]]) }
        })])
        note("Panels draw with this font and these sizes, so they match, and plot in the palette's colors in turn (color(\"KO\") in panel code). A palette is #RRGGBB, …, or name=#RRGGBB, …. A style in style.py is shared by every figure that imports it.")
        labelSection(f)
    }

    /// A text box's height and whether its type shrinks to fit it.
    private func textBoxRows(_ f: FigureData, _ e: Element, _ setp: @escaping ([String: Any]) -> Void) {
        row([("Box height", numberField(e.p("height"), width: 44) { setp(["height": $0 <= 0 ? (NSNull() as Any) : $0]) }), ("mm (empty: as tall as its text)", NSView())])
        add(check("Shrink the type to fit the box", e.s("fit") == "shrink") { setp(["fit": $0 ? "shrink" : (NSNull() as Any)]) })
        if let o = e.overflow { note("Runs \(mm(o)) mm past its box.") }
        if let s = e.shrunk { note("Shrunk to \(mm(s)) pt to fit.") }
    }

    /// A poster's size: its preset, turned, and the margin preflight keeps clear along the edges.
    private func posterSection(_ f: FigureData, _ p: [String: Any]) {
        guard let m = main else { return }
        section("Poster")
        let keys = m.posters.sorted { num($0.value["rank"]) < num($1.value["rank"]) }.map(\.key)
        let names = keys.map { k in m.posters[k]?["name"] as? String ?? k }
        let cur = (p["key"] as? String).flatMap { k in keys.firstIndex(of: k) }.map { names[$0] }
        row([("Size", popup(names, cur, width: 110) { i, _ in
                m.edit(["op": "figure", "props": ["poster": keys[i], "width": NSNull(), "height": NSNull()]]) }),
             ("", check("Landscape", p["landscape"] as? Bool ?? false) { on in
                m.edit(["op": "figure", "props": ["poster": p["key"] ?? "a0", "landscape": on, "width": NSNull(), "height": NSNull()]]) })])
        row([("Margin", numberField(num(p["margin"]), width: 40) { m.edit(["op": "figure", "props": ["margin": $0]]) }), ("mm kept clear along the edges", NSView())])
        note("Text at least \(mm(num(p["minFont"]))) pt as printed, figures placed on it included. Export as PDF with a bleed and crop marks for a print shop, or as PowerPoint on a slide the poster's size.")
    }

    /// The page's width and height, in the units picked (mm, cm, in, ft; the code keeps millimeters).
    /// A Table with nothing selected: its title, notes and the style's type, and how to send it to the journal.
    private func tableSection(_ f: FigureData) {
        guard let m = main, let cells = f.tableCells else { return }
        let set = { (props: [String: Any]) in m.edit(["op": "set", "oid": cells.oid, "props": props]) }
        let part = { (role: String) in f.elements.first { $0.role == role }?.s("text") ?? "" }
        section("Table · \(f.name)")
        note("A table of the paper: numbered apart from the figures, and sent to the journal as an editable Word or Excel table (File ▸ Export Table…). Its page is sized to its title, cells and notes.")
        row([("Title", Field(part("title"), width: 200) { set(["title": $0]) })])
        if part("notes").contains("\n") {
            note("Notes: " + part("notes").replacingOccurrences(of: "\n", with: " · "))
            row([("", button("Edit Notes in Code", symbol: "doc.text") { m.showCode(of: cells) })])
        } else {
            row([("Notes", Field(part("notes"), width: 200) { set(["notes": $0]) })])
        }
        row([("", button("Export Table…", symbol: "square.and.arrow.up") { m.exportTable(nil) })])
        section("Style (every figure and table)")
        let st = f.style
        row([("Font", fontPicker(st["font"] as? String ?? "Arial") { if let v = $0 { m.edit(["op": "style", "props": ["font": v]]) } })])
        row([("Text", numberField(num(st["size"])) { m.edit(["op": "style", "props": ["size": $0]]) }), ("pt", NSView())])
    }

    private func pageSizeRow(_ f: FigureData) {
        guard let m = main else { return }
        let units = MainController.sizeUnits
        let u = units.firstIndex { $0.0 == UserDefaults.standard.string(forKey: "newFigureUnit") } ?? 0
        let k = units[u].1
        let set = { (key: String, v: Double) in m.edit(["op": "figure", "props": [key: (v * k * 100).rounded() / 100]]) }
        row([("W", numberField(Double(f.size.width) / k, width: 50) { set("width", $0) }),
             ("H", numberField(Double(f.size.height) / k, width: 50) { set("height", $0) }),
             ("", popup(units.map(\.0), units[u].0, width: 58) { i, _ in
                UserDefaults.standard.set(units[i].0, forKey: "newFigureUnit"); self.update() })])
    }

    static let labelFormats: [(String, String, String)] = [("a", "{}", "lower"), ("(a)", "({})", "lower"), ("a)", "{})", "lower"), ("a.", "{}.", "lower"),
                                                           ("A", "{}", "upper"), ("(A)", "({})", "upper"), ("A)", "{})", "upper"), ("A.", "{}.", "upper")]

    private func labelSection(_ f: FigureData) {
        guard let m = main else { return }
        let ls = f.labelStyle
        section("Panel labels (every panel)")
        let fmt = ls["format"] as? String ?? "{}", cs = ls["case"] as? String ?? "lower"
        let cur = Inspector.labelFormats.first { $0.1 == fmt && $0.2 == cs }?.0
        row([("Style", popup(Inspector.labelFormats.map(\.0), cur, width: 70) { i, _ in
                let p = Inspector.labelFormats[i]; m.edit(["op": "labels", "props": ["format": p.1, "case": p.2]]) }),
             ("", popup(["a b c", "i ii iii", "1 2 3", "none"], ["alpha": "a b c", "roman": "i ii iii", "arabic": "1 2 3", "none": "none"][ls["numbering"] as? String ?? "alpha"], width: 82) { i, _ in
                m.edit(["op": "labels", "props": ["numbering": ["alpha", "roman", "arabic", "none"][i]]]) })])
        row([("Size", numberField(num(ls["size"]), width: 40) { m.edit(["op": "labels", "props": ["size": $0]]) }), ("pt", NSView()),
             ("", check("Bold", (ls["weight"] as? String) == "bold") { m.edit(["op": "labels", "props": ["weight": $0 ? "bold" : "normal"]]) }),
             ("", check("Italic", (ls["style"] as? String) == "italic") { m.edit(["op": "labels", "props": ["style": $0 ? "italic" : "normal"]]) })])
        row([("Font", fontPicker(ls["font"] as? String, blank: "Style's font") { m.edit(["op": "labels", "props": ["font": $0 ?? (NSNull() as Any)]]) })])
        row([("Color", color(ls["color"] as? String ?? "#000000") { m.edit(["op": "labels", "props": ["color": $0]]) })])
        row([("Offset X", numberField(num(ls["dx"]), width: 40) { m.edit(["op": "labels", "props": ["dx": $0]]) }),
             ("Y", numberField(num(ls["dy"]), width: 40) { m.edit(["op": "labels", "props": ["dy": $0]]) }), ("mm", NSView())])
        row([("Order", popup(["Reading order", "Order in the code"], (ls["order"] as? String) == "source" ? "Order in the code" : "Reading order", width: 140) { i, _ in
            m.edit(["op": "labels", "props": ["order": i == 0 ? "reading" : "source"]]) })])
        add(check("Line up labels in rows and columns", ls["align"] as? Bool ?? true) { m.edit(["op": "labels", "props": ["align": $0]]) })
        var jn = ["Use a journal's labels…"], keys: [String] = [""]
        for (k, j) in m.journals.sorted(by: { $0.key < $1.key }) { jn.append(j["name"] as? String ?? k); keys.append(k) }
        add(popup(jn, nil, width: 200) { i, _ in
            guard i > 0, let lab = m.journals[keys[i]]?["labels"] as? [String: Any] else { return }
            m.edit(["op": "labels", "props": lab])
        })
        note("Letters follow the panels' reading order and update when panels move. Drag a label to move every label; ⌥-drag moves one.")
    }

    private func positionRows(_ e: Element) {
        guard let m = main else { return }
        let set: (String) -> (Double) -> Void = { k in { v in m.edit(["op": "set", "oid": e.oid, "props": [k: v]]) } }
        switch e.kind {
        case "line":
            row([("X1", numberField(e.p("x1"), width: 48, set("x1"))), ("Y1", numberField(e.p("y1"), width: 48, set("y1")))])
            row([("X2", numberField(e.p("x2"), width: 48, set("x2"))), ("Y2", numberField(e.p("y2"), width: 48, set("y2")))])
        case "bracket":
            row([("X1", numberField(e.p("x1"), width: 48, set("x1"))), ("X2", numberField(e.p("x2"), width: 48, set("x2")))])
            row([("Y", numberField(e.p("y"), width: 48, set("y"))), ("Tick", numberField(e.p("tick"), width: 40, set("tick"))), ("mm", NSView())])
        case "text" where e.role != nil:
            break                                       // a table's title and notes go where the table puts them
        case "text":
            row([("X", numberField(e.p("x"), set("x"))), ("Y", numberField(e.p("y"), set("y"))), ("mm", NSView())])
        case "table":
            if e.role == nil { row([("X", numberField(e.p("x"), set("x"))), ("Y", numberField(e.p("y"), set("y"))), ("mm", NSView())]) }
            row([("W", numberField(Double(e.box.width), set("w"))), ("mm (never narrower than its text)", NSView())])
        case "path", "zoom", "scalebar", "legend":
            let b = e.box
            note("At \(mm(Double(b.minX))), \(mm(Double(b.minY))) mm, \(mm(Double(b.width))) × \(mm(Double(b.height))) mm.")
        case "qr":
            row([("X", numberField(e.p("x"), set("x"))), ("Y", numberField(e.p("y"), set("y"))), ("mm", NSView())])
            row([("Size", numberField(e.p("w"), set("w"))), ("mm square, with its border", NSView())])
        default:
            row([("X", numberField(e.p("x"), set("x"))), ("Y", numberField(e.p("y"), set("y"))), ("mm", NSView())])
            let hAuto = (e.kind == "image" || e.kind == "figure") && e.props["h"] is NSNull
            row([("W", numberField(e.p("w") ?? Double(e.box.width), set("w"))), ("H", numberField(hAuto ? Double(e.box.height) : e.p("h"), set("h"))), ("mm", NSView())])
            if e.kind == "image" {
                add(check("Keep the picture's proportions", hAuto) { on in
                    m.edit(["op": "set", "oid": e.oid, "props": ["h": on ? (NSNull() as Any) : Double(e.box.height)]]) })
            }
        }
    }

    /// A text or mark inside a panel, picked on the canvas: retype it, set its size, color and weight. The change
    /// goes into the panel's code where the code writes it once, otherwise onto the panel's line (texts=, text_style=,
    /// colors=).
    private func partSection(_ f: FigureData, _ e: Element, _ pt: PanelPart) {
        guard let m = main else { return }
        // an inset's panel settings are its item's
        let props = e.kind == "inset" ? e.item : e.props
        if let t = m.canvas.partText(pt) {
            section("\(t.role.prefix(1).uppercased() + t.role.dropFirst()) in the panel")
            let style = { (p: [String: Any]) in m.edit(["op": "panel_text_style", "oid": e.oid, "key": t.key, "text": t.text, "props": p]) }
            row([("Text", Field(t.text, width: 170) { m.edit(["op": "panel_text", "oid": e.oid, "key": t.key, "text": $0, "old": t.text]) })])
            row([("Size", numberField(t.size, width: 44) { if $0 > 0 { style(["size": $0]) } }), ("pt", NSView())])
            row([("Color", color(t.color) { style(["color": $0]) })])
            row([("", check("Bold", t.bold) { style(["weight": $0 ? "bold" : "normal"]) }),
                 ("", check("Italic", t.italic) { style(["style": $0 ? "italic" : "normal"]) })])
            let changed = (props["texts"] as? [String: Any])?[t.key] != nil || (props["text_style"] as? [String: Any])?[t.key] != nil
            if changed { row([("", button("Back to What the Code Draws") { m.edit(["op": "panel_text_reset", "oid": e.oid, "key": t.key]) })]) }
            note("Return or double-click retypes it on the canvas; Delete empties it; Esc goes back to the panel. A text the panel's code writes once (ax.set_xlabel(\"…\")) is changed there; any other is changed on the panel's line.")
        } else if let mk = m.canvas.partMark(pt) {
            section("Mark in the panel · \(mk.key.replacingOccurrences(of: "ax0.", with: ""))")
            row([("Color", color(mk.color) { m.edit(["op": "panel_color", "oid": e.oid, "keys": [mk.key], "color": $0, "old": mk.color]) })])
            var colors = props["colors"] as? [String: Any] ?? [:]
            if colors[mk.key] != nil {
                row([("", button("Back to the Code's Color") {
                    colors.removeValue(forKey: mk.key)
                    m.edit(["op": "set", "oid": e.oid, "props": ["colors": colors.isEmpty ? (NSNull() as Any) : colors]])
                })])
            }
            note("A color the panel's code writes once is changed there, so every mark drawn with it follows; otherwise only this mark changes (colors= on the panel's line). Esc goes back to the panel.")
        }
    }

    private func elementSection(_ f: FigureData, _ e: Element) {
        guard let m = main else { return }
        let label = f.labelText(e.oid)
        section(e.title(label: label))
        if let err = e.error {
            let l = NSTextField(wrappingLabelWithString: err.trimmingCharacters(in: .whitespacesAndNewlines))
            l.font = Theme.mono(9.5); l.textColor = .systemRed; l.isSelectable = true; l.preferredMaxLayoutWidth = 230
            add(l)
        }
        if e.isLabel { labelOfPanel(f, e); return }
        if let po = e.parentOid, let lay = f.element(po) {
            if e.kind == "legend" {
                note("Collected from the legends of the layout's panels: each entry once. Drag it to move the layout.")
            } else {
                let b = e.box
                note("In a layout, at \(mm(Double(b.minX))), \(mm(Double(b.minY))) mm, \(mm(Double(b.width))) × \(mm(Double(b.height))) mm. Dragging it moves the layout; drag the gaps between panels to resize them.")
            }
            childSection(f, e)
            layoutSection(f, lay)
            return
        }
        if e.kind == "layout" { positionRows(e); layoutSection(f, e); return }
        if e.kind == "inset" { insetSection(f, e); return }
        positionRows(e)
        let setp: ([String: Any]) -> Void = { m.edit(["op": "set", "oid": e.oid, "props": $0]) }
        switch e.kind {
        case "panel":
            section("Code")
            note((e.s("source") ?? "") + (e.s("function") == "draw" ? "" : "  ·  \(e.s("function") ?? "")()"))
            row([("", button("Edit Code", symbol: "chevron.left.forwardslash.chevron.right") { m.showCode(of: e) }),
                 ("", button("Refresh", symbol: "arrow.clockwise") { m.refresh(force: true) })])
            if let a = e.props["args"] as? [String: Any], !a.isEmpty {
                let t = a.map { "\($0.key)=\($0.value)" }.sorted().joined(separator: ", ")
                note("arguments: \(t)")
            }
            note("Resizing redraws the panel at its new size, so its text keeps its size. Click a text in the panel (an axis label, a tick, a legend entry) or a line, bar or points to change it here; double-click a text to retype it.")
            row([("Turn", popup(["0°", "90°", "180°", "270°"], "\(Int(e.p("rotation") ?? 0))°", width: 70) { i, _ in
                setp(["rotation": i == 0 ? (NSNull() as Any) : Double(i * 90)]) })])
            if let t = e.props["texts"] as? [String: Any], !t.isEmpty {
                row([("", button("Undo \(t.count) Retyped Text\(t.count == 1 ? "" : "s")") { setp(["texts": NSNull()]) })])
            }
            panelColorRows(e)
            panelLabelRows(f, e)
        case "image":
            section("Picture")
            note(e.s("source") ?? "")
            row([("Fit", popup(["contain", "cover", "stretch"], e.s("fit"), width: 90) { _, t in setp(["fit": t]) })])
            let crop = (e.props["crop"] as? [Any])?.map { num($0) } ?? [0, 0, 0, 0]
            func setCrop(_ i: Int) -> (Double) -> Void {
                { v in var c = crop; c[i] = max(0, min(0.9, v / 100)); setp(["crop": c.allSatisfy { $0 == 0 } ? (NSNull() as Any) : c]) }
            }
            row([("Crop %  L", numberField(crop[0] * 100, width: 34, setCrop(0))), ("T", numberField(crop[1] * 100, width: 34, setCrop(1))),
                 ("R", numberField(crop[2] * 100, width: 34, setCrop(2))), ("B", numberField(crop[3] * 100, width: 34, setCrop(3)))])
            row([("Opacity", numberField((e.p("opacity") ?? 1) * 100, width: 40) { setp(["opacity": max(0, min(1, $0 / 100))]) }), ("%", NSView())])
            row([("Turn", numberField(e.p("rotation") ?? 0, width: 40) { setp(["rotation": $0 == 0 ? (NSNull() as Any) : $0]) }), ("°", NSView()),
                 ("", iconButton("rotate.left", "Turn 90° left") { m.rotate(by: 90) }),
                 ("", iconButton("rotate.right", "Turn 90° right") { m.rotate(by: -90) }),
                 ("", iconButton("arrow.left.and.right.righttriangle.left.righttriangle.right", "Flip horizontally") { m.flip("h") }),
                 ("", iconButton("arrow.up.and.down.righttriangle.up.righttriangle.down", "Flip vertically") { m.flip("v") })])
            row([("", button("Crop", symbol: "crop") { m.canvas.beginCrop(e.oid) }),
                 ("", button("Replace…", symbol: "photo.on.rectangle") { m.replacePicture(e.oid) }),
                 ("", button("Show in Finder", symbol: "folder") { if let c = e.codeFile { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: c)]) } })])
            row([("", button("Scale Bar…", symbol: "ruler") { m.addScaleBar(nil) }),
                 ("", button("Zoom Box", symbol: "plus.magnifyingglass") { m.addZoom(nil) })])
            panelLabelRows(f, e)
            if let path = e.codeFile, let icon = Inspector.bioCartoonOrigin(path) { bioCartoonSection(icon) }
        case "figure":
            section("Figure placed whole")
            let src = e.s("source") ?? ""
            note(src + (e.scale.map { $0 == 1 ? "  ·  its own size" : "  ·  scaled \(mm($0 * 100))%" } ?? ""))
            row([("", button("Open Figure", symbol: "arrow.up.forward.square") { if let n = e.figureName { m.show(figure: n) } }),
                 ("", button("Own Size", symbol: "arrow.down.right.and.arrow.up.left") { setp(["w": NSNull(), "h": NSNull()]) })])
            add(check("Its panel letters", e.props["letters"] as? Bool ?? true) { setp(["letters": $0]) })
            add(check("Transparent background", e.props["background"] is NSNull) { on in setp(["background": on ? (NSNull() as Any) : "figure"]) })
            note("Drawn from \(src)'s code every time: change that figure (Open Figure, or double-click) and it changes here. Scaling scales its text and lines; preflight checks them at the size they print.")
            panelLabelRows(f, e)
        case "qr":
            section("QR code")
            add(Field(e.s("data") ?? "", width: 220) { setp(["data": $0]) })
            row([("Error correction", popup(["L (7%)", "M (15%)", "Q (25%)", "H (30%)"], ["L": "L (7%)", "M": "M (15%)", "Q": "Q (25%)", "H": "H (30%)"][e.s("level") ?? "M"], width: 90) { i, _ in
                setp(["level": ["L", "M", "Q", "H"][i]]) })])
            row([("Color", color(e.s("color") ?? "#000000") { setp(["color": $0]) }),
                 ("On", color(e.s("background") ?? "#FFFFFF") { setp(["background": $0]) })])
            note("A link (or any text) for phones to read. Keep it dark on light, and on a poster 3 cm or larger; a longer link makes smaller squares.")
        case "text" where e.s("source") != nil:
            section("Text from a file")
            note((e.s("source") ?? "") + "  ·  written as Markdown: # headings, - bullets, 1. numbered, an empty line between paragraphs")
            row([("", button("Edit Text", symbol: "doc.text") { m.showCode(of: e) })])
            textBoxRows(f, e, setp)
        case "text":
            section("Text")
            add(Field(e.s("text") ?? "", width: 220) { setp(["text": $0]) })
            row([("Font", fontPicker(e.s("font"), blank: "Style's font") { setp(["font": $0 ?? (NSNull() as Any)]) })])
            row([("Size", numberField(e.p("size") ?? num(f.style["size"]), width: 40) { setp(["size": $0]) }), ("pt", NSView()),
                 ("", color(e.s("color")) { setp(["color": $0]) })])
            row([("", check("Bold", e.s("weight") == "bold") { setp(["weight": $0 ? "bold" : "normal"]) }),
                 ("", check("Italic", e.s("style") == "italic") { setp(["style": $0 ? "italic" : "normal"]) })])
            row([("Align", popup(["left", "center", "right"], e.s("align"), width: 80) { _, t in
                    // keep the text where it is: x moves to the new anchor
                    let b = e.box
                    let x = ["left": b.minX, "center": b.midX, "right": b.maxX][t] ?? b.minX
                    setp(["align": t, "x": Double(x)]) }),
                 ("Rotate", numberField(e.p("rotation") ?? 0, width: 36) { setp(["rotation": $0]) }), ("°", NSView())])
            row([("Wrap at", numberField(e.p("width"), width: 44) { setp(["width": $0 <= 0 ? (NSNull() as Any) : $0]) }), ("mm (empty: one line)", NSView())])
            row([("Opacity", numberField((e.p("opacity") ?? 1) * 100, width: 40) { setp(["opacity": max(0, min(1, $0 / 100))]) }), ("%", NSView())])
            if e.p("width") != nil { textBoxRows(f, e, setp) }
            note("**bold**, *italic*, ^{superscript}, _{subscript}, and math: $\\alpha$. In a text box, lines starting with - are bullets, 1. numbered, # headings. Double-click the text to retype it; drag its side handles to set where it wraps.")
        case "line":
            section("Line")
            strokeRows(e, setp, fill: false)
            row([("Arrow", popup(["none", "end", "start", "both"], e.s("arrow") ?? "none", width: 80) { _, t in setp(["arrow": t == "none" ? (NSNull() as Any) : t]) }),
                 ("Head", numberField(e.p("head"), width: 36) { setp(["head": $0]) }), ("mm", NSView())])
            tipRows(e, setp)
            row([("Opacity", numberField((e.p("opacity") ?? 1) * 100, width: 40) { setp(["opacity": max(0, min(1, $0 / 100))]) }), ("%", NSView())])
            row([("Bend", numberField(e.p("curve") ?? 0, width: 44) { setp(["curve": $0 == 0 ? (NSNull() as Any) : $0]) }),
                 ("(0 straight, 0.3 curved; − the other way)", NSView())])
        case "path":
            section((e.props["closed"] as? Bool ?? false) ? "Shape" : "Path")
            row([("", check("Closed", e.props["closed"] as? Bool ?? false) { setp(["closed": $0]) }),
                 ("", check("Smooth", e.props["smooth"] as? Bool ?? false) { setp(["smooth": $0]) })])
            strokeRows(e, setp, fill: true)
            row([("Arrow", popup(["none", "end", "start", "both"], e.s("arrow") ?? "none", width: 80) { _, t in setp(["arrow": t == "none" ? (NSNull() as Any) : t]) })])
            tipRows(e, setp)
            note("Drag a point to move it. Draw new paths with the pen (P).")
        case "zoom":
            section("Zoom box")
            row([("Color", color(e.s("color")) { setp(["color": $0]) }), ("Line", numberField(e.p("width"), width: 36) { setp(["width": $0]) }), ("pt", NSView())])
            row([("", check("Lines to the magnified view", e.props["lines"] as? Bool ?? true) { setp(["lines": $0]) })])
            row([("", check("Crop the magnified picture to the box", e.props["sync"] as? Bool ?? true) { setp(["sync": $0]) })])
            note("Drag the box (or its handles) on the figure: the magnified picture follows.")
        case "scalebar":
            section("Scale bar")
            row([("Length", numberField(e.p("length"), width: 50) { setp(["length": $0]) }), ("", Field(e.s("unit") ?? "µm", width: 40) { setp(["unit": $0]) })])
            // empty: the picture's metadata gives the pixel size
            let px = Field(e.p("pixel_size").map { String(format: "%g", $0) } ?? "", width: 60) { s in
                let t = s.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
                if t.isEmpty { setp(["pixel_size": NSNull()]) } else if let d = Double(t) { setp(["pixel_size": d]) }
            }
            if e.p("pixel_size") == nil, let v = e.pixelSize, e.pixelFrom != "not set" { px.placeholderString = String(format: "%g", v) }
            row([("One pixel is", px), (e.s("unit") ?? "µm", NSView())])
            switch e.pixelFrom {
            case "not set"?: note("No pixel size: the picture's metadata has none. Enter it from the microscope.")
            case let f? where f.hasPrefix("from "): note("Pixel size \(f). Type a number to override it.")
            default: break
            }
            row([("Corner", popup(["lower right", "lower left", "upper right", "upper left"], e.s("corner"), width: 110) { _, t in
                setp(["corner": t, "x": NSNull(), "y": NSNull()]) })])
            row([("Color", color(e.s("color")) { setp(["color": $0]) }), ("", check("Label", e.props["label"] as? Bool ?? true) { setp(["label": $0]) })])
            row([("Thickness", numberField(e.p("thickness"), width: 40) { setp(["thickness": $0]) }), ("mm", NSView())])
        case "rect", "ellipse":
            section(e.kind == "rect" ? "Rectangle" : "Ellipse")
            strokeRows(e, setp, fill: true)
            if e.kind == "rect" { row([("Corner radius", numberField(e.p("radius") ?? 0, width: 40) { setp(["radius": $0]) }), ("mm", NSView())]) }
            row([("Opacity", numberField((e.p("opacity") ?? 1) * 100, width: 40) { setp(["opacity": max(0, min(1, $0 / 100))]) }), ("%", NSView())])
            if e.kind == "rect" { panelLabelRows(f, e) }        // a schematic's background can carry its letter
        case "text" where e.role != nil:
            section(e.role == "title" ? "Table title" : "Table notes")
            if (e.s("text") ?? "").contains("\n") {
                note((e.s("text") ?? "").replacingOccurrences(of: "\n", with: " · "))
                row([("", button("Edit in Code", symbol: "doc.text") { m.showCode(of: e) })])
            } else {
                add(Field(e.s("text") ?? "", width: 220) { setp(["text": $0]) })
            }
            note("Set in the table's code (\(e.role!)=). Its type follows the style; its place follows the table.")
        case "table":
            if f.isTableFigure && !f.isTable {
                section("Page")
                pageSizeRow(f)
                row([("", button("Fit Page to Table", symbol: "arrow.up.left.and.arrow.down.right") { m.edit(["op": "fit_figure", "margin": 3]) })])
            }
            section("Table")
            if let src = e.s("source") { note(src + "  ·  edit the file and the table follows") } else { note("rows written in the figure's code") }
            row([("", check("First row is a header", e.props["header"] as? Bool ?? true) { setp(["header": $0]) })])
            row([("", check("Lines between all cells", e.props["grid"] as? Bool ?? false) { setp(["grid": $0]) })])
            row([("Size", numberField(e.p("size") ?? num(f.style["size"]), width: 40) { setp(["size": $0]) }), ("pt", NSView()),
                 ("", color(e.s("color")) { setp(["color": $0]) })])
            let dec: String = {
                if let d = e.props["decimals"] as? NSNumber { return d.stringValue }
                if let l = e.props["decimals"] as? [Any] { return l.map { ($0 as? NSNumber)?.stringValue ?? "-" }.joined(separator: " ") }
                return ""
            }()
            row([("Decimals", Field(dec, width: 80) { t in
                let parts = t.split(whereSeparator: { $0 == " " || $0 == "," }).map(String.init)
                if parts.isEmpty { setp(["decimals": NSNull()]) }
                else if parts.count == 1, let d = Int(parts[0]) { setp(["decimals": d]) }
                else { setp(["decimals": parts.map { Int($0).map { $0 as Any } ?? NSNull() }]) }
            }), ("(2, or one per column: - 0 2 3)", NSView())])
            row([("Align", Field(e.s("align") ?? "", width: 60) { setp(["align": $0.isEmpty ? (NSNull() as Any) : $0]) }),
                 ("(lcr per column; empty: numbers right)", NSView())])
            row([("Font", fontPicker(e.s("font"), blank: "Style's font") { setp(["font": $0 ?? (NSNull() as Any)]) })])
            tableCells(e)
            note("Double-click a cell on the figure to type in it; Tab goes to the next cell.")
            row([("", button("Export Table…", symbol: "doc.richtext") { m.exportTable(nil) })])
            note("Journals want tables as text: Export Table writes a real Word table in the figure's font, or an Excel sheet, CSV or TSV.")
        case "bracket":
            section("Bracket")
            row([("Text", Field(e.s("text") ?? "", width: 90) { setp(["text": $0]) }), ("", color(e.s("color")) { setp(["color": $0]) })])
            row([("Size", numberField(e.p("size") ?? num(f.style["size"]), width: 40) { setp(["size": $0]) }), ("pt   Line", numberField(e.p("width"), width: 36) { setp(["width": $0]) }), ("pt", NSView())])
        default: break
        }
        section("Arrange")
        row([("", check("Locked", e.locked) { setp(["locked": $0]) }), ("", check("Hidden", e.hidden) { setp(["hidden": $0]) })])
        row([("", iconButton("square.3.layers.3d.top.filled", "Bring to Front") { m.order("front") }),
             ("", iconButton("square.2.layers.3d.top.filled", "Bring Forward") { m.order("forward") }),
             ("", iconButton("square.2.layers.3d.bottom.filled", "Send Backward") { m.order("backward") }),
             ("", iconButton("square.3.layers.3d.bottom.filled", "Send to Back") { m.order("back") })])
        alignRows(single: true)
        if let line = e.line {
            note("Made by \((e.file as NSString?)?.lastPathComponent ?? "the code"), line \(line).")
        }
    }

    /// The colors a panel draws with, one well for each (all the marks of that color change together).
    private func panelColorRows(_ e: Element) {
        guard let m = main, !e.marks.isEmpty else { return }
        var groups: [(String, [String])] = []
        for mk in e.marks {
            if let i = groups.firstIndex(where: { $0.0 == mk.color }) { groups[i].1.append(mk.key) } else { groups.append((mk.color, [mk.key])) }
        }
        section("Colors in this panel")
        for (c, keys) in groups.prefix(12) {
            let what = keys.count == 1 ? keys[0].replacingOccurrences(of: "ax0.", with: "") : "\(keys.count) marks"
            row([("", color(c) { new in m.edit(["op": "panel_color", "oid": e.oid, "keys": keys, "color": new, "old": c]) }),
                 (what, NSView())])
        }
        if let o = e.props["colors"] as? [String: Any], !o.isEmpty {
            row([("", button("Undo \(o.count) Recolored Mark\(o.count == 1 ? "" : "s")") { m.edit(["op": "set", "oid": e.oid, "props": ["colors": NSNull()]]) })])
        }
        note("Recoloring changes the color in the panel's code when it is written there once; otherwise it is kept on the panel's line. Double-click a line, bar or point on the figure to recolor just it.")
    }

    private func tipRows(_ e: Element, _ setp: @escaping ([String: Any]) -> Void) {
        row([("Tip", popup(["triangle", "stealth", "open", "bar", "dot"], e.s("tip") ?? "triangle", width: 90) { _, t in setp(["tip": t]) })])
    }

    private func strokeRows(_ e: Element, _ setp: @escaping ([String: Any]) -> Void, fill: Bool) {
        if fill {
            let hasStroke = !(e.props["stroke"] is NSNull), hasFill = !(e.props["fill"] is NSNull) && e.props["fill"] != nil
            row([("", check("Stroke", hasStroke) { setp(["stroke": $0 ? "#000000" : (NSNull() as Any)]) }), ("", color(e.s("stroke") ?? "#000000") { setp(["stroke": $0]) }),
                 ("", check("Fill", hasFill) { setp(["fill": $0 ? "#DDDDDD" : (NSNull() as Any)]) }), ("", color(e.s("fill") ?? "#DDDDDD") { setp(["fill": $0]) })])
        } else {
            row([("Color", color(e.s("color")) { setp(["color": $0]) })])
        }
        row([("Width", numberField(e.p("width"), width: 40) { setp(["width": $0]) }), ("pt", NSView()),
             ("Dash", popup(["solid", "dashed", "dotted"], e.s("dash") ?? "solid", width: 80) { _, t in setp(["dash": t == "solid" ? (NSNull() as Any) : t]) })])
    }

    private func panelLabelRows(_ f: FigureData, _ e: Element) {
        guard let m = main else { return }
        section("Label")
        let lab = e.props["label"]
        let mode = lab == nil || lab is NSNull ? "None" : ((lab as? String) == "auto" ? "Automatic" : "Own")
        row([("", popup(["Automatic", "Own", "None"], mode, width: 100) { _, t in
                let v: Any = t == "None" ? (NSNull() as Any) : (t == "Automatic" ? "auto" : (f.labelText(e.oid) ?? "a"))
                m.edit(["op": "set", "oid": e.oid, "props": ["label": v]]) }),
             ("", Field((lab as? String).map { $0 == "auto" ? (f.labelText(e.oid) ?? "") : $0 } ?? "", width: 50) { t in
                m.edit(["op": "set", "oid": e.oid, "props": ["label": t.isEmpty ? "auto" : t]]) })])
        row([("Nudge X", numberField(e.p("label_dx") ?? 0, width: 36) { m.edit(["op": "set", "oid": e.oid, "props": ["label_dx": $0 == 0 ? (NSNull() as Any) : $0]]) }),
             ("Y", numberField(e.p("label_dy") ?? 0, width: 36) { m.edit(["op": "set", "oid": e.oid, "props": ["label_dy": $0 == 0 ? (NSNull() as Any) : $0]]) }), ("mm", NSView())])
        if let l = f.label(of: e.oid) { linkRow(l) }
    }

    /// Whether the label moves with its panel; unlinked, each moves on its own (double-clicking the label toggles it).
    private func linkRow(_ label: Element) {
        guard let m = main else { return }
        add(check("Label moves with the panel", !label.detached) { on in m.edit(["op": "label_detach", "oid": label.oid, "detach": !on]) })
    }

    /// The table's cells, to type into: Return (or Apply) writes them to its file or code.
    private func tableCells(_ e: Element) {
        guard let m = main else { return }
        let rows = m.canvas.tableDraftRows(e)
        let grid = TableGrid(rows.isEmpty ? [[""]] : rows)
        grid.onApply = { m.applyTableCells(e.oid, $0) }
        add(grid)
        func small(_ t: String, _ tip: String, _ f: @escaping () -> Void) -> NSButton { let b = button(t, f); b.toolTip = tip; return b }
        row([("", small("+ Row", "Add a row at the bottom") { grid.reshape { $0.append(Array(repeating: "", count: $0.first?.count ?? 1)) } }),
             ("", small("− Row", "Remove the bottom row") { grid.reshape { if $0.count > 1 { $0.removeLast() } } }),
             ("", small("+ Col", "Add a column on the right") { grid.reshape { for i in $0.indices { $0[i].append("") } } }),
             ("", small("− Col", "Remove the right column") { grid.reshape { for i in $0.indices where $0[i].count > 1 { $0[i].removeLast() } } })])
        row([("", button("Apply Cells", symbol: "checkmark") { grid.apply() }), ("⇥ next cell · ↵ apply", NSView())])
    }

    private func labelOfPanel(_ f: FigureData, _ e: Element) {
        guard let m = main, let po = e.panelOid, let panel = f.element(po) else { return }
        note(e.autoLabel ? "An automatic label, from the panels' reading order." : "This panel's own label.")
        row([("Text", Field(e.text ?? "", width: 60) { t in m.edit(["op": "set", "oid": e.oid, "props": ["text": t]]) }),
             ("", button("Automatic") { m.edit(["op": "set", "oid": po, "props": ["label": "auto"]]) })])
        row([("Nudge X", numberField(panel.p("label_dx") ?? 0, width: 36) { m.edit(["op": "set", "oid": po, "props": ["label_dx": $0 == 0 ? (NSNull() as Any) : $0]]) }),
             ("Y", numberField(panel.p("label_dy") ?? 0, width: 36) { m.edit(["op": "set", "oid": po, "props": ["label_dy": $0 == 0 ? (NSNull() as Any) : $0]]) }), ("mm", NSView())])
        let ls = f.labelStyle
        row([("Font", fontPicker(ls["font"] as? String, blank: "Style's font") { m.edit(["op": "labels", "props": ["font": $0 ?? (NSNull() as Any)]]) })])
        row([("Size", numberField(num(ls["size"]), width: 40) { m.edit(["op": "labels", "props": ["size": $0]]) }), ("pt", NSView()),
             ("", check("Bold", (ls["weight"] as? String) == "bold") { m.edit(["op": "labels", "props": ["weight": $0 ? "bold" : "normal"]]) }),
             ("", check("Italic", (ls["style"] as? String) == "italic") { m.edit(["op": "labels", "props": ["style": $0 ? "italic" : "normal"]]) })])
        row([("Color", color(ls["color"] as? String ?? "#000000") { m.edit(["op": "labels", "props": ["color": $0]]) })])
        note("Font, size and color apply to every panel letter, so they match.")
        linkRow(e)
        note(e.detached ? "Unlinked: this label stays where it is when its panel moves, and dragging it moves only it. Double-click it to link it again."
             : "Dragging a label moves every label, so they stay consistent; ⌥-drag nudges this one. Double-click a label to unlink it from its panel.")
        add(button("Remove This Label") { m.edit(["op": "delete", "oids": [e.oid]]) })
    }

    private func alignRows(single: Bool) {
        guard let m = main else { return }
        section(single ? "Align to figure" : "Align")
        if !single {
            row([("To", popup(["Selection", "First selected", "Figure"], ["selection": "Selection", "key": "First selected", "figure": "Figure"][m.alignTo], width: 130) { i, _ in
                m.alignTo = ["selection", "key", "figure"][i]; m.updateInspector() })])
        }
        let h: [(String, String, String)] = [("align.horizontal.left", "Align Left Edges", "left"), ("align.horizontal.center", "Align Centers", "hcenter"),
                                              ("align.horizontal.right", "Align Right Edges", "right"), ("align.vertical.top", "Align Tops", "top"),
                                              ("align.vertical.center", "Align Middles", "vcenter"), ("align.vertical.bottom", "Align Bottoms", "bottom")]
        row(h.map { s in ("", iconButton(s.0, s.1) { m.align(s.2, to: single ? "figure" : nil) }) })
    }

    private func childSection(_ f: FigureData, _ e: Element) {
        guard let m = main, e.kind != "legend" else { return }
        let setp: ([String: Any]) -> Void = { m.edit(["op": "set", "oid": e.oid, "props": $0]) }
        defer { if e.kind == "panel" { panelColorRows(e) } }
        section(e.kind == "panel" ? "Code" : "Picture")
        note(e.s("source") ?? "")
        if e.kind == "panel" {
            row([("Function", Field(e.s("function") ?? "draw", width: 90) { setp(["function": $0]) }),
                 ("", button("Edit Code", symbol: "chevron.left.forwardslash.chevron.right") { m.showCode(of: e) })])
        } else {
            row([("Fit", popup(["contain", "cover", "stretch"], e.s("fit"), width: 90) { _, t in setp(["fit": t]) })])
        }
        panelLabelRows(f, e)
    }

    private func layoutSection(_ f: FigureData, _ l: Element) {
        guard let m = main else { return }
        let setp: ([String: Any]) -> Void = { m.edit(["op": "set", "oid": l.oid, "props": $0]) }
        section("Layout")
        row([("Gap", numberField(l.p("gap") ?? 4, width: 40) { setp(["gap": $0]) }), ("mm between panels", NSView())])
        func weights(_ k: String) -> String { (l.props[k] as? [Any])?.map { mm(num($0)) }.joined(separator: ", ") ?? "" }
        func parse(_ t: String) -> Any { let v = t.split(separator: ",").compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }; return v.isEmpty ? (NSNull() as Any) : v }
        row([("Widths", Field(weights("widths"), width: 110) { setp(["widths": parse($0)]) })])
        row([("Heights", Field(weights("heights"), width: 110) { setp(["heights": parse($0)]) })])
        add(check("Collect the panels' legends into one", l.s("guides") == "collect") { setp(["guides": $0 ? "collect" : (NSNull() as Any)]) })
        row([("Legend", popup(["right", "bottom"], l.s("legend") ?? "right", width: 90) { _, t in setp(["legend": t]) })])
        add(check("Line up the panels' plot areas", l.props["align"] as? Bool ?? true) { setp(["align": $0]) })
        note("Widths and heights are relative (2, 1 makes the first twice as wide). Drag the gaps between panels on the figure to set them by eye.")
        row([("", button("Release Layout", symbol: "rectangle.split.2x2") { m.releaseLayout(l.oid) })])
        note("Release turns the panels back into ones placed by hand, where they are now.")
    }

    private func insetSection(_ f: FigureData, _ e: Element) {
        guard let m = main else { return }
        let setp: ([String: Any]) -> Void = { m.edit(["op": "set", "oid": e.oid, "props": $0]) }
        func pct(_ k: String) -> Field { numberField((e.p(k) ?? 0) * 100, width: 40) { setp([k: $0 / 100]) } }
        let host = e.hostOid.flatMap { f.element($0) }
        section("Inset")
        note("On top of \(host.map { $0.title(label: f.labelText($0.oid)) } ?? "a panel"), in % of its box; it moves with it.")
        row([("X", pct("x")), ("Y", pct("y")), ("%", NSView())])
        row([("W", pct("w")), ("H", pct("h")), ("%", NSView())])
        note(e.item["source"] as? String ?? "")
        if e.inner == "panel" {
            row([("Function", Field(e.item["function"] as? String ?? "draw", width: 90) { setp(["function": $0]) }),
                 ("", button("Edit Code", symbol: "chevron.left.forwardslash.chevron.right") { m.showCode(of: e) })])
        }
        row([("Label", Field(e.s("label") ?? "", width: 50) { setp(["label": $0.isEmpty ? (NSNull() as Any) : $0]) }), ("none if empty", NSView())])
        if e.inner == "panel" { panelColorRows(e) }
    }

    private func multiSection(_ f: FigureData, _ sel: [Element]) {
        guard let m = main else { return }
        section("\(sel.count) selected")
        let free = sel.filter { ($0.kind == "panel" || $0.kind == "image") && $0.parentOid == nil }
        if free.count >= 2 {
            row([("", button("Make Layout", symbol: "rectangle.split.2x2") { m.makeLayout(nil) })])
            note("Puts these panels in a patchwork layout: one box, rows and columns by reading order, plot areas lined up, gaps you can drag.")
        }
        alignRows(single: false)
        section("Distribute and size")
        row([("", iconButton("distribute.horizontal.center", "Distribute Horizontally (equal gaps)") { m.distribute("h") }),
             ("", iconButton("distribute.vertical.center", "Distribute Vertically (equal gaps)") { m.distribute("v") }),
             ("", iconButton("arrow.left.and.right", "Match Width of the first selected") { m.match("width") }),
             ("", iconButton("arrow.up.and.down", "Match Height of the first selected") { m.match("height") })])
        row([("Gap", Field("", width: 40) { s in if let g = Double(s) { m.distribute("h", gap: g) } }), ("mm across", NSView()),
             ("", Field("", width: 40) { s in if let g = Double(s) { m.distribute("v", gap: g) } }), ("mm down", NSView())])
        note("Distribute evens out the gaps between the elements; with a gap, they are spaced that far apart.")
        section("Arrange")
        row([("", button("Group", symbol: "rectangle.3.group") { m.group(nil) }), ("", button("Ungroup") { m.ungroup(nil) })])
    }
}

/// A small round color swatch button (the project's palette).
final class SwatchButton: NSButton {
    private let swatch: NSColor
    init(color: NSColor) {
        swatch = color
        super.init(frame: NSRect(x: 0, y: 0, width: 16, height: 16))
        isBordered = false; title = ""
        widthAnchor.constraint(equalToConstant: 16).isActive = true
        heightAnchor.constraint(equalToConstant: 16).isActive = true
    }
    required init?(coder: NSCoder) { fatalError() }
    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 1.5, dy: 1.5)
        swatch.setFill(); NSBezierPath(ovalIn: r).fill()
        NSColor.separatorColor.setStroke(); let p = NSBezierPath(ovalIn: r); p.lineWidth = 1; p.stroke()
    }
}


/// A font family to pick from the Mac's fonts, or to type; an empty field means `blank` (the style's font).
final class FontPicker: NSComboBox, NSComboBoxDelegate {
    private var commit: ((String?) -> Void)?
    private var initial = ""
    private var blank: String?
    // with Office's cloud fonts by their own names: macOS lists Aptos Display as part of Aptos, matplotlib knows it apart
    static let families: [String] = Array(Set(NSFontManager.shared.availableFontFamilies.filter { !$0.hasPrefix(".") }
                                              + OfficeFonts.cloudFamilies())).sorted()

    convenience init(_ value: String?, blank: String?, width: CGFloat, commit: @escaping (String?) -> Void) {
        self.init(frame: .zero)
        self.commit = commit; self.blank = blank
        initial = value ?? ""
        stringValue = initial
        placeholderString = blank
        if let b = blank { addItem(withObjectValue: b) }
        addItems(withObjectValues: FontPicker.families)
        numberOfVisibleItems = 16; completes = true
        font = Theme.font(11); controlSize = .small
        delegate = self
        widthAnchor.constraint(equalToConstant: width).isActive = true
    }
    private func fire() {
        var v = stringValue.trimmingCharacters(in: .whitespaces)
        if v == blank { v = ""; stringValue = "" }
        guard v != initial else { return }
        initial = v
        commit?(v.isEmpty ? nil : v)
    }
    func comboBoxSelectionDidChange(_ n: Notification) {
        guard indexOfSelectedItem >= 0, let v = itemObjectValue(at: indexOfSelectedItem) as? String else { return }
        stringValue = v
        fire()
    }
    func controlTextDidEndEditing(_ obj: Notification) { fire() }
}

/// A table's cells as text fields, for typing into (from Manimble's table editor).
final class TableGrid: NSScrollView {
    var onApply: (([[String]]) -> Void)?
    private var cells: [[NSTextField]] = []
    private var heightConstraint: NSLayoutConstraint?
    private let avail: CGFloat = 212

    init(_ rows: [[String]]) {
        super.init(frame: .zero)
        hasHorizontalScroller = true; hasVerticalScroller = true; autohidesScrollers = true
        borderType = .noBorder; drawsBackground = false
        widthAnchor.constraint(equalToConstant: avail).isActive = true
        layout(rows)
    }
    required init?(coder: NSCoder) { fatalError() }

    var draft: [[String]] { cells.map { $0.map(\.stringValue) } }

    func layout(_ rows: [[String]]) {
        let rows = rows.isEmpty ? [[""]] : rows
        let nc = max(1, rows.map(\.count).max() ?? 1)
        let gap: CGFloat = 3, h: CGFloat = 22
        let w = max(54, ((avail - gap * CGFloat(nc - 1)) / CGFloat(nc)).rounded(.down))
        let doc = FlippedView(frame: NSRect(x: 0, y: 0, width: CGFloat(nc) * w + CGFloat(nc - 1) * gap,
                                            height: CGFloat(rows.count) * h + CGFloat(rows.count - 1) * gap))
        cells = rows.enumerated().map { i, row in
            (0..<nc).map { j in
                let f = NSTextField(string: j < row.count ? row[j] : "")
                f.frame = NSRect(x: CGFloat(j) * (w + gap), y: CGFloat(i) * (h + gap), width: w, height: h - 2)
                f.font = i == 0 ? Theme.font(11, .semibold) : Theme.font(11)
                f.cell?.isScrollable = true; f.cell?.wraps = false; f.controlSize = .small
                f.target = self; f.action = #selector(applyPressed); f.cell?.sendsActionOnEndEditing = false
                doc.addSubview(f)
                return f
            }
        }
        let flat = cells.flatMap { $0 }
        for (k, f) in flat.enumerated() { f.nextKeyView = flat[(k + 1) % flat.count] }
        documentView = doc
        heightConstraint?.isActive = false
        let bar: CGFloat = doc.frame.width > avail ? 14 : 0
        heightConstraint = heightAnchor.constraint(equalToConstant: min(doc.frame.height, 240) + bar)
        heightConstraint?.isActive = true
    }

    func reshape(_ change: (inout [[String]]) -> Void) {
        var rows = draft
        change(&rows)
        guard !rows.isEmpty, !(rows.first?.isEmpty ?? true) else { NSSound.beep(); return }
        layout(rows)
    }
    @objc private func applyPressed() { apply() }
    func apply() { onApply?(draft) }
}
