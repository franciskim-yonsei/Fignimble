import AppKit

/// The drawer below the canvas (after Manimble's): the caption, the code, the preflight problems and the terminals
/// share it as tabs ("Caption | Code | Preflight | History | Terminal 1 … | +"), so there is one divider between it
/// and the figure.
final class BottomDrawer: NSView {
    enum Page { case caption, code, problems, history, terminal }
    private(set) var page: Page = .code
    let caption: CaptionPane
    let code: CodePane
    let problems: ProblemsView
    let history: HistoryView
    let terminal: TerminalPanel
    private let strip = NSStackView()
    private let tools = NSStackView()
    var onShowTerminal: (() -> Void)?
    var onNewTerminal: (() -> Void)?
    var onHide: (() -> Void)?

    init(caption: CaptionPane, code: CodePane, problems: ProblemsView, history: HistoryView, terminal: TerminalPanel) {
        self.caption = caption; self.code = code; self.problems = problems; self.history = history; self.terminal = terminal
        super.init(frame: .zero)
        wantsLayer = true
        Theme.layerColors(self) { $0.layer?.backgroundColor = Theme.canvasBottom.cgColor }
        let bar = NSView(); bar.wantsLayer = true
        Theme.layerColors(bar) { $0.layer?.backgroundColor = Theme.barBackground.cgColor }
        strip.spacing = 0; strip.alignment = .centerY
        let plus = barButton("plus", "Add a terminal (⇧⌥⌘T)", self, #selector(plusPressed))
        let spacer = NSView(); spacer.setContentHuggingPriority(.init(1), for: .horizontal)
        tools.spacing = 10; tools.alignment = .centerY
        let row = NSStackView(views: [strip, plus, spacer, tools, barButton("chevron.down", "Hide (⌥⌘T)", self, #selector(hidePressed))])
        row.spacing = 10; row.alignment = .centerY
        row.translatesAutoresizingMaskIntoConstraints = false
        bar.addSubview(row)
        let body = NSView()
        for v in [caption, code, problems, history, terminal] as [NSView] { v.translatesAutoresizingMaskIntoConstraints = false; body.addSubview(v) }
        for v in [bar, body] { v.translatesAutoresizingMaskIntoConstraints = false; addSubview(v) }
        NSLayoutConstraint.activate([
            bar.leadingAnchor.constraint(equalTo: leadingAnchor), bar.trailingAnchor.constraint(equalTo: trailingAnchor),
            bar.topAnchor.constraint(equalTo: topAnchor), bar.heightAnchor.constraint(equalToConstant: 28),
            row.leadingAnchor.constraint(equalTo: bar.leadingAnchor, constant: 4), row.trailingAnchor.constraint(equalTo: bar.trailingAnchor, constant: -12),
            row.topAnchor.constraint(equalTo: bar.topAnchor), row.bottomAnchor.constraint(equalTo: bar.bottomAnchor),
            body.leadingAnchor.constraint(equalTo: leadingAnchor), body.trailingAnchor.constraint(equalTo: trailingAnchor),
            body.topAnchor.constraint(equalTo: bar.bottomAnchor), body.bottomAnchor.constraint(equalTo: bottomAnchor),
            heightAnchor.constraint(greaterThanOrEqualToConstant: 90),
        ])
        for v in [caption, code, problems, history, terminal] as [NSView] {
            NSLayoutConstraint.activate([
                v.leadingAnchor.constraint(equalTo: body.leadingAnchor), v.trailingAnchor.constraint(equalTo: body.trailingAnchor),
                v.topAnchor.constraint(equalTo: body.topAnchor), v.bottomAnchor.constraint(equalTo: body.bottomAnchor),
            ])
        }
        terminal.onChange = { [weak self] in self?.rebuildStrip() }
        show(.code)
    }
    required init?(coder: NSCoder) { fatalError() }

    func show(_ p: Page) {
        page = p
        caption.isHidden = p != .caption
        code.isHidden = p != .code
        problems.isHidden = p != .problems
        history.isHidden = p != .history
        terminal.isHidden = p != .terminal
        tools.arrangedSubviews.forEach { $0.removeFromSuperview() }
        if p == .code { tools.addArrangedSubview(code.actions) }
        if p == .terminal { tools.addArrangedSubview(terminal.actions) }
        rebuildStrip()
    }

    func rebuildStrip() {
        strip.arrangedSubviews.forEach { $0.removeFromSuperview() }
        strip.addArrangedSubview(DrawerTab(title: "Caption", tip: "The figure's caption: free text kept with the figure, not drawn on it (⌥⌘J)",
                                           selected: page == .caption, onSelect: { [weak self] in self?.show(.caption) }, onClose: nil))
        strip.addArrangedSubview(DrawerTab(title: "Code", tip: "The figure's Python and its panels' (⌥⌘E)", selected: page == .code,
                                           onSelect: { [weak self] in self?.show(.code) }, onClose: nil))
        let n = problems.count
        strip.addArrangedSubview(DrawerTab(title: n > 0 ? "Preflight (\(n))" : "Preflight", tip: "What a journal would complain about",
                                           selected: page == .problems, onSelect: { [weak self] in self?.show(.problems) }, onClose: nil))
        strip.addArrangedSubview(DrawerTab(title: "History", tip: "Every edit made in Fignimble: click one to go back to it (⌥⌘H)",
                                           selected: page == .history, onSelect: { [weak self] in self?.show(.history) }, onClose: nil))
        let active = page == .terminal ? terminal.activeTabIndex : nil
        let titles = terminal.tabTitles
        if titles.isEmpty {
            strip.addArrangedSubview(DrawerTab(title: "Terminal 1", tip: "A shell in the project folder, for a coding agent (⌥⌘T)", selected: false,
                                               onSelect: { [weak self] in self?.onShowTerminal?() }, onClose: nil))
        }
        for (i, t) in titles.enumerated() {
            strip.addArrangedSubview(DrawerTab(title: t.title, tip: t.tip, selected: i == active,
                                               onSelect: { [weak self] in self?.terminal.selectTab(i); self?.show(.terminal) },
                                               onClose: { [weak self] in self?.terminal.closeTab(i) }))
        }
    }

    @objc private func plusPressed() { onNewTerminal?() }
    @objc private func hidePressed() { onHide?() }
}

/// The figure's caption, typed freely; it is written into the figure's code (fig.caption(...)) a moment after
/// typing stops, and when the pane loses the keyboard.
final class CaptionPane: NSView, NSTextViewDelegate {
    weak var main: MainController?
    let text = NSTextView()
    private let hint = NSTextField(labelWithString: CaptionPane.figureHint)
    private var shown = ""
    private var figureName: String?
    private var work: DispatchWorkItem?

    override init(frame: NSRect) {
        super.init(frame: frame)
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.borderType = .noBorder
        text.isRichText = false; text.allowsUndo = true; text.font = Theme.font(13)
        text.textContainerInset = NSSize(width: 10, height: 8)
        text.isAutomaticQuoteSubstitutionEnabled = false; text.isAutomaticDashSubstitutionEnabled = false
        text.isContinuousSpellCheckingEnabled = true
        text.isVerticallyResizable = true; text.autoresizingMask = [.width]
        text.textContainer?.widthTracksTextView = true
        text.delegate = self
        text.backgroundColor = Theme.codeBackground
        scroll.documentView = text
        hint.font = Theme.font(10.5); hint.textColor = .tertiaryLabelColor; hint.lineBreakMode = .byTruncatingTail
        for v in [scroll, hint] as [NSView] { v.translatesAutoresizingMaskIntoConstraints = false; addSubview(v) }
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor), scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.topAnchor.constraint(equalTo: topAnchor), scroll.bottomAnchor.constraint(equalTo: hint.topAnchor, constant: -3),
            hint.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10), hint.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -10),
            hint.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -4),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }

