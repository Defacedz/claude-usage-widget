// ClaudeWidget - Claude usage gauge, pinned above the Windows taskbar.
// Built locally by Installer.ps1 with csc.exe (.NET Framework 4.x, C# 5).
// The uiAccess=true manifest puts the window in the band reserved for
// accessibility tools, so the taskbar never draws over it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeWidgetApp
{
    // ---------- JSON models ----------
    [DataContract] public class CredFile { [DataMember] public Oauth claudeAiOauth; }
    [DataContract] public class Oauth
    {
        [DataMember] public string accessToken;
        [DataMember] public string refreshToken;
        [DataMember] public long expiresAt;
    }
    [DataContract] public class TokenResp
    {
        [DataMember] public string access_token;
        [DataMember] public string refresh_token;
        [DataMember] public long expires_in;
    }
    [DataContract] public class UpdateResult { [DataMember] public string version_to; }
    [DataContract] public class Limit
    {
        [DataMember] public double? utilization;
        [DataMember] public string resets_at;
    }
    [DataContract] public class Usage
    {
        [DataMember] public Limit five_hour;
        [DataMember] public Limit seven_day;
        [DataMember] public Limit seven_day_opus;
        [DataMember] public Limit seven_day_sonnet;
    }
    [DataContract] public class Config
    {
        [DataMember] public double X = -99999;
        [DataMember] public double Y = -99999;
        [DataMember] public double Opacity = 1.0;
        [DataMember] public string Lang = "en";
        // Nullable on purpose: DataContractJsonSerializer skips field
        // initializers, so a plain bool absent from an older config file would
        // read back as false - the opposite of the intended default.
        [DataMember] public bool? HideFullScreen;
        // "dark" (the original) or "ivory". Same reason it is a string rather
        // than an enum: a config file written by an older build has no value
        // at all, and Theme.Use() maps anything unknown back to dark.
        [DataMember] public string Theme;
        // Start-with-Windows is on by default. Null (older config, or first
        // run) means "never decided": the startup shortcut gets created.
        // Only an explicit false - the user unticked the menu - blocks it.
        [DataMember] public bool? AutoStart;
    }

    // ---------- themes ----------
    // Two skins for the same widget. Dark is the original; Ivory is built on
    // Anthropic's own palette - Ivory Medium #F0EEE6 is the claude.ai
    // background - so the widget sits on a light Windows taskbar instead of
    // punching a black hole in it. The three gauge colours (orange, amber,
    // red) and the two chart colours are deliberately identical in both skins:
    // they carry meaning, not decoration.
    public class Theme
    {
        public string Name;

        public string Panel;        // widget and menu background
        public string Border;       // widget border at rest
        public string MenuBorder;
        public string Ink;          // menu text, window title
        public string Dim;          // "5h" / "7d" labels, disabled menu items
        public string Mid;          // loading and offline message, legends
        public string Bright;       // reset countdown
        public string Track;        // empty part of a gauge
        public string Sep;          // menu separator
        public string Highlight;    // hovered menu row

        public string WinBg;        // usage window body
        public string WinBorder;
        public string WinBar;       // the title bar we draw ourselves
        public string WinBarLine;
        public string WinBtn;       // close button at rest
        public string Grid;         // chart gridlines
        public string GridBase;     // the zero line, slightly stronger
        public string Axis;         // day labels
        public string MonthLab;     // month names under the axis
        public string MonthRule;    // vertical line between two months
        public string HoverCol;     // column behind the hovered day

        public static readonly Theme Dark = new Theme
        {
            Name = "dark",
            Panel = "#F21E2029", Border = "#22FFFFFF", MenuBorder = "#33FFFFFF",
            Ink = "#E8EAF2", Dim = "#6C7086", Mid = "#9BA0B5", Bright = "#B8BCCB",
            Track = "#2A2D3A", Sep = "#26FFFFFF", Highlight = "#2EDA7756",
            WinBg = "#1E2029", WinBorder = "#1FFFFFFF",
            WinBar = "#191B23", WinBarLine = "#14FFFFFF", WinBtn = "#0FFFFFFF",
            Grid = "#2A2D3A", GridBase = "#3A3E52", Axis = "#6C7086",
            MonthLab = "#7A7F94", MonthRule = "#343849", HoverCol = "#0EFFFFFF"
        };

        public static readonly Theme Ivory = new Theme
        {
            Name = "ivory",
            Panel = "#F2F0EEE6", Border = "#33191919", MenuBorder = "#33191919",
            Ink = "#191919", Dim = "#91918D", Mid = "#6B6A64", Bright = "#40403E",
            Track = "#E3DACC", Sep = "#26191919", Highlight = "#30DA7756",
            WinBg = "#FAF9F5", WinBorder = "#26191919",
            WinBar = "#F0EEE6", WinBarLine = "#1A191919", WinBtn = "#10191919",
            Grid = "#E8E5DA", GridBase = "#CFCCBE", Axis = "#91918D",
            MonthLab = "#A9A79F", MonthRule = "#DEDBCE", HoverCol = "#0D191919"
        };

        public static readonly Theme[] All = { Dark, Ivory };
        public static Theme Current = Dark;

        // Unknown or missing name falls back to the original dark skin.
        public static void Use(string name)
        {
            foreach (Theme t in All)
                if (t.Name == name) { Current = t; return; }
            Current = Dark;
        }
    }

    // ---------- localization ----------
    // To add a language: copy one of the blocks in I18n, translate the values
    // and append it to Catalog. Nothing else to touch - the language menu and
    // the config file are both driven by Catalog.
    public class Strings
    {
        public string Code;             // ISO 639-1, stored in config.json
        public string Native;           // language name written in that language

        public string Loading;
        public string Offline;          // {0} = error message
        public string FrozenFor;        // {0} = age, {1} = error message
        public string Updated;          // {0} = HH:mm
        public string ResetsIn;         // {0} = duration
        public string Colon;            // French puts a space before the colon

        public string Short5h, Short7d; // gauge labels, keep them very short
        public string Session5h, Week;  // tooltip labels
        public string DayUnit, HourUnit, MinuteUnit;

        public string MenuRefresh, MenuMoveBottomLeft, MenuOpacity,
                      MenuStartWithWindows, MenuHideFullScreen, MenuOpenLog,
                      MenuRestart, MenuLanguage, MenuQuit;

        public string MenuTheme, ThemeDark, ThemeIvory;   // appearance submenu

        public string MenuUpdate;       // shown in orange when the repository is ahead
        public string MenuLocalDetail;  // context-menu entry for the local-usage window
        public string DetailTitle;
        public string DetailToday;
        public string DetailWeek;       // {0} = window start, day + HH:mm
        public string Detail7d;         // fallback title when the API reset time is unknown
        public string DetailWrites, DetailAnswers, DetailProjects;
        public string DetailScanning;
        public string DetailNote;

        public string ErrNotSignedIn, ErrBadResponse;

        public string SourceFeed;       // tooltip line when the numbers came from the feed
        public string ErrRateLimited;   // friendlier than the raw HTTP 429 message
        public string ErrExpired;       // friendlier than the raw HTTP 401/403 message
        public string FeedHint;         // appended to the offline message on HTTP 429/401
        public string FeedHintBusy;     // same spot, when a foreign statusline blocks the feed
    }

    public static class I18n
    {
        public static readonly Strings[] Catalog = { English(), French(), Spanish(), German() };
        public static Strings T = Catalog[0];   // English is the default

        // Unknown or missing code falls back to the first entry.
        public static void Use(string code)
        {
            foreach (Strings s in Catalog)
                if (s.Code == code) { T = s; return; }
            T = Catalog[0];
        }

        static Strings English()
        {
            return new Strings
            {
                Code = "en", Native = "English",
                Loading = "loading...",
                Offline = "Offline: {0}",
                FrozenFor = "Frozen for {0}: {1}",
                Updated = "updated {0}",
                ResetsIn = "resets in {0}",
                Colon = ": ",
                Short5h = "5h", Short7d = "7d",
                Session5h = "5-hour session", Week = "Week",
                DayUnit = "d", HourUnit = "h", MinuteUnit = "min",
                MenuRefresh = "Refresh",
                MenuMoveBottomLeft = "Move to bottom left",
                MenuOpacity = "Opacity",
                MenuStartWithWindows = "Start with Windows",
                MenuHideFullScreen = "Hide in full-screen apps",
                MenuOpenLog = "Open log",
                MenuRestart = "Restart widget",
                MenuLanguage = "Language",
                MenuTheme = "Theme", ThemeDark = "Dark", ThemeIvory = "Ivory",
                MenuQuit = "Quit",
                MenuUpdate = "Update available",
                MenuLocalDetail = "Local usage details",
                DetailTitle = "Local usage - new tokens",
                DetailToday = "Today",
                DetailWeek = "Quota week (since {0})",
                Detail7d = "Last 7 days",
                DetailWrites = "cache writes",
                DetailAnswers = "prompts + answers",
                DetailProjects = "Top projects this week",
                DetailScanning = "scanning transcripts...",
                DetailNote = "Counted from the local Claude Code transcripts: sent + produced + cache-written tokens. Context re-reads are excluded - they barely count toward the limit.",
                ErrNotSignedIn = "Claude Code is not signed in (run it once)",
                ErrBadResponse = "Unreadable API response",
                SourceFeed = "source: Claude Code (local feed)",
                ErrRateLimited = "API rate limited (429)",
                ErrExpired = "Session expired",
                FeedHint = "Restart Claude Code",
                FeedHintBusy = "Statusline taken (see log)"
            };
        }

        static Strings French()
        {
            return new Strings
            {
                Code = "fr", Native = "Français",
                Loading = "chargement...",
                Offline = "Hors ligne : {0}",
                FrozenFor = "Figé depuis {0} : {1}",
                Updated = "maj {0}",
                ResetsIn = "reset dans {0}",
                Colon = " : ",
                Short5h = "5h", Short7d = "7j",
                Session5h = "Session 5 h", Week = "Semaine",
                DayUnit = "j", HourUnit = "h", MinuteUnit = "min",
                MenuRefresh = "Actualiser",
                MenuMoveBottomLeft = "Replacer en bas à gauche",
                MenuOpacity = "Opacité",
                MenuStartWithWindows = "Lancer au démarrage de Windows",
                MenuHideFullScreen = "Masquer en plein écran",
                MenuOpenLog = "Ouvrir le journal",
                MenuRestart = "Redémarrer le widget",
                MenuLanguage = "Langue",
                MenuTheme = "Thème", ThemeDark = "Sombre", ThemeIvory = "Ivoire",
                MenuQuit = "Quitter",
                MenuUpdate = "Mise à jour disponible",
                MenuLocalDetail = "Détail conso locale",
                DetailTitle = "Conso locale - tokens neufs",
                DetailToday = "Aujourd'hui",
                DetailWeek = "Semaine de quota (depuis {0})",
                Detail7d = "7 derniers jours",
                DetailWrites = "écritures de cache",
                DetailAnswers = "messages + réponses",
                DetailProjects = "Projets les plus gourmands",
                DetailScanning = "analyse des conversations...",
                DetailNote = "Compté depuis les conversations locales de Claude Code : tokens envoyés + produits + écrits en cache. Les relectures de contexte sont exclues - elles ne pèsent presque pas sur la limite.",
                ErrNotSignedIn = "Claude Code n'est pas connecté (lance-le une fois)",
                ErrBadResponse = "Réponse de l'API illisible",
                SourceFeed = "source : Claude Code (flux local)",
                ErrRateLimited = "API limitée (429)",
                ErrExpired = "Session expirée",
                FeedHint = "Relance Claude Code",
                FeedHintBusy = "Statusline occupée (voir journal)"
            };
        }

        static Strings Spanish()
        {
            return new Strings
            {
                Code = "es", Native = "Español",
                Loading = "cargando...",
                Offline = "Sin conexión: {0}",
                FrozenFor = "Congelado desde hace {0}: {1}",
                Updated = "act. {0}",
                ResetsIn = "se reinicia en {0}",
                Colon = ": ",
                Short5h = "5h", Short7d = "7d",
                Session5h = "Sesión de 5 h", Week = "Semana",
                DayUnit = "d", HourUnit = "h", MinuteUnit = "min",
                MenuRefresh = "Actualizar",
                MenuMoveBottomLeft = "Mover abajo a la izquierda",
                MenuOpacity = "Opacidad",
                MenuStartWithWindows = "Iniciar con Windows",
                MenuHideFullScreen = "Ocultar en pantalla completa",
                MenuOpenLog = "Abrir el registro",
                MenuRestart = "Reiniciar el widget",
                MenuLanguage = "Idioma",
                MenuTheme = "Tema", ThemeDark = "Oscuro", ThemeIvory = "Marfil",
                MenuQuit = "Salir",
                MenuUpdate = "Actualización disponible",
                MenuLocalDetail = "Detalle de uso local",
                DetailTitle = "Uso local - tokens nuevos",
                DetailToday = "Hoy",
                DetailWeek = "Semana de cuota (desde {0})",
                Detail7d = "Últimos 7 días",
                DetailWrites = "escrituras de caché",
                DetailAnswers = "mensajes + respuestas",
                DetailProjects = "Proyectos que más consumen",
                DetailScanning = "analizando conversaciones...",
                DetailNote = "Contado desde las conversaciones locales de Claude Code: tokens enviados + producidos + escritos en caché. Las relecturas de contexto quedan fuera - apenas cuentan para el límite.",
                ErrNotSignedIn = "Claude Code no ha iniciado sesión (ejecútalo una vez)",
                ErrBadResponse = "Respuesta de la API ilegible",
                SourceFeed = "fuente: Claude Code (local)",
                ErrRateLimited = "API limitada (429)",
                ErrExpired = "Sesión caducada",
                FeedHint = "Reinicie Claude Code",
                FeedHintBusy = "Statusline ocupada (ver registro)"
            };
        }

        static Strings German()
        {
            return new Strings
            {
                Code = "de", Native = "Deutsch",
                Loading = "lädt...",
                Offline = "Offline: {0}",
                FrozenFor = "Eingefroren seit {0}: {1}",
                Updated = "akt. {0}",
                ResetsIn = "zurückgesetzt in {0}",
                Colon = ": ",
                Short5h = "5h", Short7d = "7T",
                Session5h = "5-Stunden-Sitzung", Week = "Woche",
                DayUnit = "T", HourUnit = "h", MinuteUnit = "Min",
                MenuRefresh = "Aktualisieren",
                MenuMoveBottomLeft = "Unten links platzieren",
                MenuOpacity = "Deckkraft",
                MenuStartWithWindows = "Mit Windows starten",
                MenuHideFullScreen = "Bei Vollbild ausblenden",
                MenuOpenLog = "Protokoll öffnen",
                MenuRestart = "Widget neu starten",
                MenuLanguage = "Sprache",
                MenuTheme = "Design", ThemeDark = "Dunkel", ThemeIvory = "Elfenbein",
                MenuQuit = "Beenden",
                MenuUpdate = "Update verfügbar",
                MenuLocalDetail = "Lokale Nutzungsdetails",
                DetailTitle = "Lokale Nutzung - neue Tokens",
                DetailToday = "Heute",
                DetailWeek = "Kontingentwoche (seit {0})",
                Detail7d = "Letzte 7 Tage",
                DetailWrites = "Cache-Schreibvorgänge",
                DetailAnswers = "Nachrichten + Antworten",
                DetailProjects = "Größte Projekte",
                DetailScanning = "Analyse der Unterhaltungen...",
                DetailNote = "Gezählt aus den lokalen Claude-Code-Unterhaltungen: gesendete + erzeugte + in den Cache geschriebene Tokens. Erneut gelesener Kontext zählt nicht - er wiegt kaum auf dem Limit.",
                ErrNotSignedIn = "Claude Code ist nicht angemeldet (einmal starten)",
                ErrBadResponse = "Unlesbare API-Antwort",
                SourceFeed = "Quelle: Claude Code (lokal)",
                ErrRateLimited = "API begrenzt (429)",
                ErrExpired = "Sitzung abgelaufen",
                FeedHint = "Claude Code neu starten",
                FeedHintBusy = "Statusline belegt (s. Protokoll)"
            };
        }
    }

    public static class Json
    {
        public static T Read<T>(string text) where T : class
        {
            try
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                    return (T)ser.ReadObject(ms);
            }
            catch { return null; }
        }
        public static string Write<T>(T obj)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    // ---------- Claude API ----------
    public static class Api
    {
        // Bump this when publishing: the update check compares it against the
        // same line in the repository's ClaudeWidget.cs.
        public const string Version = "2026.09.08";
        const string SourceUrl = "https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/ClaudeWidget.cs";
        public const string ArchiveUrl = "https://github.com/Defacedz/claude-usage-widget/archive/refs/heads/main.zip";

        // Public OAuth client id of Claude Code - an identifier, not a secret.
        const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        static readonly string[] TokenUrls = {
            "https://platform.claude.com/v1/oauth/token",
            "https://console.anthropic.com/v1/oauth/token"
        };
        const string Beta = "oauth-2025-04-20";

        static string CredPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude\\.credentials.json"); }
        }
        static string VersionPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude\\.last-update-result.json"); }
        }

        // The endpoint buckets unknown User-Agents into a much harsher rate
        // limit - that is what the 2026-08 "429 for everybody" actually was,
        // and it hit the token hosts too (refreshes died as ProtocolError).
        // It wants "claude-code/<version>"; the real version is read from
        // Claude Code's own update marker. Credit: the vibespan fork found it.
        static string _userAgent;
        static string UserAgent
        {
            get
            {
                if (_userAgent != null) return _userAgent;
                string ver = null;
                try
                {
                    if (File.Exists(VersionPath))
                    {
                        var u = Json.Read<UpdateResult>(File.ReadAllText(VersionPath));
                        if (u != null) ver = u.version_to;
                    }
                }
                catch { }
                _userAgent = "claude-code/" + (string.IsNullOrEmpty(ver) ? "2.1.0" : ver);
                return _userAgent;
            }
        }
        static string CacheDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeWidget"); }
        }
        static string CachePath { get { return System.IO.Path.Combine(CacheDir, "tokens.json"); } }
        static string LogPath { get { return System.IO.Path.Combine(CacheDir, "log.txt"); } }

        static long NowMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        // Minimal log, English only: a frozen gauge is useless without a reason,
        // and a log people can paste into an issue must not need translating.
        // It never records tokens.
        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 128 * 1024)
                {
                    string[] lines = File.ReadAllLines(LogPath);
                    var keep = new string[lines.Length / 2];
                    Array.Copy(lines, lines.Length - keep.Length, keep, 0, keep.Length);
                    File.WriteAllLines(LogPath, keep);
                }
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        // ----- address family -----
        // When a router advertises an IPv6 prefix it does not actually route
        // (seen on 2026-07-26), HttpWebRequest sticks to the AAAA record and
        // sits in SYN_SENT until the timeout: no refresh ever completes. So we
        // ask for IPv4, and switch back automatically if IPv6 is the only path.
        static bool _ipv4 = true;

        static IPEndPoint BindIpv4(ServicePoint sp, IPEndPoint remote, int retry)
        {
            if (remote.AddressFamily == AddressFamily.InterNetwork)
                return new IPEndPoint(IPAddress.Any, 0);
            throw new InvalidOperationException("IPv6 address skipped");
        }

        static HttpWebRequest NewRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;   // otherwise a stalled stream can hang for 5 min
            req.UserAgent = UserAgent;
            try
            {
                req.ServicePoint.BindIPEndPointDelegate =
                    _ipv4 ? new BindIPEndPoint(BindIpv4) : null;
            }
            catch { }
            return req;
        }

        // Routing failure, not an application-level refusal: retrying in the
        // other address family is worth a shot.
        static bool IsNetworkFailure(WebException we)
        {
            return we.Status == WebExceptionStatus.Timeout
                || we.Status == WebExceptionStatus.ConnectFailure
                || we.Status == WebExceptionStatus.NameResolutionFailure;
        }

        // ----- writing back to Claude Code's credentials file -----
        // The OAuth server rotates refresh tokens: using one invalidates the
        // previous one. While we kept the new token to ourselves, Claude Code
        // was left holding a dead token and its session expired within hours
        // (observed 2026-07-28/29).
        // That file carries fields our model ignores (scopes, subscriptionType,
        // rateLimitTier...) which a re-serialization would wipe, so we patch
        // the values in place instead.
        static int ValueStart(string json, string key)
        {
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return -1;
            int p = i + needle.Length;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            return p;
        }

        static string SetJsonString(string json, string key, string value)
        {
            if (json == null || string.IsNullOrEmpty(value)) return null;
            int p = ValueStart(json, key);
            if (p < 0 || p >= json.Length || json[p] != '"') return null;
            int end = json.IndexOf('"', p + 1);
            if (end < 0) return null;
            return json.Substring(0, p + 1) + value + json.Substring(end);
        }

        static string SetJsonNumber(string json, string key, long value)
        {
            if (json == null) return null;
            int p = ValueStart(json, key);
            if (p < 0) return null;
            int end = p;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-')) end++;
            if (end == p) return null;
            return json.Substring(0, p) + value.ToString(CultureInfo.InvariantCulture) + json.Substring(end);
        }

        static void WriteBackCredentials(Oauth no)
        {
            try
            {
                if (!File.Exists(CredPath)) return;
                string json = File.ReadAllText(CredPath);
                string upd = SetJsonString(json, "accessToken", no.accessToken);
                upd = SetJsonString(upd, "refreshToken", no.refreshToken);
                upd = SetJsonNumber(upd, "expiresAt", no.expiresAt);
                if (upd == null) { Log("credentials write-back skipped: unexpected format"); return; }

                // Atomic write: a truncated file would sign Claude Code out.
                // No BOM and no trailing newline, matching the original file.
                string tmp = CredPath + ".widget.tmp";
                string bak = CredPath + ".widget.bak";
                File.WriteAllText(tmp, upd, new UTF8Encoding(false));
                try
                {
                    // Replace() swaps in one step; the backup path is mandatory
                    // here (passing null makes the call fail) and removed at once.
                    File.Replace(tmp, CredPath, bak);
                    try { File.Delete(bak); } catch { }
                }
                catch { File.Copy(tmp, CredPath, true); try { File.Delete(tmp); } catch { } }
                Log("Claude Code credentials updated (refresh token rotation)");
            }
            catch (Exception e) { Log("credentials write-back failed: " + e.Message); }
        }

        static Oauth LoadBest()
        {
            Oauth cc = null, cache = null;
            try
            {
                if (File.Exists(CredPath))
                {
                    var f = Json.Read<CredFile>(File.ReadAllText(CredPath));
                    if (f != null) cc = f.claudeAiOauth;
                }
            }
            catch { }
            try
            {
                if (File.Exists(CachePath)) cache = Json.Read<Oauth>(File.ReadAllText(CachePath));
            }
            catch { }
            if (cc == null) return cache;
            if (cache == null) return cc;
            return cache.expiresAt > cc.expiresAt ? cache : cc;
        }

        static string HttpPost(string url, string body, string contentType)
        {
            var req = NewRequest(url);
            req.Method = "POST";
            req.ContentType = contentType;
            var data = Encoding.UTF8.GetBytes(body);
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }

        static string GetToken()
        {
            var o = LoadBest();
            if (o == null || string.IsNullOrEmpty(o.accessToken))
                throw new Exception(I18n.T.ErrNotSignedIn);
            if (o.expiresAt > 0 && NowMs() < o.expiresAt - 120000) return o.accessToken;

            // Refresh, then store the result in our cache AND in Claude Code's
            // file (see WriteBackCredentials). Two OAuth hosts x two body
            // formats are tried: the API moved hosts in 2026.
            string jsonBody = "{\"grant_type\":\"refresh_token\",\"refresh_token\":\"" + o.refreshToken +
                              "\",\"client_id\":\"" + ClientId + "\"}";
            string formBody = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(o.refreshToken) +
                              "&client_id=" + ClientId;
            foreach (string url in TokenUrls)
            {
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        string resp = (i == 0)
                            ? HttpPost(url, formBody, "application/x-www-form-urlencoded")
                            : HttpPost(url, jsonBody, "application/json");
                        var tr = Json.Read<TokenResp>(resp);
                        if (tr != null && !string.IsNullOrEmpty(tr.access_token))
                        {
                            var no = new Oauth
                            {
                                accessToken = tr.access_token,
                                refreshToken = string.IsNullOrEmpty(tr.refresh_token) ? o.refreshToken : tr.refresh_token,
                                expiresAt = NowMs() + tr.expires_in * 1000
                            };
                            Directory.CreateDirectory(CacheDir);
                            File.WriteAllText(CachePath, Json.Write(no));
                            WriteBackCredentials(no);
                            Log("token refresh OK via " + url);
                            return no.accessToken;
                        }
                    }
                    catch (WebException we)
                    {
                        // without Close() the connection stays held by the ServicePoint
                        if (we.Response != null) we.Response.Close();
                        Log("token refresh failed (" + url + "): " + we.Status);
                    }
                    catch (Exception e) { Log("token refresh failed (" + url + "): " + e.Message); }
                }
            }
            return o.accessToken; // last resort
        }

        // Call from a worker thread. Any failure means "no update": the check
        // must never break a widget that only wants to show gauges.
        public static bool UpdateAvailable()
        {
            try
            {
                // raw.githubusercontent.com sits behind a CDN that serves a
                // copy for a few minutes. A unique query string gives it a
                // cache key it has never seen, and NoCacheNoStore keeps the
                // local WinINET cache out of the way as well.
                var req = NewRequest(SourceUrl + "?t=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                req.Method = "GET";
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                    System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    Match m = Regex.Match(r.ReadToEnd(), "Version = \"([^\"]+)\"");
                    if (!m.Success) return false;
                    string remote = m.Groups[1].Value;
                    bool avail = IsNewer(remote, Version);
                    if (avail) Log("update available: " + remote + " (local " + Version + ")");
                    return avail;
                }
            }
            catch { return false; }
        }

        // Strictly newer, never merely different. Versions are yyyy.MM.dd, so
        // an ordinal comparison is chronological. Comparing with != meant that
        // a stale answer from the CDN - an older version than the one already
        // installed - lit the update border and never turned it off again.
        static bool IsNewer(string remote, string local)
        {
            return string.CompareOrdinal(remote, local) > 0;
        }

        // Plain file download through the same IPv4-aware plumbing as every
        // other request. Used by the updater; follows GitHub's redirect to
        // codeload automatically.
        public static void DownloadFile(string url, string path)
        {
            var req = NewRequest(url);
            req.Method = "GET";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(path))
            {
                var buf = new byte[81920];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
            }
        }

        public static Usage GetUsage()
        {
            try { return GetUsageOnce(); }
            catch (WebException we)
            {
                var hr = we.Response as HttpWebResponse;
                int code = hr == null ? 0 : (int)hr.StatusCode;
                if (hr != null) hr.Close();

                // Token rejected -> drop our cache (it may be stale) and retry
                // once with Claude Code's own credentials.
                if (code == 401 || code == 403)
                {
                    Log("usage rejected (HTTP " + code + "), clearing cache and retrying");
                    try { File.Delete(CachePath); } catch { }
                    return GetUsageOnce();
                }

                // Unreachable route: retry in the other address family, and keep
                // the switch only if it actually helped.
                if (IsNetworkFailure(we))
                {
                    bool previous = _ipv4;
                    _ipv4 = !previous;
                    try
                    {
                        var u = GetUsageOnce();
                        Log("network failure (" + we.Status + ") -> switched to " + (_ipv4 ? "IPv4" : "auto"));
                        return u;
                    }
                    catch { _ipv4 = previous; throw; }
                }
                throw;
            }
        }

        static Usage GetUsageOnce()
        {
            string tok = GetToken();
            var req = NewRequest(UsageUrl);
            req.Method = "GET";
            req.Headers["Authorization"] = "Bearer " + tok;
            req.Headers["anthropic-beta"] = Beta;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
            {
                var u = Json.Read<Usage>(r.ReadToEnd());
                if (u == null) throw new Exception(I18n.T.ErrBadResponse);
                return u;
            }
        }
    }

    // ---------- local feed (Claude Code statusline) ----------
    // Claude Code pushes a JSON blob to the configured statusLine command on
    // every turn; for Claude.ai subscribers it carries the same five_hour /
    // seven_day numbers as the usage endpoint - pushed locally, no HTTP, no
    // rate limit. Since the endpoint started answering 429 (2026-08), this is
    // the primary source; the API poll is the fallback for when Claude Code
    // is closed.
    // The widget wires itself automatically at startup: it writes the feed
    // helper as the statusLine command of ~/.claude/settings.json. The one
    // thing it never does is overwrite a statusline configured by another
    // tool. A pristine backup is kept as settings.json.widget.bak.
    // Claude Code renders whatever the command prints, so running as the
    // statusline also has to LEAVE a status line: we print the usage summary,
    // and the terminal gains a statusline out of the deal.
    [DataContract] public class SlLimit
    {
        [DataMember] public double? used_percentage;
        [DataMember] public double? utilization;    // older builds used this name
        [DataMember] public long? resets_at;        // unix epoch seconds, not ISO-8601
    }
    [DataContract] public class SlRateLimits
    {
        [DataMember] public SlLimit five_hour;
        [DataMember] public SlLimit seven_day;
    }
    [DataContract] public class SlModel { [DataMember] public string display_name; }
    [DataContract] public class SlBlob
    {
        [DataMember] public SlRateLimits rate_limits;
        [DataMember] public SlModel model;
    }

    public static class Feed
    {
        public static string SettingsPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude\\settings.json"); }
        }
        static string Dir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeWidget"); }
        }
        static string FeedPath { get { return System.IO.Path.Combine(Dir, "feed.json"); } }

        // Data older than this is ignored: Claude Code is closed or idle, and
        // the API poll takes over again.
        const int FreshMinutes = 10;

        public enum State { Off, Ours, Foreign }

        // Locate the "statusLine" value object by counting braces. Braces
        // inside quoted strings would fool the count; a command line weird
        // enough to contain one reads as Foreign, which only disables our
        // toggle - never breaks the file.
        static string StatusLineBlock(string json, out int start, out int end)
        {
            start = end = -1;
            int p = json.IndexOf("\"statusLine\"", StringComparison.Ordinal);
            if (p < 0) return null;
            int a = json.IndexOf('{', p);
            if (a < 0) return null;
            int depth = 0;
            for (int i = a; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}' && --depth == 0)
                {
                    start = p; end = i + 1;
                    return json.Substring(p, end - p);
                }
            }
            return null;
        }

        public static State Detect()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return State.Off;
                int s, e;
                string block = StatusLineBlock(File.ReadAllText(SettingsPath), out s, out e);
                if (block == null) return State.Off;
                return block.IndexOf("ClaudeWidget", StringComparison.OrdinalIgnoreCase) >= 0
                    ? State.Ours : State.Foreign;
            }
            catch { return State.Off; }
        }

        // A statusline whose command references an absolute file that no
        // longer exists is dead: it draws nothing and belongs to nobody
        // (typically an entry left behind by an uninstalled tool, or a
        // profile copied from another machine). Dead entries are fair game
        // for replacement; anything else foreign stays untouched. Commands
        // with no absolute path (a bare name found through PATH) cannot be
        // proven dead, so they count as alive.
        static bool IsDeadCommand(string block)
        {
            try
            {
                Match m = Regex.Match(block, "\"command\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (!m.Success) return false;
                string cmd = m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                var paths = Regex.Matches(cmd, "[A-Za-z]:\\\\[^\"<>|]+?\\.(exe|ps1|cmd|bat|js|py|sh)",
                                          RegexOptions.IgnoreCase);
                if (paths.Count == 0) return false;
                foreach (Match p in paths)
                    if (!File.Exists(p.Value)) return true;
                return false;
            }
            catch { return false; }
        }

        // The settings file belongs to Claude Code and carries keys we know
        // nothing about (hooks, plugins, permissions...), so we splice text in
        // place instead of re-serializing - same reasoning as
        // Api.WriteBackCredentials. A pristine copy is kept next to the file.
        public static bool Enable()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                // Prefer the helper built without the uiAccess manifest: a
                // statusline spawn neither needs nor reliably gets the higher
                // integrity level the main exe asks for.
                string helper = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe), "ClaudeWidgetFeed.exe");
                if (File.Exists(helper)) exe = helper;
                string entry = "\"statusLine\": {\"type\": \"command\", \"command\": \"\\\"" +
                               exe.Replace("\\", "\\\\") + "\\\" --feed\", \"padding\": 0}";

                string json = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "";

                State st = Detect();
                if (st == State.Foreign)
                {
                    // A live foreign statusline is sacred. A dead one - its
                    // target file is gone - is replaced.
                    int fs, fe;
                    string fblock = StatusLineBlock(json, out fs, out fe);
                    if (fblock != null && IsDeadCommand(fblock))
                    {
                        json = StripStatusLine(json);
                        Api.Log("dead statusline replaced (its target file is gone)");
                    }
                    else { Api.Log("feed not wired: statusline owned by another tool"); return false; }
                }
                else if (st == State.Ours)
                {
                    // Right path -> nothing to do; stale path (a moved
                    // install, or an older version that registered the
                    // uiAccess exe itself) -> strip and re-insert.
                    int bs, be;
                    string block = StatusLineBlock(json, out bs, out be);
                    if (block == null) return false;
                    if (block.IndexOf(exe.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    json = StripStatusLine(json);
                    Api.Log("feed entry rewritten (stale command path)");
                }

                if (json == null) return false;
                string upd;
                int brace = json.IndexOf('{');
                if (brace < 0) upd = "{\n  " + entry + "\n}\n";
                else
                {
                    int q = brace + 1;
                    while (q < json.Length && char.IsWhiteSpace(json[q])) q++;
                    bool empty = q < json.Length && json[q] == '}';
                    upd = json.Substring(0, brace + 1) +
                          "\n  " + entry + (empty ? "\n" : ",") +
                          json.Substring(brace + 1);
                }
                WriteSettings(json, upd);
                Api.Log("local feed enabled (statusLine written)");
                return true;
            }
            catch (Exception e) { Api.Log("feed enable failed: " + e.Message); return false; }
        }

        // Remove the whole "statusLine" entry plus the comma that tied it to
        // its neighbour - after it if there is one, otherwise before.
        // Returns null when the entry is absent.
        static string StripStatusLine(string json)
        {
            int s, e;
            if (StatusLineBlock(json, out s, out e) == null) return null;
            int e2 = e;
            while (e2 < json.Length && char.IsWhiteSpace(json[e2])) e2++;
            if (e2 < json.Length && json[e2] == ',') e = e2 + 1;
            else
            {
                int s2 = s - 1;
                while (s2 >= 0 && char.IsWhiteSpace(json[s2])) s2--;
                if (s2 >= 0 && json[s2] == ',') s = s2;
            }
            return json.Substring(0, s) + json.Substring(e);
        }

        public static bool Disable()
        {
            try
            {
                if (Detect() != State.Ours) return false;
                string json = File.ReadAllText(SettingsPath);
                string upd = StripStatusLine(json);
                if (upd == null) return false;
                WriteSettings(json, upd);
                try { File.Delete(FeedPath); } catch { }
                Api.Log("local feed disabled (statusLine removed)");
                return true;
            }
            catch (Exception e) { Api.Log("feed disable failed: " + e.Message); return false; }
        }

        static void WriteSettings(string original, string updated)
        {
            // One-shot pristine backup, then the same atomic swap as the
            // credentials write-back. The backup is kept on purpose.
            string bak = SettingsPath + ".widget.bak";
            try { if (File.Exists(SettingsPath) && !File.Exists(bak)) File.WriteAllText(bak, original, new UTF8Encoding(false)); } catch { }
            string tmp = SettingsPath + ".widget.tmp";
            File.WriteAllText(tmp, updated, new UTF8Encoding(false));
            if (File.Exists(SettingsPath))
            {
                string swap = SettingsPath + ".widget.old";
                try { File.Replace(tmp, SettingsPath, swap); try { File.Delete(swap); } catch { } }
                catch { File.Copy(tmp, SettingsPath, true); try { File.Delete(tmp); } catch { } }
            }
            else File.Move(tmp, SettingsPath);
        }

        // Widget side: the parked numbers, or null when absent or stale.
        public static Usage TryRead(out DateTime stamp)
        {
            stamp = DateTime.MinValue;
            try
            {
                if (!File.Exists(FeedPath)) return null;
                DateTime w = File.GetLastWriteTime(FeedPath);
                if ((DateTime.Now - w).TotalMinutes > FreshMinutes) return null;
                var u = Json.Read<Usage>(File.ReadAllText(FeedPath));
                if (u == null || (u.five_hour == null && u.seven_day == null)) return null;
                stamp = w;
                return u;
            }
            catch { return null; }
        }

        static Limit Convert(SlLimit s)
        {
            if (s == null) return null;
            double? pct = s.used_percentage.HasValue ? s.used_percentage : s.utilization;
            if (!pct.HasValue) return null;
            var l = new Limit { utilization = pct };
            if (s.resets_at.HasValue && s.resets_at.Value > 0)
                l.resets_at = DateTimeOffset.FromUnixTimeSeconds(s.resets_at.Value)
                                            .ToString("o", CultureInfo.InvariantCulture);
            return l;
        }

        // --feed mode: consume the pushed blob on stdin, park the numbers in
        // the widget's own Usage shape, print a status line. Never throws:
        // a crash here would surface as an error in the user's terminal.
        public static int RunAsStatusLine()
        {
            string input = "";
            try { input = Console.In.ReadToEnd(); } catch { }
            var blob = Json.Read<SlBlob>(input);

            Usage u = null;
            if (blob != null && blob.rate_limits != null)
            {
                u = new Usage
                {
                    five_hour = Convert(blob.rate_limits.five_hour),
                    seven_day = Convert(blob.rate_limits.seven_day)
                };
                if (u.five_hour == null && u.seven_day == null) u = null;
            }
            if (u != null)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string tmp = FeedPath + ".tmp";
                    string old = FeedPath + ".old";
                    File.WriteAllText(tmp, Json.Write(u), new UTF8Encoding(false));
                    if (File.Exists(FeedPath))
                    {
                        try { File.Replace(tmp, FeedPath, old); try { File.Delete(old); } catch { } }
                        catch { File.Copy(tmp, FeedPath, true); try { File.Delete(tmp); } catch { } }
                    }
                    else File.Move(tmp, FeedPath);
                }
                catch { }
            }

            // Labels in the widget's configured language.
            try
            {
                string cfgPath = System.IO.Path.Combine(Dir, "config.json");
                if (File.Exists(cfgPath))
                {
                    var cfg = Json.Read<Config>(File.ReadAllText(cfgPath));
                    if (cfg != null) I18n.Use(cfg.Lang);
                }
            }
            catch { }

            var sb = new StringBuilder();
            if (blob != null && blob.model != null && !string.IsNullOrEmpty(blob.model.display_name))
                sb.Append(blob.model.display_name).Append("  ");
            bool any = false;
            if (u != null)
            {
                Limit[] parts = { u.five_hour, u.seven_day };
                string[] labels = { I18n.T.Short5h, I18n.T.Short7d };
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null || !parts[i].utilization.HasValue) continue;
                    if (any) sb.Append(" | ");
                    sb.Append(labels[i]).Append(' ')
                      .Append(((int)Math.Round(parts[i].utilization.Value)).ToString(CultureInfo.InvariantCulture))
                      .Append('%');
                    any = true;
                }
            }
            if (!any && sb.Length == 0) sb.Append("ClaudeWidget");

            try
            {
                // Console.Out on a windowless exe: fine when the parent
                // redirected stdout (Claude Code does), silently absent when
                // launched by hand. UTF-8, no BOM: the terminal renders it raw.
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                stdout.Write(sb.ToString());
                stdout.Flush();
            }
            catch { }
            return 0;
        }
    }

    // ---------- local usage (read from Claude Code's own transcripts) ----------
    // Claude Code keeps one JSONL file per conversation under
    // ~/.claude/projects/<project>/<session>.jsonl; every assistant line
    // carries a usage block. Only "new" tokens are summed (input + output +
    // cache writes): the weeks that ended at 100% all land on the same
    // new-token total, while cache reads vary wildly - so the weekly limit
    // barely counts re-reads.
    public class LocalDaily
    {
        public DateTime Start;          // local date of the first bin
        public long[] Writes, Answers;  // tokens per local day
    }

    public static class LocalStats
    {
        static readonly Regex RxIn = new Regex("\"input_tokens\":(\\d+)", RegexOptions.Compiled);
        static readonly Regex RxOut = new Regex("\"output_tokens\":(\\d+)", RegexOptions.Compiled);
        static readonly Regex RxCw = new Regex("\"cache_creation_input_tokens\":(\\d+)", RegexOptions.Compiled);
        static readonly Regex RxTs = new Regex("\"timestamp\":\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex RxMsgId = new Regex("\"id\":\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex RxReqId = new Regex("\"requestId\":\"([^\"]+)\"", RegexOptions.Compiled);

        static long Num(Regex rx, string line)
        {
            Match m = rx.Match(line);
            long v;
            return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0;
        }

        public static LocalDaily ScanDaily(DateTime startLocalDate, Action<int, int> progress)
        {
            int n = (DateTime.Today - startLocalDate).Days + 1;
            if (n < 1) n = 1;
            var d = new LocalDaily { Start = startLocalDate, Writes = new long[n], Answers = new long[n] };
            var seen = new HashSet<string>();
            DateTime minMtimeUtc = startLocalDate.AddDays(-1).ToUniversalTime();
            string root = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude\\projects");
            if (!Directory.Exists(root)) return d;

            // list eligible files first so the progress counter has a total
            var files = new List<string>();
            foreach (string dir in Directory.GetDirectories(root))
                foreach (string file in Directory.GetFiles(dir, "*.jsonl", SearchOption.AllDirectories))
                {
                    try { if (File.GetLastWriteTimeUtc(file) >= minMtimeUtc) files.Add(file); }
                    catch { }
                }

            int done = 0;
            foreach (string file in files)
                {
                    try
                    {
                        using (var r = new StreamReader(file))
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                if (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                                if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) continue;
                                Match ts = RxTs.Match(line);
                                if (!ts.Success) continue;
                                DateTimeOffset when;
                                if (!DateTimeOffset.TryParse(ts.Groups[1].Value, CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out when)) continue;
                                int idx = (when.ToLocalTime().Date - startLocalDate).Days;
                                if (idx < 0 || idx >= n) continue;
                                // one API reply can be written as several lines
                                Match mi = RxMsgId.Match(line);
                                Match rq = RxReqId.Match(line);
                                string key = (mi.Success ? mi.Groups[1].Value : "") + "|" +
                                             (rq.Success ? rq.Groups[1].Value : "");
                                if (key.Length > 1 && !seen.Add(key)) continue;

                                d.Writes[idx] += Num(RxCw, line);
                                d.Answers[idx] += Num(RxIn, line) + Num(RxOut, line);
                            }
                        }
                    }
                    catch { }
                    done++;
                    if (progress != null) progress(done, files.Count);
                }
            return d;
        }
    }

    // ---------- window ----------
    public class MainWindow : Window
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_FLAGS = 0x1 | 0x2 | 0x10;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int count);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const uint GW_HWNDPREV = 3;

        StackPanel _rows;
        Border _root;
        bool _hiddenForFullScreen;
        Usage _last;
        DateTime _lastTs;
        string _lastErr;
        Config _cfg;
        IntPtr _hwnd = IntPtr.Zero;
        MenuItem[] _opaItems;
        DispatcherTimer _poll;

        // Fallback pacing: the timer ticks every minute (checking the feed
        // file costs nothing), but the API is only called when the clock
        // reaches _nextApiAt - pushed further out on each 429.
        DateTime _nextApiAt = DateTime.MinValue;
        int _apiStrikes;
        bool _refreshBusy;
        bool _viaFeed;
        int _lastErrCode;

        static Strings L { get { return I18n.T; } }

        // Width reserved for the logo; the rows keep the same total width as
        // before, so the layout is unchanged apart from the logo itself.
        const double LogoColumn = 20;

        static string CfgDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeWidget"); }
        }
        static string CfgPath { get { return System.IO.Path.Combine(CfgDir, "config.json"); } }

        static string StartupLnkPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Claude Widget.lnk"); }
        }

        static void CreateStartupShortcut()
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic lnk = shell.CreateShortcut(StartupLnkPath);
            lnk.TargetPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            lnk.Save();
        }

        static Brush B(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }
        static string PctHex(double p)
        {
            if (p >= 90) return "#E05252";
            if (p >= 70) return "#E8A33D";
            return "#DA7756";
        }

        public MainWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
            Title = "Claude Widget";

            _cfg = null;
            try { if (File.Exists(CfgPath)) _cfg = Json.Read<Config>(File.ReadAllText(CfgPath)); } catch { }
            if (_cfg == null) _cfg = new Config();
            // DataContractJsonSerializer bypasses field initializers, so a config
            // file written by an older build leaves new fields null. Use() maps
            // null to English, and we write the resolved code back.
            I18n.Use(_cfg.Lang);
            _cfg.Lang = L.Code;
            Theme.Use(_cfg.Theme);
            _cfg.Theme = Theme.Current.Name;
            Opacity = (_cfg.Opacity >= 0.2 && _cfg.Opacity <= 1.0) ? _cfg.Opacity : 1.0;

            _root = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = B(Theme.Current.Panel),
                BorderBrush = B(Theme.Current.Border),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3)
            };
            // The logo lives in its own full-height column rather than inside
            // the first row: sitting in an 18px row of a 36px widget, it read
            // as pushed towards the top. Left-aligned in a 20px column, it also
            // sits a few pixels further left than when it was centred in 22px.
            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LogoColumn) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var logo = new ContentControl
            {
                Content = ClaudeLogo(14),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(logo, 0);
            shell.Children.Add(logo);

            _rows = new StackPanel
            {
                Orientation = Orientation.Vertical,
                MinHeight = 36,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_rows, 1);
            shell.Children.Add(_rows);

            _root.Child = shell;
            Content = _root;

            ApplySkinToApp();
            BuildMenu();
            Redraw();

            MouseLeftButtonDown += delegate
            {
                try { DragMove(); SaveConfig(); } catch { }
            };

            Loaded += delegate
            {
                _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                ApplyPos();
                // Wire the local feed automatically. Enable() is a no-op when
                // already wired, and refuses to touch a statusline owned by
                // another tool - that is the only case left to the user.
                ThreadPool.QueueUserWorkItem(delegate { try { Feed.Enable(); } catch { } });
                // Start with Windows, on by default: recreate the Startup
                // shortcut unless the user explicitly unticked the entry.
                if (_cfg.AutoStart != false && !File.Exists(StartupLnkPath))
                {
                    try { CreateStartupShortcut(); Api.Log("startup shortcut created (default on)"); }
                    catch (Exception e) { Api.Log("startup shortcut failed: " + e.Message); }
                }
                Refresh(false);
                // One-minute tick: the feed check is a local file stat, and
                // the API call inside is gated by _nextApiAt anyway.
                _poll = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                _poll.Tick += delegate { Refresh(false); };
                _poll.Start();
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t2.Tick += delegate { AssertTop(); };
                t2.Start();
                // redraw every minute so the countdowns stay alive
                var t3 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                t3.Tick += delegate { if (_last != null) Render(); };
                t3.Start();
                CheckUpdate();
                var t4 = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
                t4.Tick += delegate { CheckUpdate(); };
                t4.Start();
                // background scan of the local transcripts: shortly after
                // startup, then every 30 minutes, so the usage chart opens
                // instantly instead of rescanning on every click
                var scanKick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                scanKick.Tick += delegate { scanKick.Stop(); StartLocalScan(); };
                scanKick.Start();
                var t5 = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
                t5.Tick += delegate { StartLocalScan(); };
                t5.Start();
                // handy for tests and screenshots: open the chart right away
                foreach (string a in Environment.GetCommandLineArgs())
                    if (a == "--detail") { ShowLocalDetail(); break; }
            };
        }

        // A game running borderless-fullscreen is just a window covering the
        // whole monitor, so SHQueryUserNotificationState misses it - which is
        // exactly the common case. We compare the foreground window to its
        // monitor instead. rcMonitor, not rcWork: a merely maximized window
        // stops at the taskbar and must NOT count as full screen.
        static bool ForegroundIsFullScreen(IntPtr self)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == self) return false;

            // The desktop and the shell permanently span the screen.
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" ||
                c == "Windows.UI.Core.CoreWindow") return false;

            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;

            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
                && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        // The widget is above the taskbar exactly when it comes before it in
        // the z-order: walking upwards from the taskbar must reach our hwnd.
        static bool IsAboveTaskbar(IntPtr self)
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return false;
            IntPtr h = tray;
            for (int i = 0; i < 512; i++)
            {
                h = GetWindow(h, GW_HWNDPREV);
                if (h == IntPtr.Zero) break;
                if (h == self) return true;
            }
            return false;
        }

        bool _menuOpen;

        void AssertTop()
        {
            if (_hwnd == IntPtr.Zero) return;

            bool hide = (_cfg.HideFullScreen ?? true) && ForegroundIsFullScreen(_hwnd);
            if (hide != _hiddenForFullScreen)
            {
                _hiddenForFullScreen = hide;
                Visibility = hide ? Visibility.Hidden : Visibility.Visible;
            }
            // Re-asserting topmost over a full-screen game can kick it out of
            // its display mode or stutter it, so we stop entirely while hidden.
            if (hide) return;

            // The blind NOTOPMOST/TOPMOST dance every 500 ms caused a visible
            // flicker (and fought the context menu). Re-assert only when the
            // taskbar has actually climbed above us.
            if (_menuOpen || IsAboveTaskbar(_hwnd)) return;

            SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_FLAGS);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_FLAGS);
        }

        void ApplyPos()
        {
            var wa = SystemParameters.WorkArea;
            if (_cfg.X > -9999 && _cfg.Y > -9999 &&
                _cfg.X < SystemParameters.VirtualScreenWidth && _cfg.Y < SystemParameters.VirtualScreenHeight)
            {
                Left = _cfg.X; Top = _cfg.Y;
            }
            else
            {
                UpdateLayout();
                double h = ActualHeight > 10 ? ActualHeight : 32;
                Left = 8;
                Top = wa.Bottom - h - 4;
                SaveConfig();
            }
        }

        void SaveConfig()
        {
            _cfg.X = Left; _cfg.Y = Top;
            try
            {
                Directory.CreateDirectory(CfgDir);
                File.WriteAllText(CfgPath, Json.Write(_cfg));
            }
            catch { }
        }

        // ---------- UI ----------
        static Canvas ClaudeLogo(double size)
        {
            var cv = new Canvas { Width = size, Height = size };
            double c = size / 2;
            double[] lens = { 0.50, 0.41, 0.47, 0.42, 0.50, 0.43, 0.46, 0.40, 0.49, 0.42, 0.47, 0.41 };
            double half = 7.5 * Math.PI / 180;
            for (int i = 0; i < 12; i++)
            {
                double a = i * 30 * Math.PI / 180;
                double r = size * lens[i];
                var poly = new Polygon { Fill = B("#DA7756") };
                poly.Points.Add(new Point(c, c));
                poly.Points.Add(new Point(c + r * Math.Cos(a - half), c + r * Math.Sin(a - half)));
                poly.Points.Add(new Point(c + r * Math.Cos(a + half), c + r * Math.Sin(a + half)));
                cv.Children.Add(poly);
            }
            return cv;
        }

        static Border Bar(double pct, double w, double h)
        {
            var track = new Border
            {
                Width = w, Height = h,
                CornerRadius = new CornerRadius(h / 2),
                Background = B(Theme.Current.Track),
                VerticalAlignment = VerticalAlignment.Center
            };
            var fill = new Border
            {
                Height = h,
                CornerRadius = new CornerRadius(h / 2),
                Background = B(PctHex(pct)),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(h, w * Math.Max(0, Math.Min(100, pct)) / 100)
            };
            track.Child = fill;
            return track;
        }

        static string FmtReset(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            try
            {
                var t = DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture) - DateTimeOffset.UtcNow;
                if (t.TotalSeconds <= 0) return "";
                if (t.TotalHours >= 24)
                    return string.Format("{0}{1} {2}{3}", (int)Math.Floor(t.TotalDays), L.DayUnit, t.Hours, L.HourUnit);
                if (t.TotalHours >= 1)
                    return string.Format("{0}{1}{2:00}", (int)Math.Floor(t.TotalHours), L.HourUnit, t.Minutes);
                return string.Format("{0} {1}", (int)Math.Ceiling(t.TotalMinutes), L.MinuteUnit);
            }
            catch { return ""; }
        }

        void ShowMessage(string msg)
        {
            _rows.Children.Clear();
            _rows.Opacity = 1.0;
            // Same rule as Render(): an available update owns the border,
            // even (especially) when there is nothing else to show.
            _root.BorderBrush = B(_updateAvailable ? "#CCDA7756" : Theme.Current.Border);
            // A Grid centres the text properly; a bare TextBlock in a
            // StackPanel would sit at the top of the 36px band.
            // Width pinned to the gauge rows' footprint: the window sizes to
            // content, so an unconstrained error message used to widen the
            // whole widget. The widget must NEVER change size - the text
            // wraps, gets cut with an ellipsis, and lives whole in the
            // tooltip instead.
            var row = new Grid { Height = 36, Width = 166 };
            row.Children.Add(new TextBlock
            {
                Text = msg,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = B(Theme.Current.Mid),
                VerticalAlignment = VerticalAlignment.Center
            });
            _rows.Children.Add(row);
            _root.ToolTip = msg;
        }

        void AddPart(string shortLabel, string fullLabel, Limit d, List<string> tips)
        {
            if (d == null || !d.utilization.HasValue) return;
            double pct = Math.Max(0, Math.Min(100, d.utilization.Value));
            var row = new Grid { Height = 18, Width = 166 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            var label = new TextBlock
            {
                Text = shortLabel,
                FontSize = 8.5,
                Foreground = B(Theme.Current.Dim),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            string pctText = (int)Math.Round(pct) + "%";
            var percent = new TextBlock
            {
                Text = pctText,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(PctHex(pct)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(percent, 1);
            row.Children.Add(percent);

            var bar = Bar(pct, 58, 4);
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            string reset = FmtReset(d.resets_at);
            if (reset.Length > 0)
            {
                var resetText = new TextBlock
                {
                    Text = reset,
                    FontSize = 9,
                    Foreground = B(Theme.Current.Bright),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(resetText, 3);
                row.Children.Add(resetText);
                tips.Add(fullLabel + L.Colon + pctText + " (" + string.Format(L.ResetsIn, reset) + ")");
            }
            else tips.Add(fullLabel + L.Colon + pctText);

            _rows.Children.Add(row);
        }

        static string FmtAge(TimeSpan t)
        {
            if (t.TotalHours >= 24) return string.Format("{0} {1}", (int)Math.Floor(t.TotalDays), L.DayUnit);
            if (t.TotalHours >= 1) return string.Format("{0}{1}{2:00}", (int)Math.Floor(t.TotalHours), L.HourUnit, t.Minutes);
            return string.Format("{0} {1}", Math.Max(1, (int)Math.Floor(t.TotalMinutes)), L.MinuteUnit);
        }

        // Single entry point for repainting: also used when the language changes.
        void Redraw()
        {
            if (_last != null) Render();
            else
            {
                string msg = _lastErr == null ? L.Loading : string.Format(L.Offline, _lastErr);
                // Rate limited or signed out with nothing to show yet: a NEW
                // Claude Code session is the way out of both - it loads the
                // statusline, which pushes the numbers locally, no API and no
                // valid token needed. Sessions already open never will: Claude
                // Code only reads the statusline entry when a session starts.
                // And say the truth when a foreign statusline blocks the feed.
                if (_lastErrCode == 429 || _lastErrCode == 401 || _lastErrCode == 403)
                    msg += "\n" + (Feed.Detect() == Feed.State.Foreign ? L.FeedHintBusy : L.FeedHint);
                ShowMessage(msg);
            }
        }

        void Render()
        {
            if (_last == null) return;
            _rows.Children.Clear();
            var tips = new List<string>();
            AddPart(L.Short5h, L.Session5h, _last.five_hour, tips);
            AddPart(L.Short7d, L.Week, _last.seven_day, tips);
            tips.Add(string.Format(L.Updated, _lastTs.ToString("HH:mm")));
            if (_viaFeed) tips.Add(L.SourceFeed);
            if (_lastErr != null)
            {
                // Past two missed cycles the numbers on screen mean nothing any
                // more, so we fade them out to make the stall visible.
                TimeSpan age = DateTime.Now - _lastTs;
                bool stale = age.TotalMinutes >= 12;
                // An available update outranks the failure colour: the person
                // who most needs to see it is exactly the one whose widget is
                // broken (the 2026-08 rate-limit wave proved it). The fade and
                // the tooltip keep saying the data is stale.
                _root.BorderBrush = B(_updateAvailable ? "#CCDA7756" : (stale ? "#CCE05252" : "#99E8A33D"));
                _rows.Opacity = stale ? 0.45 : 1.0;
                tips.Add(string.Format(L.FrozenFor, FmtAge(age), _lastErr));
            }
            else
            {
                _root.BorderBrush = B(_updateAvailable ? "#CCDA7756" : Theme.Current.Border);
                _rows.Opacity = 1.0;
            }
            if (_updateAvailable) tips.Add(L.MenuUpdate);
            _root.ToolTip = string.Join(Environment.NewLine, tips.ToArray());
        }

        // Two sources, in order: the numbers Claude Code pushes locally to
        // the statusline (fresh, free, no rate limit - see Feed), then the
        // usage endpoint. The endpoint started answering 429 in 2026-08, and
        // the old reaction - tightening the poll from five minutes to one -
        // dug the hole deeper. Now a 429 backs off instead: 10, 20, 40, then
        // 60 minutes, until a call succeeds. The one-minute retry survives
        // only for network failures, where it costs nothing remote and
        // recovers fast after a wake from sleep.
        void Refresh(bool force)
        {
            if (_refreshBusy) return;
            _refreshBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Usage u = null;
                string err = null;
                int code = 0;
                bool viaFeed = false;
                DateTime feedTs;

                u = Feed.TryRead(out feedTs);
                if (u != null) viaFeed = true;
                else if (force || DateTime.Now >= _nextApiAt)
                {
                    try { u = Api.GetUsage(); }
                    catch (WebException we)
                    {
                        var hr = we.Response as HttpWebResponse;
                        code = hr == null ? 0 : (int)hr.StatusCode;
                        if (hr != null) hr.Close();
                        // Map the two codes a person can act on to plain
                        // words; anything else keeps its raw message.
                        if (code == 429) err = I18n.T.ErrRateLimited;
                        else if (code == 401 || code == 403) err = I18n.T.ErrExpired;
                        else err = we.Message;
                    }
                    catch (Exception e) { err = e.Message; }
                }
                else { _refreshBusy = false; return; }   // between API slots, nothing to do

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _refreshBusy = false;
                    if (u != null)
                    {
                        if (_lastErr != null) Api.Log("refresh recovered");
                        if (viaFeed != _viaFeed)
                            Api.Log("usage source: " + (viaFeed ? "local feed" : "API"));
                        _viaFeed = viaFeed;
                        _last = u;
                        _lastTs = viaFeed ? feedTs : DateTime.Now;
                        _lastErr = null; _lastErrCode = 0;
                        _apiStrikes = 0;
                        _nextApiAt = DateTime.Now + TimeSpan.FromMinutes(5);
                    }
                    else
                    {
                        Api.Log("refresh failed: " + err + (code != 0 ? " (HTTP " + code + ")" : ""));
                        _lastErr = err; _lastErrCode = code;
                        TimeSpan delay;
                        if (code == 429)
                        {
                            _apiStrikes = Math.Min(_apiStrikes + 1, 4);
                            delay = TimeSpan.FromMinutes(Math.Min(60, 5 * (1 << _apiStrikes)));
                        }
                        else if (code == 0) delay = TimeSpan.FromMinutes(1);
                        else delay = TimeSpan.FromMinutes(5);
                        _nextApiAt = DateTime.Now + delay;
                    }
                    Redraw();
                }));
            });
        }

        // ---------- update check ----------
        bool _updateAvailable;

        void CheckUpdate()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool avail = Api.UpdateAvailable();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (avail == _updateAvailable) return;
                    _updateAvailable = avail;
                    BuildMenu();    // the Update entry appears or disappears
                    Redraw();       // the border turns orange (or back)
                }));
            });
        }

        void StartUpdate()
        {
            // The obvious one-liner - powershell "irm ... | iex" - is the
            // exact command shape of a malware dropper, and Defender's ML
            // model flags it as such (Trojan:Win32/Commando.A!ml, observed
            // 2026-08-28, killing the update mid-flight). So the widget
            // downloads the repository archive itself over the plumbing it
            // already trusts, extracts it, and elevates the LOCAL installer:
            // no remote code is ever piped into a shell. The installer then
            // kills this instance and starts the new one.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claudewidget-update");
                    try { Directory.Delete(work, true); } catch { }
                    Directory.CreateDirectory(work);
                    string zip = System.IO.Path.Combine(work, "source.zip");
                    Api.DownloadFile(Api.ArchiveUrl, zip);
                    System.IO.Compression.ZipFile.ExtractToDirectory(zip, work);
                    string installer = System.IO.Path.Combine(work, "claude-usage-widget-main", "Installer.ps1");
                    if (!File.Exists(installer)) { Api.Log("update failed: Installer.ps1 missing from archive"); return; }
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + installer + "\"",
                        Verb = "runas",          // one UAC prompt, as before
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception e) { Api.Log("update launch failed: " + e.Message); }
            });
        }

        // ---------- local usage window ----------
        Window _detail;
        Action _detailRender;   // repaints the open window when a scan lands

        // The scan only reads the local transcript files - disk time, zero
        // tokens. It runs in the background at startup and every 30 minutes,
        // so opening the window is instant: it shows the cached chart with
        // the time of the last refresh, or a file counter while the very
        // first scan is still running.
        static LocalDaily _localData;
        static DateTime _localDataTs;
        static bool _localScanning;
        static int _scanDone, _scanTotal;

        string ScanProgressText()
        {
            return L.DetailScanning + (_scanTotal > 0 ? "  " + _scanDone + "/" + _scanTotal : "");
        }

        void StartLocalScan()
        {
            if (_localScanning) return;
            _localScanning = true;
            _scanDone = 0; _scanTotal = 0;
            DateTime today = DateTime.Today;
            DateTime start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            ThreadPool.QueueUserWorkItem(delegate
            {
                LocalDaily t = null;
                try
                {
                    t = LocalStats.ScanDaily(start, delegate(int done, int total)
                    { _scanDone = done; _scanTotal = total; });
                }
                catch (Exception e) { Api.Log("local scan failed: " + e.Message); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _localScanning = false;
                    if (t != null && t.Writes.Length >= 2) { _localData = t; _localDataTs = DateTime.Now; }
                    if (_detailRender != null) _detailRender();
                }));
            });
        }

        static TextBlock DetailText(string text, double size, string hex, bool bold)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = B(hex),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260
            };
        }

        // Numbers and dates in the chart follow the widget's language, not the
        // Windows locale - an English widget must not show French month names.
        static CultureInfo Ci()
        {
            try { return new CultureInfo(L.Code); }
            catch { return CultureInfo.InvariantCulture; }
        }

        static string Mt(long tokens)
        {
            return string.Format(Ci(), "{0:0.0} M", tokens / 1e6);
        }

        static StackPanel LegendChip(string hex, string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
            sp.Children.Add(new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(2),
                Background = B(hex),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label, FontSize = 9.5, Foreground = B(Theme.Current.Mid),
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        // One figure of the summary row: a small caption over a large number.
        // The number itself is filled in by the render pass.
        static StackPanel Kpi(string caption, TextBlock value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = caption.ToUpper(Ci()),
                FontSize = 8.5,
                Foreground = B(Theme.Current.Dim)
            });
            sp.Children.Add(value);
            return sp;
        }

        static TextBlock KpiValue(string hex)
        {
            return new TextBlock
            {
                Text = "-", FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = B(hex)
            };
        }

        void ShowLocalDetail()
        {
            if (_detail != null) { try { _detail.Close(); } catch { } _detail = null; }
            Theme th = Theme.Current;

            // Current month plus the whole previous month.
            DateTime today = DateTime.Today;
            DateTime start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);

            const double CW = 560, CH = 204, ML = 40, MR = 6, MT = 16, MB = 36;
            double plotW = CW - ML - MR, plotH = CH - MT - MB;
            const string ColWrites = "#DA7756", ColAnswers = "#6C9FE8";

            // ---- title bar. WindowStyle.None means we draw it ourselves, so
            // the window matches the widget instead of wearing the grey system
            // chrome. It carries the logo, the title and the close button, and
            // dragging it moves the window.
            var bar = new Border
            {
                Background = B(th.WinBar),
                CornerRadius = new CornerRadius(9, 9, 0, 0),
                BorderBrush = B(th.WinBarLine),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(11, 6, 7, 6)
            };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new ContentControl
            {
                Content = ClaudeLogo(13),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(mark, 0);
            barGrid.Children.Add(mark);

            var title = new TextBlock
            {
                Text = L.DetailTitle,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(th.Ink),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 1);
            barGrid.Children.Add(title);

            var closeBtn = new Border
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = B(th.WinBtn),
                Cursor = Cursors.Hand
            };
            closeBtn.Child = new TextBlock
            {
                Text = "✕", FontSize = 11,
                Foreground = B(th.Mid),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(closeBtn, 2);
            barGrid.Children.Add(closeBtn);
            bar.Child = barGrid;

            // ---- summary row: the three figures people open the window for.
            var kMonth = KpiValue(th.Ink);
            var kPrev = KpiValue(th.Ink);
            var kWeek = KpiValue(ColWrites);

            var kpis = new Grid { Margin = new Thickness(2, 0, 2, 10) };
            kpis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kpis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kpis.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            kpis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var kA = Kpi(today.ToString("MMMM", Ci()), kMonth);
            Grid.SetColumn(kA, 0); kpis.Children.Add(kA);
            var kB = Kpi(start.ToString("MMMM", Ci()), kPrev);
            Grid.SetColumn(kB, 1); kpis.Children.Add(kB);

            double weekPct = (_last != null && _last.seven_day != null && _last.seven_day.utilization.HasValue)
                ? _last.seven_day.utilization.Value : -1;
            if (weekPct >= 0)
            {
                kWeek.Text = (int)Math.Round(weekPct) + "%";
                kWeek.Foreground = B(PctHex(weekPct));
                var kC = Kpi(L.Week, kWeek);
                Grid.SetColumn(kC, 2); kpis.Children.Add(kC);
            }

            var legend = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            legend.Children.Add(LegendChip(ColWrites, L.DetailWrites));
            legend.Children.Add(LegendChip(ColAnswers, L.DetailAnswers));
            Grid.SetColumn(legend, 3);
            kpis.Children.Add(legend);

            // ---- the chart itself
            var canvas = new Canvas { Width = CW, Height = CH, ClipToBounds = true, Background = Brushes.Transparent };

            // ---- footer: one day at a time, today by default, the hovered day
            // while the mouse is over the chart.
            var fDay = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = B(th.Ink) };
            var fTot = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = B(ColWrites), Margin = new Thickness(9, 0, 9, 0) };
            var fSplit = new TextBlock { FontSize = 10, Foreground = B(th.Mid) };
            var fStamp = new TextBlock { FontSize = 10, Foreground = B(th.Mid), HorizontalAlignment = HorizontalAlignment.Right };

            var foot = new Grid { Margin = new Thickness(2, 8, 2, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(fDay, 0); foot.Children.Add(fDay);
            Grid.SetColumn(fTot, 1); foot.Children.Add(fTot);
            Grid.SetColumn(fSplit, 2); foot.Children.Add(fSplit);
            Grid.SetColumn(fStamp, 3); foot.Children.Add(fStamp);

            var body = new StackPanel { Margin = new Thickness(12, 11, 12, 9) };
            body.Children.Add(kpis);
            body.Children.Add(canvas);
            body.Children.Add(foot);

            var shell = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = B(th.WinBg),
                BorderBrush = B(th.WinBorder),
                BorderThickness = new Thickness(1)
            };
            var stack = new StackPanel();
            stack.Children.Add(bar);
            stack.Children.Add(body);
            shell.Child = stack;

            var win = new Window
            {
                Title = L.DetailTitle,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Left,
                Top = Math.Max(0, Top - CH - 130),
                Content = shell
            };
            bar.MouseLeftButtonDown += delegate { try { win.DragMove(); } catch { } };
            // Closing on the press, not the release: the drag handler on the
            // title bar would otherwise swallow the release.
            closeBtn.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            { e.Handled = true; win.Close(); };
            closeBtn.MouseEnter += delegate { closeBtn.Background = B(th.Highlight); };
            closeBtn.MouseLeave += delegate { closeBtn.Background = B(th.WinBtn); };
            win.KeyDown += delegate(object s, KeyEventArgs e)
            { if (e.Key == Key.Escape) win.Close(); };

            var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            win.Closed += delegate { if (_detail == win) { _detail = null; _detailRender = null; } ticker.Stop(); };
            _detail = win;
            win.Show();

            // Restoring the footer to "today" is needed both on mouse-leave and
            // after a repaint, so it lives in a variable the two share.
            Action restFoot = null;
            canvas.MouseLeave += delegate { if (restFoot != null) restFoot(); };

            // Paints the cached data instantly; called again when a scan lands.
            Action render = delegate
            {
                LocalDaily t = _localData;
                canvas.Children.Clear();
                if (t == null)
                {
                    fDay.Text = _localScanning ? ScanProgressText() : L.ErrBadResponse;
                    fTot.Text = ""; fSplit.Text = ""; fStamp.Text = "";
                    return;
                }
                int n = t.Writes.Length;
                long[] tot = new long[n];
                long maxTot = 1;
                for (int i = 0; i < n; i++)
                {
                    tot[i] = t.Writes[i] + t.Answers[i];
                    if (tot[i] > maxTot) maxTot = tot[i];
                }

                // round scale: three gridlines at clean values
                double[] steps = { 2, 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200 };
                double stepM = steps[steps.Length - 1];
                foreach (double sp in steps)
                    if (3 * sp * 1e6 >= maxTot) { stepM = sp; break; }
                double ymax = 3 * stepM * 1e6;

                // One slot per day; the bar sits centred in its slot.
                double slot = plotW / n;
                double barW = Math.Max(2, Math.Min(6, slot - 2.4));
                double pad = (slot - barW) / 2;
                Func<int, double> X = delegate(int i) { return ML + slot * i + pad; };
                Func<double, double> Y = delegate(double v) { return MT + plotH * (1 - v / ymax); };

                for (int g = 0; g <= 3; g++)
                {
                    double gy = Y(g * stepM * 1e6);
                    canvas.Children.Add(new Line
                    {
                        X1 = ML, X2 = CW - MR, Y1 = gy, Y2 = gy,
                        Stroke = B(g == 0 ? th.GridBase : th.Grid),
                        StrokeThickness = 1
                    });
                    if (g > 0)
                    {
                        var yl = new TextBlock
                        {
                            Text = stepM * g + " M", FontSize = 9, Foreground = B(th.Axis),
                            Width = ML - 7, TextAlignment = TextAlignment.Right
                        };
                        Canvas.SetLeft(yl, 0); Canvas.SetTop(yl, gy - 7);
                        canvas.Children.Add(yl);
                    }
                }

                // The column behind the hovered day, moved rather than recreated.
                var hot = new Rectangle
                {
                    Width = slot, Height = plotH + 4,
                    Fill = B(th.HoverCol), Visibility = Visibility.Hidden
                };
                Canvas.SetTop(hot, MT - 4);
                canvas.Children.Add(hot);

                // One stacked bar per day: cache writes at the bottom, prompts
                // and answers on top, so the whole bar is the day's total.
                for (int i = 0; i < n; i++)
                {
                    if (tot[i] <= 0) continue;
                    double yTot = Y(tot[i]), yCache = Y(t.Writes[i]), y0 = Y(0);
                    if (t.Answers[i] > 0)
                    {
                        var rp = new Rectangle
                        {
                            Width = barW, Height = Math.Max(1, yCache - yTot + 1.6),
                            RadiusX = 1.6, RadiusY = 1.6, Fill = B(ColAnswers)
                        };
                        Canvas.SetLeft(rp, X(i)); Canvas.SetTop(rp, yTot);
                        canvas.Children.Add(rp);
                    }
                    if (t.Writes[i] > 0)
                    {
                        var rc = new Rectangle
                        {
                            Width = barW, Height = Math.Max(1, y0 - yCache),
                            RadiusX = 1.6, RadiusY = 1.6, Fill = B(ColWrites)
                        };
                        Canvas.SetLeft(rc, X(i)); Canvas.SetTop(rc, yCache);
                        canvas.Children.Add(rc);
                    }
                }

                // peak label, called out once
                int peak = 0;
                for (int i = 1; i < n; i++) if (tot[i] > tot[peak]) peak = i;
                if (tot[peak] > 0)
                {
                    var pl = new TextBlock
                    {
                        Text = Mt(tot[peak]), FontSize = 9.5,
                        FontWeight = FontWeights.SemiBold, Foreground = B(th.Ink),
                        Width = 52, TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(pl, Math.Max(ML - 6, Math.Min(X(peak) + barW / 2 - 26, CW - 52)));
                    Canvas.SetTop(pl, Math.Max(0, Y(tot[peak]) - 15));
                    canvas.Children.Add(pl);
                }

                // Month boundaries get a plain vertical rule; the month names
                // themselves go under the axis, centred on their own days.
                int runStart = 0;
                for (int i = 1; i <= n; i++)
                {
                    bool edge = (i == n) || t.Start.AddDays(i).Day == 1;
                    if (!edge) continue;
                    if (i < n)
                        canvas.Children.Add(new Line
                        {
                            X1 = ML + slot * i, X2 = ML + slot * i, Y1 = MT - 4, Y2 = MT + plotH,
                            Stroke = B(th.MonthRule), StrokeThickness = 1
                        });
                    var ml = new TextBlock
                    {
                        Text = t.Start.AddDays(runStart).ToString("MMMM", Ci()).ToUpper(Ci()),
                        FontSize = 8.5, Foreground = B(th.MonthLab),
                        Width = 100, TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(ml, ML + slot * (runStart + i) / 2.0 - 50);
                    Canvas.SetTop(ml, MT + plotH + 19);
                    canvas.Children.Add(ml);
                    runStart = i;
                }

                // day labels: the 1st, the 15th and the last day, when they fit
                double lastLabelX = -100;
                for (int i = 0; i < n; i++)
                {
                    DateTime dte = t.Start.AddDays(i);
                    if (dte.Day != 1 && dte.Day != 15 && i != n - 1) continue;
                    double lx = X(i) + barW / 2;
                    if (lx - lastLabelX < 40) continue;
                    var xl = new TextBlock
                    {
                        Text = dte.ToString("d MMM", Ci()), FontSize = 9,
                        Foreground = B(th.Axis), Width = 60, TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(xl, Math.Min(lx - 30, CW - 56));
                    Canvas.SetTop(xl, MT + plotH + 5);
                    canvas.Children.Add(xl);
                    lastLabelX = lx;
                }

                // The footer text for one day, reused by the hover handlers.
                Action<int> showDay = delegate(int i)
                {
                    fDay.Text = t.Start.AddDays(i).ToString("ddd d MMM", Ci());
                    fTot.Text = Mt(tot[i]);
                    fSplit.Text = L.DetailWrites + " " + Mt(t.Writes[i]) + "   ·   " +
                                  L.DetailAnswers + " " + Mt(t.Answers[i]);
                };
                restFoot = delegate
                {
                    hot.Visibility = Visibility.Hidden;
                    showDay(n - 1);
                };
                fStamp.Text = string.Format(L.Updated, _localDataTs.ToString("HH:mm"));
                restFoot();

                // Transparent full-height strips on top of the bars: hovering a
                // thin bar directly would be a pixel-hunting exercise.
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    var hit = new Rectangle { Width = slot, Height = plotH + 4, Fill = Brushes.Transparent };
                    Canvas.SetLeft(hit, ML + slot * idx); Canvas.SetTop(hit, MT - 4);
                    hit.MouseEnter += delegate
                    {
                        Canvas.SetLeft(hot, ML + slot * idx);
                        hot.Visibility = Visibility.Visible;
                        showDay(idx);
                    };
                    canvas.Children.Add(hit);
                }

                // the two month totals, in the summary row
                long curMonth = 0, prevMonth = 0;
                for (int i = 0; i < n; i++)
                {
                    if (t.Start.AddDays(i).Month == today.Month) curMonth += tot[i];
                    else prevMonth += tot[i];
                }
                kMonth.Text = Mt(curMonth);
                kPrev.Text = Mt(prevMonth);
            };
            _detailRender = render;
            render();

            // refresh stale data; the ticker keeps the file counter moving
            // while the very first scan runs
            if (_localData == null || (DateTime.Now - _localDataTs).TotalMinutes >= 30) StartLocalScan();
            ticker.Tick += delegate
            {
                if (!win.IsVisible) { ticker.Stop(); return; }
                if (_localScanning && _localData == null) fDay.Text = ScanProgressText();
            };
            ticker.Start();
        }

        // ---------- menu ----------
        // Claude-styled skin for the context menu: rounded panel, orange
        // highlight, same palette as the widget. Replaces the gray system look.
        // The @TOKEN@ placeholders are filled from the current theme - plain
        // string.Format would choke on the {Binding} braces further down.
        const string MenuSkinXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='@PANEL@' BorderBrush='@MENUBORDER@' BorderThickness='1'
                  CornerRadius='7' Padding='4' MinWidth='170'>
            <ItemsPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Background='@SEP@' Margin='6,3'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <!-- HasDropShadow is what decides whether the tooltip's popup allows
         transparency. Left to the system setting it can be False, and then
         the popup paints an opaque white rectangle behind our rounded panel -
         the white square this style exists to get rid of. -->
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Foreground' Value='@INK@'/>
    <Setter Property='FontSize' Value='11'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='@PANEL@' BorderBrush='@MENUBORDER@' BorderThickness='1'
                  CornerRadius='6' Padding='9,6'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='@INK@'/>
    <Setter Property='FontSize' Value='11.5'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='8,5'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='15'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='12'/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name='Check' Text='&#x2713;' FontSize='10' Foreground='#DA7756'
                         Visibility='Hidden' VerticalAlignment='Center'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center'/>
              <TextBlock x:Name='Arrow' Grid.Column='2' Text='&#x203A;' FontSize='12'
                         Foreground='@MID@' Visibility='Hidden'
                         VerticalAlignment='Center' HorizontalAlignment='Right'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-6'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'
                     AllowsTransparency='True' Focusable='False'>
                <Border Background='@PANEL@' BorderBrush='@MENUBORDER@' BorderThickness='1'
                        CornerRadius='7' Padding='4' MinWidth='110'>
                  <ItemsPresenter/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='@HIGHLIGHT@'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='@DIM@'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        static ResourceDictionary _menuSkin;
        static string _menuSkinFor;     // the theme the cached skin was built for

        // The skin has to live in the application's resources, not the
        // window's: a tooltip is hosted in its own popup, and only an
        // application-level dictionary is certain to reach it.
        static ResourceDictionary _appliedSkin;

        static void ApplySkinToApp()
        {
            Application app = Application.Current;
            if (app == null) return;
            try
            {
                if (_appliedSkin != null) app.Resources.MergedDictionaries.Remove(_appliedSkin);
                _appliedSkin = MenuSkin();
                app.Resources.MergedDictionaries.Add(_appliedSkin);
            }
            catch { }
        }


        static ResourceDictionary MenuSkin()
        {
            Theme t = Theme.Current;
            if (_menuSkin != null && _menuSkinFor == t.Name) return _menuSkin;
            string xaml = MenuSkinXaml
                .Replace("@PANEL@", t.Panel)
                .Replace("@MENUBORDER@", t.MenuBorder)
                .Replace("@SEP@", t.Sep)
                .Replace("@INK@", t.Ink)
                .Replace("@MID@", t.Mid)
                .Replace("@DIM@", t.Dim)
                .Replace("@HIGHLIGHT@", t.Highlight);
            _menuSkin = (ResourceDictionary)XamlReader.Parse(xaml);
            _menuSkinFor = t.Name;
            return _menuSkin;
        }

        // Everything the theme touches, repainted in one place. The gauges and
        // the menu are rebuilt from scratch; the usage window, if it is open,
        // is simply reopened - ShowLocalDetail closes the previous one.
        void ApplyTheme()
        {
            _root.Background = B(Theme.Current.Panel);
            ApplySkinToApp();
            BuildMenu();
            Redraw();
            if (_detail != null) ShowLocalDetail();
        }

        void BuildMenu()
        {
            var menu = new ContextMenu();
            try { menu.Resources.MergedDictionaries.Add(MenuSkin()); }
            catch (Exception e) { Api.Log("menu skin failed: " + e.Message); }
            // AssertTop must not fight the open menu's popup for the z-order.
            menu.Opened += delegate { _menuOpen = true; };
            menu.Closed += delegate { _menuOpen = false; };

            if (_updateAvailable)
            {
                var miU = new MenuItem
                {
                    Header = L.MenuUpdate,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B("#DA7756")
                };
                miU.Click += delegate { StartUpdate(); };
                menu.Items.Add(miU);
                menu.Items.Add(new Separator());
            }

            // Refresh pulls the usage figures and, in the same click, asks the
            // repository whether a newer version is out - otherwise the update
            // entry only appears on the six-hourly timer.
            var miR = new MenuItem { Header = L.MenuRefresh };
            miR.Click += delegate { Refresh(true); CheckUpdate(); };
            menu.Items.Add(miR);

            var miDetail = new MenuItem { Header = L.MenuLocalDetail };
            miDetail.Click += delegate { ShowLocalDetail(); };
            menu.Items.Add(miDetail);

            var miRepl = new MenuItem { Header = L.MenuMoveBottomLeft };
            miRepl.Click += delegate
            {
                UpdateLayout();
                var wa = SystemParameters.WorkArea;
                Left = 8; Top = wa.Bottom - ActualHeight - 4;
                SaveConfig();
            };
            menu.Items.Add(miRepl);

            var miO = new MenuItem { Header = L.MenuOpacity };
            double[] vals = { 1.0, 0.85, 0.7, 0.55, 0.4 };
            _opaItems = new MenuItem[vals.Length];
            for (int i = 0; i < vals.Length; i++)
            {
                double v = vals[i];
                var mi = new MenuItem
                {
                    Header = (int)(v * 100) + "%",
                    IsCheckable = true,
                    IsChecked = Math.Abs(Opacity - v) < 0.01
                };
                mi.Click += delegate
                {
                    Opacity = v;
                    _cfg.Opacity = v;
                    SaveConfig();
                    foreach (var o in _opaItems) o.IsChecked = (o == mi);
                };
                _opaItems[i] = mi;
                miO.Items.Add(mi);
            }
            menu.Items.Add(miO);

            var miTheme = new MenuItem { Header = L.MenuTheme };
            foreach (Theme t in Theme.All)
            {
                Theme th = t;
                var mi = new MenuItem
                {
                    Header = th == Theme.Dark ? L.ThemeDark : L.ThemeIvory,
                    IsCheckable = true,
                    IsChecked = (th == Theme.Current)
                };
                mi.Click += delegate
                {
                    Theme.Use(th.Name);
                    _cfg.Theme = th.Name;
                    SaveConfig();
                    ApplyTheme();
                };
                miTheme.Items.Add(mi);
            }
            menu.Items.Add(miTheme);

            var miLang = new MenuItem { Header = L.MenuLanguage };
            foreach (Strings s in I18n.Catalog)
            {
                Strings lang = s;
                var mi = new MenuItem
                {
                    Header = lang.Native,
                    IsCheckable = true,
                    IsChecked = (lang.Code == L.Code)
                };
                mi.Click += delegate
                {
                    I18n.Use(lang.Code);
                    _cfg.Lang = lang.Code;
                    SaveConfig();
                    BuildMenu();   // the menu itself has to be rebuilt translated
                    Redraw();
                };
                miLang.Items.Add(mi);
            }
            menu.Items.Add(miLang);

            var miA = new MenuItem { Header = L.MenuStartWithWindows, IsCheckable = true };
            miA.IsChecked = File.Exists(StartupLnkPath);
            miA.Click += delegate
            {
                try
                {
                    if (miA.IsChecked) CreateStartupShortcut();
                    else if (File.Exists(StartupLnkPath)) File.Delete(StartupLnkPath);
                    // Remember the explicit choice: false is the only value
                    // that stops the automatic re-creation at startup.
                    _cfg.AutoStart = miA.IsChecked;
                    SaveConfig();
                }
                catch { }
            };
            menu.Items.Add(miA);

            var miFs = new MenuItem
            {
                Header = L.MenuHideFullScreen,
                IsCheckable = true,
                IsChecked = _cfg.HideFullScreen ?? true
            };
            miFs.Click += delegate
            {
                _cfg.HideFullScreen = miFs.IsChecked;
                SaveConfig();
                // Unticking it while hidden must bring the widget straight back.
                if (!miFs.IsChecked && _hiddenForFullScreen)
                {
                    _hiddenForFullScreen = false;
                    Visibility = Visibility.Visible;
                }
            };
            menu.Items.Add(miFs);

            var miLog = new MenuItem { Header = L.MenuOpenLog };
            miLog.Click += delegate
            {
                try
                {
                    string log = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "ClaudeWidget\\log.txt");
                    if (File.Exists(log)) System.Diagnostics.Process.Start("notepad.exe", log);
                }
                catch { }
            };
            menu.Items.Add(miLog);

            var miRestart = new MenuItem { Header = L.MenuRestart };
            miRestart.Click += delegate
            {
                try
                {
                    string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    System.Diagnostics.Process.Start(executable);
                }
                catch { }
            };
            menu.Items.Add(miRestart);

            menu.Items.Add(new Separator());
            var miQ = new MenuItem { Header = L.MenuQuit };
            miQ.Click += delegate { Close(); };
            menu.Items.Add(miQ);

            _root.ContextMenu = menu;
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // --feed: we are Claude Code's statusline command, not the
            // widget. Handled before the single-instance sweep below - the
            // helper is spawned on every turn and must never kill the widget.
            foreach (string a in Environment.GetCommandLineArgs())
                if (a == "--feed") Environment.Exit(Feed.RunAsStatusLine());

            // single instance: the new one replaces the old
            var me = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(me.ProcessName))
            {
                if (p.Id != me.Id) { try { p.Kill(); p.WaitForExit(2000); } catch { } }
            }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var app = new Application();
            app.Run(new MainWindow());
        }
    }
}
