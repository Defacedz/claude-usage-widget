// ClaudeWidget for macOS - the floating Claude usage gauge panel.
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

// Bump this when publishing: the update check compares it against the same
// line in the repository's mac/ClaudeWidget.swift.
let appVersion = "2026.08.26"
let sourceUrl = "https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/mac/ClaudeWidget.swift"
let webInstallCommand = "curl -fsSL https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/mac/web-install.sh | sh"

// ---------- localization ----------

struct Strings {
    let session5h: String
    let week: String
    let short5h: String        // gauge-row labels, keep them very short
    let short7d: String
    let resetsIn: String       // %@ = duration
    let updated: String        // %@ = HH:mm
    let offline: String        // %@ = error
    let menuRefresh: String
    let menuStartAtLogin: String
    let menuLanguage: String
    let menuQuit: String
    let menuUpdate: String
    let menuMoveBottomLeft: String
    let menuOpacity: String
    let menuHideFullScreen: String
    let menuOpenLog: String
    let menuRestart: String
    let menuLocalDetail: String
    let detailTitle: String
    let detailWrites: String
    let detailAnswers: String
    let detailScanning: String
    let errNotSignedIn: String
    let errBadResponse: String
    let dayUnit: String
    let hourUnit: String
    let minuteUnit: String
}

let catalog: [String: Strings] = [
    "en": Strings(session5h: "5-hour session", week: "Week",
                  short5h: "5h", short7d: "7d",
                  resetsIn: "resets in %@", updated: "updated %@",
                  offline: "Offline: %@",
                  menuRefresh: "Refresh", menuStartAtLogin: "Start at login",
                  menuLanguage: "Language",
                  menuQuit: "Quit", menuUpdate: "Update available",
                  menuMoveBottomLeft: "Move to bottom left",
                  menuOpacity: "Opacity",
                  menuHideFullScreen: "Hide in full-screen apps",
                  menuOpenLog: "Open log", menuRestart: "Restart widget",
                  menuLocalDetail: "Local usage details",
                  detailTitle: "Local usage - new tokens",
                  detailWrites: "cache writes", detailAnswers: "prompts + answers",
                  detailScanning: "scanning transcripts...",
                  errNotSignedIn: "Claude Code is not signed in (run it once)",
                  errBadResponse: "Unreadable API response",
                  dayUnit: "d", hourUnit: "h", minuteUnit: "min"),
    "fr": Strings(session5h: "Session 5 h", week: "Semaine",
                  short5h: "5h", short7d: "7j",
                  resetsIn: "reset dans %@", updated: "maj %@",
                  offline: "Hors ligne : %@",
                  menuRefresh: "Actualiser", menuStartAtLogin: "Lancer à l'ouverture de session",
                  menuLanguage: "Langue",
                  menuQuit: "Quitter", menuUpdate: "Mise à jour disponible",
                  menuMoveBottomLeft: "Replacer en bas à gauche",
                  menuOpacity: "Opacité",
                  menuHideFullScreen: "Masquer en plein écran",
                  menuOpenLog: "Ouvrir le journal", menuRestart: "Redémarrer le widget",
                  menuLocalDetail: "Détail conso locale",
                  detailTitle: "Conso locale - tokens neufs",
                  detailWrites: "écritures de cache", detailAnswers: "messages + réponses",
                  detailScanning: "analyse des conversations...",
                  errNotSignedIn: "Claude Code n'est pas connecté (lance-le une fois)",
                  errBadResponse: "Réponse de l'API illisible",
                  dayUnit: "j", hourUnit: "h", minuteUnit: "min"),
    "es": Strings(session5h: "Sesión de 5 h", week: "Semana",
                  short5h: "5h", short7d: "7d",
                  resetsIn: "se reinicia en %@", updated: "act. %@",
                  offline: "Sin conexión: %@",
                  menuRefresh: "Actualizar", menuStartAtLogin: "Iniciar al abrir sesión",
                  menuLanguage: "Idioma",
                  menuQuit: "Salir", menuUpdate: "Actualización disponible",
                  menuMoveBottomLeft: "Mover abajo a la izquierda",
                  menuOpacity: "Opacidad",
                  menuHideFullScreen: "Ocultar en pantalla completa",
                  menuOpenLog: "Abrir el registro", menuRestart: "Reiniciar el widget",
                  menuLocalDetail: "Detalle de uso local",
                  detailTitle: "Uso local - tokens nuevos",
                  detailWrites: "escrituras de caché", detailAnswers: "mensajes + respuestas",
                  detailScanning: "analizando conversaciones...",
                  errNotSignedIn: "Claude Code no ha iniciado sesión (ejecútalo una vez)",
                  errBadResponse: "Respuesta de la API ilegible",
                  dayUnit: "d", hourUnit: "h", minuteUnit: "min"),
    "de": Strings(session5h: "5-Stunden-Sitzung", week: "Woche",
                  short5h: "5h", short7d: "7T",
                  resetsIn: "zurückgesetzt in %@", updated: "akt. %@",
                  offline: "Offline: %@",
                  menuRefresh: "Aktualisieren", menuStartAtLogin: "Bei Anmeldung starten",
                  menuLanguage: "Sprache",
                  menuQuit: "Beenden", menuUpdate: "Update verfügbar",
                  menuMoveBottomLeft: "Unten links platzieren",
                  menuOpacity: "Deckkraft",
                  menuHideFullScreen: "Bei Vollbild ausblenden",
                  menuOpenLog: "Protokoll öffnen", menuRestart: "Widget neu starten",
                  menuLocalDetail: "Lokale Nutzungsdetails",
                  detailTitle: "Lokale Nutzung - neue Tokens",
                  detailWrites: "Cache-Schreibvorgänge", detailAnswers: "Nachrichten + Antworten",
                  detailScanning: "Analyse der Unterhaltungen...",
                  errNotSignedIn: "Claude Code ist nicht angemeldet (einmal starten)",
                  errBadResponse: "Unlesbare API-Antwort",
                  dayUnit: "T", hourUnit: "h", minuteUnit: "Min")
]

