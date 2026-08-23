// ClaudeWidget for macOS - Claude usage gauges in the menu bar.
// Single-file AppKit app, compiled from source by install.sh with swiftc.
// No dependencies, no telemetry. Tokens go to api.anthropic.com,
// platform.claude.com and console.anthropic.com, nowhere else.
//
// Credentials: Claude Code on macOS stores its OAuth tokens in the login
// keychain (generic password "Claude Code-credentials"). This app reads
// them with /usr/bin/security, and writes the rotated refresh token back
// (the OAuth server invalidates the old one on every refresh - keeping the
// new token to ourselves would sign Claude Code out within hours; the
// Windows version learned that the hard way).

import AppKit
import Foundation

// ---------- localization ----------

struct Strings {
    let session5h: String
    let week: String
    let resetsIn: String       // %@ = duration
    let updated: String        // %@ = HH:mm
    let offline: String        // %@ = error
    let menuRefresh: String
    let menuStartAtLogin: String
    let menuQuit: String
    let errNotSignedIn: String
    let errBadResponse: String
    let dayUnit: String
    let hourUnit: String
    let minuteUnit: String
}

let catalog: [String: Strings] = [
    "en": Strings(session5h: "5-hour session", week: "Week",
                  resetsIn: "resets in %@", updated: "updated %@",
                  offline: "Offline: %@",
                  menuRefresh: "Refresh", menuStartAtLogin: "Start at login",
                  menuQuit: "Quit",
                  errNotSignedIn: "Claude Code is not signed in (run it once)",
                  errBadResponse: "Unreadable API response",
                  dayUnit: "d", hourUnit: "h", minuteUnit: "min"),
    "fr": Strings(session5h: "Session 5 h", week: "Semaine",
                  resetsIn: "reset dans %@", updated: "maj %@",
                  offline: "Hors ligne : %@",
                  menuRefresh: "Actualiser", menuStartAtLogin: "Lancer à l'ouverture de session",
                  menuQuit: "Quitter",
                  errNotSignedIn: "Claude Code n'est pas connecté (lance-le une fois)",
                  errBadResponse: "Réponse de l'API illisible",
                  dayUnit: "j", hourUnit: "h", minuteUnit: "min"),
    "es": Strings(session5h: "Sesión de 5 h", week: "Semana",
                  resetsIn: "se reinicia en %@", updated: "act. %@",
                  offline: "Sin conexión: %@",
                  menuRefresh: "Actualizar", menuStartAtLogin: "Iniciar al abrir sesión",
                  menuQuit: "Salir",
                  errNotSignedIn: "Claude Code no ha iniciado sesión (ejecútalo una vez)",
                  errBadResponse: "Respuesta de la API ilegible",
                  dayUnit: "d", hourUnit: "h", minuteUnit: "min"),
    "de": Strings(session5h: "5-Stunden-Sitzung", week: "Woche",
                  resetsIn: "zurückgesetzt in %@", updated: "akt. %@",
                  offline: "Offline: %@",
                  menuRefresh: "Aktualisieren", menuStartAtLogin: "Bei Anmeldung starten",
                  menuQuit: "Beenden",
                  errNotSignedIn: "Claude Code ist nicht angemeldet (einmal starten)",
                  errBadResponse: "Unlesbare API-Antwort",
                  dayUnit: "T", hourUnit: "h", minuteUnit: "Min")
]

let L: Strings = {
    let code = String(Locale.preferredLanguages.first?.prefix(2) ?? "en")
    return catalog[code] ?? catalog["en"]!
}()

// ---------- credentials (login keychain, via /usr/bin/security) ----------

struct Oauth {
    var accessToken: String
    var refreshToken: String
    var expiresAt: Double      // unix milliseconds
}

enum Keychain {
    static let service = "Claude Code-credentials"

