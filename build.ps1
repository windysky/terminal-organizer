# Builds bin\TerminalOrganizer.Core.dll with the in-box .NET Framework 4.8 C# 5 compiler.
# Zero-install: no SDK, no NuGet. Exit code is the compiler's exit code.
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$binDir = Join-Path $root 'bin'
$out = Join-Path $binDir 'TerminalOrganizer.Core.dll'
# The single product version (src\AssemblyVersion.cs) is compiled into all three targets.
$versionSource = Join-Path $root 'src\AssemblyVersion.cs'

if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

$sources = @(Get-ChildItem -Path (Join-Path $root 'src\TerminalOrganizer.Core') -Recurse -Filter '*.cs' |
    Sort-Object -Property FullName |
    ForEach-Object { $_.FullName })

$cscArgs = @(
    '/nologo',
    '/noconfig',
    '/target:library',
    '/langversion:5',
    '/warnaserror',
    '/optimize+',
    "/out:$out",
    "/r:$fw\System.dll",
    "/r:$fw\System.Core.dll",
    "/r:$fw\System.Runtime.Serialization.dll",
    "/r:$fw\System.Xml.dll",
    "/r:$fw\System.Web.Extensions.dll",
    "/r:$fw\System.Management.dll",
    "/r:$fw\WPF\UIAutomationClient.dll",
    "/r:$fw\WPF\UIAutomationTypes.dll"
) + $sources + @($versionSource)

Write-Output ('csc ' + ($cscArgs -join ' '))
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# SPEC-TRAY-007: second target - the App tray shell (winexe; plan.md B.6 pinned line).
# Same in-box compiler; the Core invocation above stays untouched.
$appOut = Join-Path $binDir 'TerminalOrganizer.App.exe'
$appManifest = Join-Path $root 'src\TerminalOrganizer.App\app.manifest'
$appIcon = Join-Path $root 'src\TerminalOrganizer.App\Assets\TerminalOrganizer.ico'
$appDarkIcon = Join-Path $root 'src\TerminalOrganizer.App\Assets\TerminalOrganizer.Dark.ico'
$appSources = @(Get-ChildItem -Path (Join-Path $root 'src\TerminalOrganizer.App') -Recurse -Filter '*.cs' |
    Sort-Object -Property FullName |
    ForEach-Object { $_.FullName })

$appArgs = @(
    '/nologo',
    '/noconfig',
    '/target:winexe',
    "/win32icon:$appIcon",
    "/resource:$appIcon,TerminalOrganizer.ico",
    "/resource:$appDarkIcon,TerminalOrganizer.Dark.ico",
    '/langversion:5',
    '/warnaserror',
    '/optimize+',
    "/out:$appOut",
    "/win32manifest:$appManifest",
    "/r:$fw\System.dll",
    "/r:$fw\System.Core.dll",
    "/r:$fw\System.Web.Extensions.dll",
    "/r:$fw\System.Windows.Forms.dll",
    "/r:$fw\System.Drawing.dll",
    "/r:$out"
) + $appSources + @($versionSource)

Write-Output ('csc ' + ($appArgs -join ' '))
& $csc @appArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# B4: third target - the UIA probe helper console exe (hard UIA timeout via subprocess).
# Own csc invocation AFTER the App target: references Core.dll plus the UIAutomation
# assemblies its UiaTabTitleReader call needs. The Core /r: block above stays untouched
# (AC-016: eight references), and the App glob never sees this directory (scoped to
# src\TerminalOrganizer.App).
$probeOut = Join-Path $binDir 'TerminalOrganizer.UiaProbe.exe'
$probeSources = @(Get-ChildItem -Path (Join-Path $root 'src\TerminalOrganizer.UiaProbe') -Recurse -Filter '*.cs' |
    Sort-Object -Property FullName |
    ForEach-Object { $_.FullName })

$probeArgs = @(
    '/nologo',
    '/noconfig',
    '/target:exe',
    '/langversion:5',
    '/warnaserror',
    '/optimize+',
    "/out:$probeOut",
    "/r:$fw\System.dll",
    "/r:$fw\System.Core.dll",
    "/r:$fw\WPF\UIAutomationClient.dll",
    "/r:$fw\WPF\UIAutomationTypes.dll",
    "/r:$out"
) + $probeSources + @($versionSource)

Write-Output ('csc ' + ($probeArgs -join ' '))
& $csc @probeArgs
exit $LASTEXITCODE
