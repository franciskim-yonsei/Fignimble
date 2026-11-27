import AppKit

// Whole figures and tables, from the sidebar's right-click menu: cut, copy, paste, delete (each one undo step).

extension MainController {
    static let figurePasteType = NSPasteboard.PasteboardType("org.pachterlab.fignimble.figure")

    /// The figure or table a sidebar menu item is about (its representedObject), else the one on screen.
    private func figureName(_ sender: Any?) -> String? { (sender as? NSMenuItem)?.representedObject as? String ?? current }

    /// Copy: the figure's code (and the data file it keeps for itself, a table's CSV) on the pasteboard, to paste as a
    /// new figure here or in another project; and its file, for the Finder.
    @objc func copyFigureFile(_ sender: Any?) { copyFigureFile(figureName(sender), then: nil) }

    private func copyFigureFile(_ name: String?, then: (() -> Void)?) {
        guard let name else { return }
        if code.dirty { code.save() }
        enqueue { [self] in
            do {
                let r = try await engine.json("copy_figure", ["name": name])
                guard r["ok"] as? Bool == true, let text = r["text"] as? String else {
                    toast(r["error"] as? String ?? "Couldn't copy \(name)", error: true); return
                }
                let payload = try JSONSerialization.data(withJSONObject: ["name": name, "text": text, "data": r["data"] ?? [:],
                                                                          "kind": r["kind"] ?? "figure"])
                let item = NSPasteboardItem()
                item.setData(payload, forType: MainController.figurePasteType)
                item.setString(text, forType: .string)
                if let p = project, remote == nil { item.setString(URL(fileURLWithPath: p + "/" + name + ".py").absoluteString, forType: .fileURL) }
                let pb = NSPasteboard.general
                pb.clearContents()
                pb.writeObjects([item])
                if let then { then() } else { toast("Copied \(name)") }
            } catch { toast(error.localizedDescription, error: true) }
        }
    }

    @objc func cutFigureFile(_ sender: Any?) {
        guard let name = figureName(sender) else { return }
        copyFigureFile(name) { [weak self] in self?.deleteFigureFile(name, cut: true) }
    }

    /// Paste: a figure or table copied whole, as a new file (named <name>_copy when its name is taken), shown.
    @objc func pasteFigureFile(_ sender: Any?) {
        guard project != nil, let d = NSPasteboard.general.data(forType: MainController.figurePasteType),
              let obj = try? JSONSerialization.jsonObject(with: d) as? [String: Any], let text = obj["text"] as? String else { return }
        if code.dirty { code.save() }
        enqueue { [self] in
            do {
                let r = try await engine.json("paste_figure", ["name": obj["name"] ?? "fig", "text": text, "data": obj["data"] ?? [:]])
                guard let n = r["name"] as? String else { toast(r["error"] as? String ?? "Couldn't paste", error: true); return }
                await refreshState()
                canvas.selection = []
                await loadFigure(n)
                toast("Pasted \(n)")
            } catch { toast(error.localizedDescription, error: true) }
        }
    }

    @objc func deleteFigureFile(_ sender: Any?) {
        guard let name = figureName(sender) else { return }
        deleteFigureFile(name, cut: false)
    }

    /// The figure's file goes (with a table's own CSV); Edit ▸ Undo brings it back. When it was on screen, the one
    /// next to it in the list is shown.
    private func deleteFigureFile(_ name: String, cut: Bool) {
        if name == current, code.dirty { code.save() }
        enqueue { [self] in
            do {
                let r = try await engine.json("delete_figure", ["name": name])
                guard r["ok"] as? Bool == true else { toast(r["error"] as? String ?? "Couldn't delete \(name)", error: true); return }
                await refreshState()
                let used = r["usedBy"] as? [String] ?? []
                toast((cut ? "Cut \(name)" : "Deleted \(name)") + " (Undo brings it back)"
                      + (used.isEmpty ? "" : ". \(used.joined(separator: ", ")) placed it and won't run without it"), error: !used.isEmpty)
            } catch { toast(error.localizedDescription, error: true) }
        }
    }

    /// The menu for a figure or table in the sidebar (`name`), or for the list's empty space (nil).
    func figureListMenu(_ name: String?) -> NSMenu {
        let m = NSMenu()
        m.autoenablesItems = false
        func add(_ t: String, _ s: Selector, enabled: Bool = true) {
            let i = NSMenuItem(title: t, action: s, keyEquivalent: ""); i.target = self; i.representedObject = name
            i.isEnabled = enabled; m.addItem(i)
        }
        let canPaste = project != nil && NSPasteboard.general.data(forType: MainController.figurePasteType) != nil
        add("Cut", #selector(cutFigureFile(_:)), enabled: name != nil)
        add("Copy", #selector(copyFigureFile(_:)), enabled: name != nil)
        add("Paste", #selector(pasteFigureFile(_:)), enabled: canPaste)
        add("Delete", #selector(deleteFigureFile(_:)), enabled: name != nil)
        m.addItem(.separator())
        add("New Figure…", #selector(newFigure(_:)))
        add("New Table…", #selector(newTable(_:)))
        return m
    }
}
