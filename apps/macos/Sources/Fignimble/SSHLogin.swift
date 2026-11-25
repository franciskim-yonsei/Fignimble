import AppKit

/// Logging in to SSH servers, once per server per session.
///
/// Before anything talks to a server (listing a folder, fetching a panel, opening a project there), the app opens a
/// shared ssh connection to it (ControlMaster, its socket in `controlDir`). Whatever ssh asks while logging in, a
/// password, a two-factor code, a key's passphrase, is shown in a dialog: ssh runs this app's own executable as its
/// SSH_ASKPASS program, which in that mode (`askpassMain`) shows the question and prints the answer. Nothing is
/// stored. Everything else Fignimble runs over ssh (the engine's remote.py and tunnel.py, the terminal tabs) goes
/// through that connection without asking again (FIGNIMBLE_SSH_SHARE).
enum SSH {
    /// Short, as a Unix socket's path must be (under 104 bytes): /tmp/fignimble-<uid>/<hash of the connection>.
    static let controlDir = "/tmp/fignimble-\(getuid())"

    /// The options that make an ssh use (or open) the shared connection.
    static var shareOptions: [String] {
        ["-o", "ControlPath=\(controlDir)/%C", "-o", "ControlMaster=auto", "-o", "ControlPersist=4h"]
    }

    /// The tests and screenshot scenes replace ssh with a stand-in (FIGNIMBLE_SSH) that needs no login.
    static var faked: Bool { ProcessInfo.processInfo.environment["FIGNIMBLE_SSH"] != nil }

    static func makeControlDir() {
        try? FileManager.default.createDirectory(atPath: controlDir, withIntermediateDirectories: true,
                                                 attributes: [.posixPermissions: 0o700])
    }

    enum Failure: LocalizedError {
        case canceled
        case failed(String)
        var errorDescription: String? {
            switch self { case .canceled: return "Canceled"; case .failed(let s): return s }
        }
    }