    static func run(_ args: [String]) -> (status: Int32, out: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return (1, "") }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }

    static func readJson() -> String? {
        let r = run(["find-generic-password", "-s", service, "-w"])
        if r.status == 0 {
            let s = r.out.trimmingCharacters(in: .whitespacesAndNewlines)
            return s.isEmpty ? nil : s
        }
        // fallback: some setups keep the plain file like on Windows/Linux
        let path = NSString(string: "~/.claude/.credentials.json").expandingTildeInPath
        return try? String(contentsOfFile: path, encoding: .utf8)
    }

    static func account() -> String {
        let r = run(["find-generic-password", "-s", service])
        for line in r.out.split(separator: "\n") {
            if line.contains("\"acct\"") {
                let parts = line.split(separator: "\"")
                if parts.count >= 4 { return String(parts[3]) }
            }
        }
        return NSUserName()
    }

    static func writeJson(_ json: String) {
        // -U updates the existing item in place
        _ = run(["add-generic-password", "-U", "-s", service, "-a", account(), "-w", json])
    }
}

enum Credentials {
    static func load() -> (oauth: Oauth, raw: String)? {
        guard let raw = Keychain.readJson(),
              let data = raw.data(using: .utf8),
              let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let o = root["claudeAiOauth"] as? [String: Any],
              let access = o["accessToken"] as? String,
              let refresh = o["refreshToken"] as? String
        else { return nil }
        let expires = (o["expiresAt"] as? Double) ?? 0
        return (Oauth(accessToken: access, refreshToken: refresh, expiresAt: expires), raw)
    }

    // Patch the three fields in place so unknown fields survive, exactly
    // like the Windows version does.
    static func writeBack(_ new: Oauth, raw: String) {
        var json = raw
        func setString(_ key: String, _ value: String) {
            guard let range = json.range(of: "\"\(key)\":") else { return }
            var i = range.upperBound
            while i < json.endIndex, json[i] == " " { i = json.index(after: i) }
            guard i < json.endIndex, json[i] == "\"" else { return }
            let start = json.index(after: i)
            guard let end = json[start...].firstIndex(of: "\"") else { return }
            json.replaceSubrange(start..<end, with: value)
        }
        func setNumber(_ key: String, _ value: Double) {
            guard let range = json.range(of: "\"\(key)\":") else { return }
            var i = range.upperBound
            while i < json.endIndex, json[i] == " " { i = json.index(after: i) }
            var end = i
            while end < json.endIndex, "0123456789.-".contains(json[end]) { end = json.index(after: end) }
            if end > i { json.replaceSubrange(i..<end, with: String(format: "%.0f", value)) }
        }
        setString("accessToken", new.accessToken)
        setString("refreshToken", new.refreshToken)
        setNumber("expiresAt", new.expiresAt)
        Keychain.writeJson(json)
    }
}

// ---------- Claude API ----------

struct Limit { var utilization: Double?; var resetsAt: String? }
struct Usage { var fiveHour: Limit?; var sevenDay: Limit? }

enum Api {
    static let clientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
    static let usageUrl = "https://api.anthropic.com/api/oauth/usage"
    static let tokenUrls = ["https://platform.claude.com/v1/oauth/token",
                            "https://console.anthropic.com/v1/oauth/token"]

    static func post(_ url: String, body: String, contentType: String) -> Data? {
        guard let u = URL(string: url) else { return nil }
        var req = URLRequest(url: u, timeoutInterval: 20)
        req.httpMethod = "POST"
        req.setValue(contentType, forHTTPHeaderField: "Content-Type")
        req.httpBody = body.data(using: .utf8)
        let sem = DispatchSemaphore(value: 0)
        var result: Data?
        URLSession.shared.dataTask(with: req) { data, resp, _ in
            if let http = resp as? HTTPURLResponse, http.statusCode == 200 { result = data }
            sem.signal()
        }.resume()
        _ = sem.wait(timeout: .now() + 25)
        return result
    }

