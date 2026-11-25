import AppKit
import CryptoKit

/// A project on an SSH server (File ▸ Open Project on SSH Server…). The engine runs there, started over ssh by
/// fignimble.tunnel, so panel code runs next to its data; the app works on a mirror of the project's files that the
/// tunnel keeps in step with the server (engine/fignimble/tunnel.py).
struct RemoteProject: Equatable {
    var host: String
    /// The project's folder on the server, as typed or listed (may start with ~).
    var path: String
    /// The Python on the server that runs the engine and the panels' code.
    var python: String

    var name: String { (path as NSString).lastPathComponent.isEmpty ? "home" : (path as NSString).lastPathComponent }
    var title: String { "\(name) on \(host)" }

    /// Where the mirror is: ~/Library/Caches/Fignimble/remote/<name>-<hash of host:path>/<name>.
    var mirror: String {
        let slug = SHA256.hash(data: Data("\(host):\(path)".utf8)).map { String(format: "%02x", $0) }.joined().prefix(12)
        return NSHomeDirectory() + "/Library/Caches/Fignimble/remote/\(name)-\(slug)/\(name)"
    }

    /// How it appears in the recent projects: ssh:<host>:<path>.
    var recentKey: String { "ssh:\(host):\(path)" }

    init(host: String, path: String, python: String? = nil) {
        self.host = host; self.path = path
        self.python = python ?? RemoteProject.python(for: host)
    }

    init?(recentKey k: String) {
        guard k.hasPrefix("ssh:") else { return nil }
        let rest = k.dropFirst(4)
        guard let colon = rest.firstIndex(of: ":") else { return nil }
        self.init(host: String(rest[..<colon]), path: String(rest[rest.index(after: colon)...]))
    }

    /// The Python last used on a server (python3 at first).
    static func python(for host: String) -> String { UserDefaults.standard.string(forKey: "sshPython:" + host) ?? "python3" }
    static func remember(python: String, for host: String) { UserDefaults.standard.set(python, forKey: "sshPython:" + host) }

    /// The folder for the server's shell: quoted, a leading ~ left for it to expand.
    var shellPath: String { SSH.quote(path) }
}

extension MainController {
    /// File ▸ Open Project on SSH Server…: a folder on a server, its figures edited here and drawn there.
    @objc func openRemoteProject(_ sender: Any?) {
        let d = RemoteDialog(main: self, mode: .project)
        d.onOpenProject = { [weak self] r in self?.openRemote(r) }
        d.begin()
    }
}
