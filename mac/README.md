# Claude Usage Widget — macOS

The Claude usage gauges as a **menu-bar item**: `✱ 31% · 60%` (5-hour session ·
weekly limit), coloured orange to red as you approach the limits. Click it for
the details and the reset countdowns.

*Version française plus bas.*

First macOS version: gauges, countdowns, refresh, start at login. The local
usage chart of the Windows version is planned next.

## Requirements

- macOS 12 or later
- Xcode Command Line Tools (free): `xcode-select --install`
- [Claude Code](https://claude.com/claude-code) signed in once on this Mac

## Install

```sh
cd mac
sh install.sh
```

That compiles `ClaudeWidget.swift` **on your machine** and installs the app in
`~/Applications`. At first launch, macOS asks whether the app may read the
"Claude Code-credentials" item in your keychain — click **Always Allow**: that
is where Claude Code keeps the OAuth token the widget needs to query your
usage. Like the Windows version, the widget writes the rotated refresh token
back so Claude Code stays signed in.

Your tokens are sent to `api.anthropic.com`, `platform.claude.com` and
`console.anthropic.com`, and nowhere else. There is no telemetry. The usage
endpoint is not a documented public API and this project is not affiliated
with Anthropic.

## Uninstall

1. Menu bar → the ✱ item → *Quit*
2. Delete `~/Applications/ClaudeWidget.app`
3. Delete `~/Library/LaunchAgents/com.defacedz.claudewidget.plist` if you had
   enabled *Start at login*

---

# Version française

Les jauges d'utilisation Claude dans la **barre de menus** : `✱ 31% · 60%`
(session 5 h · limite hebdomadaire), colorées de l'orange au rouge à
l'approche des limites. Un clic affiche le détail et les comptes à rebours.

Première version macOS : jauges, comptes à rebours, actualisation, lancement à
l'ouverture de session. Le graphique de conso locale de la version Windows
viendra ensuite.

## Prérequis

- macOS 12 ou plus récent
- Les outils en ligne de commande Xcode (gratuits) : `xcode-select --install`
- [Claude Code](https://claude.com/claude-code) connecté une fois sur ce Mac

## Installation

```sh
cd mac
sh install.sh
```

La commande compile `ClaudeWidget.swift` **sur votre machine** et installe
l'app dans `~/Applications`. Au premier lancement, macOS demande si l'app peut
lire l'élément « Claude Code-credentials » du trousseau — cliquez **Toujours
autoriser** : c'est là que Claude Code range le jeton dont le widget a besoin
pour interroger votre usage. Comme la version Windows, le widget y réécrit le
jeton renouvelé pour que Claude Code reste connecté.

Vos jetons ne sont transmis qu'à `api.anthropic.com`, `platform.claude.com` et
`console.anthropic.com`. Aucune télémétrie.

## Désinstallation

1. Barre de menus → l'élément ✱ → *Quitter*
2. Supprimer `~/Applications/ClaudeWidget.app`
3. Supprimer `~/Library/LaunchAgents/com.defacedz.claudewidget.plist` si le
   lancement à l'ouverture de session était activé
