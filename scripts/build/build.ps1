#Requires -Version 7.4
<#
.SYNOPSIS
    Build, test, publish and package Snapdate.

.DESCRIPTION
    Developer and CI entry point. Produces release artefacts in artifacts/release:
      Snapdate-v<ver>-win-<arch>.exe, Snapdate-v<ver>-win-<arch>.zip,
      SHA256SUMS.txt and sbom.cdx.json (when the CycloneDX tool is available).
    SignRelease then adds a detached update signature (<file>.sig) for each of them
    (ECDSA P-256 / SHA-256, see docs/adr/0005-update-signatures.md).

.PARAMETER Target
    Clean | Restore | Audit | Test | Publish | Package | Sign | SignRelease | All

.PARAMETER Version
    Overrides the version from Directory.Build.props (e.g. from a git tag "v0.1.0").

.PARAMETER Runtime
    One or more runtime identifiers to publish. Default: win-x64, win-arm64.

.PARAMETER SigningKeyPath
    SignRelease only: path to a PKCS#8 PEM private key (for local tests with a throwaway key).
    Without it the key is read from the environment variable SNAPDATE_UPDATE_SIGNING_KEY.

.PARAMETER PublicKeyDir
    SignRelease only: folder with the public key(s) (*.pem) that every signature is verified
    against. Default: the keys embedded in the app. Only override it for local tests.

.PARAMETER SkipUpdateSignature
    -Target All only: do not run SignRelease (CI runs it as a separate step so that the
    private key is never exposed to the build).

.EXAMPLE
    pwsh scripts/build/build.ps1 -Target Test

.EXAMPLE
    pwsh scripts/build/build.ps1 -Target All -Version 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('Clean', 'Restore', 'Audit', 'Test', 'Publish', 'Package', 'Sign', 'SignRelease', 'All')]
    [string] $Target = 'Test',
    [string] $Version,
    [string[]] $Runtime = @('win-x64', 'win-arm64'),
    [string] $Configuration = 'Release',
    [string] $SigningKeyPath,
    [string] $PublicKeyDir,
    [switch] $SkipUpdateSignature
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

    foreach ($rid in $Runtime) {
        Write-Step "Publish $rid (self-contained single file)"
        $out = Join-Path $PublishDir $rid
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }

        # One exe that contains the .NET runtime, all assemblies and the native libraries
        # (Skia, HarfBuzz, ANGLE). Native libraries are extracted to a per-user folder and
        # loaded from there only, never from PATH. See ADR 0003.
        dotnet publish $AppProject -c $Configuration -r $rid -o $out `
            -p:Version=$ver -p:DebugType=none -p:RestoreLockedMode=true `
            --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true

        $files = @(Get-ChildItem $out -File | Where-Object Extension -notin '.xml', '.pdb')
        if ($files.Count -ne 1 -or $files[0].Name -ne 'Snapdate.exe') {
            throw "Publish for $rid must produce exactly Snapdate.exe, got: $($files.Name -join ', ')"
        }
        Get-ChildItem $out -File | Where-Object Extension -in '.xml', '.pdb' | Remove-Item -Force

        Invoke-SelfTest (Join-Path $out 'Snapdate.exe') $rid
    }
}