    static func token() throws -> String {
        guard let loaded = Credentials.load() else { throw WidgetError.text(L.errNotSignedIn) }
        let now = Date().timeIntervalSince1970 * 1000
        if loaded.oauth.expiresAt > 0, now < loaded.oauth.expiresAt - 120_000 {
            return loaded.oauth.accessToken
        }
        let form = "grant_type=refresh_token&refresh_token=\(loaded.oauth.refreshToken)&client_id=\(clientId)"
        let json = "{\"grant_type\":\"refresh_token\",\"refresh_token\":\"\(loaded.oauth.refreshToken)\",\"client_id\":\"\(clientId)\"}"
        for url in tokenUrls {
            for attempt in 0..<2 {
                let data = attempt == 0
                    ? post(url, body: form, contentType: "application/x-www-form-urlencoded")
                    : post(url, body: json, contentType: "application/json")
                guard let d = data,
                      let obj = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any],
                      let access = obj["access_token"] as? String
                else { continue }
                let refresh = (obj["refresh_token"] as? String) ?? loaded.oauth.refreshToken
                let expiresIn = (obj["expires_in"] as? Double) ?? 0
                let new = Oauth(accessToken: access, refreshToken: refresh,
                                expiresAt: Date().timeIntervalSince1970 * 1000 + expiresIn * 1000)
                Credentials.writeBack(new, raw: loaded.raw)
                return access
            }
        }
        return loaded.oauth.accessToken   // last resort
    }

    static func usage() throws -> Usage {
        let tok = try token()
        guard let u = URL(string: usageUrl) else { throw WidgetError.text(L.errBadResponse) }
        var req = URLRequest(url: u, timeoutInterval: 20)
        req.setValue("Bearer \(tok)", forHTTPHeaderField: "Authorization")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        let sem = DispatchSemaphore(value: 0)
        var payload: Data?
        var status = 0
        URLSession.shared.dataTask(with: req) { data, resp, _ in
            status = (resp as? HTTPURLResponse)?.statusCode ?? 0
            payload = data
            sem.signal()
        }.resume()
        _ = sem.wait(timeout: .now() + 25)
        guard status == 200, let d = payload,
              let obj = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any]
        else { throw WidgetError.text(L.errBadResponse + " (HTTP \(status))") }
        func limit(_ key: String) -> Limit? {
            guard let o = obj[key] as? [String: Any] else { return nil }
            var v: Double?
            if let n = o["utilization"] as? Double { v = n }
            if let n = o["utilization"] as? Int { v = Double(n) }
            return Limit(utilization: v, resetsAt: o["resets_at"] as? String)
        }
        return Usage(fiveHour: limit("five_hour"), sevenDay: limit("seven_day"))
    }
}

enum WidgetError: Error { case text(String) }

// ---------- formatting ----------

func pctColor(_ p: Double) -> NSColor {
    if p >= 90 { return NSColor(calibratedRed: 0.88, green: 0.32, blue: 0.32, alpha: 1) }
    if p >= 70 { return NSColor(calibratedRed: 0.91, green: 0.64, blue: 0.24, alpha: 1) }
    return NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1)  // Claude orange
}

func fmtReset(_ iso: String?) -> String {
    guard let iso = iso else { return "" }
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    var date = f.date(from: iso)
    if date == nil {
        f.formatOptions = [.withInternetDateTime]
        date = f.date(from: iso)
    }
    guard let d = date else { return "" }
    let s = d.timeIntervalSinceNow
    if s <= 0 { return "" }
    let minutes = Int(s / 60)
    if minutes >= 24 * 60 { return "\(minutes / (24 * 60))\(L.dayUnit) \((minutes % (24 * 60)) / 60)\(L.hourUnit)" }
    if minutes >= 60 { return String(format: "%d%@%02d", minutes / 60, L.hourUnit, minutes % 60) }
    return "\(max(1, minutes)) \(L.minuteUnit)"
}

// ---------- app ----------