let languageOrder = ["en", "fr", "es", "de"]
let languageNames = ["en": "English", "fr": "Français", "es": "Español", "de": "Deutsch"]

// Saved choice first, then the system language; the menu changes it live.
var currentLangCode: String = {
    let code = UserDefaults.standard.string(forKey: "lang")
        ?? String(Locale.preferredLanguages.first?.prefix(2) ?? "en")
    return catalog[code] != nil ? code : "en"
}()
var L: Strings = catalog[currentLangCode] ?? catalog["en"]!

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

// ---------- log ----------

enum Log {
    static let path = NSString(string: "~/Library/Logs/ClaudeWidget.log").expandingTildeInPath

    static func write(_ msg: String) {
        let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm:ss"
        let line = f.string(from: Date()) + "  " + msg + "
"
        let fm = FileManager.default
        if let attrs = try? fm.attributesOfItem(atPath: path),
           let size = attrs[.size] as? Int, size > 128 * 1024,
           let old = try? String(contentsOfFile: path, encoding: .utf8) {
            let lines = old.split(separator: "
")
            let keep = lines.suffix(lines.count / 2).joined(separator: "
") + "
"
            try? keep.write(toFile: path, atomically: true, encoding: .utf8)
        }
        if let h = FileHandle(forWritingAtPath: path) {
            h.seekToEndOfFile()
            if let d = line.data(using: .utf8) { h.write(d) }
            h.closeFile()
        } else {
            try? line.write(toFile: path, atomically: true, encoding: .utf8)
        }
    }
}

// ---------- local usage scan ----------
// Reads the local Claude Code transcripts (~/.claude/projects/**/*.jsonl)
// and sums the "new" tokens per day: cache writes, prompts and answers.
// Disk reads only - it costs no tokens.

struct LocalDaily {
    var start: Date
    var writes: [Int64]
    var answers: [Int64]
}

enum LocalScan {
    static func jsonString(_ line: String, _ key: String) -> String? {
        guard let r = line.range(of: "\"\(key)\":\"") else { return nil }
        let rest = line[r.upperBound...]
        guard let end = rest.firstIndex(of: "\"") else { return nil }
        return String(rest[..<end])
    }

    static func jsonNumber(_ line: String, _ key: String) -> Int64 {
        guard let r = line.range(of: "\"\(key)\":") else { return 0 }
        var i = r.upperBound
        var digits = ""
        while i < line.endIndex, line[i].isNumber {
            digits.append(line[i]); i = line.index(after: i)
        }
        return Int64(digits) ?? 0
    }

    static func scanDaily(start: Date, progress: (Int, Int) -> Void) -> LocalDaily {
        let cal = Calendar.current
        let startDay = cal.startOfDay(for: start)
        let today = cal.startOfDay(for: Date())
        let n = max(1, (cal.dateComponents([.day], from: startDay, to: today).day ?? 0) + 1)
        var d = LocalDaily(start: startDay,
                           writes: [Int64](repeating: 0, count: n),
                           answers: [Int64](repeating: 0, count: n))
        let root = NSString(string: "~/.claude/projects").expandingTildeInPath
        let fm = FileManager.default
        guard let en = fm.enumerator(atPath: root) else { return d }

        var files: [String] = []
        let minModified = startDay.addingTimeInterval(-86400)
        for case let rel as String in en {
            guard rel.hasSuffix(".jsonl") else { continue }
            let full = root + "/" + rel
            if let attrs = try? fm.attributesOfItem(atPath: full),
               let modified = attrs[.modificationDate] as? Date, modified >= minModified {
                files.append(full)
            }
        }

        var seen = Set<String>()
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let isoPlain = ISO8601DateFormatter()
        isoPlain.formatOptions = [.withInternetDateTime]
        var done = 0
        for file in files {
            autoreleasepool {
                if let content = try? String(contentsOfFile: file, encoding: .utf8) {
                    content.enumerateLines { line, _ in
                        guard line.contains("\"assistant\""), line.contains("\"usage\"") else { return }
                        guard let ts = jsonString(line, "timestamp"),
                              let when = iso.date(from: ts) ?? isoPlain.date(from: ts) else { return }
                        let day = cal.startOfDay(for: when)
                        guard let idx = cal.dateComponents([.day], from: startDay, to: day).day,
                              idx >= 0, idx < n else { return }
                        // one API reply can be written as several lines
                        let key = (jsonString(line, "id") ?? "") + "|" + (jsonString(line, "requestId") ?? "")
                        if key.count > 1, !seen.insert(key).inserted { return }
                        d.writes[idx] += jsonNumber(line, "cache_creation_input_tokens")
                        d.answers[idx] += jsonNumber(line, "input_tokens") + jsonNumber(line, "output_tokens")
                    }
                }
            }
            done += 1
            progress(done, files.count)
        }
        return d
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
// The same floating panel as the Windows version: logo, two gauge rows
// (5-hour session and week) with coloured bars and reset countdowns, always
// on top, draggable, position remembered. Right-click for the menu.

func gaugeColor(_ p: Double) -> NSColor {
    if p >= 90 { return NSColor(calibratedRed: 0.88, green: 0.32, blue: 0.32, alpha: 1) }
    if p >= 70 { return NSColor(calibratedRed: 0.91, green: 0.64, blue: 0.24, alpha: 1) }
    return NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1)  // Claude orange
}

final class GaugeView: NSView {
    weak var app: AppDelegate?

    override var isFlipped: Bool { return true }
    override var mouseDownCanMoveWindow: Bool { return true }

    // The standard AppKit hook: returning the menu here makes right-click
    // work even on a borderless, never-key window - popping it up manually
    // from rightMouseDown does not.
    override func menu(for event: NSEvent) -> NSMenu? {
        return app?.buildMenu()
    }

    func text(_ s: String, _ size: CGFloat, _ color: NSColor, bold: Bool = false) -> NSAttributedString {
        let font = bold ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: .semibold)
                        : NSFont.monospacedDigitSystemFont(ofSize: size, weight: .regular)
        return NSAttributedString(string: s, attributes: [.font: font, .foregroundColor: color])
    }

    func drawText(_ s: NSAttributedString, x: CGFloat, centerY: CGFloat, rightAlignedTo: CGFloat? = nil) {
        let size = s.size()
        let px = rightAlignedTo != nil ? rightAlignedTo! - size.width : x
        s.draw(at: NSPoint(x: px, y: centerY - size.height / 2))
    }

    func drawBar(x: CGFloat, centerY: CGFloat, width: CGFloat, pct: Double, color: NSColor) {
        let track = NSBezierPath(roundedRect: NSRect(x: x, y: centerY - 2, width: width, height: 4),
                                 xRadius: 2, yRadius: 2)
        NSColor(calibratedRed: 0.16, green: 0.18, blue: 0.23, alpha: 1).setFill()
        track.fill()
        let w = max(4, width * CGFloat(min(100, max(0, pct))) / 100)
        let fill = NSBezierPath(roundedRect: NSRect(x: x, y: centerY - 2, width: w, height: 4),
                                xRadius: 2, yRadius: 2)
        color.setFill()
        fill.fill()
    }

    func drawRow(label: String, limit: Limit?, centerY: CGFloat, dim: Bool) {
        guard let limit = limit, let value = limit.utilization else { return }
        let pct = min(100, max(0, value))
        let gray = NSColor(calibratedWhite: 0.55, alpha: 1)
        let color = dim ? gray : gaugeColor(pct)
        drawText(text(label, 8.5, NSColor(calibratedRed: 0.42, green: 0.44, blue: 0.52, alpha: 1), bold: true),
                 x: 26, centerY: centerY)
        drawText(text("\(Int(pct.rounded()))%", 10, color, bold: true), x: 48, centerY: centerY)
        drawBar(x: 84, centerY: centerY, width: 58, pct: pct, color: color)
        let reset = fmtReset(limit.resetsAt)
        if !reset.isEmpty {
            drawText(text(reset, 9, NSColor(calibratedRed: 0.72, green: 0.74, blue: 0.80, alpha: 1)),
                     x: 0, centerY: centerY, rightAlignedTo: bounds.width - 8)
        }
    }

    func drawLogo(cx: CGFloat, cy: CGFloat, size: CGFloat) {
        let lengths: [CGFloat] = [0.50, 0.41, 0.47, 0.42, 0.50, 0.43, 0.46, 0.40, 0.49, 0.42, 0.47, 0.41]
        let half = 7.5 * CGFloat.pi / 180
        NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1).setFill()
        for i in 0..<12 {
            let a = CGFloat(i) * 30 * CGFloat.pi / 180
            let r = size * lengths[i]
            let path = NSBezierPath()
            path.move(to: NSPoint(x: cx, y: cy))
            path.line(to: NSPoint(x: cx + r * cos(a - half), y: cy + r * sin(a - half)))
            path.line(to: NSPoint(x: cx + r * cos(a + half), y: cy + r * sin(a + half)))
            path.close()
            path.fill()
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        let app = self.app
        let stale = app?.lastError != nil
        let veryStale = stale && (app?.lastUpdate.map { Date().timeIntervalSince($0) > 720 } ?? true)

        let bg = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 7, yRadius: 7)
        NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.16, alpha: 0.95).setFill()
        bg.fill()
        let update = app?.updateAvailable ?? false
        let borderColor = veryStale
            ? NSColor(calibratedRed: 0.88, green: 0.32, blue: 0.32, alpha: 0.8)
            : (stale ? NSColor(calibratedRed: 0.91, green: 0.64, blue: 0.24, alpha: 0.6)
                     : (update ? NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 0.8)
                               : NSColor(calibratedWhite: 1, alpha: 0.13)))
        borderColor.setStroke()
        bg.lineWidth = 1
        bg.stroke()

