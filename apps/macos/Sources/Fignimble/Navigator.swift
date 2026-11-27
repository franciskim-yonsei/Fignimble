import AppKit

/// The left sidebar: the project's figures and, under their own heading, its tables (with thumbnails), and the
/// layers of the figure on screen, topmost first.
final class Navigator: NSView, NSTableViewDataSource, NSTableViewDelegate, NSMenuDelegate {
    weak var main: MainController?
    private let figuresTable = NSTableView()
    private let layersTable = NSTableView()
    /// The figures, then a "Tables" heading and the tables: a heading row is ["heading": title].
    private var figures: [[String: Any]] = []
    private var layers: [Element] = []
    private var updating = false
    private var thumbs: [String: (Date, NSImage)] = [:]

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        Theme.layerColors(self) { $0.layer?.backgroundColor = Theme.sidebarBackground.cgColor }
        let split = GrabSplitView(); split.isVertical = false; split.dividerStyle = .thin
        split.addArrangedSubview(box("Figures", figuresTable, plus: #selector(newFigure), tip: "New figure (⌘N)"))
        split.addArrangedSubview(box("Layers", layersTable, plus: nil, tip: nil))
        for (t, id) in [(figuresTable, "fig"), (layersTable, "layer")] {
            let c = NSTableColumn(identifier: .init(id)); c.resizingMask = .autoresizingMask
            t.addTableColumn(c); t.headerView = nil; t.dataSource = self; t.delegate = self
            t.backgroundColor = .clear; t.style = .sourceList; t.intercellSpacing = NSSize(width: 0, height: 2)
            t.allowsMultipleSelection = t === layersTable
        }
        figuresTable.rowHeight = 92
        figuresTable.menu = NSMenu(); figuresTable.menu?.delegate = self     // right-click: cut, copy, paste, delete, new
        layersTable.rowHeight = 22
        layersTable.target = self; layersTable.doubleAction = #selector(layerDoubleClicked)
        split.translatesAutoresizingMaskIntoConstraints = false
        addSubview(split)
        NSLayoutConstraint.activate([
            split.leadingAnchor.constraint(equalTo: leadingAnchor), split.trailingAnchor.constraint(equalTo: trailingAnchor),
            split.topAnchor.constraint(equalTo: topAnchor), split.bottomAnchor.constraint(equalTo: bottomAnchor),
            widthAnchor.constraint(greaterThanOrEqualToConstant: 160),
        ])
        DispatchQueue.main.async { split.setPosition(max(200, split.bounds.height * 0.45), ofDividerAt: 0) }
    }
    required init?(coder: NSCoder) { fatalError() }

    private func box(_ title: String, _ table: NSTableView, plus: Selector?, tip: String?) -> NSView {
        let v = NSView()
        let head = NSStackView(); head.spacing = 4
        head.addArrangedSubview(sectionLabel(title))
        let spacer = NSView(); spacer.setContentHuggingPriority(.init(1), for: .horizontal); head.addArrangedSubview(spacer)
        if let p = plus { head.addArrangedSubview(barButton("plus", tip ?? "", self, p)) }
        let scroll = NSScrollView(); scroll.documentView = table; scroll.drawsBackground = false; scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        for x in [head, scroll] as [NSView] { x.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(x) }
        NSLayoutConstraint.activate([
            head.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: 12), head.trailingAnchor.constraint(equalTo: v.trailingAnchor, constant: -10),
            head.topAnchor.constraint(equalTo: v.topAnchor, constant: 8), head.heightAnchor.constraint(equalToConstant: 18),
            scroll.leadingAnchor.constraint(equalTo: v.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: v.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: head.bottomAnchor, constant: 4), scroll.bottomAnchor.constraint(equalTo: v.bottomAnchor),
            v.heightAnchor.constraint(greaterThanOrEqualToConstant: 80),
        ])
        return v
    }

