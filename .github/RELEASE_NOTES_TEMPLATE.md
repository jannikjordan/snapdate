<!--
  Release notes template. Filled by scripts/build/New-ReleaseNotes.ps1:
    {{VERSION}}  e.g. 0.1.0
    {{DATE}}     release date from CHANGELOG.md
    {{CHANGES}}  the matching CHANGELOG.md section
    {{REPO}}     https://github.com/<owner>/<repo>
  Keep the structure stable so every release looks the same.
-->
<p align="center">
  <img src="{{REPO}}/raw/v{{VERSION}}/assets/branding/logo-256.png" width="88" alt="Snapdate">
</p>

<h2 align="center">Snapdate {{VERSION}}</h2>
<p align="center"><i>Sort your photos by the day they were taken · Fotos nach Aufnahmedatum sortieren</i></p>

## ⬇️ Download

| Your PC | Installer (recommended) | Portable (no installation) |
|---|---|---|
| 💻 Most Windows PCs (Intel / AMD) | **`Snapdate-Setup-v{{VERSION}}-win-x64.exe`** | `Snapdate-v{{VERSION}}-win-x64-portable.exe` |
| 📱 ARM PCs (Surface Pro X, Copilot+ PCs) | `Snapdate-Setup-v{{VERSION}}-win-arm64.exe` | `Snapdate-v{{VERSION}}-win-arm64-portable.exe` |

The **installer** adds Snapdate to the Start menu (and optionally the desktop), for you only or for all users.
The **portable** file is a single exe: download and double-click, nothing is installed. Windows 10 and 11.
*Der **Installer** legt Snapdate im Startmenü (optional auch auf dem Desktop) an, nur für Sie oder für alle Benutzer.
Die **portable** Datei ist eine einzelne exe: herunterladen und doppelklicken, nichts wird installiert.*

## 📖 Help · Hilfe

- 🇬🇧 [User guide (English)]({{REPO}}/blob/v{{VERSION}}/docs/user-guide.en.md)
- 🇩🇪 [Anleitung (Deutsch)]({{REPO}}/blob/v{{VERSION}}/docs/user-guide.de.md)

## ✨ What's new · Was ist neu ({{DATE}})

{{CHANGES}}

## 🔒 Verify your download

- Checksums: `SHA256SUMS.txt` (PowerShell: `Get-FileHash <file> -Algorithm SHA256`)
- Build provenance: `gh attestation verify <file> --repo {{REPO_SLUG}}`
- Software bill of materials: `sbom.cdx.json`

> ⚠️ The executable is not code-signed yet. Windows SmartScreen may warn on first start:
> choose **More info → Run anyway**. ·
> *Die Datei ist noch nicht signiert. Bei der Warnung von Windows SmartScreen:
> **Weitere Informationen → Trotzdem ausführen**.*

**Full changelog:** [CHANGELOG.md]({{REPO}}/blob/v{{VERSION}}/CHANGELOG.md)