    /// A word for the server's shell: quoted, with a leading ~ left for it to expand.
    static func quote(_ s: String) -> String {
        func q(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        if s == "~" { return "~" }
        if s.hasPrefix("~/") { return "~/" + q(String(s.dropFirst(2))) }
        return q(s)
    }

    /// Logged in to `host`: the shared connection open, asking for whatever ssh needs. Returns the questions ssh
    /// asked (none when the connection was already open or a key did it).
    static func login(_ host: String) async throws -> [String] {
        if faked { return [] }
        makeControlDir()
        if await run(host, nil, options: ["-O", "check"]).status == 0 { return [] }
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("fignimble-login-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }
        let err = dir.appendingPathComponent("stderr")
        FileManager.default.createFile(atPath: err.path, contents: nil)
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ssh")
        // -f: into the background once logged in, so the process ends and the connection stays (ControlPersist)
        p.arguments = ["-M", "-N", "-f", "-o", "ControlPath=\(controlDir)/%C", "-o", "ControlPersist=4h",
                       "-o", "ConnectTimeout=15", "-o", "StrictHostKeyChecking=accept-new",
                       "-o", "ServerAliveInterval=10", "-o", "ServerAliveCountMax=3", "-o", "NumberOfPasswordPrompts=3",
                       "--", host]
        var env = ProcessInfo.processInfo.environment
        env["SSH_ASKPASS"] = Bundle.main.executablePath
        env["SSH_ASKPASS_REQUIRE"] = "force"
        env["FIGNIMBLE_ASKPASS"] = dir.path
        env["FIGNIMBLE_ASKPASS_HOST"] = host
        p.environment = env
        p.standardInput = FileHandle.nullDevice
        p.standardOutput = FileHandle.nullDevice
        p.standardError = try? FileHandle(forWritingTo: err)        // a file: the background ssh keeps it open
        let status: Int32 = await withCheckedContinuation { c in
            p.terminationHandler = { c.resume(returning: $0.terminationStatus) }
            do { try p.run() } catch { p.terminationHandler = nil; c.resume(returning: -1) }
        }
        let asked = ((try? String(contentsOf: dir.appendingPathComponent("prompts"), encoding: .utf8)) ?? "")
            .components(separatedBy: "\u{0}").filter { !$0.isEmpty }
        if status == 0 { return asked }
        if FileManager.default.fileExists(atPath: dir.appendingPathComponent("canceled").path) { throw Failure.canceled }
        let said = ((try? String(contentsOf: err, encoding: .utf8)) ?? "").split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty && !$0.hasPrefix("Warning: Permanently added") }
        throw Failure.failed(said.last ?? "ssh couldn't log in to \(host) (exit status \(status))")
    }

    /// A shell command on `host` through the shared connection (no questions), with `input` on its stdin.
    static func run(_ host: String, _ command: String?, input: Data? = nil, options: [String] = []) async -> (status: Int32, output: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/ssh")
        var args: [String] = shareOptions + ["-o", "BatchMode=yes", "-o", "ConnectTimeout=15"]
        args += options
        args += ["--", host]
        if let c = command { args.append("sh -c " + quote(c)) }
        p.arguments = args
        let out = Pipe(), inp = Pipe()
        p.standardOutput = out; p.standardError = out
        p.standardInput = input == nil ? FileHandle.nullDevice : inp
        return await withCheckedContinuation { c in
            DispatchQueue.global().async {
                do { try p.run() } catch { c.resume(returning: (-1, error.localizedDescription)); return }
                if let d = input { try? inp.fileHandleForWriting.write(contentsOf: d); try? inp.fileHandleForWriting.close() }
                let data = out.fileHandleForReading.readDataToEndOfFile()
                p.waitUntilExit()
                c.resume(returning: (p.terminationStatus, String(decoding: data, as: UTF8.self)))
            }
        }
    }

    // MARK: SSH_ASKPASS

    /// This executable run by ssh as SSH_ASKPASS: the question (argv[1]) in a dialog, the answer on stdout.
    /// SSH_ASKPASS_PROMPT says what kind: "confirm" (yes or no), "none" (a notice, e.g. "a push was sent to your
    /// phone", ended by ssh), or empty (an answer to type).
    @MainActor
    static func askpassMain() -> Never {
        let env = ProcessInfo.processInfo.environment
        let dir = URL(fileURLWithPath: env["FIGNIMBLE_ASKPASS"] ?? NSTemporaryDirectory())
        let prompt = (CommandLine.arguments.dropFirst().first ?? "Password:").trimmingCharacters(in: .whitespacesAndNewlines)
        let kind = env["SSH_ASKPASS_PROMPT"] ?? ""
        if let h = FileHandle(forUpdatingAtPath: dir.appendingPathComponent("prompts").path)
            ?? { FileManager.default.createFile(atPath: dir.appendingPathComponent("prompts").path, contents: nil)
                 return FileHandle(forUpdatingAtPath: dir.appendingPathComponent("prompts").path) }() {
            h.seekToEndOfFile(); h.write(Data((prompt + "\u{0}").utf8)); try? h.close()
        }
        func canceled() -> Never {
            FileManager.default.createFile(atPath: dir.appendingPathComponent("canceled").path, contents: nil)
            exit(1)
        }
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        app.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = "Log in to \(env["FIGNIMBLE_ASKPASS_HOST"] ?? "the server")"
        a.informativeText = prompt
        a.icon = NSWorkspace.shared.icon(forFile: Bundle.main.bundlePath)      // run bare by ssh, not by Launch Services
        let lower = prompt.lowercased()
        if kind == "none" {
            a.addButton(withTitle: "OK")
            a.runModal()
            exit(0)
        }
        if kind == "confirm" || lower.contains("(yes/no") {
            a.addButton(withTitle: "Yes"); a.addButton(withTitle: "No")
            let yes = a.runModal() == .alertFirstButtonReturn
            if kind == "confirm" { yes ? exit(0) : canceled() }
            print(yes ? "yes" : "no"); exit(0)
        }
        let secret = ["password", "passphrase", "pin"].contains { lower.contains($0) }
        let field: NSTextField = secret ? NSSecureTextField() : NSTextField()
        field.frame = NSRect(x: 0, y: 0, width: 260, height: 24)
        a.accessoryView = field
        a.addButton(withTitle: "OK"); a.addButton(withTitle: "Cancel")
        a.window.initialFirstResponder = field
        a.window.level = .floating
        guard a.runModal() == .alertFirstButtonReturn else { canceled() }
        print(field.stringValue)
        exit(0)
    }
}

extension MainController {
    /// Logged in to `host` (SSH.login), offering afterwards, when a password was typed, to log in with a key next
    /// time. Throws SSH.Failure.canceled when the person canceled a question.
    func logIn(_ host: String) async throws {
        let asked = try await SSH.login(host)
        let lower = asked.map { $0.lowercased() }
        guard !lower.isEmpty, lower.allSatisfy({ $0.contains("password") && !$0.contains("passphrase") }),
              !UserDefaults.standard.bool(forKey: "sshNoKeyOffer:" + host) else { return }
        await offerKey(host)
    }