    /// The figure on screen was rendered: show its caption, unless what is typed hasn't been written yet.
    static let figureHint = "Type the figure's caption. It is kept in the figure's code as fig.caption(...), goes with the figure into PowerPoint as its alt text, and is never drawn on the figure."
    static let tableHint = "A table's caption is its title and notes, drawn with it and exported with it: set them in the inspector (with nothing selected) or in its code (title=, notes=)."

    func set(_ caption: String, figure: String?, table: Bool = false) {
        text.isEditable = !table
        hint.stringValue = table ? CaptionPane.tableHint : CaptionPane.figureHint
        if table { commit(); figureName = figure; shown = ""; text.string = ""; return }
        if figure != figureName { commit(); figureName = figure; shown = caption; text.string = caption; return }
        if work == nil, caption != shown { shown = caption; if text.string != caption { text.string = caption } }
    }

    func textDidChange(_ n: Notification) {
        work?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.commit() }
        work = w
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0, execute: w)
    }
    func textDidEndEditing(_ n: Notification) { commit() }

    /// Write what is typed into the figure's code.
    func commit() {
        work?.cancel(); work = nil
        let t = text.string
        guard t != shown, figureName != nil, figureName == main?.current else { return }
        shown = t
        main?.edit(["op": "caption", "text": t])
    }
}

