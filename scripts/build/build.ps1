#Requires -Version 7.4
<#
.SYNOPSIS
    Build, test, publish and package Snapdate.

.DESCRIPTION
    Developer and CI entry point. Produces release artefacts in artifacts/release:
      Snapdate-Setup-v<ver>-win-<arch>.exe (installers, built by -Target Installer),
      Snapdate-v<ver>-win-<arch>-portable.exe,
      SHA256SUMS.txt and sbom.cdx.json (when the CycloneDX tool is available).
    SignRelease then adds a detached update signature (<file>.sig) for each of them
    (ECDSA P-256 / SHA-256, see docs/adr/0005-update-signatures.md).

.PARAMETER Target
    Clean | Restore | Audit | Test | Publish | Installer | InstallerTest | Package | Sign | SignRelease | All

    Installer builds one Inno Setup installer per published runtime into artifacts/installer and
    smoke-tests the one for this machine's CPU. InstallerTest only runs that smoke test (on the
    installer from -InstallerPath, or artifacts/installer).

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

.PARAMETER InstallerPath
    InstallerTest only: the installer to smoke-test. Default: the installer for this machine's
    architecture in artifacts/installer.

.PARAMETER SkipInstallerTest
    Installer only: build the installers but do not run the install/upgrade/uninstall smoke test.

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
    [ValidateSet('Clean', 'Restore', 'Audit', 'Test', 'Publish', 'Installer', 'InstallerTest', 'Package', 'Sign', 'SignRelease', 'All')]
    [string] $Target = 'Test',
    [string] $Version,
    [string[]] $Runtime = @('win-x64', 'win-arm64'),
    [string] $Configuration = 'Release',
    [string] $SigningKeyPath,
    [string] $PublicKeyDir,
    [string] $InstallerPath,
    [switch] $SkipInstallerTest,
    [switch] $SkipUpdateSignature
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
# No long-lived background children (MSBuild nodes, MSBuild server, Roslyn compiler server): they inherit the
# step's output pipes, so a CI step (or a captured command) would never see end-of-output and never finish.
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:UseSharedCompilation = 'false'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$Solution = Join-Path $Root 'Snapdate.slnx'
$AppProject = Join-Path $Root 'src' 'Snapdate.App' 'Snapdate.App.csproj'
$Artifacts = Join-Path $Root 'artifacts'
$PublishDir = Join-Path $Artifacts 'publish'
$ReleaseDir = Join-Path $Artifacts 'release'
$InstallerDir = Join-Path $Artifacts 'installer'
$InstallerScript = Join-Path $Root 'installer' 'Snapdate.iss'
$TestResults = Join-Path $Artifacts 'test-results'

$ScriptClock = [Diagnostics.Stopwatch]::StartNew()

# Banner with wall-clock time and time since the script started, so a hang in CI can be located
# from the last banner in the log.
function Write-Step([string] $Message) {
    Write-Host ""
    Write-Host ("==> [{0:HH:mm:ss} +{1:mm\:ss}] {2}" -f (Get-Date), $ScriptClock.Elapsed, $Message) -ForegroundColor Magenta
}

