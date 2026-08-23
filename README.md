# Claude Usage Widget

A tiny always-on-top gauge for Windows showing how much of your Claude usage
limits you have burned through, and when they reset.

*Read this in [Français](README.fr.md).*

<img src="docs/screenshot.png" alt="The widget showing a 31% five-hour session and 60% weekly usage" width="382">

It sits above the taskbar and never disappears behind it, because the
executable is built with the `uiAccess` privilege — the same one the Magnifier
and the on-screen keyboard use.

## Features

- 5-hour session and 7-day usage, with a countdown to each reset
- Colour shifts from orange to red as you approach the limit
- **Gets out of the way of games**: hides itself while a full-screen app is in
  the foreground, including borderless-fullscreen, and stops re-asserting
  topmost so it cannot kick a game out of its display mode
- Hover for the full breakdown, drag to move, position is remembered
- Goes visibly stale — amber then red border, gauges fade — when the data is
  more than 12 minutes old, so a frozen number never looks like a fresh one
- Adjustable opacity, optional start with Windows
- **English, Français, Español, Deutsch** — right-click → Language
- **Local usage chart** — right-click → *Local usage details*: a stacked daily
  chart of new tokens (cache writes vs prompts + answers) over the current and
  previous month, computed from your local Claude Code transcripts. Nothing
  leaves your machine.
- **Built-in updates** — the widget checks this repository every 6 hours; when
  a newer version is published the border turns Claude-orange and an *Update
  available* entry appears at the top of the right-click menu
- Claude-styled right-click menu — dark, rounded, orange highlight
- Re-asserts topmost only when the taskbar has actually covered it, instead of
  twice a second — no more flicker

<img src="docs/screenshot-usage.png" alt="The local usage chart: stacked daily new tokens over two months" width="480">

**macOS**: a menu-bar version lives in [`mac/`](mac/README.md) — same gauges,
same keychain-safe token handling, built from source with the free Xcode
Command Line Tools.

## Requirements

- Windows 10 or 11
- .NET Framework 4.x (present on every supported Windows — nothing to install)
- [Claude Code](https://claude.com/claude-code) installed and signed in once

## Install

Paste this into **PowerShell** and accept the administrator prompt:

```powershell
irm https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/web-install.ps1 | iex
```

Or from **cmd.exe**:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/web-install.ps1 | iex"
```

That downloads this repository to a temporary folder and runs `Installer.ps1`.
If you would rather see what you are running first — which is the sensible
habit with any `| iex` command, and doubly so for a program that touches your
credentials — clone the repository and double-click `Installer.bat` instead.

Either way the window shows `[OK]` and the widget appears in the bottom-left
corner. Right-click it for language, opacity, autostart and quit.

### What the installer does

- Builds `ClaudeWidget.cs` **on your machine** with the C# compiler already
  included in Windows. Nothing is downloaded, no build toolchain is needed.
- Creates a self-signed certificate `CN=ClaudeWidget Local` and adds it to the
  machine's trusted root store. Windows only grants `uiAccess` to a signed
  executable installed under `Program Files`, so both steps are mandatory for
  the widget to stay above the taskbar. **Adding a root certificate is not a
  trivial change** — see [Uninstall](#uninstall) to remove it.
- Copies the signed binary to `C:\Program Files\ClaudeWidget\` and starts it.

## What it reads and writes

This program handles your Claude Code credentials. In full:

| Path | Access | Why |
|---|---|---|
| `%USERPROFILE%\.claude\.credentials.json` | read **and write** | reads the OAuth token to query your usage; writes the refreshed token back (see below) |
| `%APPDATA%\ClaudeWidget\tokens.json` | write | local token cache |
| `%APPDATA%\ClaudeWidget\config.json` | write | position, opacity, language |
| `%APPDATA%\ClaudeWidget\log.txt` | write | diagnostics, capped at 128 KB — **never contains tokens** |
| `%USERPROFILE%\.claude\projects\**\*.jsonl` | read | your local Claude Code transcripts, summed for the local usage chart — read only, nothing is sent anywhere |

**Why it writes back to `.credentials.json`:** the OAuth server rotates refresh
tokens — using one invalidates the previous one. An earlier version kept the
new token to itself, which left Claude Code holding a dead token and forced a
`/login` every few hours. The widget therefore patches `accessToken`,
`refreshToken` and `expiresAt` back into the file, in place, leaving every
other field untouched.

Your tokens are sent to `api.anthropic.com`, `platform.claude.com` and
`console.anthropic.com`, and nowhere else. There is no telemetry.

The usage endpoint (`/api/oauth/usage`) is not a documented public API. It can
change or disappear without notice, and this project is not affiliated with
Anthropic.

## Adding a language

Everything lives in the `I18n` class in `ClaudeWidget.cs`. Copy one of the
`English()` / `French()` blocks, translate the ~25 values, and append it to
`Catalog`:

```csharp
public static readonly Strings[] Catalog = { English(), French(), Spanish(), German(), Italian() };
```

The language menu and the config file are both driven by `Catalog` — there is
nothing else to wire up. Keep `Short5h` / `Short7d` very short, they render in a
24-pixel column. Save the file as **UTF-8 with a BOM**; pull requests welcome.

## Troubleshooting

**The numbers stop updating.** Right-click → *Open log*
(`%APPDATA%\ClaudeWidget\log.txt`) gives the exact reason for the last failure.

**Nothing refreshes at all, and the log shows timeouts.** The widget requests
IPv4 on purpose: on a router that advertises an IPv6 prefix without actually
routing it, an IPv6 request hangs until the timeout and the gauge freezes. It
switches back automatically if IPv6 is the only working path.

**`Claude Code is not signed in`.** Run Claude Code once so that
`~/.claude/.credentials.json` exists.

**The widget vanished.** A full-screen application is in the foreground; it
comes back on its own. Untick *Hide in full-screen apps* to keep it visible
over full-screen video, at the cost of it reappearing over games.

## Uninstall

1. Right-click the widget → *Quit*
2. Delete `C:\Program Files\ClaudeWidget`
3. Delete `%APPDATA%\ClaudeWidget`
4. Remove the certificate: `certlm.msc` → *Trusted Root Certification
   Authorities* → *Certificates* → delete **ClaudeWidget Local**, then do the
   same under *Trusted Publishers* and *Personal*

## License

MIT — see [LICENSE](LICENSE).