/// The preflight warnings of the figure on screen; clicking one selects what it is about.
final class ProblemsView: NSView, NSTableViewDataSource, NSTableViewDelegate {
    weak var main: MainController?
    private let table = NSTableView()
    private var items: [Warning] = []
    var count: Int { items.filter { $0.level != "info" }.count }

    override init(frame: NSRect) {
        super.init(frame: frame)
        let c = NSTableColumn(identifier: .init("w")); c.resizingMask = .autoresizingMask
        table.addTableColumn(c); table.headerView = nil; table.dataSource = self; table.delegate = self
        table.rowHeight = 22; table.backgroundColor = Theme.codeBackground
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false
        addSubview(scroll)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor), scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.topAnchor.constraint(equalTo: topAnchor), scroll.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }

    func set(_ w: [Warning]) { items = w; table.reloadData() }
    func numberOfRows(in t: NSTableView) -> Int { max(items.count, 1) }
    func tableView(_ t: NSTableView, viewFor col: NSTableColumn?, row: Int) -> NSView? {
        let v = NSTableCellView()
        let (sym, tint, text): (String, NSColor, String) = items.isEmpty
            ? ("checkmark.circle", .systemGreen, "No problems: text sizes, picture resolutions and placement look fine.")
            : { let w = items[row]
                return (w.level == "error" ? "xmark.octagon" : (w.level == "info" ? "info.circle" : "exclamationmark.triangle"),
                        w.level == "error" ? .systemRed : (w.level == "info" ? .secondaryLabelColor : .systemOrange), w.message) }()
        let i = NSImageView(image: NSImage(systemSymbolName: sym, accessibilityDescription: nil) ?? NSImage()); i.contentTintColor = tint
        let l = NSTextField(labelWithString: text); l.font = Theme.font(11.5); l.lineBreakMode = .byTruncatingTail
        for x in [i, l] { x.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(x) }
        NSLayoutConstraint.activate([
            i.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: 10), i.centerYAnchor.constraint(equalTo: v.centerYAnchor),
            l.leadingAnchor.constraint(equalTo: i.trailingAnchor, constant: 8), l.trailingAnchor.constraint(equalTo: v.trailingAnchor, constant: -8),
            l.centerYAnchor.constraint(equalTo: v.centerYAnchor),
        ])
        return v
    }
    func tableViewSelectionDidChange(_ n: Notification) {
        let r = table.selectedRow
        guard r >= 0, r < items.count, let oid = items[r].oid else { return }
        main?.select([oid.hasPrefix("L") ? (main?.figure?.element(oid)?.panelOid ?? oid) : oid])
    }
}

/// A tab in the drawer's strip (from Manimble): a title, an accent line under the selected one, a close button.
private final class DrawerTab: NSView {
    private let onSelect: () -> Void
    private let onClose: (() -> Void)?
    private let close = NSButton(image: NSImage(systemSymbolName: "xmark", accessibilityDescription: "Close") ?? NSImage(), target: nil, action: nil)
    private var tracking: NSTrackingArea?