# Runs an external program and waits for it with an explicit timeout. On timeout the whole process tree is
# killed and the step fails with a message that names it (a hung tool must never block CI until the job
# timeout). Output is inherited (streams live) unless -Capture is given; -StdErrFile writes stderr to a file.
# Only the started process is awaited, not programs it launches (Setup's /RESTARTAPP). Returns the exit code,
# or an object with ExitCode and Output when -Capture is given; fails on a non-zero exit code unless
# -AllowFailure is given.
function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [int] $TimeoutSec,
        [string] $WorkingDirectory,
        [string] $StdErrFile,
        [switch] $Capture,
        [switch] $AllowFailure
    )
    $psi = [Diagnostics.ProcessStartInfo]::new($FilePath)
    foreach ($argument in $Arguments) { $psi.ArgumentList.Add($argument) }
    $psi.UseShellExecute = $false
    # Nothing here is interactive: never let a child wait for keyboard input.
    $psi.RedirectStandardInput = $true
    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
    if ($Capture) { $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true }
    elseif ($StdErrFile) { $psi.RedirectStandardError = $true }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    [void] $process.Start()
    $process.StandardInput.Close()
    $stdout = if ($Capture) { $process.StandardOutput.ReadToEndAsync() }
    $stderr = if ($Capture -or $StdErrFile) { $process.StandardError.ReadToEndAsync() }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
    $timedOut = "Step '$Name' timed out after $TimeoutSec s and was killed ($([IO.Path]::GetFileName($FilePath)))."
    try {
        if (-not $process.WaitForExit([TimeSpan]::FromSeconds($TimeoutSec))) {
            try { $process.Kill($true) } catch { }
            throw $timedOut
        }
        # The redirected pipes only reach EOF when every program that inherited them has exited too (MSBuild
        # node reuse, compiler servers), so reading them is bounded by the same deadline.
        $streams = @(@($stdout, $stderr) | Where-Object { $_ })
        if ($streams.Count -gt 0) {
            $remainingMs = [Math]::Max(1000, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
            if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] $streams, $remainingMs)) {
                try { $process.Kill($true) } catch { }
                throw "$timedOut (output pipes stayed open: a background child process still holds them)"
            }
        }
        if ($StdErrFile) { [IO.File]::WriteAllText($StdErrFile, $stderr.GetAwaiter().GetResult()) }
        $exitCode = $process.ExitCode
        $output = if ($Capture) { $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    }
    finally {
        $process.Dispose()
    }
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ($Capture -and $output) { Write-Host $output }
        throw "Step '$Name' failed with exit code $exitCode."
    }
    if ($Capture) { return [pscustomobject]@{ ExitCode = $exitCode; Output = $output } }
    return $exitCode
}

# Runs a program that may leave long-lived children behind (Inno Setup and the app it restarts, the uninstaller's
# temporary copy) WITHOUT handing it this process's console or output pipes: UseShellExecute starts it with no
# inherited handles, so nothing it spawns can keep the CI step's stdout/stderr pipe open after this script ends
# (the GitHub runner waits for end-of-output, so such a leftover would hang the step until the job timeout).
# Waits with a timeout (the process tree is killed on expiry) and returns the exit code. There is no captured
# output: pass /LOG to Setup instead.
function Invoke-Detached {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $Arguments = @(),
        [Parameter(Mandatory)] [int] $TimeoutSec
    )
    $quoted = foreach ($argument in $Arguments) {
        if ($argument -notmatch '[\s"]') { $argument }
        elseif ($argument -match '^(/[A-Za-z0-9]+=)(.*)$') { $Matches[1] + '"' + $Matches[2].Replace('"', '\"') + '"' }
        else { '"' + $argument.Replace('"', '\"') + '"' }
    }
    $psi = [Diagnostics.ProcessStartInfo]::new($FilePath)
    $psi.UseShellExecute = $true
    $psi.Arguments = $quoted -join ' '
    $psi.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($psi)
    if (-not $process) { throw "Step '$Name' could not be started ($([IO.Path]::GetFileName($FilePath)))." }
    try {
        if (-not $process.WaitForExit([TimeSpan]::FromSeconds($TimeoutSec))) {
            try { $process.Kill($true) } catch { }
            throw "Step '$Name' timed out after $TimeoutSec s and was killed ($([IO.Path]::GetFileName($FilePath)))."
        }
        return $process.ExitCode
    }
    finally { $process.Dispose() }
}

# The dotnet CLI. Timeouts are generous upper bounds (a normal run takes a fraction of them).
function Invoke-Dotnet([string] $Name, [string[]] $Arguments, [int] $TimeoutSec = 900, [switch] $Capture) {
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    Invoke-Native -Name $Name -FilePath $dotnet -Arguments $Arguments -TimeoutSec $TimeoutSec -Capture:$Capture
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
    [void](Invoke-Dotnet 'dotnet restore' @('restore', $Solution, '--locked-mode') 600)
}

function Invoke-Audit {
    Write-Step 'Audit NuGet packages for known vulnerabilities'
    $report = (Invoke-Dotnet 'dotnet list package --vulnerable' @('list', $Solution, 'package', '--vulnerable', '--include-transitive') 600 -Capture).Output
    Write-Host $report
    if ($report -match 'has the following vulnerable packages') {
        throw 'Vulnerable packages found. Update them in Directory.Packages.props.'
    }
}