    /// "Log in to <host> with a key from now on?": the public key (made first if there's none, ~/.ssh/id_ed25519
    /// without a passphrase) added to ~/.ssh/authorized_keys there, through the connection just opened.
    private func offerKey(_ host: String) async {
        let ssh = NSHomeDirectory() + "/.ssh/"
        let existing = ["id_ed25519.pub", "id_ecdsa.pub", "id_rsa.pub"].map { ssh + $0 }
            .first { FileManager.default.fileExists(atPath: $0) }
        let a = NSAlert()
        a.messageText = "Log in to \(host) with a key from now on?"
        a.informativeText = (existing.map { "Fignimble can add your key (\(($0 as NSString).abbreviatingWithTildeInPath)) to ~/.ssh/authorized_keys on \(host)." }
            ?? "You don't have an SSH key yet. Fignimble can make one (~/.ssh/id_ed25519, without a passphrase) and add it to ~/.ssh/authorized_keys on \(host).")
            + " Then neither Fignimble nor ssh in a terminal asks for your password there."
        a.addButton(withTitle: "Use a Key"); a.addButton(withTitle: "Not Now")
        a.showsSuppressionButton = true
        a.suppressionButton?.title = "Don't ask again for \(host)"
        let r = a.runModal()
        if a.suppressionButton?.state == .on { UserDefaults.standard.set(true, forKey: "sshNoKeyOffer:" + host) }
        guard r == .alertFirstButtonReturn else { return }
        var pub = existing
        if pub == nil {
            try? FileManager.default.createDirectory(atPath: ssh, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let k = Process()
            k.executableURL = URL(fileURLWithPath: "/usr/bin/ssh-keygen")
            k.arguments = ["-q", "-t", "ed25519", "-N", "", "-f", ssh + "id_ed25519"]
            k.standardInput = FileHandle.nullDevice
            if (try? k.run()) != nil { k.waitUntilExit() }
            if k.terminationStatus == 0 { pub = ssh + "id_ed25519.pub" }
        }
        guard let p = pub, let key = try? String(contentsOfFile: p, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines),
              !key.isEmpty else { toast("Couldn't make an SSH key", error: true); return }
        let script = "umask 077; mkdir -p ~/.ssh && touch ~/.ssh/authorized_keys && read -r k && "
            + "{ grep -qxF \"$k\" ~/.ssh/authorized_keys || printf '%s\\n' \"$k\" >> ~/.ssh/authorized_keys; }"
        let res = await SSH.run(host, script, input: Data((key + "\n").utf8))
        if res.status == 0 { toast("Added your key on \(host): no more passwords there") }
        else { toast("Couldn't add your key on \(host): " + res.output.trimmingCharacters(in: .whitespacesAndNewlines), error: true) }
    }

    /// Fignimble's packages installed with pip into a Python on a server (the engine said it lacks them).
    func installRemotePackages(_ pip: [String], python: String, host: String) async -> Bool {
        busy("Installing \(pip.joined(separator: ", ")) on \(host)…")
        let res = await SSH.run(host, "\(SSH.quote(python)) -m pip install " + pip.map(SSH.quote).joined(separator: " "))
        idle()
        if res.status == 0 { return true }
        let tail = res.output.split(separator: "\n").suffix(12).joined(separator: "\n")
        let managed = res.output.contains("externally-managed-environment")
        alert("Couldn't install \(pip.joined(separator: ", ")) on \(host)",
              (managed ? "\(python) there is the system's Python, which pip doesn't install into. Choose the Python of a conda or virtual environment instead.\n\n" : "")
              + tail)
        return false
    }
}
