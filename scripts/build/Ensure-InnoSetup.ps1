#Requires -Version 7.4
<#
.SYNOPSIS
    Makes sure Inno Setup 6 (ISCC.exe) is available on a CI runner.

.DESCRIPTION
    GitHub's Windows images may ship Inno Setup; if ISCC.exe is found nothing is downloaded. Otherwise
    the pinned official installer is downloaded and run only if its SHA256 matches the value below
    (taken from the winget manifest of JRSoftware.InnoSetup, not from the download itself), then
    installed per user and silently. build.ps1 -Target Installer finds ISCC.exe on its own
    (PATH, per-user install, Program Files).

    To update the pin: `winget show JRSoftware.InnoSetup --version <x.y.z>` prints Installer Url and
    Installer SHA256; change all three values below together.
#>
[CmdletBinding()]
param(
    [string] $Version = '6.7.3',
    [string] $Url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe',
    [string] $Sha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-Iscc {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($variable in 'LOCALAPPDATA', 'ProgramFiles(x86)', 'ProgramFiles') {
        $base = [Environment]::GetEnvironmentVariable($variable)
        if (-not $base) { continue }
        $candidate = if ($variable -eq 'LOCALAPPDATA') { Join-Path $base 'Programs' 'Inno Setup 6' 'ISCC.exe' } else { Join-Path $base 'Inno Setup 6' 'ISCC.exe' }
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-Iscc
if ($iscc) {
    Write-Host "Inno Setup already present: $iscc"
    return
}

Write-Host "Inno Setup not found - downloading $Version from $Url"
$dir = Join-Path ([IO.Path]::GetTempPath()) "snapdate-innosetup-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $installer = Join-Path $dir "innosetup-$Version.exe"
    Invoke-WebRequest -Uri $Url -OutFile $installer -UseBasicParsing

    $actual = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256.ToLowerInvariant()) {
        throw "Inno Setup installer hash mismatch: expected $Sha256, got $actual. Not running it."
    }
    Write-Host "SHA256 verified: $actual"

    $process = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/CURRENTUSER', '/NORESTART' -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "The Inno Setup installer exited with code $($process.ExitCode)." }
}
finally {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}

$iscc = Find-Iscc
if (-not $iscc) { throw 'ISCC.exe not found after installing Inno Setup.' }
Write-Host "Inno Setup installed: $iscc"
