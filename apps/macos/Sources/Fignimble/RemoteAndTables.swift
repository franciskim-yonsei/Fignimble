import AppKit
import UniformTypeIdentifiers

// Tables (Insert ▸ Table from CSV…, File ▸ Export Table as Word…), panel letters turned off, and panels from an
// SSH server (Insert ▸ Panel from SSH Server…, right-click ▸ Update from Server).

extension MainController {
    // MARK: labels

    /// Labels ▸ No Letters: no panel letters at all, a panel's own label included (again: letters back).
    @objc func labelsNone(_ sender: Any?) {
        let none = figure?.labelStyle["numbering"] as? String == "none"
        edit(["op": "labels", "props": ["numbering": none ? "alpha" : "none"]])
    }

    // MARK: tables

    @objc func insertTable(_ sender: Any?) {
        guard figure != nil else { return }
        let o = NSOpenPanel()
        o.allowedContentTypes = [.commaSeparatedText, .tabSeparatedText, UTType(filenameExtension: "tsv") ?? .plainText]
        o.message = "A table as CSV or TSV (copied into the project's tables folder); the first row is the header"
        o.beginSheetModal(for: window) { [weak self] r in
            guard let self, r == .OK, let u = o.url else { return }
            self.importFiles([u], at: CGPoint(x: 5, y: 5))
        }
    }

    /// Insert ▸ New Table…: a table in a figure of its own (its cells in tables/<name>.csv), sized to the table,
    /// to type into with the inspector's cells and export as a picture, Word, Excel, CSV or TSV.
    @objc func newTable(_ sender: Any?) {
        guard project != nil else { newProject(sender); return }
        let a = NSAlert(); a.messageText = "New table"
        a.informativeText = "A table in a figure of its own. Type its cells in the inspector; export it as a picture or as Word, Excel, CSV or TSV."
        let name = NSTextField(string: "table\(figures.filter { ($0["name"] as? String ?? "").hasPrefix("table") }.count + 1)")
        let rows = NSTextField(string: "4"), cols = NSTextField(string: "3")
        for f in [name, rows, cols] { f.widthAnchor.constraint(equalToConstant: f === name ? 140 : 50).isActive = true }
        let grid = NSGridView(views: [[NSTextField(labelWithString: "Name"), name],
                                      [NSTextField(labelWithString: "Rows (with the header)"), rows],
                                      [NSTextField(labelWithString: "Columns"), cols]])
        grid.frame = NSRect(x: 0, y: 0, width: 300, height: 90)
        a.accessoryView = grid; a.addButton(withTitle: "Create"); a.addButton(withTitle: "Cancel")
        a.beginSheetModal(for: window) { [weak self] r in
            guard let self, r == .alertFirstButtonReturn else { return }
            if code.dirty { code.save() }
            let args: [String: Any] = ["name": name.stringValue, "rows": max(1, min(200, Int(rows.stringValue) ?? 4)),
                                       "cols": max(1, min(50, Int(cols.stringValue) ?? 3))]
            self.enqueue { [weak self] in guard let self else { return }
                do {
                    let res = try await self.engine.json("new_table", args)
                    if let n = res["name"] as? String {
                        await self.refreshState()
                        self.canvas.selection = []
                        await self.loadFigure(n)
                        if let t = self.figure?.elements.first(where: { $0.kind == "table" }) { self.canvas.selection = [t.oid] }
                    } else if let e = res["error"] as? String { self.toast(e, error: true) }
                } catch { self.toast(error.localizedDescription, error: true) }
            }
        }
    }

    /// The inspector's cells were applied: the table's file (or rows=) gets them; a figure that is only this table
    /// is then made the table's size.
    func applyTableCells(_ oid: String, _ rows: [[String]]) {
        var a: [String: Any] = ["op": "table_rows", "oid": oid, "rows": rows]
        if figure?.isTableFigure == true { a["then"] = ["op": "fit_figure", "margin": 3] }      // one undo step
        edit(a)
        let gen = canvas.tableDraftGeneration
        enqueue { [weak self] in self?.canvas.clearTableDraft(gen) }
    }

