<!--
  Release notes template. Filled by scripts/build/New-ReleaseNotes.ps1:
    {{VERSION}}  e.g. 0.1.0
    {{DATE}}     release date from CHANGELOG.md
    {{CHANGES}}  the matching CHANGELOG.md section
    {{REPO}}     https://github.com/<owner>/<repo>
    {{REPO_SLUG}} <owner>/<repo>
  Keep the structure stable so every release looks the same.
-->
<p align="center">
  <img src="{{REPO}}/raw/v{{VERSION}}/assets/branding/logo-256.png" width="88" alt="Snapdate">
</p>

<h2 align="center">Snapdate {{VERSION}}</h2>
<p align="center"><i>Sort your photos by the day they were taken · Fotos nach Aufnahmedatum sortieren</i></p>

## ⬇️ Install (recommended) · Installieren (empfohlen)

Download the **installer** for your PC:

| Your PC | Installer |
|---|---|
| 💻 Most Windows PCs (Intel / AMD) | **`Snapdate-Setup-v{{VERSION}}-win-x64.exe`** |
| 📱 ARM PCs (Surface Pro X, Copilot+ PCs) | **`Snapdate-Setup-v{{VERSION}}-win-arm64.exe`** |

Double-click it and choose *Install for me only* (no administrator needed) or *Install for all users*.
Snapdate appears in the Start menu (and optionally on the desktop). Installed copies can update themselves from the banner in the app. Windows 10 and 11.

*Laden Sie den **Installer** für Ihren PC herunter (x64 für die meisten PCs, arm64 für ARM-PCs), doppelklicken Sie darauf und wählen Sie
„Installation nur für Sie“ (keine Administratorrechte nötig) oder „Installation für alle Benutzer“.
Snapdate erscheint im Startmenü (auf Wunsch auch auf dem Desktop). Installierte Kopien können sich über den Hinweis in der App selbst aktualisieren. Für Windows 10 und 11.*

## 🧳 Portable (no installation) · Portabel (ohne Installation)

| Your PC | Portable |
|---|---|
| 💻 Most Windows PCs (Intel / AMD) | `Snapdate-v{{VERSION}}-win-x64-portable.exe` |
| 📱 ARM PCs | `Snapdate-v{{VERSION}}-win-arm64-portable.exe` |

A single exe: download, double-click, nothing is installed. It does not update itself: when a new version is out,
Snapdate shows a link to the download page so you can download the new file again.
*Eine einzelne exe: herunterladen, doppelklicken, nichts wird installiert. Sie aktualisiert sich nicht selbst: Gibt es eine neue Version,
zeigt Snapdate einen Link zur Download-Seite, damit Sie die neue Datei erneut herunterladen.*

> ⚠️ **Windows SmartScreen may warn** (“Windows protected your PC”) because Snapdate is new and not yet code-signed:
> choose **More info → Run anyway**. ·
> *Windows SmartScreen zeigt eventuell eine Warnung („Der Computer wurde durch Windows geschützt“), weil Snapdate neu und noch nicht
> signiert ist: **Weitere Informationen → Trotzdem ausführen**.*

🔒 Snapdate works offline — except the optional update check, which only asks GitHub for the newest version number (can be turned off in *About*).
*Snapdate arbeitet offline — bis auf die optionale Update-Prüfung, die bei GitHub nur nach der neuesten Versionsnummer fragt (unter „Über Snapdate“ abschaltbar).*

## 📖 Help · Hilfe

- 🇬🇧 [User guide (English)]({{REPO}}/blob/v{{VERSION}}/docs/user-guide.en.md)
- 🇩🇪 [Anleitung (Deutsch)]({{REPO}}/blob/v{{VERSION}}/docs/user-guide.de.md)

## ✨ What's new · Was ist neu ({{DATE}})

{{CHANGES}}

## 🔍 Verify your download · Download prüfen

- Checksums · Prüfsummen: `SHA256SUMS.txt` (PowerShell: `Get-FileHash <file> -Algorithm SHA256`)
- Build provenance: `gh attestation verify <file> --repo {{REPO_SLUG}}`
- Software bill of materials: `sbom.cdx.json`
- The `.sig` files next to each download are used by Snapdate's own update check; you don't need them. ·
  *Die `.sig`-Dateien neben jedem Download nutzt die Update-Funktion von Snapdate selbst; Sie brauchen sie nicht.*

**Full changelog:** [CHANGELOG.md]({{REPO}}/blob/v{{VERSION}}/CHANGELOG.md)
