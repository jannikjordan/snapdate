#Requires -Version 7.4
<#
.SYNOPSIS
    Renders release notes from .github/RELEASE_NOTES_TEMPLATE.md and CHANGELOG.md.

.PARAMETER Version
    Version without leading "v", e.g. 0.1.0. Defaults to VersionPrefix in Directory.Build.props.

.PARAMETER Repository
    "owner/name" on GitHub. Defaults to $env:GITHUB_REPOSITORY, then the origin remote.

.PARAMETER OutFile
    Where to write the notes. Defaults to artifacts/release-notes.md.

.EXAMPLE
    pwsh scripts/build/New-ReleaseNotes.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Repository,
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

if (-not $Version) {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1
}
$Version = $Version.TrimStart('v')

if (-not $Repository) { $Repository = $env:GITHUB_REPOSITORY }
if (-not $Repository) {
    $url = git -C $root remote get-url origin
    if ($url -match 'github\.com[:/](?<slug>[^/]+/[^/.]+)') { $Repository = $Matches.slug }
}
if (-not $Repository) { throw 'Could not determine the GitHub repository; pass -Repository owner/name.' }

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$pattern = "(?ms)^## \[$([regex]::Escape($Version))\] - (?<date>[^\r\n]+)\r?\n(?<body>.*?)(?=^## \[|\z)"
$match = [regex]::Match($changelog, $pattern)
if (-not $match.Success) { throw "CHANGELOG.md has no section '## [$Version] - <date>'." }

# Markdown link reference definitions ("[0.1.0]: https://…") belong to the changelog, not the notes.
$changes = [regex]::Replace($match.Groups['body'].Value, '(?m)^\[[^\]]+\]:\s+\S+\s*$', '').Trim()

$template = Get-Content (Join-Path $root '.github' 'RELEASE_NOTES_TEMPLATE.md') -Raw
# Drop the explanatory HTML comment at the top of the template.
$template = [regex]::Replace($template, '(?s)^<!--.*?-->\s*', '')

$notes = $template.
    Replace('{{VERSION}}', $Version).
    Replace('{{DATE}}', $match.Groups['date'].Value.Trim()).
    Replace('{{CHANGES}}', $changes).
    Replace('{{REPO_SLUG}}', $Repository).
    Replace('{{REPO}}', "https://github.com/$Repository")

if (-not $OutFile) { $OutFile = Join-Path $root 'artifacts' 'release-notes.md' }
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
Set-Content -Path $OutFile -Value $notes -Encoding utf8NoBOM
Write-Host "Release notes written to $OutFile"
