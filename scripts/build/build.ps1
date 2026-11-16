#Requires -Version 7.4
<#
.SYNOPSIS
    Build, test, publish and package Snapdate.

.DESCRIPTION
    Developer and CI entry point. Produces release artefacts in artifacts/release:
      Snapdate-v<ver>-win-<arch>.exe, Snapdate-v<ver>-win-<arch>.zip,
      SHA256SUMS.txt and sbom.cdx.json (when the CycloneDX tool is available).

.PARAMETER Target
    Clean | Restore | Audit | Test | Publish | Package | Sign | All

.PARAMETER Version
    Overrides the version from Directory.Build.props (e.g. from a git tag "v0.1.0").

.PARAMETER Runtime
    One or more runtime identifiers to publish. Default: win-x64, win-arm64.

.PARAMETER Mode
    Aot (default) needs the MSVC linker (Visual Studio "Desktop development with C++").
    SingleFile produces a self-contained single-file exe without native compilation.
    Auto uses Aot when the linker is found, otherwise SingleFile.

.EXAMPLE
    pwsh scripts/build/build.ps1 -Target Test
.EXAMPLE
    pwsh scripts/build/build.ps1 -Target All -Version 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('Clean', 'Restore', 'Audit', 'Test', 'Publish', 'Package', 'Sign', 'All')]
    [string] $Target = 'Test',
    [string] $Version,
    [string[]] $Runtime = @('win-x64', 'win-arm64'),
    [ValidateSet('Auto', 'Aot', 'SingleFile')]
    [string] $Mode = 'Auto',
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Solution = Join-Path $Root 'Snapdate.slnx'
$AppProject = Join-Path $Root 'src' 'Snapdate.App' 'Snapdate.App.csproj'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts 'publish'
$ReleaseDir = Join-Path $Artifacts 'release'
$TestResults = Join-Path $Artifacts 'test-results'

function Write-Step([string] $Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Magenta
}

function Get-ProjectVersion {
    if ($Version) { return $Version.TrimStart('v') }
    [xml] $props = Get-Content (Join-Path $Root 'Directory.Build.props')
    $prefix = $props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ } | Select-Object -First 1
    if (-not $prefix) { throw 'VersionPrefix not found in Directory.Build.props.' }
    return $prefix
}

function Test-MsvcLinker {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer' 'vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $false }
    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    return [bool] $path
}

function Invoke-Clean {
    Write-Step 'Clean'
    if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
    Get-ChildItem $Root -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } |
        Remove-Item -Recurse -Force
}

function Invoke-Restore {
    Write-Step 'Restore (locked mode)'
    dotnet restore $Solution --locked-mode
}

function Invoke-Audit {
    Write-Step 'Audit NuGet packages for known vulnerabilities'
    $report = dotnet list $Solution package --vulnerable --include-transitive 2>&1 | Out-String
    Write-Host $report
    if ($report -match 'has the following vulnerable packages') {
        throw 'Vulnerable packages found. Update them in Directory.Packages.props.'
    }
}

function Invoke-Test {
    Write-Step 'Build and test'
    $ver = Get-ProjectVersion
    dotnet build $Solution -c $Configuration --no-restore -p:Version=$ver
    New-Item -ItemType Directory -Force -Path $TestResults | Out-Null
    dotnet test --solution $Solution -c $Configuration --no-build `
        --results-directory $TestResults --report-xunit-trx
}

function Invoke-Publish {
    $ver = Get-ProjectVersion
    $useAot = switch ($Mode) {
        'Aot' { $true }
        'SingleFile' { $false }
        default { Test-MsvcLinker }
    }
    if ($Mode -eq 'Auto' -and -not $useAot) {
        Write-Warning 'MSVC linker not found: falling back to self-contained single-file publish (no Native AOT).'
    }

    foreach ($rid in $Runtime) {
        Write-Step "Publish $rid ($(if ($useAot) { 'Native AOT' } else { 'single-file' }))"
        $out = Join-Path $PublishDir $rid
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }

        # --no-restore: the locked restore (Invoke-Restore) already covers every runtime
        # identifier, including the ILCompiler packs, so nothing unpinned is pulled here.
        $common = @(
            $AppProject, '-c', $Configuration, '-r', $rid, '-o', $out, '--no-restore',
            "-p:Version=$ver", '-p:DebugType=none'
        )
        if ($useAot) {
            dotnet publish @common
        }
        else {
            dotnet publish @common -p:PublishAot=false --self-contained true `
                -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
                -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial
        }

        # Only the exe ships; drop doc XML and native debug symbols that packages copy along.
        Get-ChildItem $out -File | Where-Object Extension -in '.xml', '.pdb' | Remove-Item -Force
        if (-not (Test-Path (Join-Path $out 'Snapdate.exe'))) { throw "Publish for $rid produced no Snapdate.exe." }
    }
}

