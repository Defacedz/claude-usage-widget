# Claude Usage Widget — macOS

The same floating widget as on Windows: an always-on-top panel with the two
coloured gauge bars (5-hour session and weekly limit), the reset countdowns
and the Claude logo. Drag it anywhere, right-click it for the menu.

*Version française plus bas.*

Feature parity with the Windows version: the gauge panel, reset countdowns,
the local usage chart (right-click → *Local usage details*), the two themes
(right-click → Theme: *Dark* or *Ivory*), languages, opacity, hide in
full-screen apps, log, restart, start at login, and the built-in update check
(orange border + *Update available* in the menu, also refreshed on every
*Refresh* click). Choosing *Update available* opens a Terminal window and runs
the installer there, so the build is visible and a failure says why.

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

1. Right-click the widget → *Quit*
2. Delete `~/Applications/ClaudeWidget.app`
3. Delete `~/Library/LaunchAgents/com.defacedz.claudewidget.plist` if you had
   enabled *Start at login*

---

# Version française

Le même widget flottant que sous Windows : un panneau toujours visible avec
les deux barres de jauge colorées (session 5 h et limite hebdomadaire), les
comptes à rebours et le logo Claude. Déplaçable à la souris, clic droit pour
le menu.

Parité avec la version Windows : le panneau de jauges, les comptes à rebours,
le graphique de conso locale (clic droit → *Détail conso locale*), les
langues, l'opacité, le masquage en plein écran, le journal, le redémarrage,
le lancement à l'ouverture de session, et la détection de mise à jour
(contour orange + *Mise à jour disponible* dans le menu).

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

1. Clic droit sur le widget → *Quitter*
2. Supprimer `~/Applications/ClaudeWidget.app`
3. Supprimer `~/Library/LaunchAgents/com.defacedz.claudewidget.plist` si le
   lancement à l'ouverture de session était activé