    /// File ▸ Export Table…: the selected table (or a table figure's) as Word, Excel, CSV or TSV.
    @objc func exportTable(_ sender: Any?) {
        guard let name = current, let f = figure else { return }
        let picked = canvas.selection.count == 1 ? f.element(canvas.selection[0]) : nil
        guard let e = picked?.kind == "table" ? picked : (f.isTableFigure ? f.elements.first : nil) else { return }
        let p = NSSavePanel()
        let formats = ["docx", "xlsx", "csv", "tsv"]
        let format = NSPopUpButton(frame: .zero, pullsDown: false)
        format.addItems(withTitles: ["Word table (.docx)", "Excel (.xlsx)", "CSV", "TSV"])
        let stem = (((e.s("source") ?? name) as NSString).lastPathComponent as NSString).deletingPathExtension
        let pick = Action { _ in
            let fmt = formats[format.indexOfSelectedItem]
            p.allowedContentTypes = [UTType(filenameExtension: fmt) ?? .data]
            p.nameFieldStringValue = stem + "." + fmt
        }
        format.target = pick; format.action = #selector(Action.fire(_:))
        let acc = NSStackView(views: [NSTextField(labelWithString: "Format:"), format])
        acc.edgeInsets = NSEdgeInsets(top: 8, left: 8, bottom: 8, right: 8)
        p.accessoryView = acc
        pick.fire(format)
        if let proj = project { p.directoryURL = URL(fileURLWithPath: proj) }
        p.beginSheetModal(for: window) { [weak self] r in
            _ = pick
            guard let self, r == .OK, let u = p.url else { return }
            let fmt = formats[format.indexOfSelectedItem]
            self.enqueue { [weak self] in guard let self else { return }
                do {
                    let r = try await self.engine.json("export_table", ["figure": name, "oid": e.oid, "path": u.path, "format": fmt])
                    if r["ok"] as? Bool == true { toast("Exported \(u.lastPathComponent)") }
                    else { toast(r["error"] as? String ?? "Couldn't export the table", error: true) }
                } catch { toast(error.localizedDescription, error: true) }
            }
        }
    }

    // MARK: panels from an SSH server

    @objc func insertRemotePanel(_ sender: Any?) {
        guard let f = figure else { return }
        let d = RemoteDialog(main: self)
        d.onImport = { [weak self] host, path in
            guard let self, let name = self.current else { return }
            self.enqueue { [weak self] in guard let self else { return }
                busy("Fetching \((path as NSString).lastPathComponent) from \(host)…")
                do {
                    try await self.logIn(host)
                    let r = try await self.engine.json("import_remote", ["figure": name, "host": host, "path": path,
                                                                         "x": Double(f.size.width) / 2 - 30, "y": Double(f.size.height) / 2 - 22,
                                                                         "pxPerMM": self.lastPx])
                    if let fig = r["figure"] as? [String: Any] { apply(fig) }
                    if let sel = r["select"] as? [String] { canvas.selection = sel }
                    if r["ok"] as? Bool == true { toast("Fetched \((path as NSString).lastPathComponent) from \(host) into panels/") }
                    else { toast(r["error"] as? String ?? "Couldn't fetch the panel", error: true) }
                } catch SSH.Failure.canceled { idle() } catch { toast(error.localizedDescription, error: true) }
                await refreshState()
            }
        }
        d.begin()
    }