function Invoke-Package {
    $ver = Get-ProjectVersion
    Write-Step "Package v$ver"
    if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

    foreach ($rid in $Runtime) {
        $exe = Join-Path $PublishDir $rid 'Snapdate.exe'
        if (-not (Test-Path $exe)) { throw "Missing $exe - run -Target Publish first." }

        $name = "Snapdate-v$ver-$rid"
        Copy-Item $exe (Join-Path $ReleaseDir "$name.exe")

        $staging = Join-Path $Artifacts 'staging' $name
        if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $staging | Out-Null
        Copy-Item $exe $staging
        foreach ($doc in 'README.md', 'CHANGELOG.md', 'LICENSE') {
            $src = Join-Path $Root $doc
            if (Test-Path $src) { Copy-Item $src $staging }
        }
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath (Join-Path $ReleaseDir "$name.zip") -CompressionLevel Optimal
    }

    # SBOM (optional tool: dotnet tool install --global CycloneDX)
    if (Get-Command dotnet-CycloneDX -ErrorAction SilentlyContinue) {
        Write-Step 'SBOM (CycloneDX)'
        dotnet-CycloneDX $AppProject --output $ReleaseDir --filename 'sbom.cdx.json' --json
    }
    else {
        Write-Warning 'CycloneDX tool not installed - skipping SBOM.'
    }

    Write-Step 'Checksums'
    $lines = Get-ChildItem $ReleaseDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    Set-Content -Path (Join-Path $ReleaseDir 'SHA256SUMS.txt') -Value $lines -Encoding utf8NoBOM
    Get-ChildItem $ReleaseDir | Format-Table Name, Length -AutoSize | Out-String | Write-Host
}

function Invoke-Sign {
    Write-Step 'Sign'
    # Optional Authenticode signing. Provide SNAPDATE_SIGN_PFX (base64) and SNAPDATE_SIGN_PASSWORD.
    if (-not $env:SNAPDATE_SIGN_PFX) {
        Write-Warning 'No signing certificate configured - skipping.'
        return
    }
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }

    $pfx = Join-Path ([IO.Path]::GetTempPath()) "snapdate-$([guid]::NewGuid()).pfx"
    try {
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SNAPDATE_SIGN_PFX))
        Get-ChildItem $PublishDir -Recurse -Filter 'Snapdate.exe' | ForEach-Object {
            & $signtool sign /fd SHA256 /td SHA256 /tr 'http://timestamp.digicert.com' `
                /f $pfx /p $env:SNAPDATE_SIGN_PASSWORD $_.FullName
        }
    }
    finally {
        Remove-Item $pfx -Force -ErrorAction SilentlyContinue
    }
}

Push-Location $Root
try {
    switch ($Target) {
        'Clean' { Invoke-Clean }
        'Restore' { Invoke-Restore }
        'Audit' { Invoke-Restore; Invoke-Audit }
        'Test' { Invoke-Restore; Invoke-Test }
        'Publish' { Invoke-Restore; Invoke-Publish }
        'Package' { Invoke-Package }
        'Sign' { Invoke-Sign }
        'All' {
            Invoke-Clean
            Invoke-Restore
            Invoke-Audit
            Invoke-Test
            Invoke-Publish
            Invoke-Sign
            Invoke-Package
        }
    }
    Write-Host ""
    Write-Host "Done: $Target" -ForegroundColor Green
}
finally {
    Pop-Location
}