        drawLogo(cx: 14, cy: bounds.height / 2, size: 14)

        if let u = app?.usage {
            drawRow(label: L.short5h, limit: u.fiveHour, centerY: 13, dim: veryStale)
            drawRow(label: L.short7d, limit: u.sevenDay, centerY: 31, dim: veryStale)
        } else {
            let msg = app?.lastError ?? "…"
            drawText(text(msg, 9, NSColor(calibratedRed: 0.61, green: 0.63, blue: 0.71, alpha: 1)),
                     x: 26, centerY: bounds.height / 2)
        }
    }
}

@main
class AppDelegate: NSObject, NSApplicationDelegate {
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)   // floating panel only, no Dock icon
        app.run()
    }

    var window: NSWindow!
    var gauge: GaugeView!
    var usage: Usage?
    var lastUpdate: Date?
    var lastError: String?
    var updateAvailable = false
    var hiddenForFullScreen = false
    var localData: LocalDaily?
    var localDataTs: Date?
    var localScanning = false
    var scanDone = 0
    var scanTotal = 0
    var detailWindow: NSWindow?
    weak var chartView: ChartView?

    var hideFullScreenEnabled: Bool {
        get { return UserDefaults.standard.object(forKey: "hideFS") as? Bool ?? true }
        set { UserDefaults.standard.set(newValue, forKey: "hideFS") }
    }

    let agentPlist = NSString(string: "~/Library/LaunchAgents/com.defacedz.claudewidget.plist").expandingTildeInPath

    func applicationDidFinishLaunching(_ note: Notification) {
        let size = NSSize(width: 204, height: 44)
        var origin = NSPoint(x: 8, y: 8)
        if let screen = NSScreen.main {
            origin = NSPoint(x: screen.visibleFrame.minX + 8, y: screen.visibleFrame.minY + 8)
        }
        window = NSWindow(contentRect: NSRect(origin: origin, size: size),
                          styleMask: [.borderless], backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.level = .statusBar
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.isMovableByWindowBackground = true
        window.setFrameAutosaveName("ClaudeWidget")   // remembers the position

        gauge = GaugeView(frame: NSRect(origin: .zero, size: size))
        gauge.app = self
        window.contentView = gauge
        window.orderFrontRegardless()

        refresh()
        Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in self?.refresh() }
        // redraw every minute so the countdowns stay alive
        Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.gauge.needsDisplay = true
        }
        checkUpdate()
        Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { [weak self] _ in
            self?.checkUpdate()
        }
        let savedOpacity = UserDefaults.standard.double(forKey: "opacity")
        window.alphaValue = (savedOpacity >= 0.2 && savedOpacity <= 1.0) ? CGFloat(savedOpacity) : 1.0
        Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.checkFullScreen() }
        // background scan of the local transcripts: shortly after startup,
        // then every 30 minutes, so the usage chart opens instantly
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) { [weak self] in self?.startLocalScan() }
        Timer.scheduledTimer(withTimeInterval: 1800, repeats: true) { [weak self] _ in self?.startLocalScan() }
    }

    // A game or video running full-screen should not have the widget on top.
    // We compare the frontmost app's top window to the screen size - the
    // same heuristic as the Windows version.
    func frontmostIsFullScreen() -> Bool {
        guard let front = NSWorkspace.shared.frontmostApplication else { return false }
        if front.processIdentifier == ProcessInfo.processInfo.processIdentifier { return false }
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements],
                                                    kCGNullWindowID) as? [[String: Any]] else { return false }
        for info in list {
            guard let pid = info[kCGWindowOwnerPID as String] as? Int32, pid == front.processIdentifier,
                  let layer = info[kCGWindowLayer as String] as? Int, layer == 0,
                  let bounds = info[kCGWindowBounds as String] as? [String: Any],
                  let w = bounds["Width"] as? Double, let h = bounds["Height"] as? Double
            else { continue }
            for screen in NSScreen.screens {
                if w >= Double(screen.frame.width), h >= Double(screen.frame.height) { return true }
            }
            break   // only the frontmost app's top window matters
        }
        return false
    }

    func checkFullScreen() {
        let hide = hideFullScreenEnabled && frontmostIsFullScreen()
        if hide != hiddenForFullScreen {
            hiddenForFullScreen = hide
            if hide { window.orderOut(nil) } else { window.orderFrontRegardless() }
        }
    }

    func startLocalScan() {
        if localScanning { return }
        localScanning = true
        scanDone = 0; scanTotal = 0
        let cal = Calendar.current
        let today = Date()
        var comps = cal.dateComponents([.year, .month], from: today)
        comps.day = 1
        let firstOfMonth = cal.date(from: comps) ?? today
        let start = cal.date(byAdding: .month, value: -1, to: firstOfMonth) ?? firstOfMonth
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let data = LocalScan.scanDaily(start: start) { done, total in
                DispatchQueue.main.async {
                    self?.scanDone = done; self?.scanTotal = total
                    self?.chartView?.needsDisplay = true
                }
            }
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.localScanning = false
                if data.writes.count >= 2 { self.localData = data; self.localDataTs = Date() }
                self.chartView?.needsDisplay = true
            }
        }
    }

    func showLocalDetail() {
        if let existing = detailWindow { existing.close(); detailWindow = nil }
        let view = ChartView(frame: NSRect(x: 0, y: 0, width: 584, height: 264))
        view.app = self
        let win = NSWindow(contentRect: view.frame,
                           styleMask: [.titled, .closable], backing: .buffered, defer: false)
        win.title = L.detailTitle
        win.isReleasedWhenClosed = false
        win.level = .floating
        win.backgroundColor = NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.16, alpha: 1)
        win.contentView = view
        var origin = NSPoint(x: window.frame.minX, y: window.frame.maxY + 12)
        if let screen = NSScreen.main {
            origin.x = min(origin.x, screen.visibleFrame.maxX - view.frame.width - 8)
            origin.y = min(origin.y, screen.visibleFrame.maxY - view.frame.height - 30)
        }
        win.setFrameOrigin(origin)
        detailWindow = win
        chartView = view
        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        if localData == nil || (localDataTs.map { Date().timeIntervalSince($0) > 1800 } ?? true) {
            startLocalScan()
        }
    }

    // Compares the appVersion line of the repository's source with ours.
    // Any failure means "no update" - the check must never break the gauges.
    func checkUpdate() {
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let url = URL(string: sourceUrl) else { return }
            var available = false
            let sem = DispatchSemaphore(value: 0)
            URLSession.shared.dataTask(with: url) { data, _, _ in
                if let d = data, let s = String(data: d, encoding: .utf8),
                   let r = s.range(of: "appVersion = \"") {
                    let rest = s[r.upperBound...]
                    if let end = rest.firstIndex(of: "\"") {
                        available = String(rest[..<end]) != appVersion
                    }
                }
                sem.signal()
            }.resume()
            _ = sem.wait(timeout: .now() + 25)
            DispatchQueue.main.async {
                guard let self = self else { return }
                if self.updateAvailable != available {
                    self.updateAvailable = available
                    if available { Log.write("update available (local " + appVersion + ")") }
                    self.gauge.needsDisplay = true
                }
            }
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
                if let u = u {
                    if self.lastError != nil { Log.write("refresh recovered") }
                    self.usage = u; self.lastUpdate = Date(); self.lastError = nil
                } else {
                    Log.write("refresh failed: " + (err ?? "?"))
                    self.lastError = err
                }
                self.updateTooltip()
                self.gauge.needsDisplay = true
            }
        }
    }

    func updateTooltip() {
        var lines: [String] = []
        if let u = usage {
            if let five = u.fiveHour?.utilization {
                let reset = fmtReset(u.fiveHour?.resetsAt)
                lines.append("\(L.session5h): \(Int(five.rounded()))%" +
                             (reset.isEmpty ? "" : " (" + String(format: L.resetsIn, reset) + ")"))
            }
            if let seven = u.sevenDay?.utilization {
                let reset = fmtReset(u.sevenDay?.resetsAt)
                lines.append("\(L.week): \(Int(seven.rounded()))%" +
                             (reset.isEmpty ? "" : " (" + String(format: L.resetsIn, reset) + ")"))
            }
        }
        if let ts = lastUpdate {
            let f = DateFormatter(); f.dateFormat = "HH:mm"
            lines.append(String(format: L.updated, f.string(from: ts)))
        }
        if let err = lastError { lines.append(String(format: L.offline, err)) }
        gauge.toolTip = lines.joined(separator: "\n")
    }

    func buildMenu() -> NSMenu {
        let menu = NSMenu()
        if updateAvailable {
            let updateItem = NSMenuItem(title: L.menuUpdate, action: #selector(onUpdate), keyEquivalent: "")
            updateItem.target = self
            updateItem.attributedTitle = NSAttributedString(
                string: L.menuUpdate,
                attributes: [.font: NSFont.boldSystemFont(ofSize: 13),
                             .foregroundColor: NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1)])
            menu.addItem(updateItem)
            menu.addItem(NSMenuItem.separator())
        }
        let refreshItem = NSMenuItem(title: L.menuRefresh, action: #selector(onRefresh), keyEquivalent: "")
        refreshItem.target = self
        menu.addItem(refreshItem)

        let detailItem = NSMenuItem(title: L.menuLocalDetail, action: #selector(onLocalDetail), keyEquivalent: "")
        detailItem.target = self
        menu.addItem(detailItem)

        let moveItem = NSMenuItem(title: L.menuMoveBottomLeft, action: #selector(onMoveBottomLeft), keyEquivalent: "")
        moveItem.target = self
        menu.addItem(moveItem)

        let opacityItem = NSMenuItem(title: L.menuOpacity, action: nil, keyEquivalent: "")
        let opacityMenu = NSMenu()
        for value in [1.0, 0.85, 0.7, 0.55, 0.4] {
            let item = NSMenuItem(title: "\(Int(value * 100))%",
                                  action: #selector(onSelectOpacity(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = value
            item.state = abs(Double(window.alphaValue) - value) < 0.01 ? .on : .off
            opacityMenu.addItem(item)
        }
        opacityItem.submenu = opacityMenu
        menu.addItem(opacityItem)

        let langItem = NSMenuItem(title: L.menuLanguage, action: nil, keyEquivalent: "")
        let langMenu = NSMenu()
        for code in languageOrder {
            let item = NSMenuItem(title: languageNames[code] ?? code,
                                  action: #selector(onSelectLanguage(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = code
            item.state = code == currentLangCode ? .on : .off
            langMenu.addItem(item)
        }
        langItem.submenu = langMenu
        menu.addItem(langItem)

        let loginItem = NSMenuItem(title: L.menuStartAtLogin, action: #selector(onToggleLogin), keyEquivalent: "")
        loginItem.target = self
        loginItem.state = FileManager.default.fileExists(atPath: agentPlist) ? .on : .off
        menu.addItem(loginItem)

        let fullScreenItem = NSMenuItem(title: L.menuHideFullScreen, action: #selector(onToggleHideFullScreen), keyEquivalent: "")
        fullScreenItem.target = self
        fullScreenItem.state = hideFullScreenEnabled ? .on : .off
        menu.addItem(fullScreenItem)

        let logItem = NSMenuItem(title: L.menuOpenLog, action: #selector(onOpenLog), keyEquivalent: "")
        logItem.target = self
        menu.addItem(logItem)

        let restartItem = NSMenuItem(title: L.menuRestart, action: #selector(onRestart), keyEquivalent: "")
        restartItem.target = self
        menu.addItem(restartItem)

        menu.addItem(NSMenuItem.separator())
        menu.addItem(NSMenuItem(title: L.menuQuit, action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        return menu
    }

    @objc func onLocalDetail() { showLocalDetail() }

    @objc func onMoveBottomLeft() {
        if let screen = NSScreen.main {
            window.setFrameOrigin(NSPoint(x: screen.visibleFrame.minX + 8,
                                          y: screen.visibleFrame.minY + 8))
        }
    }

    @objc func onSelectOpacity(_ sender: NSMenuItem) {
        guard let value = sender.representedObject as? Double else { return }
        window.alphaValue = CGFloat(value)
        UserDefaults.standard.set(value, forKey: "opacity")
    }

    @objc func onToggleHideFullScreen() {
        hideFullScreenEnabled = !hideFullScreenEnabled
        // unticking it while hidden must bring the widget straight back
        if !hideFullScreenEnabled, hiddenForFullScreen {
            hiddenForFullScreen = false
            window.orderFrontRegardless()
        }
    }

    @objc func onOpenLog() {
        if FileManager.default.fileExists(atPath: Log.path) {
            NSWorkspace.shared.open(URL(fileURLWithPath: Log.path))
        }
    }

    @objc func onRestart() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/open")
        p.arguments = ["-n", Bundle.main.bundlePath]
        try? p.run()
        NSApp.terminate(nil)
    }

    @objc func onSelectLanguage(_ sender: NSMenuItem) {
        guard let code = sender.representedObject as? String, let strings = catalog[code] else { return }
        currentLangCode = code
        L = strings
        UserDefaults.standard.set(code, forKey: "lang")
        updateTooltip()
        gauge.needsDisplay = true
    }

    @objc func onRefresh() { refresh() }

    // web-install.sh downloads the repository, rebuilds from source, kills
    // this instance and starts the new one. The shell survives us dying.
    @objc func onUpdate() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", webInstallCommand]
        try? p.run()
    }

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
    }
}


// ---------- local usage chart ----------
// The same chart as the Windows widget: stacked daily areas of new tokens
// (cache writes below, prompts + answers on top) over the current and
// previous month, with month markers, a peak label, a caption line and a
// hover readout.

final class ChartView: NSView {
    weak var app: AppDelegate?
    var hoverIndex: Int?

    override var isFlipped: Bool { return true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        for area in trackingAreas { removeTrackingArea(area) }
        addTrackingArea(NSTrackingArea(rect: bounds,
                                       options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways],
                                       owner: self, userInfo: nil))
    }

    let chartX: CGFloat = 12, chartY: CGFloat = 26
    let cw: CGFloat = 560, ch: CGFloat = 200
    let ml: CGFloat = 38, mr: CGFloat = 10, mt: CGFloat = 12, mb: CGFloat = 20

    override func mouseMoved(with event: NSEvent) {
        guard let data = app?.localData, data.writes.count >= 2 else { return }
        let p = convert(event.locationInWindow, from: nil)
        let n = data.writes.count
        let plotW = cw - ml - mr
        let i = Int(((p.x - chartX - ml) / (plotW / CGFloat(n - 1))).rounded())
        let newIndex: Int? = (i >= 0 && i < n) ? i : nil
        if newIndex != hoverIndex { hoverIndex = newIndex; needsDisplay = true }
    }

    override func mouseExited(with event: NSEvent) {
        if hoverIndex != nil { hoverIndex = nil; needsDisplay = true }
    }

    func chartLocale() -> Locale { return Locale(identifier: currentLangCode) }

    func mt(_ tokens: Int64) -> String {
        return String(format: "%.1f M", locale: chartLocale(), Double(tokens) / 1e6)
    }

    func put(_ str: String, _ size: CGFloat, _ color: NSColor, x: CGFloat, y: CGFloat,
             bold: Bool = false, centered: Bool = false, rightAligned: Bool = false) {
        let font = bold ? NSFont.monospacedDigitSystemFont(ofSize: size, weight: .semibold)
                        : NSFont.monospacedDigitSystemFont(ofSize: size, weight: .regular)
        let a = NSAttributedString(string: str, attributes: [.font: font, .foregroundColor: color])
        let sz = a.size()
        var px = x
        if centered { px = x - sz.width / 2 }
        if rightAligned { px = x - sz.width }
        a.draw(at: NSPoint(x: px, y: y - sz.height / 2))
    }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.16, alpha: 1).setFill()
        bounds.fill()

        let gray = NSColor(calibratedRed: 0.61, green: 0.63, blue: 0.71, alpha: 1)
        let muted = NSColor(calibratedRed: 0.42, green: 0.44, blue: 0.52, alpha: 1)
        let ink = NSColor(calibratedRed: 0.91, green: 0.92, blue: 0.95, alpha: 1)
        let colWrites = NSColor(calibratedRed: 0.85, green: 0.47, blue: 0.34, alpha: 1)
        let colAnswers = NSColor(calibratedRed: 0.42, green: 0.62, blue: 0.91, alpha: 1)

        // legend + week percentage
        var lx: CGFloat = 14
        for (color, label) in [(colWrites, L.detailWrites), (colAnswers, L.detailAnswers)] {
            color.setFill()
            NSBezierPath(roundedRect: NSRect(x: lx, y: 8, width: 9, height: 9), xRadius: 2, yRadius: 2).fill()
            put(label, 10, gray, x: lx + 14, y: 13)
            let labelWidth = NSAttributedString(string: label,
                attributes: [.font: NSFont.monospacedDigitSystemFont(ofSize: 10, weight: .regular)]).size().width
            lx += 14 + labelWidth + 18
        }
        if let seven = app?.usage?.sevenDay?.utilization {
            put("\(L.week): \(Int(seven.rounded()))%", 10, gaugeColor(seven), x: bounds.width - 14, y: 13,
                bold: true, rightAligned: true)
        }

        guard let data = app?.localData, data.writes.count >= 2 else {
            var msg = L.detailScanning
            if let a = app, a.scanTotal > 0 { msg += "  \(a.scanDone)/\(a.scanTotal)" }
            put(msg, 11, gray, x: chartX + ml, y: chartY + ch / 2)
            return
        }

        let n = data.writes.count
        var tot = [Int64](repeating: 0, count: n)
        var maxTot: Int64 = 1
        for i in 0..<n {
            tot[i] = data.writes[i] + data.answers[i]
            if tot[i] > maxTot { maxTot = tot[i] }
        }
        let steps: [Double] = [2, 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200]
        var stepM = steps.last!
        for candidate in steps where 3 * candidate * 1e6 >= Double(maxTot) { stepM = candidate; break }
        let ymax = 3 * stepM * 1e6

        let plotW = cw - ml - mr, plotH = ch - mt - mb
        func X(_ i: Int) -> CGFloat { return chartX + ml + plotW * CGFloat(i) / CGFloat(n - 1) }
        func Y(_ v: Double) -> CGFloat { return chartY + mt + plotH * CGFloat(1 - v / ymax) }

        // grid
        for g in 0...3 {
            let gy = Y(Double(g) * stepM * 1e6)
            let line = NSBezierPath()
            line.move(to: NSPoint(x: chartX + ml, y: gy))
            line.line(to: NSPoint(x: chartX + cw - mr, y: gy))
            (g == 0 ? NSColor(calibratedRed: 0.23, green: 0.24, blue: 0.32, alpha: 1)
                    : NSColor(calibratedRed: 0.16, green: 0.18, blue: 0.23, alpha: 1)).setStroke()
            line.lineWidth = 1
            line.stroke()
            if g > 0 { put("\(Int(stepM) * g)M", 9, muted, x: chartX + ml - 6, y: gy, rightAligned: true) }
        }

        // month markers and x labels
        let dayFmt = DateFormatter(); dayFmt.locale = chartLocale(); dayFmt.dateFormat = "d MMM"
        let cal = Calendar.current
        var lastLabelX: CGFloat = -100
        for i in 0..<n {
            guard let date = cal.date(byAdding: .day, value: i, to: data.start) else { continue }
            let day = cal.component(.day, from: date)
            if day == 1, i > 0 {
                let line = NSBezierPath()
                line.move(to: NSPoint(x: X(i), y: chartY + mt))
                line.line(to: NSPoint(x: X(i), y: chartY + ch - mb))
                NSColor(calibratedRed: 0.29, green: 0.31, blue: 0.39, alpha: 1).setStroke()
                line.setLineDash([3, 4], count: 2, phase: 0)
                line.lineWidth = 1
                line.stroke()
            }
            if day == 1 || day == 15 || i == n - 1 {
                let labelX = X(i)
                if labelX - lastLabelX >= 34 {
                    put(dayFmt.string(from: date), 9, muted,
                        x: min(labelX, chartX + cw - 30), y: chartY + ch - mb + 10, centered: true)
                    lastLabelX = labelX
                }
            }
        }

        // stacked areas: writes below, answers on top
        let writesPath = NSBezierPath()
        writesPath.move(to: NSPoint(x: X(0), y: Y(Double(data.writes[0]))))
        for i in 1..<n { writesPath.line(to: NSPoint(x: X(i), y: Y(Double(data.writes[i])))) }
        for i in stride(from: n - 1, through: 0, by: -1) { writesPath.line(to: NSPoint(x: X(i), y: Y(0))) }
        writesPath.close()
        let answersPath = NSBezierPath()
        answersPath.move(to: NSPoint(x: X(0), y: Y(Double(tot[0]))))
        for i in 1..<n { answersPath.line(to: NSPoint(x: X(i), y: Y(Double(tot[i])))) }
        for i in stride(from: n - 1, through: 0, by: -1) { answersPath.line(to: NSPoint(x: X(i), y: Y(Double(data.writes[i])))) }
        answersPath.close()
        colAnswers.setFill(); answersPath.fill()
        colWrites.setFill(); writesPath.fill()
        let seam = NSBezierPath()
        seam.move(to: NSPoint(x: X(0), y: Y(Double(data.writes[0]))))
        for i in 1..<n { seam.line(to: NSPoint(x: X(i), y: Y(Double(data.writes[i])))) }
        NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.16, alpha: 1).setStroke()
        seam.lineWidth = 1.2
        seam.stroke()

        // peak label
        var peak = 0
        for i in 1..<n where tot[i] > tot[peak] { peak = i }
        if tot[peak] > 0 {
            put(mt(tot[peak]), 9.5, ink,
                x: max(chartX + ml + 16, min(X(peak), chartX + cw - 30)),
                y: max(chartY + 6, Y(Double(tot[peak])) - 10), bold: true, centered: true)
        }

        // caption: hover readout, or the month totals + refresh time
        let monthFmt = DateFormatter(); monthFmt.locale = chartLocale(); monthFmt.dateFormat = "MMMM"
        let hoverFmt = DateFormatter(); hoverFmt.locale = chartLocale(); hoverFmt.dateFormat = "EEE d MMM"
        var caption = ""
        if let i = hoverIndex, i < n, let date = cal.date(byAdding: .day, value: i, to: data.start) {
            caption = hoverFmt.string(from: date) + ": " + mt(tot[i])
                + "  (" + L.detailWrites + " " + mt(data.writes[i])
                + " · " + L.detailAnswers + " " + mt(data.answers[i]) + ")"
        } else {
            let thisMonth = cal.component(.month, from: Date())
            var cur: Int64 = 0, prev: Int64 = 0
            for i in 0..<n {
                if let date = cal.date(byAdding: .day, value: i, to: data.start),
                   cal.component(.month, from: date) == thisMonth { cur += tot[i] } else { prev += tot[i] }
            }
            caption = monthFmt.string(from: Date()) + ": " + mt(cur)
                + "   ·   " + monthFmt.string(from: data.start) + ": " + mt(prev)
            if let ts = app?.localDataTs {
                let f = DateFormatter(); f.dateFormat = "HH:mm"
                caption += "   ·   " + String(format: L.updated, f.string(from: ts))
            }
        }
        put(caption, 10.5, gray, x: 14, y: bounds.height - 14)
    }
}

