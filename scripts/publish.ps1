<#
.SYNOPSIS
    Builds a folder you can hand to somebody else.

.DESCRIPTION
    Publishes the desktop app, the service and the CLI into a single folder. They share one set
    of runtime assemblies, so all three cost barely more than one.

    Self-contained by default. A .NET runtime is a second thing to install and a second thing to
    get wrong, and "download this, then download that" is how a free tool loses the person you
    were trying to help. The framework-dependent build stays available for anyone who would
    rather have the small folder and already has the runtime.

.PARAMETER SingleFile
    Produce one self-contained .exe and nothing else, compiled ahead of time.

    This publishes only the desktop app, and it runs portable: there is no service executable
    beside it to install. The rendering libraries are C++ and cannot be linked into a managed
    executable, so they travel zipped inside it and are unpacked once, on first run, into the
    app's own data folder.

    Needs the MSVC linker: install the "Desktop development with C++" workload.

.PARAMETER FrameworkDependent
    Publish without the runtime. About 53 MB instead of about 135 MB, and requires the .NET 10
    runtime on the target machine.

.PARAMETER Portable
    Write a portable.txt marker into the output, so the app keeps its data beside itself and
    never installs the service.

.PARAMETER Zip
    Also produce a .zip beside the output folder, ready to attach to a release.

.PARAMETER Version
    Stamp this version into the binaries instead of the 0.1.0 in Directory.Build.props. The
    release workflow passes the git tag. The updater compares the running assembly's version
    against the latest release tag, so a build published without this would keep offering
    itself the update it already is.

.EXAMPLE
    .\scripts\publish.ps1 -Zip

.EXAMPLE
    .\scripts\publish.ps1 -SingleFile -Output portable