    init(title: String, tip: String, selected: Bool, onSelect: @escaping () -> Void, onClose: (() -> Void)?) {
        self.onSelect = onSelect; self.onClose = onClose
        super.init(frame: .zero)
        toolTip = tip.isEmpty ? nil : tip
        let label = NSTextField(labelWithString: title)
        label.font = Theme.font(11, selected ? .semibold : .regular)
        label.textColor = Theme.ink(selected ? 0.9 : 0.5)
        close.isBordered = false; close.toolTip = "Kill terminal"; close.contentTintColor = Theme.ink(0.6)
        close.symbolConfiguration = .init(pointSize: 8, weight: .semibold)
        close.target = self; close.action = #selector(closePressed)
        close.isHidden = true
        let edge = NSView(); edge.wantsLayer = true; edge.layer?.backgroundColor = Theme.accent.cgColor
        edge.isHidden = !selected
        for v in [label, close, edge] { v.translatesAutoresizingMaskIntoConstraints = false; addSubview(v) }
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: 28),
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10), label.centerYAnchor.constraint(equalTo: centerYAnchor),
            close.leadingAnchor.constraint(equalTo: label.trailingAnchor, constant: 4), close.centerYAnchor.constraint(equalTo: centerYAnchor),
            close.widthAnchor.constraint(equalToConstant: onClose == nil ? 0 : 12),
            close.trailingAnchor.constraint(equalTo: trailingAnchor, constant: onClose == nil ? -6 : -8),
            edge.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 4), edge.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            edge.bottomAnchor.constraint(equalTo: bottomAnchor), edge.heightAnchor.constraint(equalToConstant: 2),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let t = tracking { removeTrackingArea(t) }
        tracking = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow], owner: self)
        addTrackingArea(tracking!)
    }
    override func mouseEntered(with event: NSEvent) { close.isHidden = onClose == nil }
    override func mouseExited(with event: NSEvent) { close.isHidden = true }
    override func mouseDown(with event: NSEvent) { DispatchQueue.main.async { [onSelect] in onSelect() } }
    @objc private func closePressed() { DispatchQueue.main.async { [onClose] in onClose?() } }
}


/// The undo history: the edits made in Fignimble, oldest first, then the ones undone (grayed). Clicking one undoes or
/// redoes up to it.
final class HistoryView: NSView, NSTableViewDataSource, NSTableViewDelegate {
    weak var main: MainController?
    private let table = NSTableView()
    private var done: [String] = []
    private var undone: [String] = []
    private var updating = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        let c = NSTableColumn(identifier: .init("h")); c.resizingMask = .autoresizingMask
        table.addTableColumn(c); table.headerView = nil; table.dataSource = self; table.delegate = self
        table.rowHeight = 20; table.backgroundColor = Theme.codeBackground
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false
        addSubview(scroll)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor), scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.topAnchor.constraint(equalTo: topAnchor), scroll.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }
    required init?(coder: NSCoder) { fatalError() }

    func set(done d: [String], undone u: [String]) {
        done = d; undone = u
        updating = true
        table.reloadData()
        table.selectRowIndexes([done.count], byExtendingSelection: false)
        updating = false
    }
    // row 0 is "Opened", then each edit; the selected row is where the files are now
    func numberOfRows(in t: NSTableView) -> Int { 1 + done.count + undone.count }
    func tableView(_ t: NSTableView, viewFor col: NSTableColumn?, row: Int) -> NSView? {
        let title = row == 0 ? "Opened" : (row <= done.count ? done[row - 1] : undone[row - 1 - done.count])
        let l = NSTextField(labelWithString: (row == 0 ? "" : "\(row).  ") + title)
        l.font = Theme.font(11.5); l.textColor = row <= done.count ? .labelColor : .tertiaryLabelColor
        let v = NSTableCellView(); l.translatesAutoresizingMaskIntoConstraints = false; v.addSubview(l)
        NSLayoutConstraint.activate([l.leadingAnchor.constraint(equalTo: v.leadingAnchor, constant: 10), l.centerYAnchor.constraint(equalTo: v.centerYAnchor)])
        return v
    }
    func tableViewSelectionDidChange(_ n: Notification) {
        guard !updating, table.selectedRow >= 0, table.selectedRow != done.count else { return }
        main?.undoTo(table.selectedRow)
    }
}