    /// Where a panel's code was fetched from (its first line says), or nil.
    func remoteOrigin(of e: Element) -> String? {
        guard e.kind == "panel" || (e.kind == "inset" && e.inner == "panel"), let project,
              let src = (e.kind == "inset" ? e.item["source"] : e.props["source"]) as? String, src.hasSuffix(".py") else { return nil }
        let path = src.hasPrefix("/") ? src : project + "/" + src
        guard let h = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? h.close() }
        let first = String(decoding: h.readData(ofLength: 600), as: UTF8.self).components(separatedBy: "\n").first ?? ""
        guard first.hasPrefix("# Fetched from "), let r = first.range(of: " by Fignimble") else { return nil }
        return String(first[first.index(first.startIndex, offsetBy: 15)..<r.lowerBound])
    }

    /// Right-click ▸ Update from Server: fetch the panel's code again (one undo step).
    @objc func updateFromServer(_ sender: Any?) {
        guard let name = current, canvas.selection.count == 1, let e = figure?.element(canvas.selection[0]),
              let from = remoteOrigin(of: e), let src = (e.kind == "inset" ? e.item["source"] : e.props["source"]) as? String else { return }
        enqueue { [weak self] in guard let self else { return }
            busy("Fetching \(from)…")
            do {
                try await self.logIn(String(from.prefix { $0 != ":" }))
                let r = try await self.engine.json("update_remote", ["file": src, "figure": name, "pxPerMM": self.lastPx])
                if let fig = r["figure"] as? [String: Any] { apply(fig) }
                if r["ok"] as? Bool == true { toast("Updated \((src as NSString).lastPathComponent) from \(from)") }
                else { toast(r["error"] as? String ?? "Couldn't update the panel", error: true) }
            } catch SSH.Failure.canceled { idle() } catch { toast(error.localizedDescription, error: true) }
            await refreshState()
        }
    }
}

