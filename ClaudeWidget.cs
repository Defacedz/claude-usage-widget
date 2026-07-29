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
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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

        public string ErrNotSignedIn, ErrBadResponse;
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
                MenuQuit = "Quit",
                ErrNotSignedIn = "Claude Code is not signed in (run it once)",
                ErrBadResponse = "Unreadable API response"
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
                MenuQuit = "Quitter",
                ErrNotSignedIn = "Claude Code n'est pas connecté (lance-le une fois)",
                ErrBadResponse = "Réponse de l'API illisible"
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
                MenuQuit = "Salir",
                ErrNotSignedIn = "Claude Code no ha iniciado sesión (ejecútalo una vez)",
                ErrBadResponse = "Respuesta de la API ilegible"
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
                MenuQuit = "Beenden",
                ErrNotSignedIn = "Claude Code ist nicht angemeldet (einmal starten)",
                ErrBadResponse = "Unlesbare API-Antwort"
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
            req.UserAgent = "ClaudeWidget";
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
        const uint MONITOR_DEFAULTTONEAREST = 2;

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

        static Strings L { get { return I18n.T; } }

        // Width reserved for the logo; the rows keep the same total width as
        // before, so the layout is unchanged apart from the logo itself.
        const double LogoColumn = 20;

        static string CfgDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeWidget"); }
        }
        static string CfgPath { get { return System.IO.Path.Combine(CfgDir, "config.json"); } }

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
            Opacity = (_cfg.Opacity >= 0.2 && _cfg.Opacity <= 1.0) ? _cfg.Opacity : 1.0;

            _root = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = B("#F21E2029"),
                BorderBrush = B("#22FFFFFF"),
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
                Refresh();
                _poll = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
                _poll.Tick += delegate { Refresh(); };
                _poll.Start();
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                t2.Tick += delegate { AssertTop(); };
                t2.Start();
                // redraw every minute so the countdowns stay alive
                var t3 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
                t3.Tick += delegate { if (_last != null) Render(); };
                t3.Start();
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
                Background = B("#2A2D3A"),
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
            _root.BorderBrush = B("#22FFFFFF");
            // A Grid centres the text properly; a bare TextBlock in a
            // StackPanel would sit at the top of the 36px band.
            var row = new Grid { Height = 36 };
            row.Children.Add(new TextBlock
            {
                Text = msg,
                FontSize = 10,
                Foreground = B("#9BA0B5"),
                VerticalAlignment = VerticalAlignment.Center
            });
            _rows.Children.Add(row);
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
                Foreground = B("#6C7086"),
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
                    Foreground = B("#B8BCCB"),
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
            else ShowMessage(_lastErr == null ? L.Loading : string.Format(L.Offline, _lastErr));
        }

        void Render()
        {
            if (_last == null) return;
            _rows.Children.Clear();
            var tips = new List<string>();
            AddPart(L.Short5h, L.Session5h, _last.five_hour, tips);
            AddPart(L.Short7d, L.Week, _last.seven_day, tips);
            tips.Add(string.Format(L.Updated, _lastTs.ToString("HH:mm")));
            if (_lastErr != null)
            {
                // Past two missed cycles the numbers on screen mean nothing any
                // more, so we fade them out to make the stall visible.
                TimeSpan age = DateTime.Now - _lastTs;
                bool stale = age.TotalMinutes >= 12;
                _root.BorderBrush = B(stale ? "#CCE05252" : "#99E8A33D");
                _rows.Opacity = stale ? 0.45 : 1.0;
                tips.Add(string.Format(L.FrozenFor, FmtAge(age), _lastErr));
            }
            else
            {
                _root.BorderBrush = B("#22FFFFFF");
                _rows.Opacity = 1.0;
            }
            _root.ToolTip = string.Join(Environment.NewLine, tips.ToArray());
        }

        void Refresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Usage u = null;
                string err = null;
                try { u = Api.GetUsage(); }
                catch (Exception e) { err = e.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (u != null)
                    {
                        if (_lastErr != null) Api.Log("refresh recovered");
                        _last = u; _lastTs = DateTime.Now; _lastErr = null;
                    }
                    else
                    {
                        Api.Log("refresh failed: " + err);
                        _lastErr = err;
                    }
                    Redraw();
                    // while broken, poll every minute instead of every five
                    if (_poll != null)
                    {
                        var wanted = (err == null) ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(1);
                        if (_poll.Interval != wanted) _poll.Interval = wanted;
                    }
                }));
            });
        }

        // ---------- menu ----------
        void BuildMenu()
        {
            var menu = new ContextMenu();

            var miR = new MenuItem { Header = L.MenuRefresh };
            miR.Click += delegate { Refresh(); };
            menu.Items.Add(miR);

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
            string lnkPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Claude Widget.lnk");
            miA.IsChecked = File.Exists(lnkPath);
            miA.Click += delegate
            {
                try
                {
                    if (miA.IsChecked)
                    {
                        var t = Type.GetTypeFromProgID("WScript.Shell");
                        dynamic shell = Activator.CreateInstance(t);
                        dynamic lnk = shell.CreateShortcut(lnkPath);
                        lnk.TargetPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        lnk.Save();
                    }
                    else if (File.Exists(lnkPath)) File.Delete(lnkPath);
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
