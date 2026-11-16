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

| Your PC | File |
|---|---|
| 💻 Most Windows PCs (Intel / AMD) | **`Snapdate-v{{VERSION}}-win-x64.exe`** |
| 📱 ARM PCs (Surface Pro X, Copilot+ PCs) | `Snapdate-v{{VERSION}}-win-arm64.exe` |

Just download and double-click — **nothing to install**. Windows 10 and 11.
*Einfach herunterladen und doppelklicken — keine Installation nötig.*

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