/// Insert ▸ Panel from SSH Server…: a host (from ~/.ssh/config, or typed), a folder there, its folders and .py files.
/// File ▸ Open Project on SSH Server… (`.project`): the same, to open the listed folder as the project, with the
/// Python on the server that draws it.
/// Listing logs in first (MainController.logIn): what ssh asks is shown in a dialog.
@MainActor
final class RemoteDialog: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSComboBoxDelegate {
    enum Mode { case panel, project }
    weak var main: MainController?
    let mode: Mode
    var onImport: ((String, String) -> Void)?
    var onOpenProject: ((RemoteProject) -> Void)?
    let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 520, height: 440), styleMask: [.titled, .resizable], backing: .buffered, defer: false)
    private let host = NSComboBox()
    private let folder = NSTextField()
    private let python = NSTextField()
    private let table = NSTableView()
    private let status = NSTextField(wrappingLabelWithString: "")
    private let importButton = NSButton(title: "Import", target: nil, action: nil)
    private var entries: [(name: String, dir: Bool)] = []
    private var path = ""
    private var keep: RemoteDialog?

    /// `prefill`: a project to show again (Choose Another Python… after its Python lacked packages).
    init(main: MainController, mode: Mode = .panel, prefill: RemoteProject? = nil) {
        self.main = main
        self.mode = mode
        super.init()
        let title = NSTextField(labelWithString: mode == .project ? "Open a project on an SSH server" : "Panel from an SSH server")
        title.font = Theme.font(13, .semibold)
        host.placeholderString = "user@server or a Host from ~/.ssh/config"
        host.completes = true; host.delegate = self
        host.addItems(withObjectValues: UserDefaults.standard.stringArray(forKey: "sshHosts") ?? [])
        if let last = (UserDefaults.standard.stringArray(forKey: "sshHosts") ?? []).first { host.stringValue = last }
        folder.placeholderString = "folder (empty: the home folder)"
        folder.stringValue = UserDefaults.standard.string(forKey: "sshFolder:" + host.stringValue) ?? ""
        let go = NSButton(title: "List", target: self, action: #selector(list))
        go.keyEquivalent = "\r"
        let col = NSTableColumn(identifier: .init("name")); col.resizingMask = .autoresizingMask
        table.addTableColumn(col); table.headerView = nil; table.dataSource = self; table.delegate = self
        table.rowHeight = 22; table.target = self; table.doubleAction = #selector(opened)
        let scroll = NSScrollView(); scroll.documentView = table; scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        status.font = Theme.font(10.5); status.textColor = .secondaryLabelColor; status.preferredMaxLayoutWidth = 480
        status.stringValue = mode == .project
            ? "The figures are drawn on the server, with its Python and data; you edit them here. List a folder, then Open. If the server asks for a password or a code, Fignimble shows the question."
            : "The panel's code is copied into panels/; right-click it ▸ Update from Server to fetch it again. If the server asks for a password or a code, Fignimble shows the question."
        let cancel = NSButton(title: "Cancel", target: self, action: #selector(cancel)); cancel.keyEquivalent = "\u{1b}"
        importButton.target = self; importButton.action = #selector(doImport); importButton.isEnabled = false
        if mode == .project { importButton.title = "Open" }
        python.placeholderString = "python3, or the path of an environment's python on the server"
        python.stringValue = RemoteProject.python(for: host.stringValue)
        python.toolTip = "It needs matplotlib, pymupdf and pillow (Python 3.10 or newer), and whatever the panels' code imports"
        let hostRow = NSStackView(views: [NSTextField(labelWithString: "Server:"), host])
        let folderRow = NSStackView(views: [NSTextField(labelWithString: "Folder:"), folder, go])
        let pythonRow = NSStackView(views: [NSTextField(labelWithString: "Python there:"), python])
        let buttons = NSStackView(views: [NSView(), cancel, importButton])
        let rows: [NSView] = mode == .project ? [title, hostRow, folderRow, pythonRow, scroll, status, buttons]
                                              : [title, hostRow, folderRow, scroll, status, buttons]
        let stack = NSStackView(views: rows)
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 14, left: 16, bottom: 14, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        panel.contentView = NSView()
        panel.contentView!.addSubview(stack)
        for v in rows where v !== title { v.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true }
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: panel.contentView!.leadingAnchor), stack.trailingAnchor.constraint(equalTo: panel.contentView!.trailingAnchor),
            stack.topAnchor.constraint(equalTo: panel.contentView!.topAnchor), stack.bottomAnchor.constraint(equalTo: panel.contentView!.bottomAnchor),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 200),
        ])
        if let p = prefill {
            host.stringValue = p.host; folder.stringValue = p.path; python.stringValue = p.python
            status.stringValue = "Type the path of a Python on \(p.host) that has matplotlib, pymupdf and pillow (Python 3.10 or newer), then Open."
            DispatchQueue.main.async { [weak self] in self?.list(); self?.panel.makeFirstResponder(self?.python) }
        }
        Task { @MainActor in
            let r = (try? await main.engine.json("remote_hosts", [:])) ?? [:]
            let known = Set(host.objectValues.compactMap { $0 as? String })
            host.addItems(withObjectValues: (r["hosts"] as? [String] ?? []).filter { !known.contains($0) })
        }
    }

    func begin() {
        guard let w = main?.window else { return }
        keep = self                                     // held while the sheet is up
        w.beginSheet(panel) { [weak self] _ in self?.keep = nil }
        panel.makeFirstResponder(host.stringValue.isEmpty ? host : folder)
    }

    @objc private func cancel() { main?.window.endSheet(panel) }

    @objc func list() { load(folder.stringValue.trimmingCharacters(in: .whitespaces)) }

    private func load(_ where_: String) {
        let h = host.stringValue.trimmingCharacters(in: .whitespaces)
        guard !h.isEmpty, let m = main else { status.stringValue = "Choose or type a server first."; return }
        status.stringValue = "Listing \(where_.isEmpty ? "the home folder" : where_) on \(h)…"
        importButton.isEnabled = false
        Task { @MainActor in
            do { try await m.logIn(h) } catch {
                status.stringValue = { if case SSH.Failure.canceled = error { return "Canceled." } else { return "Couldn't log in to \(h): " + error.localizedDescription } }()
                status.textColor = .systemRed
                return
            }
            let r = (try? await m.engine.json("remote_list", ["host": h, "path": where_])) ?? ["ok": false, "error": "no answer"]
            guard r["ok"] as? Bool == true else {
                status.stringValue = "Couldn't list it: " + (r["error"] as? String ?? "")
                status.textColor = .systemRed
                return
            }
            status.textColor = .secondaryLabelColor
            path = r["path"] as? String ?? where_
            folder.stringValue = path
            entries = (r["entries"] as? [[String: Any]] ?? []).map { ($0["name"] as? String ?? "", $0["dir"] as? Bool ?? false) }
            if path != "/" { entries.insert(("..", true), at: 0) }
            table.reloadData()
            let n = entries.filter { !$0.dir }.count
            status.stringValue = mode == .project
                ? "\(h):\(path): \(n) Python file\(n == 1 ? "" : "s"). Double-click a folder to go into it; Open opens this one as the project."
                : "\(h):\(path): \(n) Python file\(n == 1 ? "" : "s"). Double-click a folder to open it, a .py file to import it."
            if mode == .project { importButton.isEnabled = true }
            var recent = UserDefaults.standard.stringArray(forKey: "sshHosts") ?? []
            recent.removeAll { $0 == h }; recent.insert(h, at: 0)
            UserDefaults.standard.set(Array(recent.prefix(10)), forKey: "sshHosts")
            UserDefaults.standard.set(path, forKey: "sshFolder:" + h)
        }
    }

    private func child(_ name: String) -> String {
        if name == ".." { return (path as NSString).deletingLastPathComponent.isEmpty ? "/" : (path as NSString).deletingLastPathComponent }
        return path.hasSuffix("/") ? path + name : path + "/" + name
    }

    @objc private func opened() {
        let r = table.clickedRow
        guard r >= 0, r < entries.count else { return }
        if entries[r].dir { load(child(entries[r].name)) } else if mode == .panel { doImport() }
    }

    @objc private func doImport() {
        if mode == .project {
            let h = host.stringValue.trimmingCharacters(in: .whitespaces)
            guard !h.isEmpty, !path.isEmpty else { return }
            let py = python.stringValue.trimmingCharacters(in: .whitespaces)
            RemoteProject.remember(python: py.isEmpty ? "python3" : py, for: h)
            main?.window.endSheet(panel)
            onOpenProject?(RemoteProject(host: h, path: path, python: py.isEmpty ? "python3" : py))
            return
        }
        let r = table.selectedRow
        guard r >= 0, r < entries.count, !entries[r].dir else { return }
        let h = host.stringValue.trimmingCharacters(in: .whitespaces), p = child(entries[r].name)
        main?.window.endSheet(panel)
        onImport?(h, p)
    }

    func comboBoxSelectionDidChange(_ n: Notification) {
        DispatchQueue.main.async { [self] in
            folder.stringValue = UserDefaults.standard.string(forKey: "sshFolder:" + host.stringValue) ?? ""
            python.stringValue = RemoteProject.python(for: host.stringValue)
            path = ""
            if mode == .project { importButton.isEnabled = false }
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int { entries.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let e = entries[row]
        let cell = NSTableCellView()
        let icon = NSImageView(image: NSImage(systemSymbolName: e.dir ? "folder" : "chart.xyaxis.line", accessibilityDescription: nil) ?? NSImage())
        let label = NSTextField(labelWithString: e.name); label.font = Theme.font(12)
        let row = NSStackView(views: [icon, label]); row.spacing = 6
        row.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(row)
        NSLayoutConstraint.activate([row.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4), row.centerYAnchor.constraint(equalTo: cell.centerYAnchor)])
        return cell
    }
    func tableViewSelectionDidChange(_ n: Notification) {
        guard mode == .panel else { return }
        let r = table.selectedRow
        importButton.isEnabled = r >= 0 && r < entries.count && !entries[r].dir
    }
}

