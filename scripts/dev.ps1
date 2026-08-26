<#
.SYNOPSIS
    Builds and runs what you just changed, without publishing anything.

.DESCRIPTION
    The loop for testing a change immediately. It runs the app straight from the build output
    in portable mode, which matters for one specific reason:

    **An installed service serves its own catalog.** When the app can reach the service it asks
    it for the whole tweak list and only re-reads the user-scoped rows locally -- see
    SplitBackend. So a freshly built app talking to a service built last week shows last week's
    tweaks, and looks exactly like a build that did not pick up your change. Portable mode
    ignores the service entirely, so what you see is what you just compiled.

    It also keeps the journal, profiles and logs in a `data` folder beside the build output
    rather than in %ProgramData%, so test applies and reverts do not mix into the record the
    installed service keeps. Delete that folder to start clean.

    What this does NOT test: the service path itself -- drift reconciliation, and
    machine-scope changes from an unelevated session. For those, publish over the install with
    scripts\publish.ps1 and restart the service. See "Testing the service path" in
    CONTRIBUTING.md.

.PARAMETER Elevated
    Launch elevated, so machine-scope tweaks can actually be applied. Without this they are
    reported as skipped, because portable mode has no privileged service to hand them to.

    Costs one UAC prompt per run. Leave it off unless the thing you are testing is machine
    scope.

.PARAMETER Cli
    Run the CLI instead of the window, passing everything after it through to nos. Faster than
    the GUI for checking whether a catalog entry reads correctly.

.PARAMETER Configuration
    Debug by default. Release builds take longer and are not what you want mid-change.

.EXAMPLE
    .\scripts\dev.ps1

.EXAMPLE
    .\scripts\dev.ps1 -Elevated

.EXAMPLE
    .\scripts\dev.ps1 -Cli status services.print-spooler
#>
# PositionalBinding is off on purpose. With it on, -Configuration is implicitly positional, so
# `dev.ps1 -Cli list --category ping` binds "list" to it and builds a configuration called
# "list" while the CLI gets no command at all and prints its help. Everything unnamed has to
# fall through to $Arguments.
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch] $Elevated,
    [switch] $Cli,
    [string] $Configuration = 'Debug',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $project = if ($Cli) { 'src\Nostos.Cli' } else { 'src\Nostos.App' }

    dotnet build $project -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "build failed with $LASTEXITCODE" }

    # The CLI has no service of its own to be confused by, so it is run directly and its
    # arguments are passed straight through.
    if ($Cli) {
        dotnet run --project $project -c $Configuration --no-build -- @Arguments
        exit $LASTEXITCODE
    }

    # Locate the built executable rather than going through `dotnet run`, because a run host
    # sitting between the shell and the app swallows the exit code and complicates elevation.
    $exe = Get-ChildItem -Path (Join-Path $root "src\Nostos.App\bin\$Configuration") `
                         -Filter 'Nostos.exe' -Recurse -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if (-not $exe) { throw "Could not find Nostos.exe under src\Nostos.App\bin\$Configuration." }

    # --portable is what keeps this off the installed service. Program.Main checks it before
    # anything touches AppPaths, so it also redirects the journal beside the build output.
    $appArgs = @('--portable') + $Arguments

    Write-Host ''
    Write-Host "  $($exe.FullName)" -ForegroundColor DarkGray
    Write-Host "  portable mode: ignoring any installed service, data in $(Split-Path -Parent $exe.FullName)\data" -ForegroundColor DarkGray
    if (-not $Elevated) {
        Write-Host '  not elevated: machine-scope tweaks will report as skipped (use -Elevated)' -ForegroundColor DarkGray
    }
    Write-Host ''

    if ($Elevated) {
        Start-Process -FilePath $exe.FullName -ArgumentList $appArgs -Verb RunAs
    } else {
        Start-Process -FilePath $exe.FullName -ArgumentList $appArgs
    }
}
finally {
    Pop-Location
}