function Invoke-SelfTest([string] $Exe, [string] $Rid) {
    $hostRid = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
    if ($Rid -ne $hostRid) {
        Write-Warning "Skipping self-test for $Rid on a $hostRid machine."
        return
    }

    Write-Step "Self-test $Rid (start platform, load views, both languages)"
    # Run from an empty folder so no stray DLL next to the working directory can be picked up.
    $sandbox = Join-Path ([IO.Path]::GetTempPath()) "snapdate-selftest-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    try {
        $copy = Join-Path $sandbox 'Snapdate.exe'
        Copy-Item $Exe $copy
        $process = Start-Process -FilePath $copy -ArgumentList '--self-test' -WorkingDirectory $sandbox `
            -PassThru -Wait -RedirectStandardError (Join-Path $sandbox 'stderr.txt')
        if ($process.ExitCode -ne 0) {
            Get-Content (Join-Path $sandbox 'stderr.txt') -ErrorAction SilentlyContinue | Write-Host
            throw "Self-test of $Rid failed with exit code $($process.ExitCode)."
        }
        Write-Host "Self-test passed." -ForegroundColor Green
    }
    finally {
        Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
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
        dotnet-CycloneDX $AppProject --output $ReleaseDir --filename 'sbom.cdx.json' --output-format Json
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

function Invoke-SignRelease {
    Write-Step 'Sign release files (update signatures)'
    if (-not (Test-Path $ReleaseDir)) { throw "Missing $ReleaseDir - run -Target Package first." }

    $keyDir = if ($PublicKeyDir) { $PublicKeyDir } else { Join-Path $Root 'src' 'Snapdate.Infrastructure' 'Updates' 'Keys' }
    $publicKeys = @(Get-ChildItem $keyDir -Filter '*.pem' -File -ErrorAction SilentlyContinue)
    if ($publicKeys.Count -eq 0) { throw "No public key (*.pem) found in $keyDir." }

    $pem = $null
    if ($SigningKeyPath) {
        if (-not (Test-Path $SigningKeyPath)) { throw "Signing key file not found: $SigningKeyPath" }
        $pem = Get-Content $SigningKeyPath -Raw
    }
    elseif ($env:SNAPDATE_UPDATE_SIGNING_KEY) {
        $pem = $env:SNAPDATE_UPDATE_SIGNING_KEY
    }

    if (-not $pem) {
        if ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_REF_TYPE -eq 'tag') {
            throw 'SNAPDATE_UPDATE_SIGNING_KEY is not set: a tag build must not produce unsigned release files.'
        }
        Write-Warning 'No update signing key (SNAPDATE_UPDATE_SIGNING_KEY or -SigningKeyPath) - skipping update signatures.'
        return
    }

    $files = @(Get-ChildItem $ReleaseDir -File | Where-Object Extension -ne '.sig' | Sort-Object Name)
    if ($files.Count -eq 0) { throw "No files to sign in $ReleaseDir." }
    Get-ChildItem $ReleaseDir -File -Filter '*.sig' | Remove-Item -Force

    $format = [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
    $sha256 = [Security.Cryptography.HashAlgorithmName]::SHA256
    $signer = [Security.Cryptography.ECDsa]::Create()
    $verifiers = @()
    $completed = $false
    try {
        try { $signer.ImportFromPem($pem) }
        catch { throw 'The update signing key is not a valid PEM private key.' }
        if ($signer.KeySize -ne 256) { throw 'The update signing key must be an ECDSA P-256 key.' }

        foreach ($key in $publicKeys) {
            $v = [Security.Cryptography.ECDsa]::Create()
            $v.ImportFromPem((Get-Content $key.FullName -Raw))
            $verifiers += $v
        }

        foreach ($file in $files) {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $signature = $signer.SignData($bytes, $sha256, $format)
            if ($signature.Length -ne 64) { throw "Unexpected signature length for $($file.Name)." }
            $sigPath = "$($file.FullName).sig"
            [IO.File]::WriteAllText($sigPath, [Convert]::ToBase64String($signature) + "`n", [Text.Encoding]::ASCII)

            # Verify straight away against the public key(s) shipped in the app.
            $ok = $false
            foreach ($v in $verifiers) {
                if ($v.VerifyData($bytes, $signature, $sha256, $format)) { $ok = $true; break }
            }
            if (-not $ok) {
                throw "Signature of $($file.Name) does not verify against the public key(s) in $keyDir - wrong private key?"
            }
            Write-Host ("signed + verified  {0}" -f $file.Name)
        }
        $completed = $true
    }
    finally {
        # Never leave a half-signed folder behind: on any failure remove every .sig made in this run.
        if (-not $completed) {
            Get-ChildItem $ReleaseDir -File -Filter '*.sig' -ErrorAction SilentlyContinue | Remove-Item -Force
        }
        $signer.Dispose()
        foreach ($v in $verifiers) { $v.Dispose() }
        $pem = $null
        $env:SNAPDATE_UPDATE_SIGNING_KEY = $null
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
        'SignRelease' { Invoke-SignRelease }
        'All' {
            Invoke-Clean
            Invoke-Restore
            Invoke-Audit
            Invoke-Test
            Invoke-Publish
            Invoke-Sign
            Invoke-Package
            if (-not $SkipUpdateSignature) { Invoke-SignRelease }
        }
    }
    Write-Host ""
    Write-Host "Done: $Target" -ForegroundColor Green
}
finally {
    Pop-Location
}