// MARK: - the command line

extension MainController {
    /// Fignimble ▸ Install Command Line Tool…: `fignimble` in ~/.local/bin (a link to the app's copy, so it follows
    /// the app's updates), and that folder on the login shell's PATH when it isn't, so Terminal, VS Code and coding
    /// agents outside Fignimble can run `fignimble snapshot`, `export`, `table`… No administrator password.
    @objc func installCommandLineTool(_ sender: Any?) {
        let fm = FileManager.default
        let bin = NSHomeDirectory() + "/.local/bin", link = bin + "/fignimble"
        let target = TerminalSession.fignimbleBin + "/fignimble"
        guard fm.fileExists(atPath: target) else { alert("The fignimble command is missing from the app", target); return }
        if let attrs = try? fm.attributesOfItem(atPath: link), attrs[.type] as? FileAttributeType != .typeSymbolicLink {
            let a = NSAlert(); a.messageText = "Replace ~/.local/bin/fignimble?"
            a.informativeText = "There is a file of that name there already, not one Fignimble made."
            a.addButton(withTitle: "Replace"); a.addButton(withTitle: "Cancel")
            if a.runModal() != .alertFirstButtonReturn { return }
        }
        do {
            try fm.createDirectory(atPath: bin, withIntermediateDirectories: true)
            try? fm.removeItem(atPath: link)
            try fm.createSymbolicLink(atPath: link, withDestinationPath: target)
        } catch { alert("Couldn't install the fignimble command", error.localizedDescription); return }
        // is ~/.local/bin on the PATH a new terminal gets?
        let shell = ProcessInfo.processInfo.environment["SHELL"] ?? "/bin/zsh"
        let p = Process(), out = Pipe()
        p.executableURL = URL(fileURLWithPath: shell); p.arguments = ["-ilc", "echo \"@@$PATH\""]
        p.standardOutput = out; p.standardError = FileHandle.nullDevice; p.standardInput = FileHandle.nullDevice
        try? p.run(); p.waitUntilExit()
        let path = String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
            .components(separatedBy: "@@").last?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        if path.split(separator: ":").contains(where: { $0 == bin || $0 == "~/.local/bin" }) {
            toast("Installed: run fignimble in any new terminal")
            return
        }
        let profile = NSHomeDirectory() + (shell.hasSuffix("bash") ? "/.bash_profile" : "/.zprofile")
        let a = NSAlert()
        a.messageText = "Add ~/.local/bin to your PATH?"
        a.informativeText = "fignimble is installed in ~/.local/bin, which your shell doesn't search. Fignimble can add this line to \((profile as NSString).abbreviatingWithTildeInPath):\n\nexport PATH=\"$HOME/.local/bin:$PATH\""
        a.addButton(withTitle: "Add It"); a.addButton(withTitle: "Not Now")
        guard a.runModal() == .alertFirstButtonReturn else { toast("Installed in ~/.local/bin (not on your PATH)"); return }
        let line = "\n# added by Fignimble (Install Command Line Tool)\nexport PATH=\"$HOME/.local/bin:$PATH\"\n"
        if let h = FileHandle(forWritingAtPath: profile) { h.seekToEndOfFile(); h.write(Data(line.utf8)); try? h.close() }
        else { try? line.write(toFile: profile, atomically: true, encoding: .utf8) }
        toast("Installed: run fignimble in a new terminal window")
    }
}