@main
class AppDelegate: NSObject, NSApplicationDelegate {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)   // menu bar only, no Dock icon
        app.run()
    }

    var statusItem: NSStatusItem!
    var usage: Usage?
    var lastUpdate: Date?
    var lastError: String?
    var timer: Timer?

    let agentPlist = NSString(string: "~/Library/LaunchAgents/com.defacedz.claudewidget.plist").expandingTildeInPath

    func applicationDidFinishLaunching(_ note: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "…"
        statusItem.menu = buildMenu()
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in
            self?.refresh()
        }
    }

    func refresh() {
        DispatchQueue.global(qos: .utility).async { [weak self] in
            var u: Usage?
            var err: String?
            do { u = try Api.usage() }
            catch WidgetError.text(let t) { err = t }
            catch { err = error.localizedDescription }
            DispatchQueue.main.async {
                guard let self = self else { return }
                if let u = u { self.usage = u; self.lastUpdate = Date(); self.lastError = nil }
                else { self.lastError = err }
                self.render()
            }
        }
    }

    func render() {
        guard let button = statusItem.button else { return }
        let title = NSMutableAttributedString()
        let font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        func chunk(_ text: String, _ color: NSColor) {
            title.append(NSAttributedString(string: text,
                attributes: [.font: font, .foregroundColor: color]))
        }
        let stale = lastError != nil
        let gray = NSColor.secondaryLabelColor
        if let u = usage, let five = u.fiveHour?.utilization, let seven = u.sevenDay?.utilization {
            chunk("✱ ", stale ? gray : pctColor(max(five, seven)))
            chunk("\(Int(five.rounded()))%", stale ? gray : pctColor(five))
            chunk(" · ", gray)
            chunk("\(Int(seven.rounded()))%", stale ? gray : pctColor(seven))
        } else {
            chunk("✱ —", gray)
        }
        button.attributedTitle = title
        statusItem.menu = buildMenu()
    }

    func buildMenu() -> NSMenu {
        let menu = NSMenu()
        func line(_ text: String, enabled: Bool = false) {
            let item = NSMenuItem(title: text, action: nil, keyEquivalent: "")
            item.isEnabled = enabled
            menu.addItem(item)
        }
        if let u = usage {
            if let five = u.fiveHour?.utilization {
                let reset = fmtReset(u.fiveHour?.resetsAt)
                line("\(L.session5h): \(Int(five.rounded()))%" +
                     (reset.isEmpty ? "" : "  (" + String(format: L.resetsIn, reset) + ")"))
            }
            if let seven = u.sevenDay?.utilization {
                let reset = fmtReset(u.sevenDay?.resetsAt)
                line("\(L.week): \(Int(seven.rounded()))%" +
                     (reset.isEmpty ? "" : "  (" + String(format: L.resetsIn, reset) + ")"))
            }
        }
        if let ts = lastUpdate {
            let f = DateFormatter(); f.dateFormat = "HH:mm"
            line(String(format: L.updated, f.string(from: ts)))
        }
        if let err = lastError { line(String(format: L.offline, err)) }
        menu.addItem(NSMenuItem.separator())

        let refreshItem = NSMenuItem(title: L.menuRefresh, action: #selector(onRefresh), keyEquivalent: "r")
        refreshItem.target = self
        menu.addItem(refreshItem)

        let loginItem = NSMenuItem(title: L.menuStartAtLogin, action: #selector(onToggleLogin), keyEquivalent: "")
        loginItem.target = self
        loginItem.state = FileManager.default.fileExists(atPath: agentPlist) ? .on : .off
        menu.addItem(loginItem)

        menu.addItem(NSMenuItem.separator())
        let quit = NSMenuItem(title: L.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
        return menu
    }

    @objc func onRefresh() { refresh() }

    @objc func onToggleLogin() {
        let fm = FileManager.default
        if fm.fileExists(atPath: agentPlist) {
            try? fm.removeItem(atPath: agentPlist)
        } else {
            let exe = Bundle.main.executablePath ?? CommandLine.arguments[0]
            let plist = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>Label</key><string>com.defacedz.claudewidget</string>
              <key>ProgramArguments</key><array><string>\(exe)</string></array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """
            let dir = NSString(string: "~/Library/LaunchAgents").expandingTildeInPath
            try? fm.createDirectory(atPath: dir, withIntermediateDirectories: true)
            try? plist.write(toFile: agentPlist, atomically: true, encoding: .utf8)
        }
        statusItem.menu = buildMenu()
    }
}