function Invoke-Test {
    Write-Step 'Build and test'
    $ver = Get-ProjectVersion
    [void](Invoke-Dotnet 'dotnet build' @('build', $Solution, '-c', $Configuration, '--no-restore', "-p:Version=$ver", '-p:UseSharedCompilation=false', '-nodeReuse:false') 900)
    New-Item -ItemType Directory -Force -Path $TestResults | Out-Null
    [void](Invoke-Dotnet 'dotnet test' @('test', '--solution', $Solution, '-c', $Configuration, '--no-build',
            '--results-directory', $TestResults, '--report-xunit-trx') 1200)
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
        [void](Invoke-Dotnet "dotnet publish $rid" @('publish', $AppProject, '-c', $Configuration, '-r', $rid, '-o', $out,
                "-p:Version=$ver", '-p:DebugType=none', '-p:RestoreLockedMode=true', '-p:UseSharedCompilation=false', '-nodeReuse:false',
                '--self-contained', 'true',
                '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
                '-p:EnableCompressionInSingleFile=true') 900)

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
        $stderrFile = Join-Path $sandbox 'stderr.txt'
        $exit = Invoke-Native -Name "Self-test $Rid" -FilePath $copy -Arguments @('--self-test') -WorkingDirectory $sandbox `
            -TimeoutSec 180 -StdErrFile $stderrFile -AllowFailure
        if ($exit -ne 0) {
            Get-Content $stderrFile -ErrorAction SilentlyContinue | Write-Host
            throw "Self-test of $Rid failed with exit code $exit."
        }
        Write-Host "Self-test passed." -ForegroundColor Green
    }
    finally {
        # --self-test exits by itself; if it ever left a process behind, stop it so it cannot hold the step's pipes.
        Get-Process -Name 'Snapdate' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and (Get-LongPath $_.Path).StartsWith((Get-LongPath $sandbox).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) } |
            Stop-Process -Force -ErrorAction SilentlyContinue
        Remove-Item $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Find-Iscc {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $candidates = @(
        [Environment]::GetEnvironmentVariable('LOCALAPPDATA') | Where-Object { $_ } | ForEach-Object { Join-Path $_ 'Programs' 'Inno Setup 6' 'ISCC.exe' }
        [Environment]::GetEnvironmentVariable('ProgramFiles(x86)') | Where-Object { $_ } | ForEach-Object { Join-Path $_ 'Inno Setup 6' 'ISCC.exe' }
        [Environment]::GetEnvironmentVariable('ProgramFiles') | Where-Object { $_ } | ForEach-Object { Join-Path $_ 'Inno Setup 6' 'ISCC.exe' }
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw ('Inno Setup 6 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php ' +
        '(per user is fine: "winget install JRSoftware.InnoSetup" or the installer''s "Install for me only"), ' +
        'or put ISCC.exe on the PATH.')
}

# Expands 8.3 short names (a TEMP like C:\Users\NAME~1\...) so paths compare equal to what Windows reports.
function Get-LongPath([string] $Path) {
    if (-not ('Snapdate.Build.NativePath' -as [type])) {
        Add-Type -Namespace Snapdate.Build -Name NativePath -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
public static extern int GetLongPathName(string shortPath, System.Text.StringBuilder longPath, int bufferSize);
"@
    }
    $builder = [Text.StringBuilder]::new(32768)
    $length = [Snapdate.Build.NativePath]::GetLongPathName($Path, $builder, $builder.Capacity)
    if ($length -gt 0) { return $builder.ToString().TrimEnd('\') }
    return $Path.TrimEnd('\')
}

function Get-InstallerAppId {
    $line = Select-String -Path $InstallerScript -Pattern '^AppId=\{\{([0-9A-Fa-f-]{36})\}' | Select-Object -First 1
    if (-not $line) { throw "AppId not found in $InstallerScript." }
    return $line.Matches[0].Groups[1].Value.ToUpperInvariant()
}

function Invoke-Installer {
    $ver = Get-ProjectVersion
    $iscc = Find-Iscc
    Write-Step "Installer v$ver ($iscc)"
    if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $InstallerDir | Out-Null

    $numeric = ($ver -split '[-+]')[0]
    foreach ($rid in $Runtime) {
        $exe = Join-Path $PublishDir $rid 'Snapdate.exe'
        if (-not (Test-Path $exe)) { throw "Missing $exe - run -Target Publish first." }
        $arch = switch ($rid) {
            'win-x64' { 'x64compatible' }
            'win-arm64' { 'arm64' }
            default { throw "No installer architecture for runtime $rid." }
        }

        Write-Step "Compile installer $rid"
        $result = Invoke-Native -Name "Inno Setup compile $rid" -FilePath $iscc -TimeoutSec 600 -Capture -AllowFailure -Arguments @(
            "/O$InstallerDir", "/DAppVersion=$ver", "/DAppVersionNumeric=$numeric",
            "/DArch=$arch", "/DRid=$rid", "/DSourceExe=$exe",
            "/DLicenseFile=$(Join-Path $Root 'LICENSE')",
            "/DIconFile=$(Join-Path $Root 'src' 'Snapdate.App' 'Assets' 'app.ico')",
            $InstallerScript)
        $output = @($result.Output -split '\r?\n')
        $isccExit = $result.ExitCode
        if ($isccExit -ne 0) {
            $output | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
            throw "Inno Setup failed for $rid with exit code $isccExit."
        }
        $output | Where-Object { $_ -notmatch '^\s*(Parsing |Reading |Compressing|Updating |Preparing|Creating |Compiling|Determining|Messages in script|Verification)' -and $_.Trim() } |
            ForEach-Object { Write-Host $_ }
        if ($output -match '^\s*Warning:') { throw "Inno Setup reported warnings for $rid (see above); warnings are errors." }

        $setup = Join-Path $InstallerDir "Snapdate-Setup-v$ver-$rid.exe"
        if (-not (Test-Path $setup)) { throw "Inno Setup did not produce $setup." }
    }

    # Optional Authenticode signature of the installers (the exe inside is signed by -Target Sign).
    # The detached update signatures (.sig) are made later by SignRelease over the final files.
    $setups = @(Get-ChildItem $InstallerDir -Filter 'Snapdate-Setup-*.exe' | ForEach-Object FullName)
    [void](Invoke-Authenticode $setups)

    Get-ChildItem $InstallerDir | Format-Table Name, Length -AutoSize | Out-String | Write-Host

    if (-not $SkipInstallerTest) { Invoke-InstallerTest }
}

# Silent per-user install into a temp folder -> checks -> in-place upgrade (twice, once with a running
# app) -> silent uninstall. Never touches a real installation: skipped when this PC already has one.
function Invoke-InstallerTest {
    $ver = Get-ProjectVersion
    $hostRid = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'win-arm64' } else { 'win-x64' }
    $setup = if ($InstallerPath) { $InstallerPath } else { Join-Path $InstallerDir "Snapdate-Setup-v$ver-$hostRid.exe" }
    if (-not (Test-Path $setup)) {
        if (-not $InstallerPath -and $hostRid -notin $Runtime) {
            Write-Warning "Skipping installer smoke test: no $hostRid installer was built on this $hostRid machine."
            return
        }
        throw "Installer not found: $setup"
    }
    $setup = (Resolve-Path $setup).Path

    Write-Step "Installer smoke test ($([IO.Path]::GetFileName($setup)))"

    $appId = Get-InstallerAppId
    $uninstallKeyName = "{$appId}_is1"
    $uninstallKeys = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyName"
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyName"
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyName"
    )
    $existing = $uninstallKeys | Where-Object { Test-Path $_ }
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    $defaultDir = Join-Path $localAppData 'Programs' 'Snapdate'
    if ($existing -or (Test-Path $defaultDir)) {
        Write-Warning 'Snapdate is already installed on this PC - skipping the installer smoke test so the real installation is not touched.'
        return
    }

    $logDir = Join-Path $Artifacts 'installer-test'
    if (Test-Path $logDir) { Remove-Item $logDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null

    # $runId is a unique marker of this run. Processes are attributed to the test only through it (it is part of
    # the temp folder and of the log folder, so it shows up on Setup's and the uninstaller's command lines).
    $runId = [guid]::NewGuid().ToString('N')
    $runLogDir = Join-Path $logDir $runId
    New-Item -ItemType Directory -Force -Path $runLogDir | Out-Null
    $root = Join-Path (Get-LongPath ([IO.Path]::GetTempPath())) "snapdate-installer-test-$runId"
    $rootShort = $null   # 8.3 form of $root, set once the folder exists
    $failed = $false
    $appDir = Join-Path $root 'app'
    $exe = Join-Path $appDir 'Snapdate.exe'
    $startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Snapdate.lnk'
    $desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Snapdate.lnk'
    $settingsDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Snapdate'
    $sentinel = Join-Path $settingsDir 'installer-smoke-test.sentinel'
    $settingsDirExisted = Test-Path $settingsDir
    $hkcuKey = $uninstallKeys[0]
    $state = @{ Step = 0 }

    function Assert-That([bool] $Condition, [string] $Message) {
        if (-not $Condition) { throw "Installer smoke test failed: $Message" }
    }

    function Invoke-Setup([string] $Program, [string[]] $Arguments, [string] $Name) {
        $state.Step++
        $log = Join-Path $runLogDir ('{0}-{1}.log' -f $state.Step, $Name)
        Write-Host "  ... $Name"
        # Only the Setup process itself is awaited, not the app that /RESTARTAPP starts. Started detached (no
        # inherited output pipes, see Invoke-Detached) so neither that app nor Setup's helper processes can keep
        # the CI step's pipe open. Always silent and with /SUPPRESSMSGBOXES: a message box nobody can click
        # would block forever (the timeout is the backstop).
        $exit = Invoke-Detached -Name "Installer smoke test: $Name" -FilePath $Program -TimeoutSec 240 `
            -Arguments ($Arguments + @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$log"))
        if ($exit -ne 0) {
            Get-Content $log -Tail 40 -ErrorAction SilentlyContinue | Write-Host
            throw "Installer smoke test failed: $Name exited with code $exit (log: $log)."
        }
        # Setup's extracted helper copy and the uninstaller's temporary copy may outlive the process awaited above.
        Wait-SmokeProcessesGone -Name $Name -TimeoutSec 60 -Except $exe
    }

    function Get-RunningInstalled {
        @(Get-Process -Name 'Snapdate' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (Get-LongPath $_.Path) -eq $exe })
    }

    # Every process that belongs to this test: anything running from the temp test folder, plus Setup and
    # uninstaller helper copies (they run from %TEMP% but carry the test folder or the per-run marker $runId on
    # their command line). Only these GUID-bearing strings are matched (case-insensitive, long and 8.3 form).
    function Test-Contains([string] $Text, [string] $Part) {
        [bool]($Text -and $Part -and $Text.IndexOf($Part, [StringComparison]::OrdinalIgnoreCase) -ge 0)
    }

    function Get-SmokeProcesses([string] $Except) {
        @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessId -ne $PID -and (
                ($_.ExecutablePath -and (
                    (Get-LongPath $_.ExecutablePath).StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
                    ($rootShort -and $_.ExecutablePath.StartsWith($rootShort + '\', [StringComparison]::OrdinalIgnoreCase)))) -or
                (Test-Contains $_.CommandLine $runId) -or (Test-Contains $_.CommandLine $root) -or (Test-Contains $_.CommandLine $rootShort)) -and
            -not ($Except -and $_.ExecutablePath -and (Get-LongPath $_.ExecutablePath) -eq $Except)
        })
    }

    # One line per process: name, PID, parent, path, command line (truncated) and start time.
    function Format-Processes($Processes) {
        (@($Processes) | Where-Object { $_ } | ForEach-Object {
            $cmd = if ($_.CommandLine) { $_.CommandLine.Substring(0, [Math]::Min(200, $_.CommandLine.Length)) } else { '' }
            "$($_.Name) pid=$($_.ProcessId) parent=$($_.ParentProcessId) created=$($_.CreationDate.ToString('HH:mm:ss')) path=$($_.ExecutablePath) cmd=$cmd"
        }) -join [Environment]::NewLine
    }

    # Prints (and saves to survivors.txt) every process of this test, before anything is killed. When there are
    # some, also lists orphans (parent gone) that look like Snapdate or this run - the likely holders of the pipes.
    function Write-SmokeDiagnostics([string] $Title) {
        $found = Get-SmokeProcesses
        $lines = @("$Title ($([DateTime]::Now.ToString('HH:mm:ss'))): $(@($found).Count) process(es) of this run ($runId)")
        if ($found) { $lines += Format-Processes $found }
        if ($found) {
            $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
            $pids = @{}
            foreach ($p in $all) { $pids[[int]$p.ProcessId] = 1 }
            $orphans = @($all | Where-Object {
                    -not $pids.ContainsKey([int]$_.ParentProcessId) -and
                    ((Test-Contains $_.ExecutablePath 'Snapdate') -or (Test-Contains $_.CommandLine 'Snapdate') -or (Test-Contains $_.CommandLine $runId))
                })
            $lines += "Orphans (parent no longer exists) mentioning Snapdate or this run: $($orphans.Count)"
            if ($orphans) { $lines += Format-Processes $orphans }
        }
        $lines | ForEach-Object { Write-Host $_ }
        try { Add-Content -Path (Join-Path $runLogDir 'survivors.txt') -Value $lines -ErrorAction Stop } catch { }
    }

    # Waits (bounded) until no process of this test is left, so nothing can hold the step's output pipes.
    function Wait-SmokeProcessesGone([string] $Name, [int] $TimeoutSec, [string] $Except) {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
        while ((Get-SmokeProcesses $Except) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 300 }
        $left = Get-SmokeProcesses $Except
        if ($left) { Assert-That $false "after '$Name' these processes are still running:`n$(Format-Processes $left)" }
    }

    # Stops every process of this test (the app, Setup and uninstaller helpers) and waits for them to be gone.
    function Stop-SmokeProcesses([int] $WaitSec = 10) {
        $deadline = [DateTime]::UtcNow.AddSeconds($WaitSec)
        do {
            foreach ($p in Get-SmokeProcesses) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Milliseconds 300
        } while ((Get-SmokeProcesses) -and [DateTime]::UtcNow -lt $deadline)
    }

    function Assert-Installed([string] $Stage) {
        Assert-That (Test-Path $exe) "$Stage - Snapdate.exe missing in $appDir."
        $marker = [IO.File]::ReadAllText((Join-Path $appDir 'snapdate-install.ini'))
        Assert-That ($marker -eq "[Install]`r`nScope=user`r`n") "$Stage - unexpected install marker: $marker"
        Assert-That (Test-Path (Join-Path $appDir 'LICENSE.txt')) "$Stage - LICENSE.txt missing."
        Assert-That (Test-Path $hkcuKey) "$Stage - no Apps & features entry (HKCU)."
        $entry = Get-ItemProperty $hkcuKey
        Assert-That ($entry.DisplayName -eq 'Snapdate') "$Stage - DisplayName is '$($entry.DisplayName)'."
        Assert-That ($entry.DisplayVersion -eq $ver) "$Stage - DisplayVersion is '$($entry.DisplayVersion)', expected $ver."
        Assert-That ($entry.Publisher -eq 'Snapdate contributors') "$Stage - Publisher is '$($entry.Publisher)'."
        Assert-That ($entry.DisplayIcon.Trim('"') -eq $exe) "$Stage - DisplayIcon is '$($entry.DisplayIcon)'."
        Assert-That ($entry.InstallLocation.TrimEnd('\') -eq $appDir) "$Stage - InstallLocation is '$($entry.InstallLocation)'."
        Assert-That (Test-Path (Join-Path $appDir 'unins000.exe')) "$Stage - uninstaller missing."

        $shell = New-Object -ComObject WScript.Shell
        foreach ($link in $startMenuLink, $desktopLink) {
            Assert-That (Test-Path $link) "$Stage - shortcut missing: $link"
            Assert-That ($shell.CreateShortcut($link).TargetPath -eq $exe) "$Stage - shortcut $link does not point to $exe."
        }
        Assert-That (-not (Test-Path $defaultDir)) "$Stage - a second copy appeared in the default folder (scope/folder not kept)."
    }

    try {
        Assert-That (-not (Test-Path $startMenuLink) -and -not (Test-Path $desktopLink)) 'a Snapdate shortcut already exists (leftover of a real installation?).'
        New-Item -ItemType Directory -Path $root | Out-Null
        $rootShort = try { (New-Object -ComObject Scripting.FileSystemObject).GetFolder($root).ShortPath } catch { $null }
        if ($rootShort -eq $root) { $rootShort = $null }

        Invoke-Setup $setup @('/CURRENTUSER', "/DIR=$appDir", '/TASKS=desktopicon') 'install'
        Assert-Installed 'install'
        Write-Host '  [ok] silent per-user install: files, marker, shortcuts, Apps entry (name, version, publisher, icon)'

        $selfTestExit = Invoke-Native -Name 'Installer smoke test: installed exe --self-test' -FilePath $exe -Arguments @('--self-test') `
            -WorkingDirectory $appDir -TimeoutSec 180 -AllowFailure -StdErrFile (Join-Path $root 'selftest-stderr.txt')
        if ($selfTestExit -ne 0) {
            Get-Content (Join-Path $root 'selftest-stderr.txt') -ErrorAction SilentlyContinue | Write-Host
            throw "Installer smoke test failed: installed exe self-test exit code $selfTestExit."
        }
        Write-Host '  [ok] installed exe passes --self-test'

        New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
        Set-Content -Path $sentinel -Value 'installer smoke test' -Encoding ascii

        # Upgrade 1: no /DIR - the folder must come from the existing installation. /RESTARTAPP starts the app.
        Invoke-Setup $setup @('/CURRENTUSER', '/RESTARTAPP') 'upgrade-restartapp'
        Assert-Installed 'upgrade 1'
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not (Get-RunningInstalled) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Assert-That ([bool](Get-RunningInstalled)) 'upgrade 1 - /RESTARTAPP did not start Snapdate.'
        # Setup can only close a window that exists, so let the app finish starting first.
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not (Get-RunningInstalled | Where-Object { $_.MainWindowHandle -ne 0 }) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Assert-That ([bool](Get-RunningInstalled | Where-Object { $_.MainWindowHandle -ne 0 })) 'upgrade 1 - the restarted Snapdate never showed its window.'
        Write-Host '  [ok] silent upgrade keeps folder and scope; /RESTARTAPP starts the app'

        # Upgrade 2: no switches at all, the app is running and must be closed by the installer.
        Invoke-Setup $setup @() 'upgrade-running-app'
        Assert-Installed 'upgrade 2'
        Assert-That (-not (Get-RunningInstalled)) 'upgrade 2 - the running Snapdate was not closed, or was restarted without /RESTARTAPP.'
        Write-Host '  [ok] upgrade closes a running Snapdate; no restart without /RESTARTAPP'

        Invoke-Setup (Join-Path $appDir 'unins000.exe') @() 'uninstall'
        # The uninstaller hands over to a temporary copy of itself, so wait for the folder to vanish.
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while ((Test-Path $appDir) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Assert-That (-not (Test-Path $appDir)) 'uninstall - the install folder is still there.'
        Assert-That (-not (Test-Path $hkcuKey)) 'uninstall - the Apps & features entry is still there.'
        Assert-That (-not (Test-Path $startMenuLink) -and -not (Test-Path $desktopLink)) 'uninstall - a shortcut is still there.'
        Assert-That (Test-Path $sentinel) 'silent uninstall deleted the settings folder (it must keep it).'
        Write-Host '  [ok] silent uninstall removes folder, shortcuts and Apps entry, keeps settings'

        Write-Host "Installer smoke test passed. Logs: $runLogDir" -ForegroundColor Green
    }
    catch {
        $failed = $true
        throw
    }
    finally {
        # Never leave a test installation behind, whatever happened - and no process of it either: a survivor
        # that holds the CI step's output pipes would keep the step from ever finishing. First give helpers
        # (the uninstaller's temporary copy) time to exit on their own, then record what is left, then kill.
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while ((Get-SmokeProcesses $exe) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Write-SmokeDiagnostics 'Processes of the smoke test before cleanup'
        Stop-SmokeProcesses
        $uninstaller = Join-Path $appDir 'unins000.exe'
        if (Test-Path $uninstaller) {
            try {
                [void](Invoke-Detached -Name 'Installer smoke test: cleanup uninstall' -FilePath $uninstaller -TimeoutSec 120 `
                    -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$(Join-Path $runLogDir 'cleanup-uninstall.log')"))
            }
            catch { Write-Warning $_.Exception.Message }
            Stop-SmokeProcesses -WaitSec 30
        }
        foreach ($link in $startMenuLink, $desktopLink) {
            if (Test-Path $link) {
                $target = (New-Object -ComObject WScript.Shell).CreateShortcut($link).TargetPath
                if ($target -eq $exe) { Remove-Item $link -Force -ErrorAction SilentlyContinue }
            }
        }
        if (Test-Path $hkcuKey) {
            $location = (Get-ItemProperty $hkcuKey -ErrorAction SilentlyContinue).InstallLocation
            if ($location -and $location.TrimEnd('\') -eq $appDir) { Remove-Item $hkcuKey -Recurse -Force -ErrorAction SilentlyContinue }
        }
        Remove-Item $sentinel -Force -ErrorAction SilentlyContinue
        if (-not $settingsDirExisted -and (Test-Path $settingsDir) -and -not (Get-ChildItem $settingsDir -Force)) {
            Remove-Item $settingsDir -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
        $survivors = Get-SmokeProcesses
        if ($survivors) {
            Write-SmokeDiagnostics 'Processes that could not be stopped'
            $message = "Installer smoke test left processes running that could not be stopped:`n$(Format-Processes $survivors)"
            # Do not hide the original failure behind this one.
            if ($failed) { Write-Warning $message } else { throw $message }
        }
    }
}

function Invoke-Package {
    $ver = Get-ProjectVersion
    Write-Step "Package v$ver"

    # Installers are built by -Target Installer (before Package, so they are signed with the rest).
    foreach ($rid in $Runtime) {
        $setup = Join-Path $InstallerDir "Snapdate-Setup-v$ver-$rid.exe"
        if (-not (Test-Path $setup)) { throw "Missing $setup - run -Target Installer first." }
    }

    if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

    foreach ($rid in $Runtime) {
        $exe = Join-Path $PublishDir $rid 'Snapdate.exe'
        if (-not (Test-Path $exe)) { throw "Missing $exe - run -Target Publish first." }

        # The portable file is the bare exe; there is no zip (README, CHANGELOG and LICENSE are on
        # the release page and in the repository, the installer also ships the LICENSE).
        Copy-Item $exe (Join-Path $ReleaseDir "Snapdate-v$ver-$rid-portable.exe")
        Copy-Item (Join-Path $InstallerDir "Snapdate-Setup-v$ver-$rid.exe") $ReleaseDir
    }
    # SBOM (optional tool: dotnet tool install --global CycloneDX)
    $cyclone = Get-Command dotnet-CycloneDX -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cyclone) {
        Write-Step "SBOM (CycloneDX, $($cyclone.Source))"
        # The assets file written by Publish already covers every runtime, so the tool must not restore on its
        # own (a second, differently configured restore in the middle of the release is a needless risk);
        # -dct caps its own dotnet calls, the outer timeout kills it if it hangs anyway.
        [void](Invoke-Native -Name 'SBOM (CycloneDX)' -FilePath $cyclone.Source -TimeoutSec 300 -Arguments @(
                $AppProject, '--output', $ReleaseDir, '--filename', 'sbom.cdx.json', '--output-format', 'Json',
                '--disable-package-restore', '--dotnet-command-timeout', '120000'))
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

# Authenticode-signs the given files with SNAPDATE_SIGN_PFX (base64 PFX) and SNAPDATE_SIGN_PASSWORD.
# Returns $false (and does nothing) when no certificate is configured.
function Invoke-Authenticode([string[]] $Files) {
    if (-not $env:SNAPDATE_SIGN_PFX) {
        Write-Warning 'No signing certificate configured - skipping.'
        return $false
    }
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK).' }

    $pfx = Join-Path ([IO.Path]::GetTempPath()) "snapdate-$([guid]::NewGuid()).pfx"
    try {
        [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SNAPDATE_SIGN_PFX))
        foreach ($file in $Files) {
            # The timestamp server is a network call: bounded, so an unreachable server fails the step.
            [void](Invoke-Native -Name "signtool $([IO.Path]::GetFileName($file))" -FilePath $signtool.Source -TimeoutSec 180 -Arguments @(
                    'sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', 'http://timestamp.digicert.com',
                    '/f', $pfx, '/p', $env:SNAPDATE_SIGN_PASSWORD, $file))
        }
    }
    finally {
        Remove-Item $pfx -Force -ErrorAction SilentlyContinue
    }
    return $true
}

function Invoke-Sign {
    Write-Step 'Sign'
    $exes = @(Get-ChildItem $PublishDir -Recurse -Filter 'Snapdate.exe' -ErrorAction SilentlyContinue | ForEach-Object FullName)
    [void](Invoke-Authenticode $exes)
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
        'Installer' { Invoke-Installer }
        'InstallerTest' { Invoke-InstallerTest }
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
            Invoke-Installer
            Invoke-Package
            if (-not $SkipUpdateSignature) { Invoke-SignRelease }
        }
    }
    Write-Host ""
    Write-Host "Done: $Target" -ForegroundColor Green
}
finally {
    Pop-Location
    # Belt and braces: stop any build server that still got started.
    try { [void](Invoke-Dotnet 'dotnet build-server shutdown' @('build-server', 'shutdown') 60 -Capture) } catch { Write-Warning $_.Exception.Message }
}