    @objc private func newFigure() { main?.newFigure(nil) }
    @objc private func layerDoubleClicked() {
        let r = layersTable.clickedRow
        guard r >= 0, r < layers.count else { return }
        main?.showCode(of: layers[r])
    }

    /// The right-click menu of the figures list: for the figure or table clicked, or the list's empty space.
    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let r = figuresTable.clickedRow
        let name = r >= 0 && r < figures.count ? figures[r]["name"] as? String : nil
        guard let built = main?.figureListMenu(name) else { return }
        menu.autoenablesItems = false
        for i in built.items { built.removeItem(i); menu.addItem(i) }
    }

    func setFigures(_ all: [[String: Any]], current: String?) {
        let isTable = { (x: [String: Any]) in x["kind"] as? String == "table" }
        let tables = all.filter(isTable)
        var f = all.filter { !isTable($0) }
        if !tables.isEmpty { f += [["heading": "Tables"]] + tables }
        figures = f
        updating = true
        figuresTable.reloadData()
        if let c = current, let i = f.firstIndex(where: { $0["name"] as? String == c }) {
            figuresTable.selectRowIndexes([i], byExtendingSelection: false)
            figuresTable.scrollRowToVisible(i)
        }
        updating = false
    }

    func setLayers(_ fig: FigureData?, selection: [String]) {
        layers = (fig?.elements ?? []).reversed()
        updating = true
        layersTable.reloadData()
        selectLayers(selection)
        updating = false
    }

    func selectLayers(_ sel: [String]) {
        updating = true
        let idx = IndexSet(layers.enumerated().filter { sel.contains($0.element.oid) }.map(\.offset))
        layersTable.selectRowIndexes(idx, byExtendingSelection: false)
        if let first = idx.first { layersTable.scrollRowToVisible(first) }
        updating = false
    }

    func numberOfRows(in t: NSTableView) -> Int { t === figuresTable ? figures.count : layers.count }

    func tableView(_ t: NSTableView, viewFor col: NSTableColumn?, row: Int) -> NSView? {
        if t === figuresTable {
            let f = figures[row]
            if let h = f["heading"] as? String {
                let v = NSTableCellView()
                let l = sectionLabel(h)
                l.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(l)
                NSLayoutConstraint.activate([l.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: 12),
                                             l.bottomAnchor.constraint(equalTo: v.bottomAnchor, constant: -2)])
                return v
            }
            let v = NSTableCellView()
            let img = NSImageView()
            img.imageScaling = .scaleProportionallyUpOrDown
            img.wantsLayer = true; img.layer?.borderColor = NSColor.separatorColor.cgColor; img.layer?.borderWidth = 0.5
            if let path = f["thumb"] as? String { img.image = thumb(path) }
            let name = NSTextField(labelWithString: f["name"] as? String ?? "")
            name.font = Theme.font(11, .medium)
            var badge = ""
            if f["error"] as? String != nil { badge = "⚠︎ doesn't run" } else if let w = f["warnings"] as? Int, w > 0 { badge = "\(w) warning\(w == 1 ? "" : "s")" }
            let b = NSTextField(labelWithString: badge); b.font = Theme.font(9.5); b.textColor = f["error"] as? String != nil ? .systemRed : .systemOrange
            for x in [img, name, b] { x.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(x) }
            NSLayoutConstraint.activate([
                img.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: 10), img.trailingAnchor.constraint(equalTo: v.trailingAnchor, constant: -10),
                img.topAnchor.constraint(equalTo: v.topAnchor, constant: 4), img.heightAnchor.constraint(equalToConstant: 66),
                name.leadingAnchor.constraint(equalTo: img.leadingAnchor), name.topAnchor.constraint(equalTo: img.bottomAnchor, constant: 2),
                b.leadingAnchor.constraint(equalTo: name.trailingAnchor, constant: 6), b.centerYAnchor.constraint(equalTo: name.centerYAnchor),
            ])
            return v
        }
        let e = layers[row]
        let v = NSTableCellView()
        let icon = NSImageView(image: NSImage(systemSymbolName: e.symbol, accessibilityDescription: nil) ?? NSImage())
        icon.contentTintColor = e.error != nil ? .systemRed : Theme.ink(0.55)
        let label = main?.figure?.labelText(e.oid)
        let t = NSTextField(labelWithString: e.title(label: label) + (e.group.map { "  ·  \($0)" } ?? ""))
        t.font = Theme.font(11); t.lineBreakMode = .byTruncatingTail
        t.textColor = e.hidden ? .tertiaryLabelColor : .labelColor
        let eye = LayerToggle(symbol: e.hidden ? "eye.slash" : "eye", on: e.hidden, tip: e.hidden ? "Show" : "Hide") { [weak self] in
            self?.main?.setLayer(e.oid, hidden: !e.hidden) }
        let lock = LayerToggle(symbol: e.locked ? "lock.fill" : "lock.open", on: e.locked, tip: e.locked ? "Unlock" : "Lock (the canvas leaves it alone)") { [weak self] in
            self?.main?.setLayer(e.oid, locked: !e.locked) }
        let toggles = e.parentOid == nil
        // as the cell's own text and image, so a selected row shows them light on the highlight like any list's
        v.textField = t; v.imageView = icon
        for x in [icon, t, eye, lock] { x.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(x) }
        eye.isHidden = !toggles; lock.isHidden = !toggles
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: e.parentOid == nil ? 8 : 22), icon.centerYAnchor.constraint(equalTo: v.centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 16),
            t.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 6), t.trailingAnchor.constraint(lessThanOrEqualTo: eye.leadingAnchor, constant: -4),
            t.centerYAnchor.constraint(equalTo: v.centerYAnchor),
            lock.trailingAnchor.constraint(equalTo: v.trailingAnchor, constant: -6), lock.centerYAnchor.constraint(equalTo: v.centerYAnchor),
            eye.trailingAnchor.constraint(equalTo: lock.leadingAnchor, constant: -4), eye.centerYAnchor.constraint(equalTo: v.centerYAnchor),
        ])
        return v
    }

    func tableView(_ t: NSTableView, heightOfRow row: Int) -> CGFloat {
        t === figuresTable ? (figures[row]["heading"] != nil ? 26 : 92) : 22
    }
    func tableView(_ t: NSTableView, shouldSelectRow row: Int) -> Bool { t !== figuresTable || figures[row]["heading"] == nil }

    /// Thumbnails are re-read when their file changes.
    private func thumb(_ path: String) -> NSImage? {
        let m = (try? FileManager.default.attributesOfItem(atPath: path)[.modificationDate] as? Date) ?? Date()
        if let (d, i) = thumbs[path], d == m { return i }
        guard let i = NSImage(contentsOfFile: path) else { return nil }
        thumbs[path] = (m, i)
        return i
    }

    func tableViewSelectionDidChange(_ n: Notification) {
        guard !updating, let t = n.object as? NSTableView else { return }
        if t === figuresTable {
            let r = t.selectedRow
            if r >= 0, r < figures.count, let name = figures[r]["name"] as? String { main?.show(figure: name) }
        } else {
            main?.select(t.selectedRowIndexes.map { layers[$0].oid })
        }
    }
}


/// The eye and lock of a layer: faint until hovered or on.
final class LayerToggle: NSButton {
    private let run: () -> Void
    init(symbol: String, on: Bool, tip: String, _ run: @escaping () -> Void) {
        self.run = run
        super.init(frame: .zero)
        image = NSImage(systemSymbolName: symbol, accessibilityDescription: tip)
        isBordered = false; toolTip = tip; imagePosition = .imageOnly
        symbolConfiguration = .init(pointSize: 10, weight: .regular)
        contentTintColor = on ? Theme.accent : Theme.ink(0.3)
        target = self; action = #selector(fire)
    }
    required init?(coder: NSCoder) { fatalError() }
    @objc private func fire() { run() }
}