// MARK: - journals, by publisher

extension MainController {
    /// The journals grouped by publisher, in the engine's order (fignimble/journals.py).
    var journalGroups: [(publisher: String, keys: [String])] {
        let keys = journals.keys.sorted { (journals[$0]?["rank"] as? Int ?? 0) < (journals[$1]?["rank"] as? Int ?? 0) }
        var out: [(publisher: String, keys: [String])] = []
        for k in keys {
            let p = journals[k]?["publisher"] as? String ?? ""
            if out.last?.publisher == p { out[out.count - 1].keys.append(k) } else { out.append((p, [k])) }
        }
        return out
    }
    func journalName(_ key: String) -> String { journals[key]?["name"] as? String ?? key }

    /// A menu of journals: a header per publisher, its journals under it; `items` makes a journal's item(s), with
    /// its key (or what they stand for) as representedObject.
    func fillJournalMenu(_ menu: NSMenu, _ items: (String) -> [NSMenuItem]) {
        for g in journalGroups {
            if menu.numberOfItems > 0 { menu.addItem(.separator()) }
            let h = NSMenuItem(title: g.publisher, action: nil, keyEquivalent: ""); h.isEnabled = false
            menu.addItem(h)
            for k in g.keys { for it in items(k) { it.indentationLevel = 1; menu.addItem(it) } }
        }
    }

    /// Where a journal's numbers come from, for the export dialog.
    func journalChecked(_ key: String) -> String {
        guard let j = journals[key] else { return "" }
        let how: String
        switch j["checked"] as? String {
        case "guidelines": how = "Checked against \(journalName(key))'s guidelines (November 2026)."
        case "secondary": how = "From summaries of \(journalName(key))'s guidelines (its site couldn't be read): check them."
        default: how = "\(j["publisher"] as? String ?? "The publisher")'s usual values, not checked for \(journalName(key)): check its guidelines."
        }
        return how + ((j["note"] as? String).map { " \($0.prefix(1).uppercased() + $0.dropFirst())." } ?? "")
    }
}