#>
[CmdletBinding()]
param(
    [string]  $Output = 'dist',
    [switch]  $SingleFile,
    [switch]  $FrameworkDependent,
    [switch]  $Portable,
    [switch]  $Zip,
    [string]  $Version,
    [string]  $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $root $Output }
    $selfContained = -not $FrameworkDependent

    # Appended to every dotnet publish below. Empty unless -Version was given, so a local build
    # keeps whatever Directory.Build.props says.
    #
    # Built as an array and passed as one variable, NOT splatted with @. Windows PowerShell 5.1
    # -- still the default powershell.exe on Windows 11 -- splats a single-element array of
    # strings to a native command one CHARACTER at a time, so `-p:Version=0.2.0` reached MSBuild
    # as `- p : V e r s i o n = 0 . 2 . 0` and it failed with "Unknown switch". PowerShell 7,
    # which is what CI runs, does it correctly. That split is the worst kind of bug to ship: the
    # release pipeline would have been green while nobody could reproduce a release locally.
    $versionArgs = if ($Version) { @("-p:Version=$Version") } else { @() }

    # The service holds its binaries open while it runs, so a publish over the top of a live
    # install fails halfway with a file-lock error and leaves a mixed-version folder behind.
    $service = Get-Service -Name 'Nostos' -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        # Compare resolved paths, not folder names: matching on the word "dist" would both
        # miss a differently named output and trip over an unrelated service that happens to
        # live in a folder called dist.
        $binary = (Get-CimInstance Win32_Service -Filter "Name='Nostos'").PathName
        $registered = if ($binary -match '^"([^"]+)"') { $Matches[1] } else { ($binary -split ' ')[0] }
        $registeredDir = if ($registered) { [System.IO.Path]::GetFullPath((Split-Path -Parent $registered)) } else { $null }
        if ($registeredDir -and $registeredDir -eq [System.IO.Path]::GetFullPath($Output)) {
            Write-Warning "The installed service is running from $Output. Stop it first:"
            Write-Warning "  sc.exe stop Nostos    (from an elevated prompt)"
            throw 'Refusing to publish over a running service.'
        }
    }

    if ($SingleFile -and $FrameworkDependent) {
        throw '-SingleFile is self-contained by definition; it cannot be combined with -FrameworkDependent.'
    }

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

    if ($SingleFile) {
        # The ahead-of-time compiler shells out to the MSVC linker, and locates it with vswhere.
        # vswhere lives under a path containing parentheses, which some shells cannot pass
        # through, so put its folder on PATH explicitly rather than leaving it to chance.
        $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
        if (-not (Test-Path (Join-Path $installer 'vswhere.exe'))) {
            throw "vswhere.exe not found under $installer. Ahead-of-time compilation needs Visual Studio's C++ tools."
        }
        $env:PATH = "$installer;$env:PATH"

        Write-Host 'compiling ahead of time (this takes a few minutes)' -ForegroundColor Cyan
        $arguments = @(
            'publish', 'src\Nostos.App\Nostos.App.csproj'
            '-c', $Configuration
            '-r', 'win-x64'
            '-p:PublishAot=true'
            '-p:EmbedNativeAssets=true'
            '-o', $Output
        ) + $versionArgs + @('--nologo', '-v', 'quiet')

        & dotnet $arguments
        if ($LASTEXITCODE -ne 0) { throw 'ahead-of-time publish failed' }

        Get-ChildItem $Output -Filter '*.pdb' | Remove-Item -Force

        $stragglers = Get-ChildItem $Output -Recurse -File | Where-Object Name -ne 'Nostos.exe'
        if ($stragglers) {
            # Not fatal, but the entire point of this mode is one file, so say so loudly rather
            # than shipping a folder that quietly is not what it claims to be.
            Write-Warning "Expected a single file. Also present: $($stragglers.Name -join ', ')"
        }

        $exe = Join-Path $Output 'Nostos.exe'
        $mb = '{0:N0} MB' -f ((Get-Item $exe).Length / 1MB)
        Write-Host ""
        Write-Host "$exe  ($mb, self-contained, portable)" -ForegroundColor Green

        if ($Zip) {
            $archive = "$Output.zip"
            if (Test-Path $archive) { Remove-Item $archive -Force }
            Compress-Archive -Path $exe -DestinationPath $archive
            Write-Host "$archive  ($('{0:N0} MB' -f ((Get-Item $archive).Length / 1MB)))" -ForegroundColor Green
        }
        return
    }

    $projects = @(
        'src\Nostos.App\Nostos.App.csproj'
        'src\Nostos.Service\Nostos.Service.csproj'
        'src\Nostos.Cli\Nostos.Cli.csproj'
    )

    foreach ($project in $projects) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host "publishing $name" -ForegroundColor Cyan

        # The window is compiled ahead of time; the service and the CLI are not.
        #
        # This is the difference between a window that appears in a quarter of a second and one
        # that takes over a second, and it was measured rather than assumed: the same build is
        # 1212ms to first window as ordinary IL, 640ms with ReadyToRun and 264ms ahead of time.
        # Nearly all of the saving is JIT compiling Avalonia on the way to the first frame.
        #
        # Only the window, because only the window is something a person waits in front of. The
        # service starts once at boot and nobody watches it, the CLI is measured in the time it
        # takes to type the next command, and ahead-of-time compilation costs several minutes of
        # build time and a C++ linker on the build machine for each of them.
        $aot = $name -eq 'Nostos.App' -and -not $FrameworkDependent

        $arguments = @(
            'publish', $project
            '-c', $Configuration
            '-r', 'win-x64'
            '--self-contained', $selfContained.ToString().ToLowerInvariant()
            '-o', $Output
        ) + $(if ($aot) { @('-p:PublishAot=true') } else { @() }) `
          + $versionArgs + @('--nologo', '-v', 'quiet')

        & dotnet $arguments
        if ($LASTEXITCODE -ne 0) { throw "publish failed for $project" }
    }

    # Managed symbols are useful to a developer and are dead weight to everyone else. The native
    # ones are dropped by the TrimNativeSymbols target during publish.
    Get-ChildItem $Output -Filter '*.pdb' | Remove-Item -Force

    if ($Portable) {
        Set-Content -Path (Join-Path $Output 'portable.txt') -Encoding utf8 -Value @'
The presence of this file puts the app in portable mode: it keeps its journal, profiles and
logs in the data folder beside this one, and never installs the background service.

Delete it to go back to a normal installation.
'@
    }

    $size = '{0:N0} MB' -f ((Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
    Write-Host ""
    Write-Host "$Output  ($size, $(if ($selfContained) { 'self-contained' } else { 'needs the .NET 10 runtime' }))" -ForegroundColor Green

    if ($Zip) {
        $archive = "$Output.zip"
        if (Test-Path $archive) { Remove-Item $archive -Force }
        Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $archive
        $zipSize = '{0:N0} MB' -f ((Get-Item $archive).Length / 1MB)
        Write-Host "$archive  ($zipSize)" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
